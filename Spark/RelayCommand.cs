using System.Windows.Input;
///////////////////////////////////////////////
namespace Spark;

sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    /// <summary>A no-op command that is always disabled.</summary>
    public static readonly RelayCommand Empty = new(_ => { }, _ => false);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
}
