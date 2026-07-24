using System.Threading;
using System.IO.Pipes;

namespace TypeyTypey;

internal sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = "TypeyTypey.SingleInstance.4D39A83C-52EC-4620-BB73-0265522DF85E";
    private const string PipeName = "TypeyTypey.Commands.4D39A83C-52EC-4620-BB73-0265522DF85E";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;

    public SingleInstanceManager()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }
    public event Action<AppCommand>? CommandReceived;

    public static bool SendCommand(AppCommand command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.None);
            pipe.Connect(1_500);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch (IOException) { return false; }
        catch (TimeoutException) { return false; }
    }

    public void Listen()
    {
        if (!IsPrimaryInstance || _listener is not null)
            return;

        _listener = Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(_cancellation.Token);
                using var reader = new StreamReader(pipe);
                string? raw = await reader.ReadLineAsync(_cancellation.Token);
                if (Enum.TryParse(raw, ignoreCase: true, out AppCommand command) && command != AppCommand.None)
                    CommandReceived?.Invoke(command);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) when (!_cancellation.IsCancellationRequested) { }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        _cancellation.Dispose();
        if (IsPrimaryInstance)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
