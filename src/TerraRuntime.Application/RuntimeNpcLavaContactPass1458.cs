using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Direct Collision_LavaCollision strike slice for verified ordinary NPC defaults. This is not a general
/// NPC buff engine: OnFire replication/DoT and additional types/seed variations remain unadmitted.
/// Runs only on the world writer, after committed motion; never trusts an inbound Wet flag.
/// </summary>
internal sealed class RuntimeNpcLavaContactPass1458(
    RuntimeNpcStore npcs, WorldTileStore tiles, RuntimeNpcNetworkCombatPipeline combat, Func<long> tickProvider)
{
    private readonly NpcSnapshot[] scan = new NpcSnapshot[npcs.Capacity];
    private readonly NpcHandle[] generations = new NpcHandle[npcs.Capacity];
    private readonly long[] nextStrike = new long[npcs.Capacity];

    public void Tick()
    {
        long tick = tickProvider();
        int count = npcs.CopyActive(scan);
        for (int i = 0; i < count; i++)
        {
            NpcSnapshot npc = scan[i];
            // NPC.SetDefaults resets lavaImmune=false; these five branches do not override it.
            // Do not infer vulnerability for other definitions (notably Hell creatures and dynamic AI flags).
            if (npc.Type is not (1 or 2 or 3 or 21 or 22) || npc.Simulation.DontTakeDamage ||
                npc.Simulation.Immortal != false || npc.Simulation.Life <= 0 ||
                !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out var definition) ||
                !definition.TryResolveHitbox(npc.Simulation, out var hitbox)) continue;
            int slot = npc.Handle.Slot;
            if (generations[slot] == npc.Handle && tick < nextStrike[slot]) continue;
            if (!VanillaWorldCollision.LavaCollision(tiles, npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height)) continue;

            generations[slot] = npc.Handle;
            nextStrike[slot] = tick + 30; // NPC.immune[255] duration for the direct lava contact strike.
            combat.TryStrikeEnvironment(npc.Handle, 50);
        }
    }
}
