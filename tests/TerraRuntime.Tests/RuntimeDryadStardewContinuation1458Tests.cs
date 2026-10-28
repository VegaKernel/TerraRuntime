using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeDryadStardewContinuation1458Tests
{
    [Fact]
    public void Existing_Dryad_animation_completes_source_frames_emotes_and_idle_rng_in_the_real_world_tick()
    {
        // Actual 1.4.5.8 NPC.UpdateNPC, seed1458, one Dryad on stone row30.
        // This starts from HaveDryadDoStardewAnimation's already owned state;
        // triggering that separate interaction is outside this continuation test.
        var tiles = new WorldTileStore(new WorldDimensions(100, 80));
        Assert.True(tiles.TryAttachWorldSurface(20d));
        for (int x = 0; x < 100; x++) tiles.Set(x, 30, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [
            new WorldTownNpc(20, "Dryad", 639f, 440f, false, 40, 30, null, false)], []),
            [new WorldTownRoom(20, 40, 30)], tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        Assert.True(npcs.TryGetActive(0, out var initial));
        var seeded = new NpcStateUpdate(20, 20, 639f, 440f, .5f, 0f, 255, new(24f, 480f, 0f, 0f),
            initial.Simulation with { OldPositionX = 639f, OldPositionY = 440f,
                OldVelocityX = .5f, OldVelocityY = 0f, DirectionX = 1, DirectionY = -1, SpriteDirection = 1,
                TimeLeft = 750, HomeTileX = 40, HomeTileY = 30, Homeless = false,
                LocalAi = new(0f, 0f, 480f, 0f), FrameCounter = 0d, FrameIndex = 0 });
        Assert.True(npcs.TryUpdate(initial.Handle, in seeded, out _));
        var random = new RandomSource();
        var registry = new RuntimeNpcReplicationRegistry();
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(1024, 1024 * 1024, 65536));
        var source = GameCommandSourceId.FromConnection(9001);
        Assert.True(registry.TryRegister(source, queue));
        var handle = new ConnectionHandle(source, new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        var spawn = new PlayerSpawnCommitRequest(handle.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(handle, in spawn);
        var world = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, townNpcs: town,
            naturalSpawnRandom: random, npcReplication: registry);
        var outbound = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(queue)!;
        while (outbound.TryRead(out _)) { }
        var emitted = new List<(int Frame, int Id, int Lifetime, int Emote)>();
        for (int frame = 0; frame < 496; frame++)
        {
            world.Tick();
            Assert.True(npcs.TryGetActive(0, out var current));
            if (frame == 0)
            {
                Assert.Equal(0x441fd99au, BitConverter.SingleToUInt32Bits(current.PositionX));
                Assert.Equal(.4f, current.VelocityX);
                Assert.Equal(479f, current.Ai.Ai1);
                Assert.Equal(1d, current.Simulation.FrameCounter);
                Assert.Equal(19, current.Simulation.FrameIndex);
            }
            if (frame is 59 or 149 or 239 or 478)
            {
                Assert.Equal(24f, current.Ai.Ai0);
                Assert.Equal(frame + 1d, current.Simulation.FrameCounter);
                Assert.Equal(frame == 149 ? 20 : frame >= 239 ? 0 : 19, current.Simulation.FrameIndex);
                Assert.Equal(0x44203e85u, BitConverter.SingleToUInt32Bits(current.PositionX));
                Assert.Equal(0, random.Draws);
            }
            if (frame == 479)
            {
                Assert.Equal(0f, current.Ai.Ai0);
                Assert.Equal(480f, current.Ai.Ai1);
                Assert.Equal(479f, current.Simulation.LocalAi.Ai1);
                Assert.Equal(480f, current.Simulation.LocalAi.Ai3);
                Assert.Equal(0d, current.Simulation.FrameCounter);
                Assert.Equal(7, random.Draws);
            }
            while (outbound.TryRead(out var packet))
            {
                byte[] bytes = packet.Bytes.ToArray();
                if (bytes[2] != 91) continue;
                Assert.Equal(13, bytes.Length);
                Assert.Equal(0, bytes[7]);
                Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)));
                emitted.Add((frame, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(3)),
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10)), bytes[12]));
            }
        }
        Assert.Equal(new[] { (59, 0, 60, 87), (149, 1, 90, 3) }, emitted);
        Assert.True(npcs.TryGetActive(0, out var completed));
        Assert.Equal(0f, completed.Ai.Ai0);
        Assert.Equal(464f, completed.Ai.Ai1);
        Assert.Equal(463f, completed.Simulation.LocalAi.Ai1);
        Assert.Equal(464f, completed.Simulation.LocalAi.Ai3);
        Assert.Equal(135, random.Draws);
        var expected = new VanillaUnifiedRandom1458(1458);
        for (int i = 0; i < 135; i++) expected.Next();
        Assert.True(expected.HasSameState(random.Stream)); // All56 physical slots and cursor.
    }

    private sealed class RandomSource : IVanillaNpcRandom
    {
        public VanillaUnifiedRandom1458 Stream { get; } = new(1458);
        public int Draws { get; private set; }
        public int NextInt32(int min, int max) { Draws++; return Stream.Next(min, max); }
        public double NextDouble() { Draws++; return Stream.NextDouble(); }
    }
}
