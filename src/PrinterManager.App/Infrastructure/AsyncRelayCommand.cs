using System.Windows;
using System.Windows.Input;
using PrinterManager.App.Services;

namespace PrinterManager.App.Infrastructure;

/// <summary>Comando assíncrono que impede reexecução enquanto roda e exibe erros ao usuário.</summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> execute;
    private readonly Predicate<object?>? canExecute;
    private bool running;

    public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        running = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await execute(parameter);
        }
        catch (Exception ex)
        {
            Dialogs.ShowError(ex);
        }
        finally
        {
            running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

/// <summary>Comando síncrono com tratamento de erro padronizado.</summary>
public sealed class UiCommand : ICommand
{
    private readonly Action<object?> execute;
    private readonly Predicate<object?>? canExecute;

    public UiCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public UiCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        try
        {
            execute(parameter);
        }
        catch (Exception ex)
        {
            Dialogs.ShowError(ex);
        }
    }
}

public static class Dialogs
{
    public static void ShowError(Exception ex)
    {
        if (ex is not (UnauthorizedAccessException or InvalidOperationException))
            FileLog.Error("Erro em comando da interface", ex);
        Show(ex.Message, "Gestão de Impressoras", MessageBoxButton.OK,
            ex is UnauthorizedAccessException ? MessageBoxImage.Stop : MessageBoxImage.Warning);
    }

    public static void Info(string message) =>
        Show(message, "Gestão de Impressoras", MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool Confirm(string message) =>
        Show(message, "Confirmação", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static Window? Owner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    private static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner() is { IsLoaded: true } owner
            ? MessageBox.Show(owner, text, caption, buttons, image)
            : MessageBox.Show(text, caption, buttons, image);
}
