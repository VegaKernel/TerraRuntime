using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GroundCombatProjectileUpdates1458Tests
{
    public static IEnumerable<object[]> OriginalCases() => Rows("GroundCombatProjectileUpdatesLinux1458").Concat(Rows("GroundCombatProjectileInactiveTailLinux1458")).Concat(Rows("GroundCombatProjectileSlopeOverlapLinux1458")).Concat(Rows("GroundCombatProjectileLiquidHistoryLinux1458"));

    private static IEnumerable<object[]> Rows(string resource)
    {
        using var source = typeof(GroundCombatProjectileUpdates1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalCases))]
    public void Usual_updates_match_actual_original_motion_ai_lifecycle_and_full_random_state(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var initial = row.GetProperty("initial");
        var shot = Shot(initial);
        Assert.True(VanillaDefinitionCatalog.TryGet(shot.Type, out var definition));
        Assert.Equal(initial.GetProperty("width").GetInt32(), definition.Width);
        Assert.Equal(initial.GetProperty("height").GetInt32(), definition.Height);
        Assert.Equal(initial.GetProperty("aiStyle").GetInt32(), definition.AiStyle.Value);
        Assert.Equal(initial.GetProperty("tileCollide").GetBoolean(), definition.TileCollide);
        Assert.Equal(initial.GetProperty("ignoreWater").GetBoolean(), definition.IgnoreWater);
        Assert.True(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(shot.Type, out int penetration));
        Assert.Equal(initial.GetProperty("penetrate").GetInt32(), penetration);
        int subupdates = initial.GetProperty("extraUpdates").GetInt32() + 1;
        Assert.Equal(subupdates, VanillaProjectileUpdateFacts.GetExtraUpdates(shot.Type) + 1);
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        AssertRandom(row.GetProperty("initialRandom"), random);
        var capture = new Capture(random);
        var tiles = Terrain(row);
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        stepper.SetWindPhysics(row.GetProperty("wind").GetBoolean(), row.GetProperty("windSpeed").GetSingle(), row.GetProperty("windStrength").GetSingle());
        var store = new RuntimeProjectileStore(2);
        var state = State(in shot);
        Assert.True(store.TrySpawnVanilla(in state, initial.GetProperty("timeLeft").GetInt32(), out shot));
        Assert.True(store.TryCommitSimulationStep(shot.Handle, in state, initial.GetProperty("timeLeft").GetInt32(),
            Liquid(initial), Local(initial), out shot, out bool initiallyExpired));
        Assert.False(initiallyExpired);
        var executor = new RuntimeProjectileStateExecutor(store, capture, capture);
        byte retainedWetCount = 0;
        foreach (var frame in row.GetProperty("frames").EnumerateArray())
        {
            // Main only visits active projectiles. Later explicit inactive oracle
            // calls are retained diagnostics, not additional runtime ticks.
            if (!frame.GetProperty("before").GetProperty("active").GetBoolean()) break;
            AssertRandom(frame.GetProperty("rngBefore"), random);
            capture.Subupdates = [];
            var summary = executor.Tick(stepper);
            Assert.Equal(1, summary.Applied);
            Assert.Equal(0, summary.Rejected);
            Assert.NotEmpty(capture.Subupdates);
            var next = capture.Subupdates[^1];
            shot = capture.Final;
            foreach (var committedStep in capture.Subupdates)
                retainedWetCount = committedStep.SourceWetCountOverride ?? retainedWetCount;
            var expected = frame.GetProperty("after");
            bool active = expected.GetProperty("active").GetBoolean();
            Assert.Equal(active, store.TryGet(shot.Handle, out var live));
            if (active)
            {
                Assert.Equal(shot, live);
                Assert.True(store.TryGetLifecycle(shot.Handle, out var committedLifecycle));
                Assert.Equal(retainedWetCount, committedLifecycle.SourceWetCount);
            }
            Assert.Equal(active, next.TimeLeft > 0);
            AssertState(expected, in shot);
            // World-bounds removal preserves original raw timeLeft; runtime zero
            // represents removal and must not run the Kill RNG hook.
            if (next.TerminationReason != ProjectileSimulationTerminationReason.WorldBounds)
                Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), next.TimeLeft);
            Assert.Equal(Liquid(expected), next.Liquid ?? Liquid(initial));
            Assert.Equal(Local(expected), next.LocalAi ?? Local(initial));
            Assert.Equal(expected.GetProperty("penetrate").GetInt32(), next.PenetrateOverride ?? penetration);
            if (expected.TryGetProperty("wetCount", out var expectedWetCount))
                Assert.Equal(expectedWetCount.GetInt32(), retainedWetCount);
            Assert.Equal(active ? 0 : 1, capture.Terminations);
            AssertRandom(frame.GetProperty("rngAfter"), random);
        }
    }

    private sealed class Capture : IProjectileSimulationCommitSink, IProjectileSimulationPrePublicationCommitSink, IProjectileTerminationCommitSink
    {
        private readonly RuntimeProjectileSimulationCommitSink simulation;
        private readonly RuntimeProjectileTerminationEffectSink termination;
        public ProjectileSnapshot Final;
        public ProjectileSimulationStepResult[] Subupdates = [];
        public int Terminations;
        public Capture(VanillaUnifiedRandom1458 random)
        {
            simulation = new(new RuntimeProjectileLiveChildSpawnQueue(2), new RuntimeCultistLightningArcTrailRegistry(2),
                collisionTileCuts: new RuntimeProjectileCollisionTileCut1458(random), ordinaryAmmoRandom: random);
            termination = new(new RuntimeProjectileExplosionQueue(2, random), new RuntimeProjectileTileExplosionQueue(2),
                new RuntimeProjectileChildSpawnQueue(2), random: random);
        }
        public bool OwnsCollisionTileCutOffers => simulation.OwnsCollisionTileCutOffers;
        public bool OwnsShimmerVisualOffers => simulation.OwnsShimmerVisualOffers;
        public bool OwnsOrdinaryAmmoSourceOffers => simulation.OwnsOrdinaryAmmoSourceOffers;
        public void ProjectileSimulationCommittedBeforePublication(ReadOnlySpan<ProjectileSimulationStepResult> steps) =>
            simulation.ProjectileSimulationCommittedBeforePublication(steps);
        public void ProjectileSimulationCommitted(in ProjectileSnapshot initial, in ProjectileLifecycleState lifecycle,
            ReadOnlySpan<ProjectileSimulationStepResult> steps, in ProjectileSnapshot final, bool expired)
        {
            Final = final; Subupdates = steps.ToArray();
            simulation.ProjectileSimulationCommitted(in initial, in lifecycle, steps, in final, expired);
        }
        public void ProjectileTerminated(in ProjectileTerminationCommit commit)
        { Terminations++; termination.ProjectileTerminated(in commit); }
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
            if ((scene is "wall" or "retained-fast-wall" || scene.EndsWith("-wall", StringComparison.Ordinal)) && x == 50 ||
                (scene is "floor" or "retained-fast-floor" or "halfbrick" || scene.StartsWith("slope", StringComparison.Ordinal)) && y == 100 && x is >= 30 and <= 70)
                tile = new() { Type = 1, Flags = WorldTileFlags.Active,
                    Shape = (byte)(scene == "halfbrick" ? 1 : scene.StartsWith("slope", StringComparison.Ordinal) ? int.Parse(scene[5..6]) + 1 : 0) };
            if (scene is "water" or "honey" && y is >= 70 and <= 78)
                tile = tile with { LiquidAmount = 255, LiquidKind = scene == "honey" ? WorldLiquidKind.Honey : WorldLiquidKind.Water };
            if ((scene.StartsWith("exit-", StringComparison.Ordinal) || scene.StartsWith("enter-", StringComparison.Ordinal) ||
                scene.StartsWith("mixed-", StringComparison.Ordinal)) && y is >= 70 and <= 78)
            {
                var kind = scene.EndsWith("honey", StringComparison.Ordinal) ? WorldLiquidKind.Honey :
                    scene.EndsWith("lava", StringComparison.Ordinal) ? WorldLiquidKind.Lava :
                    scene.EndsWith("shimmer", StringComparison.Ordinal) ? WorldLiquidKind.Shimmer : WorldLiquidKind.Water;
                if (scene.StartsWith("mixed-", StringComparison.Ordinal) && x % 2 == 0) kind = WorldLiquidKind.Water;
                tile = tile with { LiquidAmount = 255, LiquidKind = kind };
            }
            if (tile.IsActive || tile.LiquidAmount > 0) tiles.Set(x, y, in tile);
        }
        return tiles;
    }

    private static ProjectileSnapshot Shot(JsonElement p) => new(new(0, new(1)), new(1), new(p.GetProperty("type").GetInt32()), 255,
        F(p.GetProperty("position")[0]), F(p.GetProperty("position")[1]), F(p.GetProperty("velocity")[0]), F(p.GetProperty("velocity")[1]),
        new(F(p.GetProperty("ai")[0]), F(p.GetProperty("ai")[1]), F(p.GetProperty("ai")[2])), 0,
        checked((short)p.GetProperty("damage").GetInt32()), F(p.GetProperty("knockback")), checked((short)p.GetProperty("originalDamage").GetInt32()));

    private static ProjectileStateUpdate State(in ProjectileSnapshot p) => new(p.Type, p.Spawner,
        p.PositionX, p.PositionY, p.VelocityX, p.VelocityY, p.Ai, p.BannerIdToRespondTo, p.Damage, p.KnockBack, p.OriginalDamage);

    private static void AssertState(JsonElement expected, in ProjectileSnapshot actual)
    {
        Assert.Equal(expected.GetProperty("position")[0].GetString(), Bits(actual.PositionX));
        Assert.Equal(expected.GetProperty("position")[1].GetString(), Bits(actual.PositionY));
        Assert.Equal(expected.GetProperty("velocity")[0].GetString(), Bits(actual.VelocityX));
        Assert.Equal(expected.GetProperty("velocity")[1].GetString(), Bits(actual.VelocityY));
        Assert.Equal(expected.GetProperty("ai")[0].GetString(), Bits(actual.Ai.Ai0));
        Assert.Equal(expected.GetProperty("ai")[1].GetString(), Bits(actual.Ai.Ai1));
        Assert.Equal(expected.GetProperty("ai")[2].GetString(), Bits(actual.Ai.Ai2));
        Assert.Equal(expected.GetProperty("damage").GetInt32(), actual.Damage);
        Assert.Equal(expected.GetProperty("originalDamage").GetInt32(), actual.OriginalDamage);
        Assert.Equal(expected.GetProperty("knockback").GetString(), Bits(actual.KnockBack));
    }

    private static ProjectileLiquidState Liquid(JsonElement p) => new(p.GetProperty("wet").GetBoolean(),
        p.GetProperty("lavaWet").GetBoolean(), p.GetProperty("honeyWet").GetBoolean(), p.GetProperty("shimmerWet").GetBoolean());
    private static ProjectileLocalAiState Local(JsonElement p) => new(F(p.GetProperty("localAi")[0]), F(p.GetProperty("localAi")[1]), F(p.GetProperty("localAi")[2]));
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

