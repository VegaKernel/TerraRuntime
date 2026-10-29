using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class NpcTransformInteractions1458Tests
{
    [Theory]
    [InlineData(534)]
    [InlineData(687)]
    public void Actual_client_room_and_kick_commands_update_the_same_resident_used_by_the_next_tick(int sourceType)
    {
        string source = OriginalHomePhases().Select(row => (string)row[0]).First(json =>
        {
            using var candidate = JsonDocument.Parse(json);
            var row = candidate.RootElement;
            return row.GetProperty("seed").GetInt32() == 1458 && row.GetProperty("type").GetInt32() == sourceType &&
                row.GetProperty("scene").GetString() == "powder";
        });
        using var document = JsonDocument.Parse(source);
        using var f = new Fixture(1458, [document.RootElement.GetProperty("input").GetProperty("npc")],
            withWorldInfo: true, clock: new RuntimeWorldClock(0d, false, VanillaMoonPhase.Full, 0d, 0), withHomeCommands: true);
        Assert.True(f.Powder.TryApply(10, 1020, 1540, 100, 100, null, out int transformed));
        Assert.Equal(1, transformed);
        GemBunnyWire1458Tests.Drain(f.Outbound);
        BuildRoom();

        var before = f.Current(0);
        f.World.Apply(new ClientNpcHomeRuntimeCommand(f.Connection, new(before.Handle.Slot, 25, 25,
            (byte)TerrariaNpcHomeStatus.HasRoom)));
        Assert.Equal(before, f.Current(0));
        Assert.Empty(GemBunnyWire1458Tests.Drain(f.Outbound));
        var old = History();
        f.World.Apply(new ClientNpcHomeRuntimeCommand(f.Connection, new(before.Handle.Slot, 25, 25,
            (byte)TerrariaNpcHomeStatus.None)));
        var housed = f.Current(0);
        Assert.False(housed.Simulation.Homeless);
        Assert.True(f.Town.TryGet(housed.Handle.Slot, out var resident));
        Assert.Equal(resident.HomeTileX, housed.Simulation.HomeTileX);
        Assert.Equal(resident.HomeTileY, housed.Simulation.HomeTileY);
        Assert.Equal(old, History());
        Assert.Empty(GemBunnyWire1458Tests.Drain(f.Outbound));
        Tick();
        Assert.False(f.Current(0).Simulation.Homeless);
        Assert.Equal(housed.Simulation.HomeTileX, f.Current(0).Simulation.HomeTileX);
        Assert.Equal(housed.Simulation.HomeTileY, f.Current(0).Simulation.HomeTileY);
        Assert.Contains(GemBunnyWire1458Tests.Drain(f.Outbound), frame => frame[2] == 60);

        old = History();
        f.World.Apply(new ClientNpcHomeRuntimeCommand(f.Connection, new(before.Handle.Slot, 0, 0,
            (byte)TerrariaNpcHomeStatus.Homeless)));
        Assert.True(f.Current(0).Simulation.Homeless);
        Assert.True(f.Town.TryGet(before.Handle.Slot, out resident));
        Assert.True(resident.Homeless);
        Assert.Empty(f.Town.CaptureTownRooms());
        Assert.Equal(old, History());
        Assert.Empty(GemBunnyWire1458Tests.Drain(f.Outbound));
        Tick();
        Assert.True(f.Current(0).Simulation.Homeless);
        Assert.True(f.Town.TryGet(before.Handle.Slot, out resident));
        Assert.True(resident.Homeless);
        Assert.Contains(GemBunnyWire1458Tests.Drain(f.Outbound), frame => frame[2] == 60);

        (int, int, bool) History()
        {
            var current = f.Current(0);
            Assert.True(f.Registry.TryCaptureSourceTownHomeHistory(in current, out int x, out int y, out bool homeless));
            return (x, y, homeless);
        }

        void Tick()
        {
            var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState)
                .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.World)!;
            object cycle = typeof(NpcAuthority).GetField("sourceSpawnCycle", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(graph.Npcs)!;
            cycle.GetType().GetMethod("SuppressNext", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cycle, null);
            graph.Npcs.AdvanceWorldTick();
            graph.Npcs.TickSimulation();
        }

        void BuildRoom()
        {
            for (int x = 20; x <= 31; x++)
            for (int y = 20; y <= 29; y++)
            {
                bool boundary = x is 20 or 31 || y is 20 or 29;
                f.Tiles.Set(x, y, new WorldTile { Type = boundary ? (ushort)1 : (ushort)0, Wall = 1,
                    Flags = boundary ? WorldTileFlags.Active : WorldTileFlags.None });
            }
            Place(22, 26, 15);
            Place(24, 26, 14);
            Place(26, 23, 4);
            Place(28, 25, 10);
        }

        void Place(int x, int y, ushort type) =>
            f.Tiles.Set(x, y, new WorldTile { Type = type, Wall = 1, Flags = WorldTileFlags.Active });
    }
}
