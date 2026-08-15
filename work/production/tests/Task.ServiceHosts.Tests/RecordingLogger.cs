using Microsoft.Extensions.Logging;

namespace Task.ServiceHosts.Tests;

public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly object _gate = new();
    private readonly List<string> _messages = [];
    private TaskCompletionSource<bool> _messageAdded = CreateSignal();

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        TaskCompletionSource<bool> signal;
        lock (_gate)
        {
            _messages.Add(formatter(state, exception));
            signal = _messageAdded;
            _messageAdded = CreateSignal();
        }

        signal.TrySetResult(true);
    }

    public async Task<IReadOnlyList<string>> WaitUntilAsync(Func<IReadOnlyList<string>, bool> predicate, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);

        while (true)
        {
            IReadOnlyList<string> snapshot;
            global::System.Threading.Tasks.Task nextMessage;
            lock (_gate)
            {
                snapshot = _messages.ToArray();
                if (predicate(snapshot))
                {
                    return snapshot;
                }

                nextMessage = _messageAdded.Task;
            }

            try
            {
                await nextMessage.WaitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                return Messages;
            }
        }
    }

    private static TaskCompletionSource<bool> CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
