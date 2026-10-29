using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GroundCombatProjectileBirthLiquid1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        using var stream = typeof(GroundCombatProjectileBirthLiquid1458Tests).Assembly.GetManifestResourceStream("GroundCombatProjectileBirthLiquidLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalCases))]
    public void Captured_source_birth_liquid_is_adopted_before_the_real_birth_observer(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var expected = row.GetProperty("initial");
        var f = new Fixture(row.GetProperty("type").GetInt32(), row.GetProperty("scene").GetString()!);
        Assert.True(f.Environment.TryCapture(in f.Before, new(), out var world));
        using (world)
        {
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            var beforeRandom = random.Clone();
            f.Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
            Assert.True(f.Npcs.TryCreateAiSpawnPreview(in f.Before, random, out var preview));
            Assert.NotNull(preview);
            Assert.True(world.TryStageStatus(preview, new SystemVanillaNpcRandom(preview.Random), out var staged));
            var intent = Intent(expected);
            Assert.True(world.TryStageProjectiles([intent]));
            Assert.Equal(0, f.Projectiles.ActiveCount);
            Assert.Equal(0, f.Sink.Births);
            var state = State(in staged);
            var raw = VanillaNpcRawPlayer1458.Constructor(0);
            Assert.True(world.TryFinishMotion(preview, in f.Before, in state, in raw, out var final));
            Assert.True(world.TryPrepareAdoption(in final));
            Assert.True(f.Npcs.TryUpdateUnpublished(f.Before.Handle, in final, out var adopted));
            Assert.True(world.TryAdoptStatus(in adopted));
            Assert.Equal(1, f.Projectiles.ActiveCount);
            Assert.Equal(0, f.Sink.Births);
            f.Sink.Callback = shot =>
            {
                Assert.True(f.Npcs.TryGet(adopted.Handle, out var current));
                Assert.Equal(adopted, current);
                Assert.True(f.Projectiles.TryGetServerNpcSource(shot.Handle, out var source));
                Assert.Equal(adopted.Handle, source);
                Assert.True(f.Projectiles.TryGetLifecycle(shot.Handle, out var lifecycle));
                Assert.Equal(Liquid(expected), lifecycle.Liquid);
                Assert.Equal(expected.GetProperty("wetCount").GetInt32(), lifecycle.SourceWetCount);
                Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), lifecycle.TimeLeft);
                Assert.Equal(expected.GetProperty("npcProj").GetBoolean(), lifecycle.NpcProjectile);
                Assert.Equal(expected.GetProperty("originalDamage").GetInt32(), shot.OriginalDamage);
                Assert.Equal(F(expected.GetProperty("position")[0]), shot.PositionX);
                Assert.Equal(F(expected.GetProperty("position")[1]), shot.PositionY);
            };
            world.PublishStatus();
            Assert.Equal(1, f.Sink.Births);
            Assert.True(beforeRandom.HasSameState(random));
            world.PublishStatus();
            Assert.Equal(1, f.Sink.Births);
        }
    }

    [Fact]
    public void Changed_birth_liquid_section_refuses_prepared_resources_before_callbacks()
    {
        foreach (bool change in new[] { false, true })
        {
            var f = new Fixture(471, "honey");
            Assert.True(f.Environment.TryCapture(in f.Before, new(), out var world));
            using (world)
            {
                var random = new VanillaUnifiedRandom1458(1458);
                f.Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
                Assert.True(f.Npcs.TryCreateAiSpawnPreview(in f.Before, random, out var preview));
                Assert.NotNull(preview);
                Assert.True(world.TryStageStatus(preview, new SystemVanillaNpcRandom(preview.Random), out var staged));
                var intent = new NpcAiProjectileIntent(new(471), 640f, 1150f, 3.25f, -2.5f, 12, 0f);
                Assert.True(world.TryStageProjectiles([intent]));
                var state = State(in staged);
                var raw = VanillaNpcRawPlayer1458.Constructor(0);
                Assert.True(world.TryFinishMotion(preview, in f.Before, in state, in raw, out var final));
                Assert.True(world.TryPrepareAdoption(in final));
                var actorSection = TerrariaSectionGeometry.FromTile(f.Tiles.Dimensions, 218, 98);
                var birthSection = TerrariaSectionGeometry.FromTile(f.Tiles.Dimensions, 40, 72);
                Assert.NotEqual(actorSection, birthSection);
                long actorVersion = f.Tiles.GetSectionVersion(actorSection);
                if (change) f.Tiles.Set(40, 72, new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Water });
                Assert.Equal(actorVersion, f.Tiles.GetSectionVersion(actorSection));
                Assert.Equal(!change, world.IsCurrent);
                if (change)
                {
                    Assert.False(world.TryAdoptStatus(in f.Before));
                    Assert.True(f.Npcs.TryGet(f.Before.Handle, out var current));
                    Assert.Equal(f.Before, current);
                }
                else
                {
                    Assert.True(f.Npcs.TryUpdateUnpublished(f.Before.Handle, in final, out var adopted));
                    Assert.True(world.TryAdoptStatus(in adopted));
                    world.PublishStatus();
                }
                Assert.Equal(change ? 0 : 1, f.Projectiles.ActiveCount);
                Assert.Equal(change ? 0 : 1, f.Sink.Births);
            }
        }
    }

    private sealed class Fixture
    {
        internal readonly WorldTileStore Tiles = new(new(600, 200));
        internal readonly RuntimeNpcStore Npcs = new(2);
        internal readonly Sink Sink = new();
        internal readonly RuntimeProjectileStore Projectiles;
        internal readonly VanillaNpcPhaseEnvironment1458 Environment;
        internal readonly NpcSnapshot Before;
        internal Fixture(int type, string scene)
        {
            Assert.True(Tiles.TryAttachWorldSurface(80d));
            for (int x = 0; x < 600; x++)
            {
                Tiles.Set(x, 100, new() { Type = 1, Flags = WorldTileFlags.Active });
                if (scene != "dry") for (int y = 70; y <= 78; y++)
                    Tiles.Set(x, y, new() { LiquidAmount = scene.StartsWith("water64", StringComparison.Ordinal) ? (byte)64 : (byte)255,
                        LiquidKind = scene switch { "honey" => WorldLiquidKind.Honey, "lava" => WorldLiquidKind.Lava,
                            "shimmer" => WorldLiquidKind.Shimmer, _ => WorldLiquidKind.Water } });
            }
            Projectiles = new(2, Sink);
            int npcType = type switch { 436 => 381, 438 => 382, 471 => 449, 508 => 481, _ => 520 };
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(npcType), out var definition));
            Assert.True(definition.TryResolveHitbox(NpcSimulationState.Initial with { Scale = definition.Scale }, out var body));
            var state = new NpcStateUpdate(npcType, checked((short)npcType), 3500f, 1600f - body.Height, 0f, 0f, 255, default,
                NpcSimulationState.Initial with { DirectionX = 1, DirectionY = 1, SpriteDirection = 1, FrameIndex = 0 });
            Assert.True(Npcs.TrySpawn(0, in state, out Before));
            var motion = new VanillaNpcWorldMotionAiStepper(new NoAi(), Tiles);
            Environment = new(Tiles, Npcs, Projectiles, new RuntimeNpcBuffStatus1458(Npcs), motion,
                () => new(true, true, false, false, false, false, 0), null);
        }
    }
    private sealed class NoAi : INpcAiStateStepper
    { public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; } }
    private sealed class Sink : IProjectileStateCommitSink
    {
        internal int Births;
        internal Action<ProjectileSnapshot>? Callback;
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot shot)
        { if (kind == ProjectileStateCommitKind.Spawn) { Births++; Callback?.Invoke(shot); } }
    }
    private static NpcAiProjectileIntent Intent(JsonElement p) => new(new(p.GetProperty("type").GetInt32()),
        F(p.GetProperty("position")[0]), F(p.GetProperty("position")[1]), F(p.GetProperty("velocity")[0]), F(p.GetProperty("velocity")[1]),
        p.GetProperty("damage").GetInt32(), F(p.GetProperty("knockback")));
    private static NpcStateUpdate State(in NpcSnapshot n) => new(n.Type, n.NetId, n.PositionX, n.PositionY,
        n.VelocityX, n.VelocityY, n.Target, n.Ai, n.Simulation);
    private static ProjectileLiquidState Liquid(JsonElement p) => new(p.GetProperty("wet").GetBoolean(), p.GetProperty("lavaWet").GetBoolean(),
        p.GetProperty("honeyWet").GetBoolean(), p.GetProperty("shimmerWet").GetBoolean());
    private static float F(JsonElement x) => BitConverter.UInt32BitsToSingle(Convert.ToUInt32(x.GetString(), 16));
}
