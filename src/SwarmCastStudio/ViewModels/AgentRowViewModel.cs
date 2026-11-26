using System.Collections.ObjectModel;
using System.Collections.Specialized;
using SwarmCastStudio.Models;

namespace SwarmCastStudio.ViewModels;

/// <summary>
/// Editable projection of one swarm agent definition plus its saved and live state.
/// </summary>
public sealed class AgentRowViewModel : ObservableBase
{
    private bool _isEnabled = true;
    private string? _model;
    private double _temperature = 0.7;
    private bool _hasTemperature;
    private bool _gateSatisfied;
    private bool _isLive;
    private string? _liveModelRef;

    public AgentRowViewModel(AgentDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Fallbacks = new ObservableCollection<string>();
        Fallbacks.CollectionChanged += OnFallbacksChanged;
        _gateSatisfied = definition.Gate == AgentGate.None;
    }

    public AgentDefinition Definition { get; }

    public string Name => Definition.Name;

    public string DisplayName => Definition.DisplayName;

    public string Category => Definition.Category;

    public string Description => Definition.Description;

    public bool IsCore => Definition.Core;

    public AgentGate Gate => Definition.Gate;

    public ObservableCollection<string> Fallbacks { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => Set(ref _isEnabled, value);
    }

    public string? Model
    {
        get => _model;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (!Set(ref _model, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(ModelDisplay));
            OnPropertyChanged(nameof(HasModel));
            OnPropertyChanged(nameof(LiveStatusText));
        }
    }

    public double Temperature
    {
        get => _temperature;
        set
        {
            var clamped = Math.Clamp(value, 0d, 2d);
            Set(ref _temperature, clamped);
        }
    }

    public bool HasTemperature
    {
        get => _hasTemperature;
        set => Set(ref _hasTemperature, value);
    }

    /// <summary>True when the feature gate that registers this role is enabled.</summary>
    public bool GateSatisfied
    {
        get => _gateSatisfied;
        set
        {
            if (!Set(ref _gateSatisfied, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(IsGateLocked));
            OnPropertyChanged(nameof(GateNote));
        }
    }

    public bool CanEdit => GateSatisfied;

    public bool IsGateLocked => !GateSatisfied;

    public bool IsLive
    {
        get => _isLive;
        set
        {
            if (!Set(ref _isLive, value))
            {
                return;
            }

            OnPropertyChanged(nameof(LiveStatusText));
        }
    }

    public string? LiveModelRef
    {
        get => _liveModelRef;
        set
        {
            if (!Set(ref _liveModelRef, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ModelDisplay));
            OnPropertyChanged(nameof(LiveStatusText));
        }
    }

    public string ModelDisplay => Model
        ?? (LiveModelRef is not null ? $"{LiveModelRef} · inherited" : "No model assigned");

    public bool HasModel => !string.IsNullOrWhiteSpace(Model);

    public bool HasFallbacks => Fallbacks.Count > 0;

    public string GateFeatureName => Gate switch
    {
        AgentGate.UiReview => "ui_review",
        AgentGate.DesignDocs => "design_docs",
        AgentGate.CouncilGeneral => "council.general",
        _ => string.Empty
    };

    public string GateNote => Gate switch
    {
        AgentGate.UiReview => "Enable the UI review gate to unlock this role.",
        AgentGate.DesignDocs => "Enable the design docs gate to unlock this role.",
        AgentGate.CouncilGeneral => "Enable the council.general gate to unlock this role.",
        _ => string.Empty
    };

    public string LiveStatusText
    {
        get
        {
            var hasConfiguredModel = !string.IsNullOrWhiteSpace(Model);
            var hasLiveModel = !string.IsNullOrWhiteSpace(LiveModelRef);

            if (IsLive)
            {
                if (hasLiveModel && !hasConfiguredModel)
                {
                    return $"Inherits the OpenCode session model · {LiveModelRef}";
                }

                if (hasLiveModel)
                {
                    return $"Live resolved model · {LiveModelRef}";
                }

                return hasConfiguredModel
                    ? $"Live in OpenCode · session-selected model (configured: {Model})"
                    : "No model resolved";
            }

            if (!hasConfiguredModel && !hasLiveModel)
            {
                return "No model resolved";
            }

            return "Not currently registered in OpenCode";
        }
    }

    private void OnFallbacksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasFallbacks));
    }
}
