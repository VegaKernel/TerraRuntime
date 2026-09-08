using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Transport;

namespace TerraRuntime.Application;

internal static class SandboxWorkerProgram
{
    internal const string Option = "--sandbox-worker";
    internal const string SecretEnvironmentVariable = "TERRARUNTIME_WORKER_BOOT_SECRET";

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (!args.Contains(Option, StringComparer.Ordinal)) return false;
        // A worker never enters normal server startup or opens a public listener.
        if (args.Length != 2 || args[0] != Option || !Guid.TryParseExact(args[1], "N", out _))
        { exitCode = 64; return true; }
        string? secret = Environment.GetEnvironmentVariable(SecretEnvironmentVariable);
        Environment.SetEnvironmentVariable(SecretEnvironmentVariable, null);
        if (secret is null || secret.Length != 64)
        { exitCode = 64; return true; }
        try
        {
            byte[] token = Convert.FromHexString(secret);
            try { exitCode = RunAsync(args[1], token).GetAwaiter().GetResult(); }
            finally { CryptographicOperations.ZeroMemory(token); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            OperationCanceledException or ArgumentException or JsonException or FormatException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Sandbox worker rejected/stopped: {exception.GetType().Name}.");
            exitCode = 65;
        }
        return true;
    }

    private static async Task<int> RunAsync(string pipeName, byte[] token)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var startup = new CancellationTokenSource(SandboxWorkerProtocol.StartupTimeout);
        await pipe.ConnectAsync(startup.Token).ConfigureAwait(false);
        Guid bootId = Guid.NewGuid();
        BoundedControlFrame auth = await ReadAsync(pipe, startup.Token).ConfigureAwait(false);
        Require(auth, MessageKind.Handshake, SandboxWorkerProtocol.Authenticate, payloadLength: 32);
        byte[] proof = SandboxWorkerProtocol.CreateWorkerProof(token, auth.Payload, bootId, Environment.ProcessId);
        await ReplyAsync(pipe, auth, MessageKind.Handshake, proof, startup.Token).ConfigureAwait(false);
        BoundedControlFrame confirmation = await ReadAsync(pipe, startup.Token).ConfigureAwait(false);
        Require(confirmation, MessageKind.Handshake, SandboxWorkerProtocol.Authenticate, payloadLength: 32);
        if (!CryptographicOperations.FixedTimeEquals(confirmation.Payload, SandboxWorkerProtocol.AuthenticationTag(token, 2, proof)))
            throw new InvalidDataException("Supervisor authentication failed.");
        await ReplyAsync(pipe, confirmation, MessageKind.Handshake, [], startup.Token).ConfigureAwait(false);

        BoundedControlFrame create = await ReadAsync(pipe, startup.Token).ConfigureAwait(false);
        Require(create, MessageKind.Request, SandboxWorkerProtocol.Create);
        SandboxWorkerDescriptor descriptor = SandboxWorkerProtocol.DecodeDescriptor(create.Payload);
        SandboxWorldSource source = descriptor.ValidateAndCreateSource();
        var materializer = new SandboxWorldMaterializer(BuiltInWorldGeneratorSource.Instance, ServerWorldLoadPolicy.CreateLimits());
        byte[] digest = descriptor.Sha256 is null ? [] : Convert.FromHexString(descriptor.Sha256);
        SandboxWorldMaterializationResult prepared = await Task.Run(
            () => materializer.Materialize(source, startup.Token, digest), startup.Token).ConfigureAwait(false);
        if (!prepared.Succeeded)
            throw new InvalidDataException("Worker world materialization failed: " + prepared.Status);
        startup.Token.ThrowIfCancellationRequested();
        using var runtime = new WorldRuntime(descriptor.Identity, source, prepared.World!, prepared.Bootstrap!,
            new InterestManagementControl(), new WorldRuntimeOptions { MaxPlayers = descriptor.MaxPlayers });
        runtime.Start();
        while (runtime.GameLoop.Snapshot.Tick == 0 && runtime.Lifecycle == WorldRuntimeLifecycle.Running)
            await Task.Delay(10, startup.Token).ConfigureAwait(false);
        if (runtime.Lifecycle != WorldRuntimeLifecycle.Running)
            throw new InvalidOperationException("Worker runtime failed during startup.");
        await ReplyAsync(pipe, create, MessageKind.Response, Capture(runtime, bootId), startup.Token).ConfigureAwait(false);

        while (true)
        {
            using var deadline = new CancellationTokenSource(SandboxWorkerProtocol.LivenessTimeout);
            BoundedControlFrame request = await ReadAsync(pipe, deadline.Token).ConfigureAwait(false);
            if (request.Header.MessageType == SandboxWorkerProtocol.Stop)
            {
                Require(request, MessageKind.Request, SandboxWorkerProtocol.Stop, 0);
                if (!await runtime.StopAsync(TimeSpan.FromSeconds(5), captureFinalSave: false).ConfigureAwait(false))
                    throw new InvalidOperationException("Worker stop failed.");
                await ReplyAsync(pipe, request, MessageKind.Response, Capture(runtime, bootId), deadline.Token).ConfigureAwait(false);
                return 0;
            }
            Require(request, MessageKind.Heartbeat, SandboxWorkerProtocol.Snapshot, 0);
            if (runtime.Lifecycle != WorldRuntimeLifecycle.Running)
                throw new InvalidOperationException("Worker runtime faulted.");
            await ReplyAsync(pipe, request, MessageKind.Response, Capture(runtime, bootId), deadline.Token).ConfigureAwait(false);
        }
    }

    private static byte[] Capture(WorldRuntime runtime, Guid bootId) => SandboxWorkerProtocol.Encode(
        new SandboxWorkerSnapshot(runtime.Identity.RuntimeId.Value, runtime.Identity.SessionId.Value, bootId,
            Environment.ProcessId, runtime.Lifecycle, runtime.GameLoop.Snapshot.Tick, runtime.Options.TargetTicksPerSecond));

    private static void Require(in BoundedControlFrame frame, MessageKind kind, uint messageType, int? payloadLength = null)
    {
        if (frame.Header.Kind != kind || frame.Header.MessageType != messageType || frame.Header.CorrelationId == Guid.Empty ||
            (payloadLength.HasValue && frame.Payload.Length != payloadLength.Value))
            throw new InvalidDataException("Unexpected worker control message.");
    }
    private static ValueTask<BoundedControlFrame> ReadAsync(Stream pipe, CancellationToken token) =>
        BoundedControlFrame.ReadAsync(pipe, SandboxWorkerProtocol.MaximumPayloadBytes, token);
    private static ValueTask ReplyAsync(Stream pipe, in BoundedControlFrame request, MessageKind kind, byte[] payload, CancellationToken token) =>
        BoundedControlFrame.WriteAsync(pipe, kind, request.Header.MessageType, request.Header.CorrelationId,
            payload, SandboxWorkerProtocol.MaximumPayloadBytes, token);
}
