using System.Windows.Input;

namespace Studio.Ide;

public sealed class RelayCommand : ICommand
{
    /// <summary>Raised by <see cref="InvalidateAll"/>: every command re-evaluates CanExecute
    /// (replaces the CommandManager.RequerySuggested of WPF, which Avalonia does not have).</summary>
    static event EventHandler? RequerySuggested;

    readonly Action<object?> _execute;
    readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        if (canExecute != null) RequerySuggested += (_, e) => CanExecuteChanged?.Invoke(this, e);
    }

    /// <summary>Asks all the commands to refresh their enabled state (UI thread).</summary>
    public static void InvalidateAll() => RequerySuggested?.Invoke(null, EventArgs.Empty);

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) _execute(parameter); }

    public event EventHandler? CanExecuteChanged;
}
