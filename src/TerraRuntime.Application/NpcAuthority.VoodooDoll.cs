using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    internal void ApplyBurnedGuideDoll(in WorldItemSnapshot burned)
    {
        // Only WorldItemAuthority calls this after exact-generation, source-contact removal.
        if (burned.ItemNetId != VanillaWallOfFleshItemIds.GuideVoodooDoll.Value || burned.Stack <= 0 || worldTiles is null) return;
        int remaining = burned.Stack;
        bool hadGuide = false;
        int count = npcs.CopyActive(naturalSpawnNpcBuffer);
        for (int i = 0; i < count; i++)
        {
            NpcSnapshot guide = naturalSpawnNpcBuffer[i];
            if (guide.TypeIdentity != VanillaNpcIds.Guide) continue;
            StrikeDollVictim(in guide);
            remaining--;
            hadGuide = true;
            TrySpawnWallFromDoll(burned.PositionX, burned.PositionY);
        }
        if (!hadGuide || remaining <= 0) return;
        count = npcs.CopyActive(naturalSpawnNpcBuffer);
        int eligible = 0;
        for (int i = 0; i < count; i++)
        {
            NpcSnapshot npc = naturalSpawnNpcBuffer[i];
            // SetDefaults townNPC residents plus Old Man/Traveling Merchant; isLikeATownNPC adds453.
            if (VanillaTownNpcFacts1458.IsHousingEligible(npc.TypeIdentity) || npc.Type is 37 or 368 or 453)
                naturalSpawnNpcBuffer[eligible++] = npc;
        }
        while (remaining-- > 0 && eligible > 0)
        {
            int selected = naturalSpawnRandom.NextInt32(0, eligible);
            if ((uint)selected >= (uint)eligible) return;
            StrikeDollVictim(in naturalSpawnNpcBuffer[selected]);
            Array.Copy(naturalSpawnNpcBuffer, selected + 1, naturalSpawnNpcBuffer, selected, --eligible - selected);
        }
    }

    private void StrikeDollVictim(in NpcSnapshot victim) =>
        combat.TryStrikeEnvironment(victim.Handle, 9999, 10f, victim.Simulation.DirectionX);

    private bool TrySpawnWallFromDoll(float x, float y)
    {
        if (worldTiles is null) return false;
        // The active store is the existing owner of the live Wall root; never infer it from a client request.
        for (int slot = 0; slot < npcs.Capacity; slot++)
            if (npcs.TryGetActive((byte)slot, out NpcSnapshot npc) && npc.TypeIdentity == VanillaNpcIds.WallOfFlesh)
                return false;
        int playersCount = 0;
        for (int slot = 0; slot < byte.MaxValue; slot++)
            if (playerSnapshots.TryGetPlayer(new PlayerSlotId((byte)slot), out var player))
                serverPlayerSnapshots[playersCount++] = player;
        if (!VanillaWallOfFleshSpawn1458.TryFind(worldTiles, x, y, serverPlayerSnapshots.AsSpan(0, playersCount), out int bx, out int by))
            return false;
        // NPC.NewNPC's target argument is omitted in SpawnWOF; target defaults to255, not the source player.
        var intent = new NpcAiSpawnIntent(VanillaNpcIds.WallOfFlesh, bx, by, 0, 0, 255);
        if (!npcs.TrySpawnIntent(in intent, out _)) { RejectedSpawns++; return false; }
        AppliedSpawns++;
        return true;
    }
}
