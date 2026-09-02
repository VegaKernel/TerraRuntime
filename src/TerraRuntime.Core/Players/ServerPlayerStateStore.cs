using TerraRuntime.Core;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

/// <summary>
/// Authoritative state for runtime-owned players. Identity ownership is validated against
/// <see cref="ServerPlayerSlotRegistry"/> on every operation; no transport connection is accepted anywhere in
/// this API, so client packet ingress cannot become an alternate writer by constructing a <see cref="ConnectionHandle"/>.
/// </summary>
public sealed partial class ServerPlayerStateStore
{
    private readonly ServerPlayerSlotRegistry identities;
    private readonly ServerPlayerRuntimeState?[] states;

    public ServerPlayerStateStore(ServerPlayerSlotRegistry identities, int capacity)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, byte.MaxValue);
        this.identities = identities;
        states = new ServerPlayerRuntimeState?[capacity];
    }

    /// <summary>Maximum number of server-owned player states retained by this store.</summary>
    public int Capacity => states.Length;
}
