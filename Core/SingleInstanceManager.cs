using System.Threading;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

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

    public bool IsPrimaryInstance { get; private set; }
    public event Action<AppCommand>? CommandReceived;

    public bool WaitForPrimaryInstance(TimeSpan timeout)
    {
        if (IsPrimaryInstance)
            return true;

        try
        {
            IsPrimaryInstance = _mutex.WaitOne(timeout);
            return IsPrimaryInstance;
        }
        catch (AbandonedMutexException)
        {
            IsPrimaryInstance = true;
            return true;
        }
    }

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

    /// <summary>
    /// Creates the command pipe with an explicit DACL granting only the current user and
    /// LocalSystem.
    ///
    /// The Windows default measured on v1.0.3 was
    /// <c>D:(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;BA)(A;;FR;;;WD)(A;;FR;;;AN)</c>: read access for
    /// Everyone and for ANONYMOUS LOGON. That did not permit another user to send commands, because
    /// the server is inbound and sending requires write access, but it did let any local process
    /// open the single pipe instance and it extended reach to anonymous logons for no reason. This
    /// application relays a command that types clipboard contents, so the pipe is scoped to its
    /// owner instead of relying on that default.
    /// </summary>
    private static NamedPipeServerStream CreateRestrictedPipe()
    {
        var security = new PipeSecurity();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();

        SecurityIdentifier owner = identity.User ?? new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            inBufferSize: 0, outBufferSize: 0, security);
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
                using NamedPipeServerStream pipe = CreateRestrictedPipe();
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
