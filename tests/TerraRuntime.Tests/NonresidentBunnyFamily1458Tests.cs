using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NonresidentBunnyFamily1458Tests
{
    [Fact]
    public void Ordinary_bunny_siblings_use_the_existing_nonresident_update_owner()
    {
        int[] types = [46, 303, 337, 443, 540];
        var missing = new List<int>();
        foreach (int type in types)
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
            if (!definition.SupportsRuntimeLifecycle || definition.CreationDefaultsOnly) missing.Add(type);
            Assert.Equal(VanillaNpcAiStyles.Town, definition.AiStyle);
            Assert.Equal(5, definition.LifeMax);
            Assert.Equal(18, definition.BaseWidth);
            Assert.Equal(20, definition.BaseHeight);
        }
        Assert.True(missing.Count == 0, "Creation-only bunny identities: " + string.Join(',', missing));

        foreach (int type in types)
        foreach (bool wholeWorld in new[] { false, true })
        {
            var tiles = new WorldTileStore(new WorldDimensions(100, 200));
            Assert.True(tiles.TryAttachWorldSurface(80d));
            for (int x = 0; x < 100; x++) tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var npcs = new RuntimeNpcStore();
            var items = new RuntimeWorldItemStore();
            var random = new VanillaUnifiedRandom1458(1458);
            var world = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, worldItems: items,
                worldClock: new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 0),
                naturalSpawnRandom: new SystemVanillaNpcRandom(random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9040), session.Handle);
            world.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 40, 100, 0, 0, 0, 0, 0)));
            world.Apply(new PlayerMovementRuntimeCommand(connection,
                new(session.Slot, 0, 0, 0, 0, 0, 660f, 1558f, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            var creation = new NpcStateUpdate(type, (short)type, 639f, 1580f, .9f, 0f, 255,
                new(1f, 60f, 0f, 0f), NpcSimulationState.Initial with
                {
                    OldPositionX = 638f, OldPositionY = 1580f, OldVelocityX = .9f, OldVelocityY = 0f,
                    DirectionX = 1, DirectionY = -1, SpriteDirection = 1,
                    HomeTileX = 40, HomeTileY = 100, Homeless = false,
                    LocalAi = new(0f, 0f, 0f, 0f)
                });
            Assert.True(npcs.TrySpawnVanilla(in creation, out var birth));
            Assert.Equal(false, birth.Simulation.TownNpc);
            // Actual initialized NewNPC/91-phase source capture: seasonal303/337/540
            // retain catchItem but Main.npcCatchable is false, so protection stays zero.
            Assert.Equal(type is 46 or 443 ? 90 : 0, birth.Simulation.CatchableNpcTempImmunityCounter);
            Assert.Equal((short)255, birth.Simulation.ReleaseOwner);
            var beforeRandom = random.Clone();
            if (wholeWorld) world.Tick();
            else NonresidentBunnyNpcPhase1458.Tick(world);
            Assert.True(npcs.TryGet(birth.Handle, out var current));
            Assert.True(current.Revision.Value > birth.Revision.Value);
            Assert.Equal(59f, current.Ai.Ai1);
            Assert.Equal(0x441ffe14u, BitConverter.SingleToUInt32Bits(current.PositionX));
            Assert.Equal(0x3f7851ebu, BitConverter.SingleToUInt32Bits(current.VelocityX));
            Assert.Equal((ushort)0, current.Target);
            Assert.True(current.Simulation.CollideY);
            Assert.Equal(-1f, current.Simulation.LocalAi.Ai3);
            Assert.Equal(type is 46 or 443 ? 89 : 0, current.Simulation.CatchableNpcTempImmunityCounter);
            if (!wholeWorld) Assert.True(beforeRandom.HasSameState(random));
        }
    }
}
