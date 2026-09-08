using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using TerraRuntime.Transport;

namespace TerraRuntime.Application;

/// <summary>One runtime-only Level2 worker lease. Host admission owns the number of leases.
/// No player socket or gameplay data plane is admitted by this lifecycle foundation.</summary>
internal sealed class SandboxSupervisor : IAsyncDisposable
{
    private readonly Process process;
    private readonly NamedPipeServerStream pipe;
    private readonly SandboxWorkerDescriptor descriptor;
    private readonly Guid bootId;
    private readonly SemaphoreSlim requests = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Task heartbeat = Task.CompletedTask;
    private SandboxWorkerSnapshot snapshot;
    private Exception? fault;
    private int stopping;
    private int disposed;

    private SandboxSupervisor(Process process, NamedPipeServerStream pipe, SandboxWorkerDescriptor descriptor,
        Guid bootId, SandboxWorkerSnapshot snapshot)
    {
        this.process = process;
        this.pipe = pipe;
        this.descriptor = descriptor;
        this.bootId = bootId;
        this.snapshot = snapshot;
    }

    public SandboxWorkerSnapshot Snapshot => Volatile.Read(ref snapshot);
    public Exception? Fault => Volatile.Read(ref fault);

    public static async Task<SandboxSupervisor> StartAsync(string executable, SandboxWorkerDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new ArgumentException("Worker executable must be an existing absolute path.", nameof(executable));
        byte[] createPayload = SandboxWorkerProtocol.Encode(descriptor);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(SandboxWorkerProtocol.StartupTimeout);
        string pipeName = Guid.NewGuid().ToString("N");
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        info.ArgumentList.Add(SandboxWorkerProgram.Option);
        info.ArgumentList.Add(pipeName);
        info.Environment[SandboxWorkerProgram.SecretEnvironmentVariable] = Convert.ToHexString(secret);
        Process? process = null;
        Task? connected = null;
        Task? exited = null;
        try
        {
            startup.Token.ThrowIfCancellationRequested();
            process = Process.Start(info) ?? throw new InvalidOperationException("Worker process was not started.");
            connected = pipe.WaitForConnectionAsync(startup.Token);
            exited = process.WaitForExitAsync(startup.Token);
            if (await Task.WhenAny(connected, exited).ConfigureAwait(false) == exited)
            {
                await exited.ConfigureAwait(false);
                throw new IOException($"Worker exited before connecting (exit code {process.ExitCode}).");
            }
            await connected.ConfigureAwait(false);
            byte[] challenge = RandomNumberGenerator.GetBytes(32);
            BoundedControlFrame auth = await ExchangeAsync(pipe, MessageKind.Handshake, SandboxWorkerProtocol.Authenticate,
                challenge, startup.Token).ConfigureAwait(false);
            Guid boot = SandboxWorkerProtocol.ValidateWorkerProof(secret, challenge, auth.Payload, process.Id);
            BoundedControlFrame confirmed = await ExchangeAsync(pipe, MessageKind.Handshake, SandboxWorkerProtocol.Authenticate,
                SandboxWorkerProtocol.AuthenticationTag(secret, 2, auth.Payload), startup.Token).ConfigureAwait(false);
            if (confirmed.Payload.Length != 0) throw new InvalidDataException("Unexpected authentication confirmation.");
            BoundedControlFrame ready = await ExchangeAsync(pipe, MessageKind.Request, SandboxWorkerProtocol.Create,
                createPayload, startup.Token).ConfigureAwait(false);
            SandboxWorkerSnapshot initial = SandboxWorkerProtocol.DecodeSnapshot(ready.Payload);
            ValidateSnapshot(initial, descriptor, boot, process.Id, WorldRuntimeLifecycle.Running);
            var supervisor = new SandboxSupervisor(process, pipe, descriptor, boot, initial);
            supervisor.heartbeat = supervisor.RunHeartbeatAsync();
            return supervisor;
        }
        catch
        {
            pipe.Dispose();
            if (process is not null) { KillOwnedProcess(process); process.Dispose(); }
            throw;
        }
        finally
        {
            startup.Cancel();
            // Observe the losing race as well: closing a not-yet-connected pipe can fault its task.
            // The winning failure already propagates through the catch above; these are cleanup completions.
            await Task.WhenAll(connected ?? Task.CompletedTask, exited ?? Task.CompletedTask)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            CryptographicOperations.ZeroMemory(secret);
            info.Environment.Remove(SandboxWorkerProgram.SecretEnvironmentVariable);
        }
    }

    public async Task<SandboxWorkerSnapshot> ProbeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref stopping) != 0) throw new InvalidOperationException("Worker is stopping.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(SandboxWorkerProtocol.RequestTimeout);
        await requests.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            BoundedControlFrame response = await ExchangeAsync(pipe, MessageKind.Heartbeat, SandboxWorkerProtocol.Snapshot,
                ReadOnlyMemory<byte>.Empty, deadline.Token).ConfigureAwait(false);
            SandboxWorkerSnapshot latest = SandboxWorkerProtocol.DecodeSnapshot(response.Payload);
            ValidateSnapshot(latest, descriptor, bootId, process.Id, WorldRuntimeLifecycle.Running);
            Volatile.Write(ref snapshot, latest);
            return latest;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref fault, exception);
            pipe.Dispose(); // A canceled partial exchange is not a reusable control session.
            KillOwnedProcess(process);
            throw;
        }
        finally { requests.Release(); }
    }

    private async Task RunHeartbeatAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
                _ = await ProbeAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (InvalidOperationException) when (Volatile.Read(ref stopping) != 0 || Volatile.Read(ref disposed) != 0) { }
        catch (Exception exception) { Volatile.Write(ref fault, exception); }
    }

    public async Task<bool> StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref stopping, 1) != 0) return process.HasExited;
        // Let an in-flight heartbeat finish before canceling its timer: canceling an exchange deliberately
        // retires the channel and would otherwise turn an ordinary stop into a forced kill.
        bool acquired = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(SandboxWorkerProtocol.RequestTimeout);
        try
        {
            await requests.WaitAsync(deadline.Token).ConfigureAwait(false);
            acquired = true;
            lifetime.Cancel();
            if (process.HasExited) return process.ExitCode == 0;
            BoundedControlFrame response = await ExchangeAsync(pipe, MessageKind.Request, SandboxWorkerProtocol.Stop,
                ReadOnlyMemory<byte>.Empty, deadline.Token).ConfigureAwait(false);
            SandboxWorkerSnapshot stopped = SandboxWorkerProtocol.DecodeSnapshot(response.Payload);
            ValidateSnapshot(stopped, descriptor, bootId, process.Id, WorldRuntimeLifecycle.Stopped);
            Volatile.Write(ref snapshot, stopped);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref fault, exception);
            KillOwnedProcess(process);
            return false;
        }
        finally
        {
            lifetime.Cancel();
            if (acquired) requests.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { _ = await StopAsync(CancellationToken.None).ConfigureAwait(false); }
        finally
        {
            lifetime.Cancel();
            await heartbeat.ConfigureAwait(false);
            pipe.Dispose();
            KillOwnedProcess(process);
            process.Dispose();
            lifetime.Dispose();
            requests.Dispose();
        }
    }

    private static void ValidateSnapshot(SandboxWorkerSnapshot value, SandboxWorkerDescriptor expected, Guid boot,
        int pid, WorldRuntimeLifecycle lifecycle)
    {
        if (value.RuntimeId != expected.RuntimeId || value.SessionId != expected.SessionId || value.ProcessInstanceId != boot ||
            value.ProcessId != pid || value.Lifecycle != lifecycle || value.Tick <= 0 || value.TargetTicksPerSecond != 60)
            throw new InvalidDataException("Worker runtime identity/state mismatch.");
    }

    private static async Task<BoundedControlFrame> ExchangeAsync(Stream pipe, MessageKind kind, uint type,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        Guid correlation = Guid.NewGuid();
        await BoundedControlFrame.WriteAsync(pipe, kind, type, correlation, payload,
            SandboxWorkerProtocol.MaximumPayloadBytes, cancellationToken).ConfigureAwait(false);
        BoundedControlFrame response = await BoundedControlFrame.ReadAsync(pipe,
            SandboxWorkerProtocol.MaximumPayloadBytes, cancellationToken).ConfigureAwait(false);
        MessageKind expectedKind = kind == MessageKind.Handshake ? MessageKind.Handshake : MessageKind.Response;
        if (response.Header.Kind != expectedKind || response.Header.MessageType != type || response.Header.CorrelationId != correlation)
            throw new InvalidDataException("Worker response does not match the pending request.");
        return response;
    }

    private static void KillOwnedProcess(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill();
            _ = process.WaitForExit(5000);
        }
        catch (InvalidOperationException) { }
    }
}
