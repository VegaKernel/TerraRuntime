using System.Text.Json;
using System.Text.Json.Serialization;
using System.Buffers.Binary;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal enum SandboxWorkerSourceKind : byte { Generated = 1, WorldFile = 2 }

/// <summary>Version-one runtime-only worker creation. Unsupported sources/modules are not implicit defaults.</summary>
internal sealed record SandboxWorkerDescriptor(
    Guid RuntimeId, Guid SessionId, SandboxWorkerSourceKind SourceKind, string Source,
    string? WorldName, ulong Seed, int WidthTiles, int HeightTiles,
    WorldGenerationGameMode GameMode, WorldGenerationEvil Evil, string? SeedText, string? Sha256, int MaxPlayers)
{
    [JsonIgnore]
    public WorldRuntimeIdentity Identity => new(new WorldRuntimeId(RuntimeId), new WorldSessionId(SessionId));

    public SandboxWorldSource ValidateAndCreateSource()
    {
        _ = Identity;
        if (MaxPlayers is < 1 or > 255 || string.IsNullOrWhiteSpace(Source) || Source.Length > 4096 ||
            SeedText?.Length > 1024)
            throw new InvalidDataException("Invalid worker descriptor limits.");
        if (SourceKind == SandboxWorkerSourceKind.Generated)
        {
            if (Sha256 is not null || WorldName is null ||
                (long)WidthTiles * HeightTiles > ServerWorldLoadPolicy.CreateLimits().MaxTileCount)
                throw new InvalidDataException("Invalid generated worker source.");
            var source = new SandboxWorldSource.Generated(new WorldGeneratorId(Source), WorldName, Seed,
                WidthTiles, HeightTiles, new WorldGenerationOptions(GameMode, Evil), SeedText);
            source.ToRequest().Validate();
            return source;
        }
        if (SourceKind == SandboxWorkerSourceKind.WorldFile)
        {
            if (!Path.IsPathFullyQualified(Source) || !Source.EndsWith(".wld", StringComparison.OrdinalIgnoreCase) ||
                Sha256 is null || Sha256.Length != 64 || WorldName is not null || SeedText is not null ||
                WidthTiles != 0 || HeightTiles != 0 || Seed != 0 || GameMode != 0 || Evil != 0)
                throw new InvalidDataException("Invalid world-file worker source.");
            _ = Convert.FromHexString(Sha256);
            return new SandboxWorldSource.WorldFile(Source);
        }
        throw new InvalidDataException("Unsupported worker source kind.");
    }
}

internal sealed record SandboxWorkerSnapshot(Guid RuntimeId, Guid SessionId, Guid ProcessInstanceId,
    int ProcessId, WorldRuntimeLifecycle Lifecycle, long Tick, int TargetTicksPerSecond);

internal static class SandboxWorkerProtocol
{
    public const int MaximumPayloadBytes = 8192;
    public const uint Authenticate = 1;
    public const uint Create = 2;
    public const uint Snapshot = 3;
    public const uint Stop = 4;
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan LivenessTimeout = TimeSpan.FromSeconds(15);

    public static byte[] CreateWorkerProof(byte[] secret, byte[] challenge, Guid boot, int pid)
    {
        if (secret.Length != 32 || challenge.Length != 32 || boot == Guid.Empty || pid <= 0)
            throw new InvalidDataException("Invalid worker authentication inputs.");
        byte[] proof = new byte[52];
        boot.TryWriteBytes(proof.AsSpan(0, 16));
        BinaryPrimitives.WriteInt32LittleEndian(proof.AsSpan(16), pid);
        byte[] input = new byte[52];
        challenge.CopyTo(input, 0);
        proof.AsSpan(0, 20).CopyTo(input.AsSpan(32));
        AuthenticationTag(secret, 1, input).CopyTo(proof, 20);
        return proof;
    }

    public static Guid ValidateWorkerProof(byte[] secret, byte[] challenge, byte[] proof, int pid)
    {
        if (proof.Length != 52 || BinaryPrimitives.ReadInt32LittleEndian(proof.AsSpan(16)) != pid)
            throw new InvalidDataException("Invalid worker process proof.");
        Guid boot = new(proof.AsSpan(0, 16));
        if (boot == Guid.Empty || !CryptographicOperations.FixedTimeEquals(proof, CreateWorkerProof(secret, challenge, boot, pid)))
            throw new InvalidDataException("Worker authentication failed.");
        return boot;
    }

    public static byte[] AuthenticationTag(byte[] secret, byte purpose, byte[] input)
    {
        byte[] tagged = new byte[input.Length + 1];
        tagged[0] = purpose;
        input.CopyTo(tagged, 1);
        return HMACSHA256.HashData(secret, tagged);
    }

    public static byte[] Encode(SandboxWorkerDescriptor descriptor)
    {
        _ = descriptor.ValidateAndCreateSource();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(descriptor, SandboxWorkerJsonContext.Default.SandboxWorkerDescriptor);
        if (payload.Length > MaximumPayloadBytes) throw new InvalidDataException("Worker descriptor too large.");
        return payload;
    }
    public static SandboxWorkerDescriptor DecodeDescriptor(byte[] payload) =>
        JsonSerializer.Deserialize(payload, SandboxWorkerJsonContext.Default.SandboxWorkerDescriptor) ??
        throw new InvalidDataException("Missing worker descriptor.");
    public static byte[] Encode(SandboxWorkerSnapshot snapshot) =>
        JsonSerializer.SerializeToUtf8Bytes(snapshot, SandboxWorkerJsonContext.Default.SandboxWorkerSnapshot);
    public static SandboxWorkerSnapshot DecodeSnapshot(byte[] payload) =>
        JsonSerializer.Deserialize(payload, SandboxWorkerJsonContext.Default.SandboxWorkerSnapshot) ??
        throw new InvalidDataException("Missing worker snapshot.");
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    MaxDepth = 8, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SandboxWorkerDescriptor))]
[JsonSerializable(typeof(SandboxWorkerSnapshot))]
internal sealed partial class SandboxWorkerJsonContext : JsonSerializerContext;
