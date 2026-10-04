using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class BlueSlimeSpawnDefaults1458Tests
{
    public static IEnumerable<object[]> Rows()
    {
        foreach (var row in Load(false)) yield return [row, false];
        foreach (var row in Load(true)) yield return [row, true];
    }

    public static IEnumerable<object[]> StoreRows()
    {
        foreach (var row in Load(OperatingSystem.IsWindows())) yield return [row];
    }

    private static IEnumerable<JsonElement> Load(bool windows)
    {
        using var input = typeof(BlueSlimeSpawnDefaults1458Tests).Assembly.GetManifestResourceStream(
            $"TerraRuntime.Tests.Fixtures.BlueSlimeSpawnDefaults{(windows ? "Windows" : "")}1458.json.gz")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return row.Clone();
    }

    [Theory, MemberData(nameof(Rows))]
    public void Pure_defaults_match_actual_original_spawn_overrides(JsonElement row, bool windowsArithmetic)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BlueSlime, new NpcNetId(checked((short)I(row, "type"))), out var definition));
        var context = Context(row);
        Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, windowsArithmetic, out var actual));
        Assert.Equal(I(row, "width"), actual.Hitbox.Width);
        Assert.Equal(I(row, "height"), actual.Hitbox.Height);
        Assert.Equal(F(row, "scale"), actual.Scale);
        Assert.Equal(I(row, "alpha"), definition.AlphaAtSpawn);
        Assert.Equal(I(row, "lifeMax"), actual.LifeMax);
        Assert.Equal(I(row, "damage"), actual.Damage);
        Assert.Equal(I(row, "defense"), actual.Defense);
        Assert.Equal(F(row, "knockback"), actual.KnockBackResist);
        Assert.Equal(F(row, "value"), actual.VerifiedMoneyValue);
        Assert.Equal(F(row, "actualDifficulty"), actual.Difficulty);
        Assert.False(B(row, "dontDoHardmodeScaling"));
        Assert.False(B(row, "projectileNpc"));
        Assert.False(B(row, "needsExpertScaling"));
    }

    [Theory, MemberData(nameof(StoreRows))]
    public void Store_materializes_owned_body_combat_money_and_source_creation_rng(JsonElement row)
    {
        var context = Context(row);
        var random = new VanillaUnifiedRandom1458(I(row, "seed"));
        var expected = random.Clone();
        // NewNPC's GoodWorld pre-allocation selector is separate from the original SetDefaults fixture.
        if (context.GoodWorld) expected.Next(3);
        else Assert.Equal(I(row, "next"), expected.Clone().Next());
        var store = new RuntimeNpcStore(4);
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        store.SetVanillaSpawnContextSource(() => context);
        int type = I(row, "type");
        var update = new NpcStateUpdate(1, checked((short)type), 1000f, 1000f,
            0f, 0f, 255, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawnVanilla(in update, out var actual));
        Assert.Equal(1, actual.Type);
        Assert.Equal(type, actual.NetId);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BlueSlime, new NpcNetId(checked((short)type)), out var definition));
        Assert.True(definition.TryResolveHitbox(actual.Simulation, out var body));
        Assert.Equal((I(row, "width"), I(row, "height")), (body.Width, body.Height));
        Assert.Equal(F(row, "scale"), actual.Simulation.Scale);
        Assert.Equal(I(row, "alpha"), actual.Simulation.Alpha);
        Assert.Equal(I(row, "lifeMax"), actual.Simulation.LifeMax);
        Assert.Equal(actual.Simulation.LifeMax, actual.Simulation.Life);
        Assert.Equal(I(row, "damage"), actual.Simulation.DamageOverride ?? definition.Damage);
        Assert.Equal(I(row, "defense"), actual.Simulation.DefenseOverride ?? definition.Defense);
        Assert.Equal(F(row, "knockback"), actual.Simulation.KnockBackResist!.Value, precision: 6);
        Assert.Equal(F(row, "value"), actual.Simulation.MoneyValue);
        Assert.Equal(F(row, "actualDifficulty"), actual.Simulation.SpawnDifficulty);
        Assert.True(random.HasSameState(expected));
        Assert.Equal(expected.Next(), random.Next());
    }

    private static VanillaNpcSpawnContext Context(JsonElement row) => new(F(row, "requestedDifficulty"),
        I(row, "playerCount"), B(row, "good"))
    { RemixWorld = B(row, "remix"), HardMode = B(row, "hard"), DownedPlantera = B(row, "plantera") };
    private static int I(JsonElement row, string key) => row.GetProperty(key).GetInt32();
    private static float F(JsonElement row, string key) => row.GetProperty(key).GetSingle();
    private static bool B(JsonElement row, string key) => row.GetProperty(key).GetBoolean();
}
