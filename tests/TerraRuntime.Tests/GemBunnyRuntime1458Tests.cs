using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GemBunnyRuntime1458Tests
{
    [Fact]
    public void Reachable_inner_sequences_match_original_AI007_motion_states_rng_and_completion()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GemBunnyRuntimeLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var fixture = JsonDocument.Parse(gzip);
        int sequences = 0, updates = 0;
        foreach (var group in fixture.RootElement.EnumerateArray().Where(row => !row.GetProperty("outer").GetBoolean())
                     .GroupBy(row => (Type: row.GetProperty("type").GetInt32(), Scene: row.GetProperty("scene").GetString()!)))
        {
            sequences++;
            var tiles = Scene(group.Key.Scene);
            var terrain = new VanillaGemBunnyTerrain1458(tiles);
            var first = group.First().GetProperty("before");
            var state = State(first);
            var random = new VanillaUnifiedRandom1458(1458);
            int target = first.GetProperty("target").GetInt32();
            foreach (var row in group)
            {
                updates++;
                var before = row.GetProperty("before");
                Assert.Equal(before.GetProperty("next").GetInt32(), random.Clone().Next());
                AssertState(before, state);
                state.Force = false;
                bool targetInitialization = target == 255;
                if (targetInitialization)
                {
                    // Original TargetClosest sees the independently configured living player0 at660,1558.
                    target = 0;
                    state.Direction = state.X < 660f ? 1 : state.X > 660f ? -1 : state.Direction;
                    if (state.HomeX == -1) state.HomeX = (int)((state.X + state.Width / 2f) / 16f);
                }
                else if (state.HomeX == -1 && state.HomeY == -1 && state.Vy == 0f)
                {
                    state.HomeX = (int)(state.X + state.Width * .5f) / 16;
                    state.HomeY = (int)(state.Y + state.Height + 4f) / 16;
                }
                terrain.FindRestingSpot(state.HomeX, state.HomeY, group.Key.Scene != "night", [],
                    out state.RestingX, out state.RestingY);
                state.ShelterWeather = group.Key.Scene == "night";
                var danger = new GemBunnyDanger1458(group.Key.Scene is "danger" or "dangerBlocked", -1, false);
                Assert.True(state.TryStep(terrain, danger, new SystemVanillaNpcRandom(random)));
                Assert.True(terrain.IsCurrent);
                var expected = row.GetProperty("after");
                AssertState(expected, state);
                Assert.Equal(expected.GetProperty("target").GetInt32(), target);
                Assert.Equal(expected.GetProperty("homeX").GetInt32(), state.HomeX);
                Assert.Equal(expected.GetProperty("homeY").GetInt32(), state.HomeY);
                Assert.Equal(expected.GetProperty("forced").GetBoolean(), state.Force || targetInitialization);
                Assert.Equal(expected.GetProperty("next").GetInt32(), random.Clone().Next());
            }
        }
        Assert.Equal(84, sequences);
        Assert.Equal(252, updates);
    }

    [Fact]
    public void Terrain_plan_rejects_a_changed_tile_section_and_preserves_canonical_collision_owner()
    {
        var tiles = Scene("wall");
        var terrain = new VanillaGemBunnyTerrain1458(tiles);
        var step = terrain.StepUp(639f, 1580f, .57f, 0f, 18, 20, true);
        Assert.True(terrain.IsCurrent);
        Assert.Equal(VanillaWorldPlayerStepCollision.StepUp(tiles, 639f, 1580f, .57f, 18, 20,
            holdsMatching: true, npcSpecialChecks: true).PositionY, step.Y);
        tiles.Set(41, 98, default);
        Assert.False(terrain.IsCurrent);
    }

    private static VanillaGemBunnyAi1458 State(JsonElement row) => new()
    {
        X = F(row, "x"), Y = F(row, "y"), Vx = F(row, "vx"), Vy = F(row, "vy"),
        OldX = F(row, "oldX"), OldY = F(row, "oldY"), OldVx = F(row, "oldVx"), OldVy = F(row, "oldVy"),
        Width = row.GetProperty("width").GetInt32(), Height = row.GetProperty("height").GetInt32(),
        HomeX = row.GetProperty("homeX").GetInt32(), HomeY = row.GetProperty("homeY").GetInt32(),
        Homeless = row.GetProperty("homeless").GetBoolean(), Direction = row.GetProperty("direction").GetInt32(),
        Life = row.GetProperty("life").GetInt32(), LifeMax = row.GetProperty("lifeMax").GetInt32(),
        Wet = row.GetProperty("wet").GetBoolean(), Ai = Ai(row.GetProperty("ai")),
        Local = Ai(row.GetProperty("local")), Gravity = .3f
    };

    private static void AssertState(JsonElement expected, VanillaGemBunnyAi1458 actual)
    {
        Assert.Equal(F(expected, "x"), actual.X);
        Assert.Equal(F(expected, "y"), actual.Y);
        Assert.Equal(F(expected, "vx"), actual.Vx);
        Assert.Equal(F(expected, "vy"), actual.Vy);
        Assert.Equal(expected.GetProperty("direction").GetInt32(), actual.Direction);
        Assert.Equal(Ai(expected.GetProperty("ai")), actual.Ai);
        Assert.Equal(Ai(expected.GetProperty("local")), actual.Local);
    }

    private static float F(JsonElement value, string name) => value.GetProperty(name).GetSingle();
    private static NpcAiState Ai(JsonElement row) => new(row[0].GetSingle(), row[1].GetSingle(), row[2].GetSingle(), row[3].GetSingle());

    internal static WorldTileStore Scene(string scene)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 200));
        for (int x = 0; x < 100; x++)
            for (int y = 0; y < 200; y++)
            {
                bool solid = y == 100 || (scene == "step" && x == 41 && y == 99) ||
                    (scene is "wall" or "ceiling" or "dangerBlocked" && x == 41 && y is >= 97 and <= 99) ||
                    (scene is "ceiling" or "dangerBlocked" && x == 40 && y == 95);
                bool wet = scene == "wet" && x is >= 38 and <= 44 && y is >= 97 and < 100;
                if (solid || wet)
                    tiles.Set(x, y, new WorldTile { Type = solid ? scene == "platform" ? (ushort)19 : (ushort)1 : (ushort)0,
                        Flags = solid ? WorldTileFlags.Active : default, LiquidAmount = wet ? byte.MaxValue : (byte)0 });
            }
        return tiles;
    }
}
