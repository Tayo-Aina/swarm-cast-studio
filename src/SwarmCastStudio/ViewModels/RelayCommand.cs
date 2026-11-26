using System.Diagnostics;
using System.Windows.Input;

namespace SwarmCastStudio.ViewModels;

/// <summary>
/// ICommand implementation supporting synchronous and asynchronous handlers.
/// Async handlers are awaited internally and never allowed to bubble to the dispatcher.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?>? _execute;
    private readonly Func<object?, Task>? _executeAsync;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _executeAsync = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        try
        {
            return _canExecute?.Invoke(parameter) ?? true;
        }
        catch
        {
            return false;
        }
    }

    public void Execute(object? parameter)
    {
        try
        {
            if (_executeAsync is not null)
            {
                _ = ExecuteAsync(parameter);
                return;
            }

            _execute?.Invoke(parameter);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RelayCommand] {ex}");
        }
    }

    private async Task ExecuteAsync(object? parameter)
    {
        try
        {
            await _executeAsync!(parameter);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RelayCommand] async handler failed: {ex}");
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
