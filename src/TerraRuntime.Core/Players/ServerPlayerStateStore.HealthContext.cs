using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

public sealed partial class ServerPlayerStateStore
{
    /// <summary>Commits the retained Player.Update health context only against the captured generation and revision.</summary>
    public bool TrySetDerivedLifeMax(in PlayerStateSnapshot expected, int? value)
        => TrySetHealthContext(in expected, value, expected.Debuffs);

    public bool TrySetHealthContext(in PlayerStateSnapshot expected, int? value,
        PlayerDebuffSnapshot1458? debuffs)
    {
        if (value is < 0 || !TryGetState(expected.Player, out ServerPlayerRuntimeState? state) ||
            state.Revision != expected.Revision.Value)
            return false;
        if (state.DerivedLifeMax == value && state.Debuffs == debuffs)
            return true;
        if (state.Revision == ulong.MaxValue)
            return false;
        state.DerivedLifeMax = value;
        state.Debuffs = debuffs;
        state.Revision++;
        return true;
    }
}
