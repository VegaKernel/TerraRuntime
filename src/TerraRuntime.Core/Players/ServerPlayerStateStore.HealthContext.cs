using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

public sealed partial class ServerPlayerStateStore
{
    /// <summary>Commits the retained Player.Update health context only against the captured generation and revision.</summary>
    public bool TrySetDerivedLifeMax(in PlayerStateSnapshot expected, int? value)
    {
        if (value is < 0 || !TryGetState(expected.Player, out ServerPlayerRuntimeState? state) ||
            state.Revision != expected.Revision.Value)
            return false;
        if (state.DerivedLifeMax == value)
            return true;
        if (state.Revision == ulong.MaxValue)
            return false;
        state.DerivedLifeMax = value;
        state.Revision++;
        return true;
    }
}
