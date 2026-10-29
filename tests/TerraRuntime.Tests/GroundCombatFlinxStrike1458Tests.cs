using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class GroundCombatFlinxStrike1458Tests
{
    public static IEnumerable<object[]> OriginalCases() =>
        Rows("GroundCombatFlinxStrikeLinux1458").Concat(Rows("GroundCombatFlinxNaturalLinux1458"));

    private static IEnumerable<object[]> Rows(string resource)
    {
        using var stream = typeof(GroundCombatFlinxStrike1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray()) yield return [row.GetRawText()];
    }

    public static IEnumerable<object[]> OriginalStrongCases()
    {
        foreach (var item in OriginalCases())
        {
            using var document = JsonDocument.Parse((string)item[0]);
            var row = document.RootElement;
            // This feature is the strong branch's type185 multiplier. All weak
            // rows remain in the original corpus; the existing Windows wider
            // arithmetic and its dual-edition differences have a separate scope.
            int damage = (int)row.GetProperty("returned").GetDouble();
            if (unchecked(damage * (row.GetProperty("mode").GetInt32() == 1 ? 15 : 10)) > row.GetProperty("before").GetProperty("npc").GetProperty("lifeMax").GetInt32())
                yield return [row.GetRawText()];
        }
    }

    [Theory, MemberData(nameof(OriginalCases))]
    public void Shared_accepted_strike_matches_all_original_response_flags_and_life_profiles(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        int type = row.GetProperty("type").GetInt32();
        var store = new RuntimeNpcStore(1);
        var initial = NpcSimulationState.Initial with
        {
            Life = row.GetProperty("before").GetProperty("npc").GetProperty("life").GetInt32(),
            LifeMax = row.GetProperty("before").GetProperty("npc").GetProperty("lifeMax").GetInt32(),
            DefenseOverride = row.GetProperty("before").GetProperty("npc").GetProperty("defense").GetInt32(),
            KnockBackResist = row.GetProperty("resist").GetSingle()
        };
        var state = new NpcStateUpdate(type, checked((short)type), 640, 480, -.7f, row.GetProperty("vy").GetSingle(), 255, default, initial);
        Assert.True(store.TrySpawn(0, in state, out var before));
        var executor = new RuntimeNpcDamageExecutor(store, expertMode: row.GetProperty("mode").GetInt32() == 1);
        var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, row.GetProperty("damage").GetInt32(),
            Critical: row.GetProperty("critical").GetBoolean(), KnockBack: row.GetProperty("knockback").GetSingle(), HitDirection: 1);
        Assert.True(executor.TryApply(in request, out var result));
        Assert.Equal((int)row.GetProperty("returned").GetDouble(), result.ResolvedDamage);
        Assert.True(store.TryGet(before.Handle, out var after));
        var source = row.GetProperty("after").GetProperty("npc");
        Assert.Equal(source.GetProperty("life").GetInt32(), after.Simulation.Life);
        Assert.Equal(source.GetProperty("justHit").GetBoolean(), after.Simulation.JustHit);
        Assert.Equal(source.GetProperty("localAi")[0].GetString(), Bits(after.Simulation.LocalAi.Ai0));
        Assert.Equal(row.GetProperty("before").GetProperty("rng").GetRawText(), row.GetProperty("after").GetProperty("rng").GetRawText());
        // Velocity is deliberately qualified by the separate strong-branch test.
        // Existing weak/capped platform-precision differences are not golden-stamped.
    }

    [Theory, MemberData(nameof(OriginalStrongCases))]
    public void Flinx_strong_vertical_multiplier_matches_actual_source_with_parent77_controls(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        int type = row.GetProperty("type").GetInt32();
        var actual = VanillaNpcKnockbackResolver.Resolve(-.7f, row.GetProperty("vy").GetSingle(), false,
            row.GetProperty("before").GetProperty("npc").GetProperty("lifeMax").GetInt32(),
            row.GetProperty("resist").GetSingle(), row.GetProperty("knockback").GetSingle(), 1,
            (int)row.GetProperty("returned").GetDouble(), row.GetProperty("critical").GetBoolean(),
            row.GetProperty("mode").GetInt32() == 1, flinxVerticalKnockback: type == 185);
        var expected = row.GetProperty("after").GetProperty("npc");
        Assert.Equal(expected.GetProperty("velocity")[0].GetString(), Bits(actual.VelocityX));
        Assert.Equal(expected.GetProperty("velocity")[1].GetString(), Bits(actual.VelocityY));
        Assert.Equal(type == 185 ? "3f800000" : "00000000", expected.GetProperty("localAi")[0].GetString());
        Assert.Equal(row.GetProperty("before").GetProperty("rng").GetRawText(), row.GetProperty("after").GetProperty("rng").GetRawText());
    }

    [Fact]
    public void Accepted_strike_marks_Flinx_response_once_while_stale_invalid_and_invulnerable_requests_are_inert()
    {
        var store = new RuntimeNpcStore(2);
        var before = Spawn(store);
        var executor = new RuntimeNpcDamageExecutor(store);
        var invalid = new NpcDamageRequest(before.Handle, DamageSource.Environment, -1);
        Assert.False(executor.TryApply(in invalid, out _));
        Assert.True(store.TryGet(before.Handle, out var untouched));
        Assert.Equal(before, untouched);

        var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 10);
        Assert.True(executor.TryApply(in request, out _));
        Assert.True(store.TryGet(before.Handle, out var accepted));
        Assert.Equal(1f, accepted.Simulation.LocalAi.Ai0);
        Assert.True(accepted.Simulation.JustHit);
        Assert.Equal(66, accepted.Simulation.Life);

        var protectedState = new NpcStateUpdate(accepted.Type, accepted.NetId, accepted.PositionX, accepted.PositionY,
            accepted.VelocityX, accepted.VelocityY, accepted.Target, accepted.Ai,
            accepted.Simulation with { DontTakeDamage = true, LocalAi = default });
        Assert.True(store.TryUpdate(accepted.Handle, in protectedState, out var protectedNpc));
        Assert.False(executor.TryApply(in request, out _));
        Assert.True(store.TryGet(before.Handle, out untouched));
        Assert.Equal(protectedNpc, untouched);
        var stale = request with { Target = new NpcHandle(0, new NpcGeneration(999)) };
        Assert.False(executor.TryApply(in stale, out _));
        Assert.True(store.TryGet(before.Handle, out untouched));
        Assert.Equal(protectedNpc, untouched);
    }

    [Fact]
    public void Refused_lethal_preview_cannot_commit_response_and_accepted_death_keeps_the_same_transaction()
    {
        var store = new RuntimeNpcStore(2);
        var before = Spawn(store);
        bool allow = false;
        int previews = 0;
        bool Admit(in NpcSnapshot pending)
        {
            previews++;
            Assert.Equal(1f, pending.Simulation.LocalAi.Ai0);
            Assert.Equal(0, pending.Simulation.Life);
            return allow;
        }
        var executor = new RuntimeNpcDamageExecutor(store, lethalAdmission: Admit);
        var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 9999);
        Assert.False(executor.TryApply(in request, out _));
        Assert.True(store.TryGet(before.Handle, out var untouched));
        Assert.Equal(before, untouched);
        allow = true;
        Assert.True(executor.TryApply(in request, out var result));
        Assert.True(result.Lethal);
        Assert.True(store.TryGet(before.Handle, out var accepted));
        Assert.Equal(1f, accepted.Simulation.LocalAi.Ai0);
        Assert.Equal(0, accepted.Simulation.Life);
        Assert.False(executor.TryApply(in request, out _));
        Assert.Equal(2, previews);
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore store)
    {
        var state = new NpcStateUpdate(185, 185, 640, 480, 0, 0, 255, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in state, out var before));
        return before;
    }

    private static string Bits(float value) => BitConverter.SingleToUInt32Bits(value).ToString("x8");
}
