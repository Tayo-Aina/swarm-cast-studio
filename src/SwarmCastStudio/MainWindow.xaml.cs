using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwarmCastStudio.Models;
using SwarmCastStudio.ViewModels;

namespace SwarmCastStudio;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        UpdateMaximizeGlyph();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _viewModel.ReportUnexpected(ex);
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeGlyph();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.ConfirmClose())
        {
            e.Cancel = true;
        }
    }

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeGlyph is null)
        {
            return;
        }

        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void ModelList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not ListBox list)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(list, source) is ListBoxItem item
            && item.DataContext is OpenCodeModel model)
        {
            // Highlight only here; the command is the explicit apply action.
            _viewModel.SelectedModel = model;
            if (_viewModel.SetPrimaryModelCommand.CanExecute(null))
            {
                _viewModel.SetPrimaryModelCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }
}
