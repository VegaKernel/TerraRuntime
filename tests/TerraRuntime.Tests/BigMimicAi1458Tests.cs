using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class BigMimicAi1458Tests
{
    // Actual complete NPC.AI/StrikeNPC/SetDefaults calls from the pinned Linux server 1.4.5.8,
    // SHA256 4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
    public static IEnumerable<object[]> WholeCases() => Read("BigMimicWholeAi1458").Concat(Read("BigMimicSeedAi1458"));
    public static IEnumerable<object[]> CannonCases() => Read("BigMimicCannon1458");
    public static IEnumerable<object[]> ReflectionCases() => Read("BigMimicReflection1458");
    public static IEnumerable<object[]> DefaultCases() => Read("BigMimicDefaults1458");
    public static IEnumerable<object[]> ProjectileCases() => Read("BigMimicProjectileDefaults1458");
    public static IEnumerable<object[]> StrikeCases() => Read("BigMimicStrike1458");

    internal static IEnumerable<object[]> Read(string name)
    {
        using var stream = typeof(BigMimicAi1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
            yield return [row.Clone()];
    }

    [Theory]
    [MemberData(nameof(WholeCases))]
    public void Complete_original_AI_matches_all_phases_and_random_checkpoint(JsonElement row)
    {
        float F(string key) => row.GetProperty(key).GetSingle();
        int I(string key) => row.GetProperty(key).GetInt32();
        bool B(string key) => row.GetProperty(key).GetBoolean();
        var random = new SystemVanillaNpcRandom(I("seed"));
        var state = new VanillaBigMimicAi1458
        {
            X = 1000.75f, Y = 1000.25f, Type = I("type"), Phase = F("phase"), Clock = F("clock"), Repeat = F("repeat"), Hops = F("hops"),
            Vx = F("vx"), Vy = F("vy"), TargetX = 1014.75f + F("dx"), TargetY = 1022.25f + F("dy"),
            Life = (int)(3500 * F("ratio")), Expert = B("expert"), Damage = row.TryGetProperty("inputDamage", out var inputDamage) ? inputDamage.GetInt32() : B("expert") ? 180 : 90,
            Difficulty = row.TryGetProperty("difficulty", out var difficulty) ? difficulty.GetSingle() : B("expert") ? 2f : 1f
        };
        Span<VanillaBigMimicCannonItem1458> cannon = stackalloc VanillaBigMimicCannonItem1458[10];
        Assert.Equal(0, state.Step(new Environment(B("sight"), B("bodySolid")), random, cannon));
        var ai = row.GetProperty("ai");
        Assert.Equal(Number(row.GetProperty("outVx")), state.Vx);
        Assert.Equal(Number(row.GetProperty("outVy")), state.Vy);
        Assert.Equal(Number(ai[0]), state.Phase);
        Assert.Equal(Number(ai[1]), state.Clock);
        Assert.Equal(Number(ai[2]), state.Repeat);
        Assert.Equal(Number(ai[3]), state.Hops);
        Assert.Equal(F("knockback"), state.Knockback);
        Assert.Equal(I("direction"), state.Dir);
        Assert.Equal(I("directionY"), state.DirY);
        Assert.Equal(I("sprite"), state.Sprite);
        Assert.Equal(B("noTile"), state.NoTile);
        Assert.Equal(B("noGravity"), state.NoGravity);
        Assert.Equal(B("dontTakeDamage"), state.Invulnerable);
        Assert.Equal(B("reflects"), state.Reflects);
        Assert.Equal(B("netUpdate"), state.Sync);
        Assert.Equal(I("alpha"), state.Alpha);
        Assert.Equal(I("damage"), state.Damage);
        Assert.Equal(I("defense"), state.Defense);
        Assert.Equal(I("life"), state.Life);
        Assert.Equal(I("next"), random.SourceRandom.Next());
    }

    [Theory]
    [MemberData(nameof(CannonCases))]
    public void Original_cannon_ten_offers_keep_source_aim_arithmetic_and_no_extra_velocity_draws(JsonElement row)
    {
        int I(string key) => row.GetProperty(key).GetInt32();
        var random = new SystemVanillaNpcRandom(I("seed"));
        var state = new VanillaBigMimicAi1458
        {
            X = 1000.75f, Y = 1000.25f, Type = 476, Phase = 8f, Clock = row.GetProperty("clock").GetSingle(),
            Width = I("width"), Height = I("height"), Tenth = row.GetProperty("tenth").GetBoolean(),
            TargetX = 1810f, TargetY = 1021f, Vx = .5f
        };
        Span<VanillaBigMimicCannonItem1458> cannon = stackalloc VanillaBigMimicCannonItem1458[10];
        int count = state.Step(new Environment(true, false), random, cannon);
        var drops = row.GetProperty("drops");
        Assert.Equal(drops.GetArrayLength(), count);
        for (int index = 0; index < count; index++)
        {
            var drop = drops[index];
            Assert.Equal(drop.GetProperty("id").GetInt32(), cannon[index].Item);
            Assert.Equal(drop.GetProperty("vx").GetSingle(), cannon[index].VelocityX);
            Assert.Equal(drop.GetProperty("vy").GetSingle(), cannon[index].VelocityY);
            Assert.Equal(0, drop.GetProperty("prefix").GetInt32());
            Assert.Equal(1, drop.GetProperty("stack").GetInt32());
        }
        Assert.Equal(row.GetProperty("ai")[0].GetSingle(), state.Phase);
        Assert.Equal(row.GetProperty("ai")[1].GetSingle(), state.Clock);
        Assert.Equal(row.GetProperty("invulnerable").GetBoolean(), state.Invulnerable);
        Assert.Equal(I("next"), random.SourceRandom.Next());
    }

    [Theory]
    [MemberData(nameof(ReflectionCases))]
    public void Original_two_slot_reflection_has_exact_old_velocity_and_nonfinite_source_outcomes(JsonElement row)
    {
        float F(string key) => row.GetProperty(key).GetSingle();
        bool B(string key) => row.GetProperty(key).GetBoolean();
        var random = new SystemVanillaNpcRandom(row.GetProperty("seed").GetInt32());
        Assert.True(TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(new ProjectileTypeId(1), out var body));
        body = body with { Width = 6, Height = 6 };
        bool intersects = (int)F("projectileX") < 1028 && (int)F("projectileX") + 6 > 1000;
        var adapter = new ReflectionRandom(random);
        foreach (var expected in row.GetProperty("mutations").EnumerateArray())
        {
            var projectile = new ProjectileSnapshot(new((ushort)expected.GetProperty("slot").GetInt32(), new(1)),
                new(1), new(1), 0, F("projectileX"), 1019.25f, 1f, 2f, default, 0, 101, 0f, 101);
            bool reflects = B("expert") && B("eligible") && intersects;
            var result = reflects
                ? VanillaProjectileReflection1458.ResolveSource(in projectile, in body, F("oldVx"), F("oldVy"),
                    B("centered") ? 1014.75f : 1810f, B("centered") ? 1022.25f : 1021f, adapter)
                : new VanillaProjectileReflectionResult(1f, 2f, 101);
            Assert.Equal(Number(expected.GetProperty("vx")), result.VelocityX);
            Assert.Equal(Number(expected.GetProperty("vy")), result.VelocityY);
            Assert.Equal(expected.GetProperty("damage").GetInt32(), result.Damage);
            Assert.Equal(expected.GetProperty("reflected").GetBoolean(), reflects);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), random.SourceRandom.Next());
    }

    [Theory]
    [MemberData(nameof(ProjectileCases))]
    public void Default_reflection_predicate_matches_every_original_projectile_identity(JsonElement row)
    {
        int style = row.GetProperty("style").GetInt32();
        int type = row.GetProperty("type").GetInt32();
        bool expected = row.GetProperty("friendly").GetBoolean() && !row.GetProperty("hostile").GetBoolean() &&
            (type is 728 or 955 || style is 1 or 2 or 8 or 21 or 24 or 28 or 29 or 131);
        Assert.Equal(expected, VanillaProjectileReflection1458.CanBeReflectedAtDefaults(new(type)));
    }

    [Theory]
    [MemberData(nameof(DefaultCases))]
    public void Original_defaults_include_good_seed_effective_difficulty(JsonElement row)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(row.GetProperty("type").GetInt32()), out var definition));
        var context = new VanillaNpcSpawnContext(row.GetProperty("difficulty").GetSingle(), 1, row.GetProperty("good").GetBoolean());
        Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, windowsArithmetic: false, out var defaults));
        Assert.Equal(row.GetProperty("width").GetInt32(), defaults.Hitbox.Width);
        Assert.Equal(row.GetProperty("height").GetInt32(), defaults.Hitbox.Height);
        Assert.Equal(row.GetProperty("scale").GetSingle(), defaults.Scale);
        Assert.Equal(row.GetProperty("life").GetInt32(), defaults.LifeMax);
        Assert.Equal(row.GetProperty("damage").GetInt32(), defaults.Damage);
        Assert.Equal(row.GetProperty("defense").GetInt32(), defaults.Defense);
        Assert.Equal(row.GetProperty("knockback").GetSingle(), defaults.KnockBackResist);
        Assert.True(VanillaNpcMoneyDefaults1458.TryResolve(definition.Type, new NpcNetId((short)definition.Type.Value), context.Difficulty, out float value));
        Assert.Equal(row.GetProperty("value").GetSingle(), value);
    }

    [Theory]
    [MemberData(nameof(StrikeCases))]
    public void Accepted_strike_wakes_before_next_AI_and_preserves_other_clocks(JsonElement row)
    {
        int type = row.GetProperty("type").GetInt32();
        var store = new RuntimeNpcStore();
        var state = new NpcStateUpdate(type, (short)type, 1000.75f, 1000.25f, 0f, 0f, 0,
            new(row.GetProperty("phase").GetSingle(), 23f, 91f, 2f),
            NpcSimulationState.Initial with { Life = 3500, LifeMax = 3500 });
        Assert.True(store.TrySpawn(0, in state, out var npc));
        var request = new NpcDamageRequest(npc.Handle, DamageSource.Server, 30);
        Assert.True(new RuntimeNpcDamageExecutor(store).TryApply(in request, out var result));
        Assert.Equal(row.GetProperty("damage").GetInt32(), result.ResolvedDamage);
        Assert.True(store.TryGet(npc.Handle, out var after));
        Assert.Equal(row.GetProperty("life").GetInt32(), after.Simulation.Life);
        var ai = row.GetProperty("ai");
        Assert.Equal(new NpcAiState(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle()), after.Ai);
    }

    private static float Number(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? float.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : value.GetSingle();

    private sealed class Environment(bool sight, bool solid) : IVanillaBigMimicEnvironment1458
    {
        public bool CanHit(float sourceX, float sourceY, float targetX, float targetY) => sight;
        public bool SolidCollision(float x, float y, int width, int height) => solid;
    }

    private sealed class ReflectionRandom(IVanillaNpcRandom source) : IVanillaProjectileReflectionRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => source.NextInt32(inclusiveMin, exclusiveMax);
    }
}
