using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    internal bool TryBurnGuideDoll(in WorldItemSnapshot doll)
    {
        // Only the owned lava-contact producer calls this; admission precedes source whole-stack removal.
        if (doll.ItemNetId != VanillaWallOfFleshItemIds.GuideVoodooDoll.Value || doll.Stack <= 0 || worldTiles is null) return false;
        if (!combat.TryBurnGuideDollBatch(in doll, out int wallsBorn)) return false;
        AppliedSpawns += wallsBorn;
        return true;
    }
}
