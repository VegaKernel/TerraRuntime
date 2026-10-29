using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RangedCohortUsualProjectiles1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var source = typeof(RangedCohortUsualProjectiles1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.RangedCohortUsualProjectilesLinux1458.v1.json.gz")!;
        using var zip = new GZipStream(source, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(zip);
        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Composed_authority_matches_original_usual_projectile_history_without_live_entropy_staging(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var input = row.GetProperty("initial");
        var tiles = Terrain(row);
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var shots = new RuntimeProjectileStore(2);
        var world = new ServerRuntimeState(worldTiles: tiles, projectiles: shots,
            naturalSpawnRandom: new SystemVanillaNpcRandom(random));
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(world)!;
        var stepper = (VanillaProjectileWorldStateStepper)typeof(ProjectileAuthority).GetField("stepper", flags)!
            .GetValue(graph.Projectiles)!;
        stepper.SetWindPhysics(row.GetProperty("wind").GetBoolean(), row.GetProperty("windSpeed").GetSingle(),
            row.GetProperty("windStrength").GetSingle());
        var observer = new TerminationObserver();
        typeof(RuntimeProjectileTerminationEffectSink).GetField("falling", flags)!
            .SetValue(graph.Projectiles.TerminationEffects, observer);
        var state = State(input);
        Assert.True(VanillaDefinitionCatalog.TryGet(state.Type, out var definition));
        Assert.Equal(input.GetProperty("width").GetInt32(), definition.Width);
        Assert.Equal(input.GetProperty("height").GetInt32(), definition.Height);
        Assert.Equal(input.GetProperty("aiStyle").GetInt32(), definition.AiStyle.Value);
        Assert.Equal(input.GetProperty("tileCollide").GetBoolean(), definition.TileCollide);
        Assert.Equal(input.GetProperty("ignoreWater").GetBoolean(), definition.IgnoreWater);
        Assert.Equal(input.GetProperty("extraUpdates").GetInt32(), VanillaProjectileUpdateFacts.GetExtraUpdates(state.Type));
        Assert.True(shots.TrySpawnVanilla(in state, input.GetProperty("timeLeft").GetInt32(), out var initial));
        // Source NewProjectile carries an NPC source while that source NPC is inactive.
        // The actor reads provenance only; this fixture does not invent a live NPC-state guard.
        Assert.True(shots.TrySetServerNpcSource(initial.Handle, new NpcHandle(0, new NpcGeneration(1))));
        Assert.True(shots.TryCommitSimulationStep(initial.Handle, in state, input.GetProperty("timeLeft").GetInt32(),
            Liquid(input), Local(input), out initial, out bool retired));
        Assert.False(retired);
        AssertState(input, in initial);
        AssertRandom(row.GetProperty("initialRandom"), random);
        var retained = initial;
        foreach (var frame in row.GetProperty("frames").EnumerateArray())
        {
            var before = frame.GetProperty("before");
            Assert.Equal(before.GetProperty("active").GetBoolean(), shots.TryGet(initial.Handle, out var actualBefore));
            if (before.GetProperty("active").GetBoolean()) AssertState(before, in actualBefore);
            AssertRandom(frame.GetProperty("rngBefore"), random);
            Assert.True(graph.Projectiles.TryTickState());
            graph.ProjectilePlayerCombat.Tick(graph.Projectiles.PendingExplosions);
            var expected = frame.GetProperty("after");
            bool active = expected.GetProperty("active").GetBoolean();
            Assert.Equal(active, shots.TryGet(initial.Handle, out var actual));
            if (active) retained = actual;
            else if (observer.Last is { } terminal) retained = terminal.FinalProjectile;
            AssertState(expected, in retained);
            Assert.True(shots.TryGetSourceDirection(initial.Handle.Slot, out int direction));
            Assert.Equal(expected.GetProperty("direction").GetInt32(), direction);
            if (active)
            {
                Assert.True(shots.TryGetLifecycle(initial.Handle, out var life));
                Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
                Assert.Equal(Liquid(expected), life.Liquid);
                Assert.Equal(Local(expected), life.LocalAi);
                Assert.Equal(expected.GetProperty("oldVelocity")[0].GetString(), Bits(life.OldVelocityX));
                Assert.Equal(expected.GetProperty("oldVelocity")[1].GetString(), Bits(life.OldVelocityY));
                Assert.Equal(expected.GetProperty("npcProj").GetBoolean(), life.NpcProjectile);
            }
            // The inactive lifecycle is not exposed by the live-store API. In particular,
            // raw source world-bounds timeLeft is retained while runtime removal uses zero.
            // Visual alpha/frame/rotation and those unrepresented inactive fields remain diagnostics.
            Assert.Equal(active ? 0 : 1, observer.Count);
            AssertRandom(frame.GetProperty("rngAfter"), random);
        }
    }

    private sealed class TerminationObserver : IProjectileTerminationCommitSink
    {
        internal ProjectileTerminationCommit? Last;
        internal int Count;
        public void ProjectileTerminated(in ProjectileTerminationCommit termination) { Last = termination; Count++; }
    }

    private static WorldTileStore Terrain(JsonElement row)
    {
        var tiles = new WorldTileStore(new(row.GetProperty("worldWidth").GetInt32(), row.GetProperty("worldHeight").GetInt32()));
        Assert.True(tiles.TryAttachWorldSurface(row.GetProperty("surface").GetDouble()));
        string scene = row.GetProperty("scene").GetString()!;
        for (int x = 0; x < tiles.Dimensions.WidthTiles; x++)
        for (int y = 0; y < tiles.Dimensions.HeightTiles; y++)
        {
            WorldTile tile = default;
            if ((scene is "wall" or "retained-fast-wall") && x == 50 ||
                (scene is "floor" or "retained-fast-floor" or "halfbrick" || scene.StartsWith("slope", StringComparison.Ordinal)) &&
                y == 100 && x is >= 30 and <= 70)
                tile = new() { Type = 1, Flags = WorldTileFlags.Active, Shape = (byte)(scene == "halfbrick" ? 1 :
                    scene.StartsWith("slope", StringComparison.Ordinal) ? int.Parse(scene[5..6]) + 1 : 0) };
            if (scene is "water" or "honey" && y is >= 70 and <= 78)
                tile = tile with { LiquidAmount = 255, LiquidKind = scene == "honey" ? WorldLiquidKind.Honey : WorldLiquidKind.Water };
            if (tile.IsActive || tile.LiquidAmount > 0) tiles.Set(x, y, in tile);
        }
        return tiles;
    }

    private static ProjectileStateUpdate State(JsonElement p) => new(new(p.GetProperty("type").GetInt32()), 255,
        F(p.GetProperty("position")[0]), F(p.GetProperty("position")[1]), F(p.GetProperty("velocity")[0]), F(p.GetProperty("velocity")[1]),
        new(F(p.GetProperty("ai")[0]), F(p.GetProperty("ai")[1]), F(p.GetProperty("ai")[2])), 0,
        checked((short)p.GetProperty("damage").GetInt32()), F(p.GetProperty("knockback")),
        checked((short)p.GetProperty("originalDamage").GetInt32()));
    private static ProjectileLiquidState Liquid(JsonElement p) => new(p.GetProperty("wet").GetBoolean(),
        p.GetProperty("lavaWet").GetBoolean(), p.GetProperty("honeyWet").GetBoolean(), p.GetProperty("shimmerWet").GetBoolean());
    private static ProjectileLocalAiState Local(JsonElement p) => new(F(p.GetProperty("localAi")[0]), F(p.GetProperty("localAi")[1]), F(p.GetProperty("localAi")[2]));
    private static void AssertState(JsonElement p, in ProjectileSnapshot actual)
    {
        Assert.Equal(p.GetProperty("type").GetInt32(), actual.Type.Value);
        Assert.Equal(p.GetProperty("owner").GetInt32(), actual.Spawner);
        Assert.Equal(p.GetProperty("position")[0].GetString(), Bits(actual.PositionX));
        Assert.Equal(p.GetProperty("position")[1].GetString(), Bits(actual.PositionY));
        Assert.Equal(p.GetProperty("velocity")[0].GetString(), Bits(actual.VelocityX));
        Assert.Equal(p.GetProperty("velocity")[1].GetString(), Bits(actual.VelocityY));
        Assert.Equal(p.GetProperty("ai")[0].GetString(), Bits(actual.Ai.Ai0));
        Assert.Equal(p.GetProperty("ai")[1].GetString(), Bits(actual.Ai.Ai1));
        Assert.Equal(p.GetProperty("ai")[2].GetString(), Bits(actual.Ai.Ai2));
        Assert.Equal(p.GetProperty("damage").GetInt32(), actual.Damage);
        Assert.Equal(p.GetProperty("originalDamage").GetInt32(), actual.OriginalDamage);
        Assert.Equal(p.GetProperty("knockback").GetString(), Bits(actual.KnockBack));
    }
    private static float F(JsonElement value) => BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(), 16));
    private static string Bits(float value) => BitConverter.SingleToUInt32Bits(value).ToString("x8");
    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
        Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
    }
}
