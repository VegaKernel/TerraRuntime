using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringTeleporter1458Tests
{
    public static IEnumerable<object[]> OriginalDestinationSectionRows()
    {
        using var input = typeof(WiringTeleporter1458Tests).Assembly.GetManifestResourceStream("WiringTeleportSectionsLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalDestinationSectionRows))]
    public void Current_destination_sections_precede_wiring_teleport_in_original_order(string observation)
    {
        using var json = JsonDocument.Parse(observation);
        var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var world = CreateTeleportSectionWorld();
        var tiles = world.Tiles;
        int start = row.GetProperty("source").GetInt32();
        int destination = row.GetProperty("destination").GetInt32();
        int y = row.GetProperty("y").GetInt32();
        int switchX = Math.Min(start, destination) - 4;
        for (int x = switchX; x <= Math.Max(start, destination) + 2; x++)
            tiles.Set(x, y, new WorldTile { Flags = WorldTileFlags.WireRed });
        // The original pressure plate trigger keeps the landing-section revision current.
        tiles.Set(switchX, y, new WorldTile
        {
            Type = row.GetProperty("trigger").GetUInt16(), Flags = WorldTileFlags.Active | WorldTileFlags.WireRed
        });
        foreach (int left in new[] { start, destination })
            for (int dx = 0; dx < 3; dx++)
                tiles.Set(left + dx, y, new WorldTile
                {
                    Type = 235, FrameX = (short)(dx * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireRed
                });

        var packets = PlayerBootstrapPacketSet.Create(world);
        var streaming = new PlayerSectionStreamingState(world.Header.Dimensions);
        foreach (var known in row.GetProperty("initial").EnumerateArray())
            streaming.MarkSent(new(known.GetProperty("x").GetInt32(), known.GetProperty("y").GetInt32()));
        float xLanding = row.GetProperty("x").GetSingle(), yLanding = row.GetProperty("yPixels").GetSingle();
        Span<WorldSectionId> sections = stackalloc WorldSectionId[PlayerSectionStreamingState.MaximumTeleportWindowSectionCount];
        int count = streaming.PlanTeleportUnsent(xLanding, yLanding, sections);
        for (int i = 0; i < count; i++)
            Assert.Equal(SectionFrameLookupResult.Available, packets.ResolveSectionFrameForInitialBootstrap(sections[i], out _));
        int sentBefore = streaming.SentSectionCount;

        var output = new TerrariaConnectionOutboundQueue(new(32, 1_048_576, ushort.MaxValue));
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        using var sink = new PlayerBootstrapFrameSink(slots, output, packets);
        typeof(PlayerBootstrapFrameSink).GetField("_sectionStreaming", Hidden)!.SetValue(sink, streaming);
        var registry = new RuntimeConnectionRegistry();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1458121), session.Handle);
        Assert.True(registry.TryRegister(connection.Source, output, sink.PrepareServerTeleportSections, sink.CommitServerTeleport));
        var random = new VanillaUnifiedRandom1458(1458);
        var state = new ServerRuntimeState(playerEvents: registry, worldTiles: tiles,
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random), naturalSpawnRandom: new SystemVanillaNpcRandom(random));
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, (short)start, (short)y, 0, 0, 0, 0, 0)));
        state.Apply(new PlayerMovementRuntimeCommand(connection, Motion(session.Slot, start * 16f - 2, y * 16f - 42)));
        _ = Drain(output);
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Hidden)!.GetValue(state)!;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Hidden)!.GetValue(graph.WorldTileAuthority)!;
        Assert.True(circuit.HitSwitch(switchX, y));

        string[] expected = row.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToArray();
        string[] actual = Drain(output);
        Assert.True(expected.Length == actual.Length,
            $"Expected {string.Join(' ', expected)}, received {string.Join(' ', actual)}.");
        for (int i = 0; i < actual.Length; i++)
        {
            byte[] expectedFrame = Convert.FromHexString(expected[i]);
            byte[] actualFrame = Convert.FromHexString(actual[i]);
            Assert.Equal(expectedFrame[2], actualFrame[2]);
            if (expectedFrame[2] == 10)
                Assert.Equal(ReadSectionBounds(expectedFrame), ReadSectionBounds(actualFrame));
            else
                Assert.Equal(expectedFrame, actualFrame);
        }
        Assert.Equal(sentBefore + expected.Count(frame => frame.AsSpan(4, 2).SequenceEqual("0A")), streaming.SentSectionCount);
        Assert.True(graph.Players.TryGet(connection, out var player));
        Assert.Equal(xLanding, player.PositionX);
        Assert.Equal(yLanding, player.PositionY);
    }

    [Fact]
    public void Current_subset_is_sent_while_cold_destination_sections_enter_the_background_budget()
    {
        var world = CreateTeleportSectionWorld();
        var packets = PlayerBootstrapPacketSet.Create(world);
        var readySection = new WorldSectionId(3, 3);
        Assert.Equal(SectionFrameLookupResult.Available, packets.ResolveSectionFrameForInitialBootstrap(readySection, out var readyFrame));
        var requested = new List<WorldSectionId>();
        packets.AttachSectionRebuildRequester(section =>
        {
            requested.Add(section);
            return new(true, requested.Count);
        });

        var output = new TerrariaConnectionOutboundQueue(new(32, 1_048_576, ushort.MaxValue));
        using var sink = new PlayerBootstrapFrameSink(new PlayerSlotPool(1), output, packets);
        float x = (3 * TerrariaSectionGeometry.WidthTiles + 6) * 16f;
        float y = (3 * TerrariaSectionGeometry.HeightTiles + 6) * 16f;
        sink.PrepareServerTeleportSections(x, y);

        WorldSectionId[] expected =
        [
            new(2, 2), new(2, 3), new(2, 4),
            new(3, 2), new(3, 4), new(4, 2), new(4, 3), new(4, 4)
        ];
        Assert.Equal(expected, requested);
        Assert.Equal(0, packets.CaptureSectionCacheSnapshot().Waits);
        Assert.Equal(new[] { Convert.ToHexString(TerraRuntime.Protocol.Multiplicity.PlayerJoinFrameEncoder.EncodeStatus(1)),
            Convert.ToHexString(readyFrame.Span) }, Drain(output));
    }

    private static WorldFileData CreateTeleportSectionWorld()
    {
        const BindingFlags helper = BindingFlags.Static | BindingFlags.NonPublic;
        var worldBytes = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld", helper)!.Invoke(null, null)!;
        var limits = (WorldFileLoadLimits)typeof(WorldFileLoaderTests).GetMethod("CreateLimits", helper)!.Invoke(null, null)!;
        Assert.True(WorldFileLoader.TryLoad(worldBytes, limits, out var source).IsLoaded);
        var dimensions = new WorldDimensions(1000, 750);
        var loaded = Assert.IsType<WorldFileData>(source);
        return loaded with
        {
            Header = loaded.Header with { RightWorld = 16000, BottomWorld = 12000, Dimensions = dimensions },
            Tiles = new WorldTileStore(dimensions)
        };
    }

    private static (int X, int Y, short Width, short Height) ReadSectionBounds(byte[] frame)
    {
        using var payload = new MemoryStream(frame, 3, frame.Length - 3);
        using var deflate = new DeflateStream(payload, CompressionMode.Decompress);
        using var reader = new BinaryReader(deflate);
        return (reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt16(), reader.ReadInt16());
    }
}
