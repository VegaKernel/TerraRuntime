using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;

namespace TerraRuntime.Application.Bots;

internal enum RuntimeBotBodyKind : byte
{
    Player = 0,
    Npc = 1
}

internal enum RuntimeBotMode : byte
{
    Idle = 0,
    Follow = 1,
    Guard = 2
}

internal enum RuntimeBotWeaponPolicy : byte
{
    Automatic = 0,
    Melee = 1,
    Gun = 2,
    Bow = 3
}

internal enum RuntimeBotClothingPreset : byte
{
    Classic = 0,
    Forest = 1,
    Crimson = 2,
    Monochrome = 3
}

internal enum RuntimeBotArmorPreset : byte
{
    None = 0,
    Wood = 1,
    Copper = 2,
    Iron = 3,
    Silver = 4,
    Gold = 5
}

internal readonly record struct RuntimeBotTarget(
    PlayerHandle Player,
    string DisplayName)
{
    public bool IsAssigned => Player.IsAssigned;
}

internal readonly record struct RuntimeBotConfiguration(
    RuntimeBotClothingPreset Clothing,
    RuntimeBotArmorPreset Armor,
    RuntimeBotMode Mode,
    RuntimeBotTarget Target,
    RuntimeBotBodyKind Body = RuntimeBotBodyKind.Player,
    NpcTypeId NpcType = default,
    RuntimeBotWeaponPolicy WeaponPolicy = RuntimeBotWeaponPolicy.Automatic,
    bool FlightEnabled = true,
    bool AutoPickup = true,
    bool AutoUseConsumables = true);

internal readonly record struct RuntimeBotCreateRequest(
    RuntimeBotBodyKind Body,
    NpcTypeId NpcType = default)
{
    public static RuntimeBotCreateRequest Player => new(RuntimeBotBodyKind.Player);

    public bool IsValid => Body switch
    {
        RuntimeBotBodyKind.Player => NpcType == default,
        RuntimeBotBodyKind.Npc => NpcType.IsAssigned,
        _ => false
    };
}

internal readonly record struct RuntimeBotSnapshot(
    int Id,
    ServerPlayerId ServerPlayerId,
    PlayerHandle Player,
    NpcHandle Npc,
    string Name,
    RuntimeBotConfiguration Configuration,
    bool TargetAvailable,
    bool PvpEnabled,
    bool IsStuck,
    long TeleportCount,
    DateTimeOffset UpdatedAtUtc);

internal sealed record RuntimeBotCreateCommand(
    RuntimeBotCreateRequest Request,
    TaskCompletionSource<RuntimeBotSnapshot?> Completion) : RuntimeCommand;

internal sealed record RuntimeBotConfigureCommand(
    int Id,
    RuntimeBotConfiguration Configuration,
    TaskCompletionSource<RuntimeBotSnapshot?> Completion) : RuntimeCommand;

internal sealed record RuntimeBotDespawnCommand(
    int Id,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

/// <summary>
/// Lock-protected detached read model for the terminal UI. Authoritative behavior remains owned by the world loop;
/// this cache intentionally stores only immutable bot configuration/status snapshots.
/// </summary>
internal sealed class RuntimeBotTelemetry
{
    private readonly object gate = new();
    private RuntimeBotSnapshot[] bots = [];

    public RuntimeBotSnapshot[] Capture()
    {
        lock (gate)
            return (RuntimeBotSnapshot[])bots.Clone();
    }

    public void Publish(ReadOnlySpan<RuntimeBotSnapshot> value)
    {
        var copy = value.ToArray();
        lock (gate)
            bots = copy;
    }
}

/// <summary>
/// Operator facade for primary-world runtime bots. Mutations are serialized through the authoritative world queue;
/// snapshot reads are detached and never inspect mutable runtime state from the UI thread.
/// </summary>
internal sealed class RuntimeBotOperations
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;
    private readonly RuntimeBotTelemetry telemetry;

    public RuntimeBotOperations(
        IGameCommandIngress<RuntimeCommand> ingress,
        RuntimeBotTelemetry telemetry)
    {
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
        this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    public RuntimeBotSnapshot[] CaptureSnapshot() => telemetry.Capture();

    public ValueTask<RuntimeBotSnapshot?> CreateAsync(CancellationToken cancellationToken = default) =>
        CreateAsync(RuntimeBotCreateRequest.Player, cancellationToken);

    public async ValueTask<RuntimeBotSnapshot?> CreateAsync(
        RuntimeBotCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.IsValid)
            return null;
        var completion = new TaskCompletionSource<RuntimeBotSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(GameCommandSourceId.System, new RuntimeBotCreateCommand(request, completion)))
            return null;
        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<RuntimeBotSnapshot?> ConfigureAsync(
        int id,
        RuntimeBotConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<RuntimeBotSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(GameCommandSourceId.System, new RuntimeBotConfigureCommand(id, configuration, completion)))
            return null;
        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> DespawnAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(GameCommandSourceId.System, new RuntimeBotDespawnCommand(id, completion)))
            return false;
        return await completion.Task.ConfigureAwait(false);
    }
}
