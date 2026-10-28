using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NonresidentBunnyBloodMoon1458Tests
{
    [Fact]
    public void Unknown_or_active_blood_moon_refuses_convertible_bunnies_but_not_gem_bunnies()
    {
        foreach (bool? bloodMoon in new bool?[] { null, true, false })
        foreach (int type in new[] { 46, 303, 337, 443, 540, 646 })
        foreach (bool lowerWorld in new[] { false, true })
        foreach (bool wholeWorld in new[] { false, true })
        {
            if (lowerWorld && (type == 646 || bloodMoon is false)) continue;
            var tiles = new WorldTileStore(new WorldDimensions(100, 200));
            Assert.True(tiles.TryAttachWorldSurface(80d));
            for (int x = 0; x < 100; x++) tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var npcs = new RuntimeNpcStore();
            var random = new VanillaUnifiedRandom1458(1458);
            var clock = bloodMoon.HasValue
                ? new RuntimeWorldClock(0, !bloodMoon.Value, (VanillaMoonPhase)0, 0, 0, bloodMoonActive: bloodMoon.Value)
                : null;
            var world = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, worldClock: clock,
                naturalSpawnRandom: new SystemVanillaNpcRandom(random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9040), session.Handle);
            world.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 40, 100, 0, 0, 0, 0, 0)));
            world.Apply(new PlayerMovementRuntimeCommand(connection,
                new(session.Slot, 0, 0, 0, 0, 0, 660f, 1558f, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            float y = lowerWorld ? 2780f : 1580f;
            var creation = new NpcStateUpdate(type, (short)type, 639f, y, .9f, 0f, 255,
                new(1f, 60f, 0f, 0f), NpcSimulationState.Initial with {
                    OldPositionX = 638f, OldPositionY = y, OldVelocityX = .9f,
                    DirectionX = 1, DirectionY = -1, SpriteDirection = 1,
                    HomeTileX = 40, HomeTileY = 100, Homeless = false });
            Assert.True(npcs.TrySpawnVanilla(in creation, out var before));
            var checkpoint = random.Clone();
            if (wholeWorld) world.Tick();
            else NonresidentBunnyNpcPhase1458.Tick(world);
            Assert.True(npcs.TryGet(before.Handle, out var after));
            if (type != 646 && bloodMoon is not false)
            {
                Assert.True(after == before, $"type{type}/bloodMoon{bloodMoon}: unsupported transformation phase committed");
                // The complete world also owns time and natural-spawn RNG. The isolated real
                // NPC phase must refuse without consuming its shared physical random stream.
                if (!wholeWorld) Assert.True(checkpoint.HasSameState(random));
            }
            else
            {
                Assert.True(after.Revision.Value > before.Revision.Value);
                Assert.Equal(59f, after.Ai.Ai1);
                Assert.Equal(89, after.Simulation.CatchableNpcTempImmunityCounter);
            }
        }
    }
}

internal static class NonresidentBunnyNpcPhase1458
{
    internal static void Tick(ServerRuntimeState world)
    {
        // Exact existing composition fields, test-only access. Invoke the shipping NPC phase;
        // no substitute AI, reset random state, or translated caller prefix is used here.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var composition = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(world)!;
        var owner = composition.Npcs;
        var cycle = (RuntimeNpcSpawnCycle1458)typeof(NpcAuthority).GetField("sourceSpawnCycle", flags)!.GetValue(owner)!;
        // NPC.noSpawnCycle=true is an explicit fixture preimage. Spawner and world-time
        // are caller phases outside the original individual NPC.UpdateNPC observation.
        cycle.SuppressNext();
        owner.AdvanceWorldTick();
        owner.TickSimulation();
    }
}
