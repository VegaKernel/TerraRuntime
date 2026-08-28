using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime;

/// <summary>
/// Runtime-owned routing boundary between authoritative entity state and outbound fan-out.
/// External hosts can only toggle the control surface; the active policy remains an internal
/// TerraRuntime implementation detail.
/// </summary>
internal sealed class RuntimeInterestRouter
{
    private readonly IInterestManagementControl _control;
    private readonly IRuntimeInterestPolicy _enabledPolicy;

    public RuntimeInterestRouter(
        IInterestManagementControl control,
        IRuntimeInterestPolicy? enabledPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        _control = control;
        _enabledPolicy = enabledPolicy ?? PassthroughInterestPolicy.Instance;
    }

    public bool ShouldRelayPlayerMovement(
        in RuntimePlayerInterestState observer,
        in RuntimePlayerInterestState subject)
    {
        if (!_control.IsEnabled)
            return true;

        return _enabledPolicy.ShouldObservePlayer(in observer, in subject);
    }
}

/// <summary>
/// Internal spatial input prepared by the synchronization layer. Unknown positions deliberately
/// remain representable so a future spatial policy can fail open until it has enough state to make
/// a safe visibility decision.
/// </summary>
internal readonly record struct RuntimePlayerInterestState(
    PlayerSlotId Slot,
    bool HasPosition,
    float PositionX,
    float PositionY);

internal interface IRuntimeInterestPolicy
{
    bool ShouldObservePlayer(
        in RuntimePlayerInterestState observer,
        in RuntimePlayerInterestState subject);
}

/// <summary>
/// Foundation policy used until enter/leave snapshots, hysteresis and forced resync semantics are
/// implemented. Enabling interest management today therefore changes routing ownership, not
/// player-visible network behavior.
/// </summary>
internal sealed class PassthroughInterestPolicy : IRuntimeInterestPolicy
{
    public static PassthroughInterestPolicy Instance { get; } = new();

    private PassthroughInterestPolicy()
    {
    }

    public bool ShouldObservePlayer(
        in RuntimePlayerInterestState observer,
        in RuntimePlayerInterestState subject) => true;
}
