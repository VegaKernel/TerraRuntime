using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private readonly RuntimeProjectileStore? moonLordProjectiles;
    private readonly RuntimeProjectileReplicationRegistry? moonLordProjectileReplication;
    private readonly ProjectileSnapshot[] moonLordProjectileBuffer;

    private void MarkMoonLordCoreInteraction(in NpcSnapshot member, PlayerHandle player)
    {
        // Terraria 1.4.5.8 NPC.ApplyInteraction propagates head/hand participation to their core.
        // Validate the server-owned slot reference before reading the bounded NPC table.
        if ((member.TypeIdentity != VanillaNpcIds.MoonLordHead && member.TypeIdentity != VanillaNpcIds.MoonLordHand) ||
            !float.IsFinite(member.Ai.Ai3) || member.Ai.Ai3 < 0 || member.Ai.Ai3 >= npcs.Capacity)
            return;
        if (npcs.TryGetActive((byte)member.Ai.Ai3, out NpcSnapshot core) && core.TypeIdentity == VanillaNpcIds.MoonLordCore)
            interactions.TryMark(core.Handle, player);
    }

    public void NpcAiStateCommitted(in NpcSnapshot snapshot)
    {
        if (snapshot.TypeIdentity != VanillaNpcIds.MoonLordCore || snapshot.Ai.Ai0 is not (2f or 3f) ||
            !npcs.TryGet(snapshot.Handle, out NpcSnapshot current) || current.Revision != snapshot.Revision)
        {
            return;
        }

        if (current.Ai.Ai0 == 3f)
        {
            if (current.Ai.Ai1 == 40f) ClearMoonLordAttacks(departure: true);
            if (current.Ai.Ai1 >= 60f)
            {
                int count = npcs.CopyActive(npcFamilyBuffer);
                for (int i = 0; i < count; i++)
                {
                    NpcSnapshot part = npcFamilyBuffer[i];
                    if (part.TypeIdentity == VanillaNpcIds.MoonLordFreeEye ||
                        part.TypeIdentity == VanillaNpcIds.MoonLordHand || part.TypeIdentity == VanillaNpcIds.MoonLordHead)
                        DespawnMoonLordMember(in part, publish: true);
                }
                DespawnMoonLordMember(in current, publish: true);
                progression.SetLunarApocalypseIsUp(false);
                worldClock?.RequestWorldInfoSync();
            }
            return;
        }

        // TerrariaServer 1.4.5.8 NPC.AI_077_MoonLordCore clears these five attack types and every
        // True Eye at death tick 60. The source intentionally scans globally, including another core's attacks.
        // Scratch storage is bounded by the live entity tables; this scan runs once per dying core.
        if (current.Ai.Ai1 == 60f) ClearMoonLordAttacks(departure: false);

        // AI-triggered checkDead must reach the same progression/loot boundary as combat-triggered death.
        // No packet-28 strike is invented for a timer expiry. Loot and progression commit at tick 600.
        if (current.Ai.Ai1 < 600f || current.Simulation.Life != 0 ||
            !TryExecuteImportedLoot(in current, eaterBoss: false))
        {
            return;
        }

        ApplyHardmodeBossDeathEffects(in current);
        if (npcs.TryDespawn(current.Handle))
        {
            interactions.Forget(current.Handle);
            npcReplication?.TryPublishDeath(in current);
        }
    }
    private void ClearMoonLordAttacks(bool departure)
    {
        if (moonLordProjectiles is not null)
        {
            int count = moonLordProjectiles.CopyActive(moonLordProjectileBuffer);
            for (int i = 0; i < count; i++)
            {
                ProjectileSnapshot projectile = moonLordProjectileBuffer[i];
                if (projectile.Type == VanillaProjectileIds.MoonLeech ||
                    projectile.Type == VanillaProjectileIds.PhantasmalBolt ||
                    projectile.Type == VanillaProjectileIds.PhantasmalDeathray ||
                    projectile.Type == VanillaProjectileIds.PhantasmalEye ||
                    projectile.Type == VanillaProjectileIds.PhantasmalSphere)
                {
                    if (departure)
                    {
                        moonLordProjectileReplication?.TryPublishRemovalSnapshot(in projectile);
                        moonLordProjectiles.TryRemove(projectile.Handle, out _);
                    }
                    else moonLordProjectiles.TryDespawn(projectile.Handle, out _);
                }
            }
        }

        int npcCount = npcs.CopyActive(npcFamilyBuffer);
        for (int i = 0; i < npcCount; i++)
        {
            NpcSnapshot eye = npcFamilyBuffer[i];
            if (eye.TypeIdentity == VanillaNpcIds.MoonLordFreeEye)
                DespawnMoonLordMember(in eye, publish: departure);
        }
    }

    private void DespawnMoonLordMember(in NpcSnapshot member, bool publish)
    {
        if (!npcs.TryDespawn(member.Handle)) return;
        interactions.Forget(member.Handle);
        if (publish) npcReplication?.TryPublishDeath(in member);
    }

}
