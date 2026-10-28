using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GroundCritterBirthProtection1458Tests
{
    private static readonly JsonElement[] Rows = Read();
    public static IEnumerable<object[]> Cases() => Rows.Select((row, index) => new object[] {
        index, row.GetProperty("type").GetInt32(), row.GetProperty("seed").GetInt32() });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Initialized_birth_defaults_and_ninety_one_owned_updates_match_source_protection(int index, int type, int seed)
    {
        var row = Rows[index];
        var input = row.GetProperty("input");
        Assert.Equal(type, input.GetProperty("npc").GetProperty("type").GetInt32());
        var tiles = new WorldTileStore(new(192, 200));
        Assert.True(tiles.TryAttachWorldSurface(80d));
        for (int x = 0; x < 192; x++) tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var npcs = new RuntimeNpcStore();
        var random = new VanillaUnifiedRandom1458(seed);
        var world = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(0, false, (VanillaMoonPhase)0, 0, 0),
            naturalSpawnRandom: new SystemVanillaNpcRandom(random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random));
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9430), session.Handle);
        world.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 74, 100, 0, 0, 0, 0, 0)));
        world.Apply(new PlayerAppearanceRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, "Oracle", 0, 0, 0,
            default, default, default, default, default, default, default, 0, 0, 0)));
        var sourcePlayer = input.GetProperty("players")[0];
        world.Apply(new PlayerMovementRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, 0,
            Float(sourcePlayer, "position", 0), Float(sourcePlayer, "position", 1), false, 0, 0, false, 0,
            false, 0, 0, 0, 0, false, 0, 0)));
        var source = input.GetProperty("npc");
        // Friendly and protection counter are deliberately omitted: real spawn
        // policy must supply source defaults rather than importing golden values.
        var creation = new NpcStateUpdate(type, checked((short)type), Float(source, "position", 0), Float(source, "position", 1),
            Float(source, "velocity", 0), Float(source, "velocity", 1), source.GetProperty("target").GetUInt16(), default,
            NpcSimulationState.Initial with { TimeLeft = source.GetProperty("timeLeft").GetInt32(),
                DirectionX = source.GetProperty("direction").GetInt32(), DirectionY = source.GetProperty("directionY").GetInt32(),
                SpriteDirection = source.GetProperty("spriteDirection").GetInt32() });
        Assert.True(npcs.TrySpawnVanilla(in creation, out var birth));
        SameProtection(source, birth);
        Assert.Equal(type == 46, VanillaNonresidentGroundCritterCatalog1458.HasCatchProtection(type));
        Assert.Equal(2019, source.GetProperty("catchItem").GetInt32());
        Assert.Equal(type == 46, source.GetProperty("catchable").GetBoolean());
        int steps = 0;
        JsonElement previous = input;
        foreach (var frame in row.GetProperty("frames").EnumerateArray())
        {
            Assert.Equal(previous.GetRawText(), frame.GetProperty("before").GetRawText());
            NonresidentBunnyNpcPhase1458.Tick(world);
            Assert.True(npcs.TryGet(birth.Handle, out var current));
            var after = frame.GetProperty("after");
            SameProtection(after.GetProperty("npc"), current);
            // These are independent original physical RNG receipts. The test's
            // runtime comparison is bounded to friendly/counter, not full RNG parity.
            Assert.Equal(56, frame.GetProperty("before").GetProperty("rng").GetProperty("slots").GetArrayLength());
            Assert.Equal(56, after.GetProperty("rng").GetProperty("slots").GetArrayLength());
            previous = after;
            steps++;
        }
        Assert.Equal(91, steps);
    }

    private static void SameProtection(JsonElement source, NpcSnapshot actual)
    {
        Assert.Equal(source.GetProperty("friendly").GetBoolean(), actual.Simulation.Friendly);
        Assert.Equal(source.GetProperty("immunity").GetInt32(), actual.Simulation.CatchableNpcTempImmunityCounter);
    }

    private static float Float(JsonElement value, string name, int index) => BitConverter.UInt32BitsToSingle(
        uint.Parse(value.GetProperty(name)[index].GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static JsonElement[] Read()
    {
        using var stream = typeof(GroundCritterBirthProtection1458Tests).Assembly.GetManifestResourceStream("GroundCritterBirthProtectionLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal("25120fe7d33cb3c37e957fcaaaf8d37220c7228e23b9a5c6e7da4f522261c3bf", Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        var rows = json.RootElement.GetProperty("cases").EnumerateArray().Select(x => x.Clone()).ToArray();
        Assert.Equal(12, rows.Length);
        Assert.Equal(new[] { 46, 303, 337, 540 }, rows.Select(x => x.GetProperty("type").GetInt32()).Distinct().Order().ToArray());
        return rows;
    }
}
