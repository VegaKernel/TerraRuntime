using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private readonly RuntimeProjectileStore? moonLordProjectiles;
    private readonly ProjectileSnapshot[] moonLordProjectileBuffer;

    public void NpcAiStateCommitted(in NpcSnapshot snapshot)
    {
        if (snapshot.TypeIdentity != VanillaNpcIds.MoonLordCore || snapshot.Ai.Ai0 != 2f ||
            !npcs.TryGet(snapshot.Handle, out NpcSnapshot current) || current.Revision != snapshot.Revision)
        {
            return;
        }

        // TerrariaServer 1.4.5.8 NPC.AI_077_MoonLordCore clears these five attack types and every
        // True Eye at death tick 60. The source intentionally scans globally, including another core's attacks.
        // Scratch storage is bounded by the live entity tables; this scan runs once per dying core.
        if (current.Ai.Ai1 == 60f)
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
                        moonLordProjectiles.TryDespawn(projectile.Handle, out _);
                    }
                }
            }

            int npcCount = npcs.CopyActive(npcFamilyBuffer);
            for (int i = 0; i < npcCount; i++)
            {
                NpcSnapshot eye = npcFamilyBuffer[i];
                if (eye.TypeIdentity == VanillaNpcIds.MoonLordFreeEye && npcs.TryDespawn(eye.Handle))
                    interactions.Forget(eye.Handle);
            }
        }

        // AI-triggered checkDead must reach the same progression/loot boundary as combat-triggered death.
        // No packet-28 strike is invented for a timer expiry. Unsupported difficulty loot remains explicit
        // in TryExecuteImportedLoot and the coverage ledger; this lifecycle does not claim a new loot table.
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
}
