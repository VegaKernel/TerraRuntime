using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GemStaffProjectile1458Tests
{
    [Fact]
    public void Original_gem_projectile_motion_and_boundaries_preserve_source_state_and_randomness()
    {
        int motions = 0, targets = 0, boundaries = 0, geometry = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        foreach (string repeat in new[] { "First", "Repeat" })
        {
            bool windows = platform == "Windows";
            using (var document = Read("Motion", platform, repeat))
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var type = Type(row);
                var features = Features(row);
                var before = FinalizeState(row.GetProperty("defaults"));
                Assert.True(VanillaGemStaffProjectile1458.TryFinalize(type, features, in before, out var finalized));
                Assert.Equal(FinalizeState(row.GetProperty("finalized")), finalized);
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    var random = Random(step.GetProperty("beforeRandom"));
                    var motion = Motion(step.GetProperty("before"));
                    Assert.True(VanillaGemStaffProjectile1458.TryStepMotion(type, features, in motion,
                        false, [], random.Next, out var result, windows));
                    Assert.Equal(Motion(step.GetProperty("after")), result.State);
                    AssertRandom(step.GetProperty("afterRandom"), step.GetProperty("afterNext").GetInt32(), random);
                    motions++;
                }
            }
            using (var document = Read("Targets", platform, repeat))
            foreach (var row in document.RootElement.EnumerateArray())
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                var random = Random(step.GetProperty("beforeRandom"));
                var motion = Motion(step.GetProperty("before"));
                var candidates = step.GetProperty("candidates").EnumerateArray().Select(value =>
                    new GemStaffTarget1458(value.GetProperty("index").GetInt32(),
                        value.GetProperty("centerX").GetSingle(), value.GetProperty("centerY").GetSingle(),
                        value.GetProperty("canChase").GetBoolean(), value.GetProperty("visible").GetBoolean(),
                        value.GetProperty("locallyImmune").GetBoolean())).ToArray();
                float range = (Features(row) & (GemStaffFeatures1458.FastThenSlow | GemStaffFeatures1458.CanBounce)) != 0 ? 1000f : 500f;
                Assert.True(VanillaGemStaffProjectile1458.TrySelectTarget(candidates,
                    motion.PositionX + 5f, motion.PositionY + 5f, range, out int selected));
                Assert.Equal(step.GetProperty("selected").GetInt32(), selected);
                Assert.True(VanillaGemStaffProjectile1458.TryStepMotion(Type(row), Features(row), in motion,
                    row.GetProperty("local").GetBoolean(), candidates, random.Next, out var result, windows));
                Assert.Equal(Motion(step.GetProperty("after")), result.State);
                AssertRandom(step.GetProperty("afterRandom"), step.GetProperty("afterNext").GetInt32(), random);
                targets++;
            }
            using (var document = Read("Boundary", platform, repeat))
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var before = row.GetProperty("before");
                var expected = row.GetProperty("after");
                var type = Type(row);
                var features = Features(row);
                Assert.True(VanillaGemStaffProjectile1458.TryBuildDamageHitbox(1000f, 1000f, 10, 10, out var hitbox));
                var box = row.GetProperty("damageHitbox");
                Assert.Equal(new GemStaffHitbox1458(box.GetProperty("x").GetInt32(), box.GetProperty("y").GetInt32(),
                    box.GetProperty("w").GetInt32(), box.GetProperty("h").GetInt32()), hitbox);
                var trail = new GemStaffTrail1458(default, new(800f, 1000f), new(790f, 1000f), default, default, default);
                foreach (var collision in row.GetProperty("collision").EnumerateArray())
                {
                    var target = new GemStaffHitbox1458(1000 + collision.GetProperty("dx").GetInt32(), 1000, 10, 10);
                    Assert.True(VanillaGemStaffProjectile1458.TryCollide(features, in hitbox, 1005f, 1005f,
                        in trail, in target, static _ => true, out bool hit));
                    Assert.Equal(collision.GetProperty("hit").GetBoolean(), hit);
                }
                Assert.True(VanillaGemStaffProjectile1458.TryPrepareKill(type, features,
                    before.GetProperty("damage").GetInt32(), before.GetProperty("knockBack").GetSingle(), windows, out var kill));
                Assert.Equal(expected.GetProperty("damage").GetInt32(), kill.Damage);
                Assert.Equal(expected.GetProperty("singleBits").GetProperty("knockBack").GetInt32(), BitConverter.SingleToInt32Bits(kill.KnockBack));
                Assert.Equal(expected.GetProperty("width").GetInt32(), kill.FinalWidth);
                Assert.Equal(expected.GetProperty("height").GetInt32(), kill.FinalHeight);
                var random = Random(row.GetProperty("beforeRandom"));
                for (int i = 0; i < kill.RandomSamples; i++) random.Next();
                AssertRandom(row.GetProperty("afterRandom"), row.GetProperty("afterNext").GetInt32(), random);
                int[] immunity = new int[200];
                immunity[0] = 60; immunity[1] = 59; immunity[2] = 1; immunity[3] = -1;
                if (kill.HasAreaDamage)
                    for (int i = 0; i < immunity.Length; i++)
                        if (immunity[i] != kill.RetainedLocalImmunityValue) immunity[i] = 0;
                Assert.Equal(row.GetProperty("afterImmunity").EnumerateArray().Select(value => value.GetInt32()).ToArray(), immunity);
                boundaries++;
            }
            using (var document = Read("Geometry", platform, repeat))
            foreach (var row in document.RootElement.EnumerateArray())
            {
                Assert.True(VanillaDefinitionCatalog.TryGet(Type(row), out var definition));
                var state = row.GetProperty("state");
                Assert.Equal(state.GetProperty("width").GetInt32(), definition.Width);
                Assert.Equal(state.GetProperty("height").GetInt32(), definition.Height);
                Assert.Equal(row.GetProperty("collisionWidth").GetInt32(), definition.CollisionWidth);
                Assert.Equal(row.GetProperty("collisionHeight").GetInt32(), definition.CollisionHeight);
                Assert.Equal(state.GetProperty("tileCollide").GetBoolean(), definition.TileCollide);
                Assert.Equal(row.GetProperty("canCutTiles").GetBoolean(), definition.CanCutTiles);
                geometry++;
            }
        }
        Assert.Equal(108, AssertTargetBoundaries());
        Assert.Equal(5376, motions);
        Assert.Equal(3072, targets);
        Assert.Equal(896, boundaries);
        Assert.Equal(28, geometry);
    }

    private static int AssertTargetBoundaries()
    {
        int contexts = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
            foreach (string repeat in new[] { "First", "Repeat" })
            {
                bool windows = platform == "Windows";
                using var document = Read("TargetBoundary", platform, repeat);
                foreach (var row in document.RootElement.EnumerateArray())
                {
                    float x = BitConverter.Int32BitsToSingle(row.GetProperty("centerXBits").GetInt32());
                    float y = BitConverter.Int32BitsToSingle(row.GetProperty("centerYBits").GetInt32());
                    float range = BitConverter.Int32BitsToSingle(row.GetProperty("radiusBits").GetInt32());
                    var targets = row.GetProperty("candidates").EnumerateArray().Select(candidate =>
                        new GemStaffTarget1458(candidate.GetProperty("index").GetInt32(),
                            BitConverter.Int32BitsToSingle(candidate.GetProperty("centerXBits").GetInt32()),
                            BitConverter.Int32BitsToSingle(candidate.GetProperty("centerYBits").GetInt32()),
                            true, true, false)).ToArray();
                    int expected = row.GetProperty("selected").GetInt32();
                    Assert.True(VanillaGemStaffProjectile1458.TrySelectTarget(targets, x, y, range,
                        out int selected, windows));
                    Assert.Equal(expected, selected);
                    // Keep the published five-argument entrypoint on its existing CoreCLR arithmetic.
                    if (!windows)
                    {
                        Assert.True(VanillaGemStaffProjectile1458.TrySelectTarget(targets, x, y, range,
                            out int legacy));
                        Assert.Equal(expected, legacy);
                    }
                    string name = row.GetProperty("name").GetString()!;
                    if (name.StartsWith("cutoff-", StringComparison.Ordinal))
                    {
                        var before = new GemStaffMotionState1458(x - 5f, y - 5f, 8f, 2f, 1f, 0f, 0f, 0f, 0f);
                        if (range == 2400f)
                        {
                            Assert.True(VanillaGemStaffProjectile1458.TryResolveTileImpact(new(121),
                                GemStaffFeatures1458.Homing | GemStaffFeatures1458.CanBounce,
                                in before, 0f, 2f, 3, true, targets, out var impact, windows));
                            Assert.Equal(expected + 1f, impact.State.Ai2);
                        }
                        else
                        {
                            var flags = GemStaffFeatures1458.Homing;
                            if (range == 1000f) flags |= GemStaffFeatures1458.FastThenSlow;
                            var random = Random(row.GetProperty("beforeRandom"));
                            Assert.True(VanillaGemStaffProjectile1458.TryStepMotion(new(121), flags,
                                in before, true, targets, random.Next, out var motion, windows));
                            Assert.Equal(expected + 1f, motion.State.Ai2);
                        }
                    }
                    contexts++;
                }
            }
        return contexts;
    }

    private static JsonDocument Read(string kind, string platform, string repeat)
    {
        using var stream = typeof(GemStaffProjectile1458Tests).Assembly.GetManifestResourceStream(
            "GemStaff" + kind + platform + repeat + "1458");
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static ProjectileTypeId Type(JsonElement row) => new(row.GetProperty("type").GetInt32());
    private static GemStaffFeatures1458 Features(JsonElement row) => (GemStaffFeatures1458)row.GetProperty("mask").GetInt32();
    private static GemStaffFinalize1458 FinalizeState(JsonElement state) => new(
        state.GetProperty("penetrate").GetInt32(), state.GetProperty("maxPenetrate").GetInt32(),
        state.GetProperty("timeLeft").GetInt32(), state.GetProperty("armorPenetration").GetInt32());
    private static GemStaffMotionState1458 Motion(JsonElement state) => new(
        state.GetProperty("position").GetProperty("x").GetSingle(), state.GetProperty("position").GetProperty("y").GetSingle(),
        state.GetProperty("velocity").GetProperty("x").GetSingle(), state.GetProperty("velocity").GetProperty("y").GetSingle(),
        state.GetProperty("scale").GetSingle(), state.GetProperty("rotation").GetSingle(),
        BitConverter.Int32BitsToSingle(state.GetProperty("localAiBits")[0].GetInt32()),
        BitConverter.Int32BitsToSingle(state.GetProperty("aiBits")[1].GetInt32()),
        BitConverter.Int32BitsToSingle(state.GetProperty("aiBits")[2].GetInt32()));
    private static VanillaUnifiedRandom1458 Random(JsonElement state)
    {
        var random = new VanillaUnifiedRandom1458(0);
        Cursor(random) = state.GetProperty("cursor").GetUInt32();
        RandomState(random) = state.GetProperty("state").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        return random;
    }
    private static void AssertRandom(JsonElement state, int next, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(state.GetProperty("cursor").GetUInt32(), Cursor(random));
        Assert.Equal(state.GetProperty("state").EnumerateArray().Select(value => value.GetInt32()).ToArray(), RandomState(random));
        Assert.Equal(next, random.Clone().Next());
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] RandomState(VanillaUnifiedRandom1458 random);
}
