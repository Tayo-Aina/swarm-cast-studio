using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using SwarmCastStudio.Converters;
using SwarmCastStudio.Models;
using SwarmCastStudio.Services;

namespace SwarmCastStudio.ViewModels;

/// <summary>
/// Orchestrates the live OpenCode bridge, the editable swarm config, filtering,
/// target switching, and save/reload for the main window.
/// </summary>
public sealed class MainViewModel : ObservableBase
{
    private const string GlobalTarget = "global";
    private const string ProjectTarget = "project";
    private const string UiReviewPath = "ui_review.enabled";
    private const string DesignDocsPath = "design_docs.enabled";
    private const string CouncilGeneralPath = "council.general.enabled";

    private OpenCodeBridge? _bridge;
    private readonly CollectionViewSource _agentSource;
    private readonly CollectionViewSource _modelSource;

    private SwarmConfig _config = SwarmConfig.CreateNew();
    private SwarmConfigService? _configService;
    private AppSettings? _settings;

    private bool _initialized;
    private bool _isBusy;
    private bool _isConnected;
    private bool _suppressConfigWrites;
    private bool _configUsable;
    private bool _configLoadFailed;
    private bool _isDirty;
    private int _editVersion;

    private string? _configError;
    private string? _configLoadError;
    private string? _connectionError;
    private string? _catalogWarning;
    private string? _operationError;

    private string _agentSearch = string.Empty;
    private string _modelSearch = string.Empty;
    private AgentRowViewModel? _selectedAgent;
    private AgentRowViewModel? _agentSelectionBeforeFilter;
    private OpenCodeModel? _selectedModel;

    private string _target = GlobalTarget;
    private string? _projectDirectory;
    private string _configPath = SwarmConfigService.DefaultGlobalPath;
    private bool _configExists;

    private bool _uiReviewEnabled;
    private bool _designDocsEnabled;
    private bool _councilGeneralEnabled;

    private string _statusText = "Starting Swarm Cast Studio…";
    private string _lastAction = "No action yet";
    private string _cliPath = "Detecting…";
    private string _version = "—";
    private int _liveModelCount;
    private int _liveAgentCount;
    private string _rawJsonText = "{}";

    public MainViewModel()
    {
        Agents = new ObservableCollection<AgentRowViewModel>();
        Models = new ObservableCollection<OpenCodeModel>();

        _agentSource = new CollectionViewSource { Source = Agents };
        _agentSource.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AgentRowViewModel.Category)));
        AgentsView = _agentSource.View;
        AgentsView.Filter = FilterAgent;

        _modelSource = new CollectionViewSource { Source = Models };
        _modelSource.GroupDescriptions.Add(new PropertyGroupDescription(
            nameof(OpenCodeModel.ProviderId),
            new ProviderGroupKeyConverter()));
        ModelsView = _modelSource.View;
        ModelsView.Filter = FilterModel;

        RefreshCommand = new RelayCommand(RefreshAsync, () => !IsBusy);
        RetryCommand = new RelayCommand(RetryAsync, () => !IsBusy);
        SaveCommand = new RelayCommand(
            SaveAsync,
            () => !IsBusy && IsConfigUsable && _configService is not null);
        BrowseFolderCommand = new RelayCommand(BrowseFolderAsync, () => !IsBusy);
        AddFallbackCommand = new RelayCommand(AddSelectedAsFallback, CanAddFallback);
        RemoveFallbackCommand = new RelayCommand(RemoveFallback, _ => !IsBusy && IsConfigUsable && SelectedAgent is not null);
        SetPrimaryModelCommand = new RelayCommand(SetPrimaryModel, CanSetPrimaryModel);
        ClearPrimaryModelCommand = new RelayCommand(ClearPrimaryModel, CanClearPrimaryModel);
        OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);

        BuildAgentRows();
        RefreshRawJsonFromConfig();
        UpdateCommandStates();
    }

    public ObservableCollection<AgentRowViewModel> Agents { get; }

    public ICollectionView AgentsView { get; }

    public ObservableCollection<OpenCodeModel> Models { get; }

    public ICollectionView ModelsView { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand RetryCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand BrowseFolderCommand { get; }

    public RelayCommand AddFallbackCommand { get; }

    public RelayCommand RemoveFallbackCommand { get; }

    public RelayCommand SetPrimaryModelCommand { get; }

    public RelayCommand ClearPrimaryModelCommand { get; }

    public RelayCommand OpenConfigFolderCommand { get; }

    public int AgentRoleCount => Agents.Count;

    public string AgentSearch
    {
        get => _agentSearch;
        set
        {
            var next = value ?? string.Empty;
            var wasFiltered = !string.IsNullOrWhiteSpace(_agentSearch);
            var willFilter = !string.IsNullOrWhiteSpace(next);
            var previous = _selectedAgent;

            if (!Set(ref _agentSearch, next))
            {
                return;
            }

            if (willFilter && !wasFiltered)
            {
                _agentSelectionBeforeFilter = previous;
            }

            var restoreTarget = !willFilter && wasFiltered ? _agentSelectionBeforeFilter : null;
            if (restoreTarget is not null)
            {
                _agentSelectionBeforeFilter = null;
            }

            AgentsView.Refresh();

            AgentRowViewModel? target;
            if (restoreTarget is not null)
            {
                target = restoreTarget;
            }
            else if (previous is not null && AgentsView.Contains(previous))
            {
                target = previous;
            }
            else
            {
                target = AgentsView.Cast<object>().OfType<AgentRowViewModel>().FirstOrDefault();
            }

            if (target is null)
            {
                // An empty result set must not collapse the editor.
                target = previous ?? _selectedAgent;
            }

            SelectAgent(target, forceNotification: true);
        }
    }

    public string ModelSearch
    {
        get => _modelSearch;
        set
        {
            if (!Set(ref _modelSearch, value ?? string.Empty))
            {
                return;
            }

            // Filtering is a view-only action. Drop the highlight so the explicit
            // Set as primary action always starts from a deliberate selection.
            SelectedModel = null;
            ModelsView.Refresh();
            SelectedModel = null;
        }
    }

    public AgentRowViewModel? SelectedAgent
    {
        get => _selectedAgent;
        set
        {
            if (value is null && _selectedAgent is not null)
            {
                // The ListBox clears SelectedItem while its view refreshes. Keep the
                // editor bound; AgentSearch chooses a replacement explicitly.
                return;
            }

            SelectAgent(value, forceNotification: false);
        }
    }

    public bool HasSelectedAgent => SelectedAgent is not null;

    private void SelectAgent(AgentRowViewModel? agent, bool forceNotification)
    {
        var changed = !ReferenceEquals(_selectedAgent, agent);
        if (changed)
        {
            _selectedAgent = agent;
        }

        if (!changed && !forceNotification)
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedAgent));
        OnPropertyChanged(nameof(HasSelectedAgent));

        if (changed)
        {
            SetSelectedModelFromAgent();
            UpdateCommandStates();
        }
    }

    public OpenCodeModel? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!Set(ref _selectedModel, value))
            {
                return;
            }

            // Highlight only. Applying a model is an explicit Set as primary / double-click action.
            UpdateCommandStates();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsEditingEnabled));
            OnPropertyChanged(nameof(StatusPillText));
            OnPropertyChanged(nameof(ConnectionStatusText));
            UpdateCommandStates();
        }
    }

    public bool IsIdle => !IsBusy;

    public bool IsConfigUsable => _configUsable;

    public bool ConfigLoadFailed => _configLoadFailed;

    public bool IsEditingEnabled => IsIdle && IsConfigUsable;

    public bool IsDirty => _isDirty;

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (!Set(ref _isConnected, value))
            {
                return;
            }

            OnPropertyChanged(nameof(StatusPillText));
            OnPropertyChanged(nameof(ConnectionStatusText));
        }
    }

    public string StatusPillText
    {
        get
        {
            if (HasError)
            {
                return "ERROR";
            }

            if (IsBusy)
            {
                return "SYNCING";
            }

            return IsConnected ? "LIVE" : "IDLE";
        }
    }

    public string ConnectionStatusText
    {
        get
        {
            if (HasError)
            {
                return "Error";
            }

            if (IsConnected)
            {
                return "Connected";
            }

            return IsBusy ? "Connecting…" : "Not connected";
        }
    }

    public bool HasError =>
        _configLoadError is not null ||
        _configError is not null ||
        _connectionError is not null ||
        _catalogWarning is not null ||
        _operationError is not null;

    public string ErrorMessage
    {
        get
        {
            var messages = new List<string>(5);
            if (_configLoadError is not null)
            {
                messages.Add(_configLoadError);
            }

            if (_configError is not null)
            {
                messages.Add(_configError);
            }

            if (_connectionError is not null)
            {
                messages.Add(_connectionError);
            }

            if (_catalogWarning is not null)
            {
                messages.Add(_catalogWarning);
            }

            if (_operationError is not null)
            {
                messages.Add(_operationError);
            }

            return string.Join("  •  ", messages);
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string LastAction
    {
        get => _lastAction;
        private set => Set(ref _lastAction, value);
    }

    public string CliPath
    {
        get => _cliPath;
        private set => Set(ref _cliPath, value);
    }

    public string Version
    {
        get => _version;
        private set => Set(ref _version, value);
    }

    public int ModelCount => _liveModelCount > 0 ? _liveModelCount : Models.Count;

    public int LiveAgentCount
    {
        get => _liveAgentCount;
        private set => Set(ref _liveAgentCount, value);
    }

    public bool HasModels => Models.Count > 0;

    public string Target => _target;

    public bool IsGlobalTarget
    {
        get => string.Equals(_target, GlobalTarget, StringComparison.Ordinal);
        set
        {
            if (!value || IsGlobalTarget)
            {
                return;
            }

            _ = ChangeTargetSafeAsync(GlobalTarget);
        }
    }

    public bool IsProjectTarget
    {
        get => string.Equals(_target, ProjectTarget, StringComparison.Ordinal);
        set
        {
            if (!value || IsProjectTarget)
            {
                return;
            }

            _ = ChangeTargetSafeAsync(ProjectTarget);
        }
    }

    public string? ProjectDirectory
    {
        get => _projectDirectory;
        private set
        {
            if (!Set(ref _projectDirectory, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProjectFolderDisplay));
        }
    }

    public string ProjectFolderDisplay =>
        string.IsNullOrWhiteSpace(ProjectDirectory) ? "No folder selected" : ProjectDirectory!;

    public string ConfigPath
    {
        get => _configPath;
        private set => Set(ref _configPath, value);
    }

    public bool ConfigExists
    {
        get => _configExists;
        private set
        {
            if (!Set(ref _configExists, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ConfigExistsLabel));
        }
    }

    public string ConfigExistsLabel => ConfigExists ? "EXISTING FILE" : "NEW FILE";

    public bool UiReviewEnabled
    {
        get => _uiReviewEnabled;
        set
        {
            if (Set(ref _uiReviewEnabled, value))
            {
                ApplyFeatureChange(UiReviewPath, value);
            }
        }
    }

    public bool DesignDocsEnabled
    {
        get => _designDocsEnabled;
        set
        {
            if (Set(ref _designDocsEnabled, value))
            {
                ApplyFeatureChange(DesignDocsPath, value);
            }
        }
    }

    public bool CouncilGeneralEnabled
    {
        get => _councilGeneralEnabled;
        set
        {
            if (Set(ref _councilGeneralEnabled, value))
            {
                ApplyFeatureChange(CouncilGeneralPath, value);
            }
        }
    }

    public string RawJsonText
    {
        get => _rawJsonText;
        private set => Set(ref _rawJsonText, value);
    }

    /// <summary>Called once when the main window loads. Never throws to the dispatcher.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || IsBusy)
        {
            return;
        }

        _initialized = true;
        IsBusy = true;
        try
        {
            ClearAllErrors();
            LoadSettings();

            StatusText = "Loading swarm configuration…";
            try
            {
                await LoadConfigForTargetAsync();
            }
            catch (Exception ex)
            {
                ApplyFallbackConfig();
                SetConfigLoadFailure($"Could not load configuration: {ex.Message}");
                StatusText = "Configuration could not be loaded — editing and saving are disabled until Retry succeeds.";
            }

            SetSelectedModelFromAgent();
            StatusText = "Connecting to OpenCode…";
            await RefreshConnectionCoreAsync(retrySuspectCatalog: true);

            if (ConfigLoadFailed)
            {
                StatusText =
                    "Configuration could not be loaded — editing and saving are disabled. Use Retry after fixing the file.";
                LastAction = "Configuration load failed";
            }
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Surfaces an exception that escaped the regular async paths.</summary>
    public void ReportUnexpected(Exception ex)
    {
        try
        {
            SetOperationError($"Unexpected UI error: {Compact(ex.Message)}");
            StatusText = "An unexpected error occurred — see error above.";
            LastAction = "Unexpected error";
        }
        catch
        {
            // Never let error reporting throw back to the dispatcher.
        }
    }

    private void BuildAgentRows()
    {
        foreach (var definition in AgentCatalog.All)
        {
            var row = new AgentRowViewModel(definition);
            row.PropertyChanged += OnAgentPropertyChanged;
            row.Fallbacks.CollectionChanged += (_, _) => OnAgentFallbacksChanged(row);
            Agents.Add(row);
        }

        OnPropertyChanged(nameof(AgentRoleCount));
        SelectedAgent = Agents.FirstOrDefault(
                             agent => string.Equals(agent.Name, "architect", StringComparison.OrdinalIgnoreCase))
                         ?? Agents.FirstOrDefault();
    }

    private void LoadSettings()
    {
        try
        {
            _settings = AppSettings.Load();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SwarmCastStudio] Could not load app settings: {ex}");
            _settings = new AppSettings();
        }

        var settings = _settings ?? new AppSettings();
        _settings = settings;

        var target = string.Equals(settings.Target, ProjectTarget, StringComparison.OrdinalIgnoreCase)
            ? ProjectTarget
            : GlobalTarget;
        var directory = settings.ProjectDirectory;

        if (string.Equals(target, ProjectTarget, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(directory))
        {
            target = GlobalTarget;
        }

        SetTargetState(target);
        ProjectDirectory = string.IsNullOrWhiteSpace(directory) ? null : directory;
    }

    private async Task LoadConfigForTargetAsync()
    {
        string path;
        if (IsProjectTarget)
        {
            if (string.IsNullOrWhiteSpace(ProjectDirectory))
            {
                throw new InvalidOperationException("Choose a project folder before loading the project override.");
            }

            path = SwarmConfigService.ProjectPathFor(ProjectDirectory);
        }
        else
        {
            path = SwarmConfigService.DefaultGlobalPath;
        }

        var service = new SwarmConfigService(path);
        var existed = service.Exists;
        var seeded = false;
        SwarmConfig config;

        if (IsProjectTarget && !existed && File.Exists(SwarmConfigService.DefaultGlobalPath))
        {
            var globalPath = SwarmConfigService.DefaultGlobalPath;
            config = await Task.Run(() => new SwarmConfigService(globalPath).Load());
            seeded = true;
        }
        else
        {
            config = await Task.Run(service.Load);
        }

        _configService = service;
        _config = config;
        ConfigPath = service.Path;
        ConfigExists = existed;
        SetConfigLoadSuccess();

        ApplyConfigToRows(config);
        RefreshRawJsonFromConfig();
        LastAction = seeded
            ? "Project override seeded from global config"
            : $"Configuration loaded at {DateTime.Now:HH:mm}";
        UpdateCommandStates();
    }

    private void ApplyFallbackConfig()
    {
        var path = GetCurrentConfigPath();
        _configService = null;
        _config = SwarmConfig.CreateNew();

        if (path is not null)
        {
            ConfigPath = path;
            ConfigExists = File.Exists(path);
        }

        ApplyConfigToRows(_config);
        RefreshRawJsonFromConfig();
        SetConfigUsable(false);
        UpdateCommandStates();
    }

    private void ApplyConfigToRows(SwarmConfig config)
    {
        _suppressConfigWrites = true;
        try
        {
            UiReviewEnabled = config.GetFeature(UiReviewPath);
            DesignDocsEnabled = config.GetFeature(DesignDocsPath);
            CouncilGeneralEnabled = config.GetFeature(CouncilGeneralPath);

            foreach (var row in Agents)
            {
                row.IsEnabled = config.IsAgentEnabled(row.Name);
                row.Model = config.GetAgentModel(row.Name);

                row.Fallbacks.Clear();
                foreach (var fallback in config.GetAgentFallbacks(row.Name)
                             .Where(fallback => !string.IsNullOrWhiteSpace(fallback))
                             .Select(fallback => fallback.Trim())
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    row.Fallbacks.Add(fallback);
                }

                var temperature = config.GetAgentTemperature(row.Name);
                row.HasTemperature = temperature.HasValue;
                row.Temperature = temperature ?? 0.7;
                row.GateSatisfied = IsGateSatisfied(row.Gate);
            }
        }
        finally
        {
            _suppressConfigWrites = false;
        }

        SetSelectedModelFromAgent();
    }

    private void ApplyFeatureChange(string path, bool value)
    {
        if (!_suppressConfigWrites && IsConfigUsable)
        {
            try
            {
                _config.SetFeature(path, value);
                SyncConfigAndRefreshJson();
                MarkDirty($"{path} changed — unsaved changes");
            }
            catch (Exception ex)
            {
                SetOperationError($"Could not update feature gate {path}: {Compact(ex.Message)}");
            }
        }

        RefreshGates();
    }

    private void RefreshGates()
    {
        foreach (var row in Agents)
        {
            row.GateSatisfied = IsGateSatisfied(row.Gate);
        }
    }

    private bool IsGateSatisfied(AgentGate gate) => gate switch
    {
        AgentGate.None => true,
        AgentGate.UiReview => UiReviewEnabled,
        AgentGate.DesignDocs => DesignDocsEnabled,
        AgentGate.CouncilGeneral => CouncilGeneralEnabled,
        _ => true
    };

    private async Task ChangeTargetSafeAsync(string target)
    {
        try
        {
            if (IsBusy)
            {
                NotifyTargetFlags();
                return;
            }

            if (string.Equals(_target, target, StringComparison.Ordinal))
            {
                return;
            }

            if (!ConfirmDiscardUnsavedChanges("switch the configuration target"))
            {
                NotifyTargetFlags();
                return;
            }

            SetTargetState(target);
            SaveSettings();
            await LoadTargetConfigWithBusyAsync();
        }
        catch (Exception ex)
        {
            SetConfigLoadFailure($"Could not change configuration target: {Compact(ex.Message)}");
            StatusText = "Could not change the configuration target — see error above.";
            NotifyTargetFlags();
        }
    }

    private async Task LoadTargetConfigWithBusyAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (IsProjectTarget && string.IsNullOrWhiteSpace(ProjectDirectory))
            {
                _configService = null;
                ConfigPath = "Choose a project folder to continue";
                ConfigExists = false;
                SetConfigUsable(false);
                UpdateCommandStates();
                StatusText = "Project override selected — choose a project folder.";
                LastAction = "Waiting for a project folder";
                return;
            }

            StatusText = "Loading configuration…";
            await LoadConfigForTargetAsync();
            var enabled = Agents.Count(agent => agent.IsEnabled);
            StatusText = $"Loaded {(ConfigExists ? "existing" : "new")} configuration — {enabled} agents enabled";
            LastAction = $"Configuration target changed at {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            var path = GetCurrentConfigPath();
            _configService = null;
            if (path is not null)
            {
                ConfigPath = path;
                ConfigExists = File.Exists(path);
            }
            else
            {
                ConfigPath = "Choose a project folder to continue";
                ConfigExists = false;
            }

            SetConfigLoadFailure($"Could not load configuration: {Compact(ex.Message)}");
            StatusText = "Could not load configuration — editing and saving are disabled. Use Retry after fixing the file.";
            LastAction = "Configuration load failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BrowseFolderAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose a project folder for the project override"
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                return;
            }

            if (IsProjectTarget &&
                string.Equals(ProjectDirectory, dialog.FolderName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!ConfirmDiscardUnsavedChanges("change the project folder"))
            {
                return;
            }

            ProjectDirectory = dialog.FolderName;
            SetTargetState(ProjectTarget);
            SaveSettings();
            await LoadTargetConfigWithBusyAsync();
        }
        catch (Exception ex)
        {
            SetOperationError($"Could not open the folder picker: {Compact(ex.Message)}");
            StatusText = "Could not select a project folder — see error above.";
        }
    }

    private void SetTargetState(string target)
    {
        _target = target;
        NotifyTargetFlags();
    }

    private void NotifyTargetFlags()
    {
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(IsGlobalTarget));
        OnPropertyChanged(nameof(IsProjectTarget));
    }

    private void SaveSettings()
    {
        try
        {
            var settings = _settings ??= new AppSettings();
            settings.Target = Target;
            settings.ProjectDirectory = ProjectDirectory;
            settings.Save();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SwarmCastStudio] Could not save app settings: {ex}");
        }
    }

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!_initialized)
        {
            await InitializeAsync();
            return;
        }

        IsBusy = true;
        try
        {
            ClearConnectionError();
            ClearOperationError();
            StatusText = "Refreshing live OpenCode catalog…";
            LastAction = "Refresh requested";
            await RefreshConnectionCoreAsync();
        }
        catch (Exception ex)
        {
            SetConnectionError($"Refresh failed: {Compact(ex.Message)}");
            StatusText = "Refresh failed — see error above.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RetryAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ClearOperationError();
            StatusText = "Retrying configuration and OpenCode…";
            _bridge = null;

            if (ConfigLoadFailed)
            {
                try
                {
                    if (IsProjectTarget && string.IsNullOrWhiteSpace(ProjectDirectory))
                    {
                        throw new InvalidOperationException(
                            "Choose a project folder before reloading the project override.");
                    }

                    await LoadConfigForTargetAsync();
                }
                catch (Exception ex)
                {
                    var path = GetCurrentConfigPath();
                    _configService = null;
                    if (path is not null)
                    {
                        ConfigPath = path;
                        ConfigExists = File.Exists(path);
                    }

                    SetConfigLoadFailure($"Could not load configuration: {Compact(ex.Message)}");
                }
            }

            await RefreshConnectionCoreAsync(retrySuspectCatalog: true, recreateBridge: true);

            if (ConfigLoadFailed)
            {
                StatusText = "Configuration could not be loaded — editing and saving stay disabled. See error above.";
                LastAction = "Retry failed";
            }
        }
        catch (Exception ex)
        {
            SetConnectionError($"Retry failed: {Compact(ex.Message)}");
            StatusText = "Retry failed — see error above.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<OpenCodeBridge> GetOrCreateBridgeAsync(bool recreate = false)
    {
        if (recreate)
        {
            _bridge = null;
        }

        if (_bridge is not null)
        {
            return _bridge;
        }

        try
        {
            _bridge = await Task.Run(() => new OpenCodeBridge());
            return _bridge;
        }
        catch
        {
            _bridge = null;
            throw;
        }
    }

    private async Task RefreshConnectionCoreAsync(
        bool retrySuspectCatalog = false,
        bool recreateBridge = false)
    {
        ClearConnectionError();

        OpenCodeBridge? bridge = null;
        try
        {
            bridge = await GetOrCreateBridgeAsync(recreateBridge);
            var connection = await bridge.ConnectAsync();
            CliPath = connection.CliPath;
            Version = connection.Version;
            IsConnected = true;

            string? modelsError = null;
            string? agentsError = null;
            IReadOnlyList<OpenCodeModel>? models = null;
            IReadOnlyList<LiveAgent>? agents = null;

            try
            {
                models = await bridge.GetModelsAsync();
            }
            catch (Exception ex)
            {
                modelsError = Compact(ex.Message);
            }

            try
            {
                agents = await bridge.GetAgentsAsync();
            }
            catch (Exception ex)
            {
                agentsError = Compact(ex.Message);
            }

            var firstCatalogLooksIncomplete =
                models is null ||
                agents is null ||
                (models is not null && models.Count == 0) ||
                (agents is not null && agents.Count <= 7);

            if (retrySuspectCatalog && firstCatalogLooksIncomplete)
            {
                StatusText = "OpenCode is finishing a reload — retrying the catalog once…";
                await Task.Delay(1500);

                try
                {
                    models = await bridge.GetModelsAsync();
                    modelsError = null;
                }
                catch (Exception ex)
                {
                    models = null;
                    modelsError = Compact(ex.Message);
                }

                try
                {
                    agents = await bridge.GetAgentsAsync();
                    agentsError = null;
                }
                catch (Exception ex)
                {
                    agents = null;
                    agentsError = Compact(ex.Message);
                }
            }

            var catalogWarnings = new List<string>(2);
            if (models is not null && models.Count == 0)
            {
                catalogWarnings.Add(
                    "OpenCode reported 0 models; using model refs from the saved configuration.");
            }

            if (agents is not null && agents.Count <= 7)
            {
                catalogWarnings.Add(
                    $"OpenCode reported only {agents.Count} agents; the swarm may still be reloading.");
            }

            SetCatalogWarning(catalogWarnings.Count == 0 ? null : string.Join("  •  ", catalogWarnings));

            if (models is not null && models.Count > 0)
            {
                _liveModelCount = models.Count;
                ReplaceModels(models);
            }
            else
            {
                _liveModelCount = 0;
                if (models is not null || Models.Count == 0)
                {
                    SynthesizeModelsFromConfig(replaceExisting: true);
                }

                OnPropertyChanged(nameof(ModelCount));
            }

            if (agents is not null)
            {
                LiveAgentCount = agents.Count;
                UpdateLiveAgents(agents);
            }
            else
            {
                LiveAgentCount = 0;
                ClearLiveStates();
            }

            if (modelsError is not null || agentsError is not null || _catalogWarning is not null)
            {
                var parts = new List<string>(2);
                if (modelsError is not null)
                {
                    parts.Add($"models: {modelsError}");
                }

                if (agentsError is not null)
                {
                    parts.Add($"agents: {agentsError}");
                }

                if (parts.Count > 0)
                {
                    _connectionError = string.Join("  •  ", parts);
                }

                RefreshErrorState();
                StatusText = "OpenCode connected, but the live catalog is incomplete — see error above.";
                LastAction = $"Catalog partly refreshed at {DateTime.Now:HH:mm}";
                return;
            }

            ClearConnectionError();
            StatusText = $"Connected to OpenCode {Version} — {ModelCount} models, {LiveAgentCount} agents";
            LastAction = $"Live catalog refreshed at {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _connectionError = $"OpenCode unavailable: {Compact(ex.Message)}";
            RefreshErrorState();
            LiveAgentCount = 0;
            ClearLiveStates();
            EnsureSyntheticModels();
            await DiscoverCliPathAsync(bridge);
            StatusText = "OpenCode unavailable — editing from saved configuration.";
            LastAction = "Connection failed";
        }
    }

    private async Task DiscoverCliPathAsync(OpenCodeBridge? bridge)
    {
        try
        {
            if (bridge is not null && !string.IsNullOrWhiteSpace(bridge.CliPath))
            {
                CliPath = bridge.CliPath!;
                return;
            }

            var discovered = await Task.Run(OpenCodeBridge.DiscoverCli);
            CliPath = string.IsNullOrWhiteSpace(discovered) ? "Not found" : discovered!;
        }
        catch
        {
            CliPath = "Not found";
        }
    }

    private void ReplaceModels(IEnumerable<OpenCodeModel> models)
    {
        var unique = new Dictionary<string, OpenCodeModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            if (model is null)
            {
                continue;
            }

            unique.TryAdd(model.Ref, model);
        }

        var ordered = unique.Values
            .OrderBy(model => ProviderRank(model.ProviderId))
            .ThenBy(model => model.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Models.Clear();
        foreach (var model in ordered)
        {
            Models.Add(model);
        }

        OnPropertyChanged(nameof(HasModels));
        OnPropertyChanged(nameof(ModelCount));
        SetSelectedModelFromAgent();
        UpdateCommandStates();
    }

    private void SynthesizeModelsFromConfig(bool replaceExisting = false)
    {
        if (!replaceExisting && Models.Count > 0)
        {
            return;
        }

        var references = new List<string>();
        foreach (var row in Agents)
        {
            if (!string.IsNullOrWhiteSpace(row.Model))
            {
                references.Add(row.Model!);
            }

            references.AddRange(row.Fallbacks.Where(fallback => !string.IsNullOrWhiteSpace(fallback)));
        }

        var synthetic = new List<OpenCodeModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references)
        {
            if (!seen.Add(reference))
            {
                continue;
            }

            var slash = reference.IndexOf('/');
            var provider = slash > 0 ? reference[..slash] : "saved";
            var modelId = slash > 0 ? reference[(slash + 1)..] : reference;
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(modelId))
            {
                continue;
            }

            synthetic.Add(new OpenCodeModel
            {
                ProviderId = provider,
                ModelId = modelId,
                DisplayName = $"{modelId} (from config)",
                ContextLimit = 0,
                OutputLimit = 0,
                InputPricePerMillion = 0,
                OutputPricePerMillion = 0,
                SupportsTools = true,
                IsFree = false,
                Status = "unavailable",
                Enabled = true
            });
        }

        ReplaceModels(synthetic);
    }

    private void EnsureSyntheticModels()
    {
        if (Models.Count == 0)
        {
            SynthesizeModelsFromConfig();
        }
    }

    private void UpdateLiveAgents(IReadOnlyList<LiveAgent> liveAgents)
    {
        var byId = new Dictionary<string, LiveAgent>(StringComparer.OrdinalIgnoreCase);
        foreach (var live in liveAgents)
        {
            if (!string.IsNullOrWhiteSpace(live.Id))
            {
                byId[live.Id] = live;
            }
        }

        foreach (var row in Agents)
        {
            LiveAgent? matched = null;
            if (!byId.TryGetValue(row.Name, out matched))
            {
                byId.TryGetValue(row.DisplayName, out matched);
            }

            row.IsLive = matched is not null;
            row.LiveModelRef = matched?.ModelRef;
        }
    }

    private void ClearLiveStates()
    {
        foreach (var row in Agents)
        {
            row.IsLive = false;
            row.LiveModelRef = null;
        }
    }

    private void SetSelectedModelFromAgent()
    {
        var reference = SelectedAgent?.Model;
        var match = string.IsNullOrWhiteSpace(reference)
            ? null
            : Models.FirstOrDefault(
                model => string.Equals(model.Ref, reference, StringComparison.OrdinalIgnoreCase));

        SelectedModel = match;
    }

    private void OnAgentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressConfigWrites || !IsConfigUsable)
        {
            return;
        }

        if (e.PropertyName is not (nameof(AgentRowViewModel.IsEnabled)
            or nameof(AgentRowViewModel.Model)
            or nameof(AgentRowViewModel.Temperature)
            or nameof(AgentRowViewModel.HasTemperature)))
        {
            return;
        }

        if (e.PropertyName == nameof(AgentRowViewModel.Model) && ReferenceEquals(sender, SelectedAgent))
        {
            SetSelectedModelFromAgent();
            ClearPrimaryModelCommand.RaiseCanExecuteChanged();
        }

        // Moving the temperature slider is an explicit edit: turn on the override automatically.
        if (e.PropertyName == nameof(AgentRowViewModel.Temperature)
            && sender is AgentRowViewModel temperatureRow
            && !temperatureRow.HasTemperature)
        {
            _suppressConfigWrites = true;
            try
            {
                temperatureRow.HasTemperature = true;
            }
            finally
            {
                _suppressConfigWrites = false;
            }
        }

        SyncConfigAndRefreshJson();
        MarkDirty("Unsaved changes");
    }

    private void OnAgentFallbacksChanged(AgentRowViewModel row)
    {
        if (_suppressConfigWrites || !IsConfigUsable)
        {
            return;
        }

        SyncConfigAndRefreshJson();
        MarkDirty("Unsaved changes");
    }

    private bool FilterAgent(object item)
    {
        if (item is not AgentRowViewModel row)
        {
            return false;
        }

        var query = AgentSearch;
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return ContainsText(row.DisplayName, query)
               || ContainsText(row.Name, query)
               || ContainsText(row.Description, query)
               || ContainsText(row.Category, query);
    }

    private bool FilterModel(object item)
    {
        if (item is not OpenCodeModel model)
        {
            return false;
        }

        var query = ModelSearch;
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return ContainsText(model.DisplayName, query)
               || ContainsText(model.Ref, query)
               || ContainsText(model.ProviderId, query)
               || ContainsText(model.ModelId, query);
    }

    private static bool ContainsText(string? source, string query)
    {
        return !string.IsNullOrEmpty(source)
               && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int ProviderRank(string? providerId)
    {
        var provider = providerId?.Trim() ?? string.Empty;
        if (string.Equals(provider, "opencode-go", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(provider, "opencode", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (provider.Length == 0
            || string.Equals(provider, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 2;
    }

    private bool CanAddFallback()
    {
        return !IsBusy && IsConfigUsable && SelectedAgent is not null && SelectedModel is not null;
    }

    private bool CanSetPrimaryModel()
    {
        return !IsBusy && IsConfigUsable && SelectedAgent is not null && SelectedModel is not null;
    }

    private void SetPrimaryModel()
    {
        var agent = SelectedAgent;
        var model = SelectedModel;
        if (agent is null || model is null || !IsConfigUsable)
        {
            return;
        }

        var reference = model.Ref;
        if (string.Equals(agent.Model, reference, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "That model is already the primary.";
            return;
        }

        var existingFallback = agent.Fallbacks.FirstOrDefault(
            fallback => string.Equals(fallback, reference, StringComparison.OrdinalIgnoreCase));
        if (existingFallback is not null)
        {
            agent.Fallbacks.Remove(existingFallback);
        }

        agent.Model = reference;
        StatusText = $"Primary model set to {reference}.";
        LastAction = "Unsaved changes";
    }

    private bool CanClearPrimaryModel()
    {
        return !IsBusy && IsConfigUsable && SelectedAgent?.HasModel == true;
    }

    private void ClearPrimaryModel()
    {
        var agent = SelectedAgent;
        if (agent is null || !agent.HasModel || !IsConfigUsable)
        {
            return;
        }

        agent.Model = null;
        SelectedModel = null;
        LastAction = "Primary model cleared — unsaved changes";
    }

    private void AddSelectedAsFallback()
    {
        var agent = SelectedAgent;
        var model = SelectedModel;
        if (agent is null || model is null || !IsConfigUsable)
        {
            LastAction = "Select a model first";
            return;
        }

        var reference = model.Ref;
        if (string.Equals(agent.Model, reference, StringComparison.OrdinalIgnoreCase))
        {
            LastAction = "That model is already the primary model";
            return;
        }

        if (agent.Fallbacks.Any(
                fallback => string.Equals(fallback, reference, StringComparison.OrdinalIgnoreCase)))
        {
            LastAction = "That model is already a fallback";
            return;
        }

        agent.Fallbacks.Add(reference);
        LastAction = "Fallback added — unsaved changes";
    }

    private void RemoveFallback(object? parameter)
    {
        if (!IsConfigUsable || SelectedAgent is null || parameter is not string reference)
        {
            return;
        }

        var existing = SelectedAgent.Fallbacks.FirstOrDefault(
            fallback => string.Equals(fallback, reference, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        SelectedAgent.Fallbacks.Remove(existing);
        LastAction = "Fallback removed — unsaved changes";
    }

    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!IsConfigUsable || ConfigLoadFailed || _configService is null)
        {
            StatusText = ConfigLoadFailed
                ? "Configuration failed to load — use Retry before saving."
                : "Choose a valid configuration target before saving.";
            LastAction = "Save unavailable";
            return;
        }

        if (!ConfirmIfCoreAgentsDisabled())
        {
            return;
        }

        IsBusy = true;
        try
        {
            ClearOperationError();
            StatusText = "Writing configuration…";
            var saveEditVersion = _editVersion;

            SyncConfigFromRows();
            RefreshRawJsonFromConfig();

            var service = _configService!;
            var snapshotJson = _config.ToJson();

            await Task.Run(() =>
            {
                var snapshot = SwarmConfig.FromJson(snapshotJson);
                service.Save(snapshot);
            });

            ConfigExists = true;
            ClearConfigError();
            StatusText = "Configuration saved — reloading OpenCode…";

            string? reloadError = null;
            try
            {
                var bridge = await GetOrCreateBridgeAsync();
                await bridge.ReloadAsync();
            }
            catch (Exception ex)
            {
                reloadError = Compact(ex.Message);
            }

            string? agentRefreshError = null;
            if (reloadError is null)
            {
                try
                {
                    var bridge = await GetOrCreateBridgeAsync();
                    var agents = await bridge.GetAgentsAsync();
                    LiveAgentCount = agents.Count;
                    UpdateLiveAgents(agents);
                }
                catch (Exception ex)
                {
                    agentRefreshError = Compact(ex.Message);
                }
            }

            var editsLanded = _editVersion != saveEditVersion;

            if (reloadError is not null)
            {
                SetOperationError($"Configuration saved, but OpenCode reload failed: {reloadError}");
                StatusText = "Configuration saved — OpenCode reload failed. See error above.";
                LastAction = editsLanded ? "Unsaved changes" : "Config saved; reload failed";
                if (editsLanded)
                {
                    SetDirty(true);
                }
            }
            else if (agentRefreshError is not null)
            {
                ClearConnectionError();
                IsConnected = true;
                SetOperationError(
                    $"Configuration saved and reloaded, but live agents could not be refreshed: {agentRefreshError}");

                if (editsLanded)
                {
                    SetDirty(true);
                    StatusText = "Saved and reloaded OpenCode, but newer edits remain unsaved.";
                    LastAction = "Unsaved changes";
                }
                else
                {
                    ClearDirty();
                    StatusText = "Saved and reloaded OpenCode — live agent refresh failed.";
                    LastAction = "Config saved; live agent refresh failed";
                }
            }
            else
            {
                ClearConnectionError();
                IsConnected = true;
                ClearOperationError();

                if (editsLanded)
                {
                    SetDirty(true);
                    StatusText =
                        $"Saved and reloaded OpenCode, but newer edits remain unsaved — {ModelCount} models, {LiveAgentCount} agents";
                    LastAction = "Unsaved changes";
                }
                else
                {
                    ClearDirty();
                    StatusText = $"Saved and reloaded OpenCode — {ModelCount} models, {LiveAgentCount} agents";
                    LastAction = $"Saved and reloaded at {DateTime.Now:HH:mm:ss}";
                }
            }
        }
        catch (Exception ex)
        {
            SetOperationError($"Save failed: {Compact(ex.Message)}");
            StatusText = "Save failed — see error above.";
            LastAction = "Save failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool ConfirmIfCoreAgentsDisabled()
    {
        var disabled = new List<string>();
        foreach (var name in AgentCatalog.CoreRequiredNames)
        {
            var row = Agents.FirstOrDefault(
                agent => string.Equals(agent.Name, name, StringComparison.OrdinalIgnoreCase));
            if (row is not null && !row.IsEnabled)
            {
                disabled.Add(row.DisplayName);
            }
        }

        if (disabled.Count == 0)
        {
            return true;
        }

        var message =
            "These required swarm roles are currently disabled:\n\n  • " +
            string.Join("\n  • ", disabled) +
            "\n\nThe architect → coder → reviewer → test engineer pipeline may fail, stall, " +
            "or produce incomplete results because the swarm plugin registers these roles as required." +
            "\n\nSave anyway?";

        const string caption = "Disable required agents?";
        var owner = Application.Current?.MainWindow;
        var result = owner is null
            ? MessageBox.Show(
                message,
                caption,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No)
            : MessageBox.Show(
                owner,
                message,
                caption,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            StatusText = "Save cancelled — required agents are still disabled.";
            LastAction = "Save cancelled";
            return false;
        }

        return true;
    }

    private void SyncConfigFromRows()
    {
        foreach (var row in Agents)
        {
            var fallbacks = row.Fallbacks
                .Where(fallback => !string.IsNullOrWhiteSpace(fallback))
                .Select(fallback => fallback.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var model = string.IsNullOrWhiteSpace(row.Model) ? null : row.Model;
            var temperature = row.HasTemperature ? Math.Round(row.Temperature, 2) : (double?)null;

            // Do not create empty agent objects for untouched, enabled agents with no overrides.
            if (row.IsEnabled && model is null && fallbacks.Count == 0 && temperature is null)
            {
                var hasExistingValues = !_config.IsAgentEnabled(row.Name)
                                        || !string.IsNullOrWhiteSpace(_config.GetAgentModel(row.Name))
                                        || _config.GetAgentFallbacks(row.Name).Count > 0
                                        || _config.GetAgentTemperature(row.Name).HasValue;
                if (!hasExistingValues)
                {
                    continue;
                }
            }

            _config.SetAgent(row.Name, row.IsEnabled, model, fallbacks, temperature);
        }
    }

    private void SyncConfigAndRefreshJson()
    {
        try
        {
            SyncConfigFromRows();
            RefreshRawJsonFromConfig();
        }
        catch (Exception ex)
        {
            SetOperationError($"Could not stage configuration change: {Compact(ex.Message)}");
        }
    }

    private void RefreshRawJsonFromConfig()
    {
        try
        {
            RawJsonText = _config.ToJson();
        }
        catch (Exception ex)
        {
            RawJsonText = "// Unable to render configuration\n// " + ex.Message;
        }
    }

    private void OpenConfigFolder()
    {
        try
        {
            var path = _configService?.Path ?? ConfigPath;
            var folder = string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(folder))
            {
                StatusText = "Config path is not available yet.";
                return;
            }

            if (!Directory.Exists(folder))
            {
                StatusText = "Config folder does not exist yet — save once to create it.";
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"")
            {
                UseShellExecute = true
            });
            LastAction = "Opened config folder";
        }
        catch (Exception ex)
        {
            SetOperationError($"Could not open config folder: {Compact(ex.Message)}");
            StatusText = "Could not open the config folder — see error above.";
        }
    }

    private string? GetCurrentConfigPath()
    {
        if (IsProjectTarget)
        {
            return string.IsNullOrWhiteSpace(ProjectDirectory)
                ? null
                : SwarmConfigService.ProjectPathFor(ProjectDirectory);
        }

        return SwarmConfigService.DefaultGlobalPath;
    }

    private void MarkDirty(string message)
    {
        _editVersion++;
        SetDirty(true);
        LastAction = message;
    }

    private void SetDirty(bool dirty)
    {
        if (_isDirty == dirty)
        {
            return;
        }

        _isDirty = dirty;
        OnPropertyChanged(nameof(IsDirty));
    }

    private void ClearDirty()
    {
        SetDirty(false);
    }

    private void SetConfigUsable(bool usable)
    {
        if (_configUsable == usable)
        {
            return;
        }

        _configUsable = usable;
        OnPropertyChanged(nameof(IsConfigUsable));
        OnPropertyChanged(nameof(IsEditingEnabled));
        UpdateCommandStates();
    }

    private void SetConfigLoadSuccess()
    {
        _configLoadFailed = false;
        _configLoadError = null;
        SetConfigUsable(true);
        ClearConfigError();
        ClearDirty();
        OnPropertyChanged(nameof(ConfigLoadFailed));
        RefreshErrorState();
        UpdateCommandStates();
    }

    private void SetConfigLoadFailure(string message)
    {
        _configLoadFailed = true;
        _configLoadError = Compact(message);
        _configService = null;
        SetConfigUsable(false);
        OnPropertyChanged(nameof(ConfigLoadFailed));
        RefreshErrorState();
        UpdateCommandStates();
    }

    public bool ConfirmClose()
    {
        return !IsDirty || ConfirmDiscardUnsavedChanges("close the window");
    }

    private bool ConfirmDiscardUnsavedChanges(string action)
    {
        if (!IsDirty)
        {
            return true;
        }

        const string caption = "Discard unsaved changes?";
        var message = $"You have unsaved changes. Discard them and {action}?";
        var owner = Application.Current?.MainWindow;
        var result = owner is null
            ? MessageBox.Show(
                message,
                caption,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No)
            : MessageBox.Show(
                owner,
                message,
                caption,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            ClearDirty();
            StatusText = "Unsaved changes discarded.";
            LastAction = "Changes discarded";
            return true;
        }

        StatusText = "Action cancelled — unsaved changes were kept.";
        LastAction = "Unsaved changes";
        return false;
    }

    private void SetConfigError(string message)
    {
        _configError = Compact(message);
        RefreshErrorState();
    }

    private void SetConnectionError(string message)
    {
        _connectionError = Compact(message);
        RefreshErrorState();
    }

    private void SetOperationError(string message)
    {
        _operationError = Compact(message);
        RefreshErrorState();
    }

    private void ClearConfigError()
    {
        if (_configError is null)
        {
            return;
        }

        _configError = null;
        RefreshErrorState();
    }

    private void ClearConnectionError()
    {
        if (_connectionError is null)
        {
            return;
        }

        _connectionError = null;
        RefreshErrorState();
    }

    private void ClearOperationError()
    {
        if (_operationError is null)
        {
            return;
        }

        _operationError = null;
        RefreshErrorState();
    }

    private void ClearAllErrors()
    {
        _configLoadError = null;
        _configError = null;
        _connectionError = null;
        _catalogWarning = null;
        _operationError = null;
        RefreshErrorState();
    }

    private void SetCatalogWarning(string? message)
    {
        var compact = string.IsNullOrWhiteSpace(message) ? null : Compact(message);
        if (string.Equals(_catalogWarning, compact, StringComparison.Ordinal))
        {
            return;
        }

        _catalogWarning = compact;
        RefreshErrorState();
    }

    private void RefreshErrorState()
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(StatusPillText));
        OnPropertyChanged(nameof(ConnectionStatusText));
    }

    private void UpdateCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        RetryCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        BrowseFolderCommand.RaiseCanExecuteChanged();
        AddFallbackCommand.RaiseCanExecuteChanged();
        RemoveFallbackCommand.RaiseCanExecuteChanged();
        SetPrimaryModelCommand.RaiseCanExecuteChanged();
        ClearPrimaryModelCommand.RaiseCanExecuteChanged();
    }

    private static string Compact(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Unknown error.";
        }

        var compact = string.Join(
            " ",
            message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

        while (compact.Contains("  ", StringComparison.Ordinal))
        {
            compact = compact.Replace("  ", " ", StringComparison.Ordinal);
        }

        return compact.Length <= 600 ? compact : compact[..600] + "…";
    }
}
