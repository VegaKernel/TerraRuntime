namespace TerraRuntime.Network;

/// <summary>
/// Optional sink-chain hint for a more precise connection-lifetime stop reason than the generic
/// rejection category can express. Implementations must return <see cref="TerrariaConnectionStopReason.None"/>
/// unless the current terminal sink outcome has a stable, connection-level meaning.
/// </summary>
public interface ITerrariaConnectionStopReasonSource
{
    TerrariaConnectionStopReason ConnectionStopReason { get; }
}
