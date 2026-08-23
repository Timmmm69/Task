using System.Windows.Input;

namespace Task.Desktop.ViewModels;

/// <summary>
/// ICommand implementation for one cancellable asynchronous operation. A command instance
/// never starts a second execution while the first one is running and never lets an exception
/// escape from WPF's async-void command boundary.
/// </summary>
public sealed class AsyncCommand : ICommand, IDisposable
{
    private readonly Func<object?, CancellationToken, global::System.Threading.Tasks.Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private CancellationTokenSource? _executionCancellation;
    private int _isExecuting;
    private bool _disposed;

    public AsyncCommand(
        Func<object?, CancellationToken, global::System.Threading.Tasks.Task> execute,
        Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>Raised for an unexpected command exception after it has been contained.</summary>
    public event Action<Exception>? ExecutionFailed;

    public bool IsExecuting => Volatile.Read(ref _isExecuting) != 0;

    public bool CanExecute(object? parameter) =>
        !_disposed && !IsExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        await ExecuteAsync(parameter).ConfigureAwait(true);
    }

    /// <summary>
    /// Executes the command for tests and non-ICommand callers. Returns false when another
    /// execution already owns the command or the command is disabled.
    /// </summary>
    public async global::System.Threading.Tasks.Task<bool> ExecuteAsync(
        object? parameter = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameter)
            || Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            return false;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionCancellation = linkedCancellation;
        RaiseCanExecuteChanged();

        try
        {
            await _execute(parameter, linkedCancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            try
            {
                ExecutionFailed?.Invoke(exception);
            }
            catch
            {
                // The async-void ICommand boundary must remain contained even when a UI
                // notification subscriber fails while the window is being torn down.
            }
        }
        finally
        {
            _executionCancellation = null;
            Volatile.Write(ref _isExecuting, 0);
            RaiseCanExecuteChanged();
        }

        return true;
    }

    public void Cancel() => _executionCancellation?.Cancel();

    public void RaiseCanExecuteChanged()
    {
        try
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // WPF may detach command targets while a cancellation is completing.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cancel();
        RaiseCanExecuteChanged();
    }
}
