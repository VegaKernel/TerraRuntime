using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Authoritative state for runtime-owned players. Identity ownership is validated against
/// <see cref="RuntimeServerPlayerSlotRegistry"/> on every operation; no transport connection is accepted anywhere in
/// this API, so client packet ingress cannot become an alternate writer by constructing a <see cref="ConnectionHandle"/>.
/// </summary>
public sealed partial class RuntimeServerPlayerStateStore
{
    private readonly RuntimeServerPlayerSlotRegistry identities;
    private readonly ServerPlayerRuntimeState?[] states;

    public RuntimeServerPlayerStateStore(RuntimeServerPlayerSlotRegistry identities, int capacity)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, byte.MaxValue);
        this.identities = identities;
        states = new ServerPlayerRuntimeState?[capacity];
    }
}
