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
        if (!npcs.TryCaptureDeathMutationSerial(out ulong serial)) return false;
        int count = npcs.CopyActive(naturalSpawnNpcBuffer);
        NpcSnapshot guide = default;
        int guides = 0, laterVictims = 0;
        for (int i = 0; i < count; i++)
        {
            NpcSnapshot npc = naturalSpawnNpcBuffer[i];
            if (npc.TypeIdentity == VanillaNpcIds.Guide) { guide = npc; guides++; }
            else if (VanillaTownNpcFacts1458.IsHousingEligible(npc.TypeIdentity) || npc.Type is 37 or 368 or 453)
                laterVictims++;
        }
        if (guides == 0) return combat.TryBurnDollWithoutGuide(in doll);
        // Source walks every Guide, then random later town victims. An atomic multi-death batch
        // is not owned yet; never consume a stack and leave one of those deaths rejected halfway.
        if (guides != 1 || (doll.Stack > 1 && laterVictims > 0)) return false;
        if (!combat.TryStrikeGuideDoll(in doll, in guide, serial, out var wall)) return false;
        if (wall is { } spawn)
        {
            if (!npcs.TrySpawnIntent(in spawn, out _))
                throw new InvalidOperationException("Accepted Guide doll continuation lost its preflighted Wall spawn.");
            AppliedSpawns++;
        }
        return true;
    }
}
