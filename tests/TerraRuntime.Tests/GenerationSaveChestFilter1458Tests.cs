using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationSaveChestFilter1458Tests
{
    private static readonly JsonDocument Golden = ReadGolden();
    public static IEnumerable<object[]> OriginalCases => Golden.RootElement.EnumerateArray()
        .Select(x => new object[] { x.GetProperty("definition").GetProperty("Key").GetString()! });

    [Theory, MemberData(nameof(OriginalCases))]
    public void Late_persistence_filter_matches_actual_SaveChests_and_LoadChests_order(string key)
    {
        JsonElement row = Golden.RootElement.EnumerateArray().Single(x => x.GetProperty("definition").GetProperty("Key").GetString() == key);
        JsonElement definition = row.GetProperty("definition");
        var workspace = new Workspace(80, 120);
        Register(workspace, 10, "before", true, 0); Register(workspace, 40, "target", definition.GetProperty("Filled").GetBoolean(), 100);
        Register(workspace, 60, "after", true, 200);
        int targetX = definition.GetProperty("X").GetInt32(), targetY = definition.GetProperty("Y").GetInt32();
        if (targetX != 40 || targetY != 40)
        {
            // The public fresh registration refuses these coordinates. Exercise the source I/O boundary on
            // the same canonical private list after valid registration, without widening production admission.
            var owner = (List<WorldChest>)typeof(Workspace).GetField("generatedChests", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
            owner[1] = owner[1] with { X = targetX, Y = targetY };
            WorldTile empty = default;
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++) workspace.TileStore.Set(40 + dx, 40 + dy, in empty);
        }
        MutateTarget(workspace, definition);
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
        AssertSnapshot(row.GetProperty("before"), workspace.CaptureGeneratedChests());
        AssertSnapshot(row.GetProperty("before"), workspace.CaptureGeneratedChests()); // Capture remains pure.

        Assert.Equal(row.GetProperty("retained").GetBoolean() ? 0 : 1, workspace.PrepareVanillaChestPersistence());
        // Vanilla's live array can retain index gaps after SaveChests. File order becomes dense on LoadChests.
        AssertSnapshot(row.GetProperty("afterLoad"), workspace.CaptureGeneratedChests());
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(0, workspace.PrepareVanillaChestPersistence());
        AssertSnapshot(row.GetProperty("afterLoad"), workspace.CaptureGeneratedChests());
    }

    private static void Register(Workspace workspace, int x, string name, bool filled, int offset)
    {
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
        {
            var tile = new WorldTile { Type = 21, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x + dx, 40 + dy, in tile);
        }
        WorldChestItem[] items = Enumerable.Range(0, 40).Select(slot => filled ? new WorldChestItem(offset + slot + 1, 9, 0) : default).ToArray();
        Assert.True(workspace.TryAddGeneratedChest(x, 40, name, items));
    }
    private static void MutateTarget(Workspace workspace, JsonElement definition)
    {
        int x = definition.GetProperty("X").GetInt32(), y = definition.GetProperty("Y").GetInt32();
        int[] types = definition.GetProperty("Types").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        string frames = definition.GetProperty("Frames").GetString()!, flags = definition.GetProperty("Flags").GetString()!;
        for (int corner = 0; corner < 4; corner++)
        {
            int dx = corner / 2, dy = corner % 2;
            if ((uint)(x + dx) >= 80 || (uint)(y + dy) >= 120) continue;
            var tile = new WorldTile
            {
                Type = (ushort)types[corner],
                Flags = WorldTileFlags.Active,
                FrameX = (short)(frames == "allzero" ? 0 : frames == "mismatched-axis" ? dy * 36 : frames == "negative-and-large" ? (corner == 0 ? -1 : 32000 - corner) : dx * 18),
                FrameY = (short)(frames == "allzero" ? 0 : frames == "mismatched-axis" ? dx * 72 : frames == "negative-and-large" ? (corner == 0 ? -18 : 30000 - corner) : dy * 18)
            };
            if (corner == definition.GetProperty("Corner").GetInt32()) switch (flags)
                {
                    case "inactive": tile.Flags &= ~WorldTileFlags.Active; break;
                    case "stone": tile.Type = 1; break;
                    case "plant": tile.Type = 3; break;
                    case "dirt": tile.Type = 0; break;
                    case "actuated": tile.Flags |= WorldTileFlags.Inactive; break;
                    case "half": tile.Shape = 1; break;
                    case "slope1": tile.Shape = 2; break;
                    case "slope4": tile.Shape = 5; break;
                    case "half-slope4": tile.Shape = 5; tile.Flags |= WorldTileFlags.HalfBrickWithSlope; break;
                    case "lava": tile.LiquidAmount = 255; tile.LiquidKind = WorldLiquidKind.Lava; break;
                    case "wires-paint-coating":
                        tile.Flags |= WorldTileFlagMasks.Wires | WorldTileFlags.Actuator | WorldTileFlagMasks.Visibility | WorldTileFlagMasks.Fullbright;
                        tile.TileColor = 3; tile.Wall = 7; tile.WallColor = 4; break;
                }
            workspace.TileStore.Set(x + dx, y + dy, in tile);
        }
    }
    private static void AssertSnapshot(JsonElement expected, WorldChest[] actual)
    {
        JsonElement[] rows = expected.EnumerateArray().ToArray(); Assert.Equal(rows.Length, actual.Length);
        for (int index = 0; index < rows.Length; index++)
        {
            Assert.Equal(rows[index].GetProperty("slot").GetInt16(), actual[index].SlotId);
            Assert.Equal(rows[index].GetProperty("x").GetInt32(), actual[index].X); Assert.Equal(rows[index].GetProperty("y").GetInt32(), actual[index].Y);
            Assert.Equal(rows[index].GetProperty("name").GetString(), actual[index].Name); Assert.Equal(40, actual[index].Items.Length);
            JsonElement[] items = rows[index].GetProperty("items").EnumerateArray().ToArray();
            for (int slot = 0; slot < 40; slot++)
            {
                Assert.Equal(items[slot].GetProperty("type").GetInt32(), actual[index].Items[slot].ItemType);
                Assert.Equal(items[slot].GetProperty("stack").GetInt32(), actual[index].Items[slot].Stack);
                Assert.Equal(items[slot].GetProperty("prefix").GetByte(), actual[index].Items[slot].Prefix);
            }
        }
    }
    private static JsonDocument ReadGolden()
    {
        using var stream = typeof(GenerationSaveChestFilter1458Tests).Assembly.GetManifestResourceStream("GenerationSaveChestFilter1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    }
}
