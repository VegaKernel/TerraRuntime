using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
namespace TerraRuntime.Tests;

public sealed class EnduranceCombatSource1458Tests
{
    private static void Represented_mitigation_retains_finite_Endurance_above_one()
    {
        foreach (float value in new[] { 0f, .1f, 1f, 1.0000001f, 3.199999f, 4.399998f, float.MaxValue })
        {
            var mitigation = new TargetMitigation(0, 0, value, false, false, false);
            Assert.True(mitigation.IsValid);
            Assert.True(new FinalDamageToHp(1, mitigation).IsValid);
            Assert.Equal(value, mitigation.Endurance);
        }
        foreach (float value in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Assert.False(new TargetMitigation(0, 0, value, false, false, false).IsValid);
    }

    [Fact]
    public void Common_incoming_minimum_preserves_source_value_and_malformed_fences()
    {
        Represented_mitigation_retains_finite_Endurance_above_one();
        var attack = new AuthoritativeAttackDamage(new(DamageSourceKind.Server, default, default, default), 100, 0, false, 0, 1);
        foreach (int mode in new[] { 0, 1, 2 })
        foreach (float value in new[] { 0f, .1f, 1f, 1.0000001f, 3.199999f, 4.399998f, float.MaxValue })
        foreach (bool immune in new[] { false, true })
        {
            var combat = VanillaPlayerCombatSnapshot.Baseline with { Endurance = value };
            Assert.True(VanillaCombatDamagePipeline.TryResolvePlayerDamage(attack, combat, immune, out var final, mode > 0, mode == 2));
            Assert.True(final.IsValid);
            Assert.Equal(immune ? 0 : value >= 1 ? 1 : value == 0 ? 100 : 89, final.Damage);
            Assert.Equal(value, final.Mitigation.Endurance);
        }
        // Observed source Windows and Linux 117 rows give HP5->4 above one.
        // float.MaxValue is generalized safe-domain validation, not an original capture.
        foreach (float value in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Assert.False(VanillaCombatDamagePipeline.TryResolvePlayerDamage(attack, VanillaPlayerCombatSnapshot.Baseline with { Endurance = value }, false, out _));
        using var originalStream = typeof(EnduranceCombatSource1458Tests).Assembly.GetManifestResourceStream("OriginalWindowsEnduranceHurt1458")!;
        using var originalGzip = new GZipStream(originalStream, CompressionMode.Decompress);
        using var original = JsonDocument.Parse(originalGzip);
        foreach (var row in original.RootElement.EnumerateArray())
        {
            int mode = row.GetProperty("mode").GetInt32();
            var before = row.GetProperty("before");
            var combat = VanillaPlayerCombatSnapshot.Baseline with { Endurance = before.GetProperty("endurance").GetSingle(), Defense = before.GetProperty("defense").GetInt32() };
            Assert.True(VanillaCombatDamagePipeline.TryResolvePlayerDamage(attack, combat, false, out var actual, mode > 0, mode == 2));
            Assert.Equal(before.GetProperty("life").GetInt32() - row.GetProperty("after").GetProperty("life").GetInt32(), actual.Damage);
            Assert.Equal(combat.Endurance, actual.Mitigation.Endurance);
        }
    }

    [Fact]
    public async Task Transfer_preserves_exact_Endurance_without_minting_unknown_or_malformed_combat()
    {
        using var stream = typeof(EnduranceCombatSource1458Tests).Assembly.GetManifestResourceStream("HumanDefensiveCritBuffPhase1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        var row = source.RootElement.GetProperty("linux").EnumerateArray().Single(x => x.GetProperty("spec").GetProperty("name").GetString() == "neutral");
        foreach (VanillaPlayerCombatSnapshot? combat in new VanillaPlayerCombatSnapshot?[]
        {
            VanillaPlayerCombatSnapshot.Baseline with { Endurance = 4.399998f },
            null, default(VanillaPlayerCombatSnapshot),
            VanillaPlayerCombatSnapshot.Baseline with { Endurance = -1f },
            VanillaPlayerCombatSnapshot.Baseline with { Endurance = float.NaN },
            VanillaPlayerCombatSnapshot.Baseline with { Endurance = float.PositiveInfinity },
            VanillaPlayerCombatSnapshot.Baseline with { Endurance = float.NegativeInfinity }
        })
        {
            using var f = new HumanDefensiveCritBuffPhase1458Tests.Fixture(row, false);
            f.Member.ItemPhase = f.Member.ItemPhase!.Value with { DerivedCombat = combat };
            var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detach));
            var transfer = Assert.IsType<RuntimePlayerTransferState>(await detach.Task);
            Assert.Equal(combat, transfer.ItemPhase!.Value.DerivedCombat);
            var inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Players)!;
            ulong serial = inventory.Serial;
            var attach = new TaskCompletionSource<bool>();
            f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 100, 103, true, false, attach));
            bool valid = combat is null || combat.Value.MinionDamage > 0 && float.IsFinite(combat.Value.Endurance) && combat.Value.Endurance >= 0;
            Assert.Equal(valid, await attach.Task);
            Assert.Equal(valid, f.Players.TryGet(f.Connection, out var member));
            if (valid)
            {
                Assert.Equal(combat, member.ItemPhase!.Value.DerivedCombat);
                Assert.Equal(combat is not null, f.Players.TryCaptureProjectileCombatSnapshot(f.Connection.Player, out var captured));
                if (combat is not null) Assert.Equal(combat.Value, captured);
                if (combat is { Endurance: > 1f })
                {
                    // Configured known-owned HP5, then a real phase-owned contact producer.
                    // This tests capture and commit; it does not claim source cosmetic RNG parity.
                    member.Life = 5;
                    member.NpcHealth = member.NpcHealth!.Value with { Life = 5, RegenTime = 0f };
                    member.NpcLifeCurrent = true;
                    Assert.True(f.Players.TryCaptureIncomingCombat(f.Connection.Player, out var incoming));
                    Assert.Equal(combat.Value, incoming.Combat);
                    var npcs = new RuntimeNpcStore(2);
                    var sourceNpc = new NpcStateUpdate(3, 3, member.PositionX, member.PositionY, 0, 0, 255, default,
                        NpcSimulationState.Initial with { Friendly = false, DamageOverride = 100 });
                    Assert.True(npcs.TrySpawn(0, sourceNpc, out _));
                    var contact = new RuntimeNpcPlayerCombatPass(npcs, f.Players, new Random(1458));
                    contact.Tick(1);
                    Assert.Equal(1, contact.CommittedHits);
                    Assert.Equal((short)4, member.Life);
                    Assert.Equal(4, member.NpcHealth!.Value.Life);
                    Assert.False(member.IsDead);
                    Assert.Equal(combat, member.ItemPhase!.Value.DerivedCombat);
                }
            }
            else
            {
                Assert.Equal(serial, inventory.Serial);
                Assert.False(f.Players.TryGetInventoryItem(f.Connection, 0, out _));
                Assert.False(f.Profiles.TryCapture(f.Connection, out _, out _, out _));
            }
        }
    }
}
