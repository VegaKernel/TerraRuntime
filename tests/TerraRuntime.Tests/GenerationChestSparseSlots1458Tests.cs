using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationChestSparseSlots1458Tests
{
    public static IEnumerable<object[]> SourceCases()
    {
        using JsonDocument source = ReadSource();
        foreach (JsonElement row in source.RootElement.GetProperty("results").EnumerateArray())
            yield return [row.GetProperty("name").GetString()!, row.GetProperty("seed").GetInt32()];
    }

    [Theory, MemberData(nameof(SourceCases))]
    public void Source_chest_holes_survive_callbacks_until_dense_file_projection(string name, int seed)
    {
        using JsonDocument source = ReadSource();
        JsonElement definition = source.RootElement.GetProperty("definitions").EnumerateArray().Single(d => d.GetProperty("name").GetString() == name);
        JsonElement expected = source.RootElement.GetProperty("results").EnumerateArray().Single(d => d.GetProperty("name").GetString() == name && d.GetProperty("seed").GetInt32() == seed);
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TileStore.TryAttachTileEntities([], 1000));
        var random = new RandomAdapter(seed);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace);
        int stepIndex = 0;
        foreach (JsonElement op in definition.GetProperty("ops").EnumerateArray())
        {
            JsonElement step = expected.GetProperty("steps")[stepIndex++];
            string kind = op.GetProperty("op").GetString()!;
            int id = op.TryGetProperty("id", out JsonElement n) ? n.GetInt32() : -1;
            (int x, int y) = Coordinate(id);
            int result = 0;
            if (kind == "fill-pool") for (int i = 0; i < 8000; i++) Add(workspace, i, false, captureSlot: false);
            else if (kind == "add") result = Add(workspace, id, op.TryGetProperty("filled", out n) && n.GetBoolean());
            else if (kind == "remove") result = workspace.TryRemoveEmptyGeneratedChest(x, y) ? 1 : 0;
            else if (kind == "kill") { framing.KillTile(x, y); result = workspace.TileStore.Get(x, y).IsActive ? 0 : 1; }
            else if (kind == "query") result = workspace.CanDestroyGeneratedChest(x, y) ? 1 : 0;
            else if (kind == "replace")
            {
                WorldChestItem[] items = Items(op.GetProperty("filled").GetBoolean());
                var replacement = items.Select(i => new WorldGenerationChestItem((short)i.Stack, new(i.ItemType), new(i.Prefix))).ToArray();
                result = workspace.TryReplaceGeneratedChestItems(x, y, replacement) ? 1 : 0;
            }
            else if (kind == "invalidate") workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)].Flags &= ~WorldTileFlags.Active;
            else if (kind == "save")
            {
                workspace.PrepareVanillaChestPersistence();
                using var memory = new MemoryStream();
                Assert.Equal(WorldFileChestEncodeResult.Encoded, WorldFileChestEncoder.TryEncode(workspace.CaptureGeneratedChests(), workspace.TileStore.Dimensions, memory, out _));
                Assert.Equal(step.GetProperty("fileHash").GetString(), Convert.ToHexString(SHA256.HashData(memory.ToArray())));
                Assert.Equal(step.GetProperty("loadedHash").GetString(), StateHash(workspace));
            }
            Assert.Equal(step.GetProperty("result").GetInt32(), result);
            Assert.Equal(step.GetProperty("count").GetInt32(), workspace.GeneratedChestCount);
            Assert.Equal(step.GetProperty("stateHash").GetString(), StateHash(workspace));
            Assert.Equal(step.GetProperty("canRegister").GetBoolean(), id < 0 || workspace.CanRegisterGeneratedChest(x, y));
            JsonElement physical = step.GetProperty("physicalRandom");
            Assert.Equal(RandomHash(physical), GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
            Assert.Equal(physical.GetProperty("next").GetInt32(), random.PeekNext());
            if (step.GetProperty("gridHash").ValueKind != JsonValueKind.Null)
                Assert.Equal(step.GetProperty("gridHash").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
        }
    }

    [Fact]
    public void Raw_source_factory_coordinates_do_not_expand_fresh_registration_admission()
    {
        using JsonDocument source = ReadSource(); var workspace = new Workspace(1000, 500);
        foreach (JsonElement invalid in source.RootElement.GetProperty("rawInvalidFactoryScopeOnly").EnumerateArray())
        {
            // Chest.CreateChest is a raw factory and accepts coordinates that generation's admission
            // deliberately refuses. Its larger domain is observed, not exposed by this shared owner fix.
            Assert.Equal(0, invalid.GetProperty("created").GetInt32());
            Assert.False(workspace.TryAddGeneratedChest(invalid.GetProperty("x").GetInt32(), invalid.GetProperty("y").GetInt32(), "", []));
        }
        Assert.Empty(workspace.CaptureGeneratedChests());
    }

    [Fact]
    public void Mixed_lifecycle_projects_the_actual_public_SaveNewWorld_LoadWorld_chest_section()
    {
        using JsonDocument source = ReadSource();
        JsonElement expected = source.RootElement.GetProperty("publicLifecycle");
        var workspace = new Workspace(1000, 500);
        for (int i = 0; i < 5; i++) Add(workspace, i, i % 2 == 0);
        Assert.True(workspace.TryRemoveEmptyGeneratedChest(14, 10)); Add(workspace, 10, true);
        Assert.True(workspace.TryRemoveEmptyGeneratedChest(22, 10)); Add(workspace, 11, false);
        Assert.True(workspace.TryReplaceGeneratedChestItems(18, 10, []));
        Assert.True(workspace.TryRemoveEmptyGeneratedChest(18, 10)); Add(workspace, 12, true);
        workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(26, 10)].Flags &= ~WorldTileFlags.Active;
        Assert.Equal(expected.GetProperty("before").GetString(), StateHash(workspace));
        workspace.PrepareVanillaChestPersistence();
        Assert.Equal(expected.GetProperty("afterLoad").GetString(), StateHash(workspace));
        using var memory = new MemoryStream();
        Assert.Equal(WorldFileChestEncodeResult.Encoded, WorldFileChestEncoder.TryEncode(workspace.CaptureGeneratedChests(), workspace.TileStore.Dimensions, memory, out _));
        Assert.Equal(expected.GetProperty("sectionHash").GetString(), Convert.ToHexString(SHA256.HashData(memory.ToArray())));
    }

    [Fact]
    public void Sparse_capture_stays_detached_and_strict_file_writer_refuses_live_holes()
    {
        var workspace = new Workspace(100, 100);
        Assert.Equal(0, Add(workspace, 0, true)); Assert.Equal(1, Add(workspace, 1, false)); Assert.Equal(2, Add(workspace, 2, true));
        Assert.True(workspace.TryRemoveEmptyGeneratedChest(14, 10));
        WorldChest[] detached = workspace.CaptureGeneratedChests();
        Assert.Equal(new short[] { 0, 2 }, detached.Select(c => c.SlotId));
        string before = StateHash(workspace); detached[0].Items[0] = default;
        Assert.Equal(before, StateHash(workspace));
        using var memory = new MemoryStream();
        Assert.Equal(WorldFileChestEncodeResult.NonCanonicalSlotOrder, WorldFileChestEncoder.TryEncode(workspace.CaptureGeneratedChests(), workspace.TileStore.Dimensions, memory, out _));
        Assert.Equal(before, StateHash(workspace));
        workspace.PrepareVanillaChestPersistence();
        Assert.Equal(new short[] { 0, 1 }, workspace.CaptureGeneratedChests().Select(c => c.SlotId));
        Assert.Equal(WorldFileChestEncodeResult.Encoded, WorldFileChestEncoder.TryEncode(workspace.CaptureGeneratedChests(), workspace.TileStore.Dimensions, memory, out _));
    }

    private static int Add(Workspace workspace, int id, bool filled, bool captureSlot = true)
    {
        (int x, int y) = Coordinate(id);
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
        {
            var tile = new WorldTile { Type = 21, Flags = WorldTileFlags.Active, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) };
            workspace.TileStore.Set(x + dx, y + dy, in tile);
        }
        for (int dx = 0; dx < 2; dx++) { var tile = new WorldTile { Type = 1, Flags = WorldTileFlags.Active }; workspace.TileStore.Set(x + dx, y + 2, in tile); }
        if (!workspace.TryAddGeneratedChest(x, y, "Chest" + id, Items(filled))) return -1;
        return !captureSlot ? 0 : workspace.CaptureGeneratedChests().Single(c => c.X == x && c.Y == y).SlotId;
    }

    private static WorldChestItem[] Items(bool filled) => Enumerable.Range(0, 40).Select(i => !filled ? default : new WorldChestItem(i % 7 == 0 ? 1 : i + 1, i % 7 == 0 ? 1156 : 8, (byte)(i % 7 == 0 ? 55 : 0))).ToArray();
    private static (int X, int Y) Coordinate(int id) => (10 + id % 200 * 4, 10 + id / 200 * 4);
    private static string StateHash(Workspace workspace)
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        WorldChest[] rows = workspace.CaptureGeneratedChests(); writer.Write(rows.Length);
        foreach (WorldChest c in rows)
        {
            writer.Write((int)c.SlotId); writer.Write(c.X); writer.Write(c.Y); writer.Write(c.Name); writer.Write(c.Items.Length);
            foreach (WorldChestItem item in c.Items) { writer.Write((short)item.Stack); writer.Write(item.ItemType); writer.Write(item.Prefix); }
        }
        writer.Flush(); return Convert.ToHexString(SHA256.HashData(memory.ToArray()));
    }
    private static string RandomHash(JsonElement physical)
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        writer.Write(physical.GetProperty("cursor").GetUInt32()); foreach (JsonElement slot in physical.GetProperty("slots").EnumerateArray()) writer.Write(slot.GetInt32());
        writer.Flush(); return Convert.ToHexString(SHA256.HashData(memory.ToArray()));
    }
    private static JsonDocument ReadSource()
    {
        using Stream compressed = typeof(GenerationChestSparseSlots1458Tests).Assembly.GetManifestResourceStream("GenerationChestSparseSlots1458")!;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int PeekNext()
        {
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var clone = new VanillaUnifiedRandom1458(0);
            var cursor = typeof(VanillaUnifiedRandom1458).GetField("inext", fields)!;
            var slots = typeof(VanillaUnifiedRandom1458).GetField("seedArray", fields)!;
            cursor.SetValue(clone, cursor.GetValue(random)); Array.Copy((int[])slots.GetValue(random)!, (int[])slots.GetValue(clone)!, 56);
            return clone.Next();
        }
        public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
