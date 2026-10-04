using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcImmortalKnockback1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        using var input = typeof(RuntimeNpcImmortalKnockback1458Tests).Assembly.GetManifestResourceStream("NonclientKnockback1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            // Generation/wire coverage is retained by RuntimeNpcServerStrikeWire1458Tests. Avoid duplicate
            // noncritical Strike/NoInteraction rows here; this fixture compares original life/result/velocity.
            if (row.GetProperty("generation").GetInt32() != 1 ||
                (row.GetProperty("method").GetString() == "strike" && !row.GetProperty("crit").GetBoolean())) continue;
            yield return [row.Clone()];
        }
    }

    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void Immortal_strike_preserves_life_and_original_weak_or_strong_knockback(JsonElement row)
    {
        var store = new RuntimeNpcStore();
        var simulation = NpcSimulationState.Initial with
        {
            Life = int.MaxValue, LifeMax = int.MaxValue, Immortal = true, DefenseOverride = 15,
            NoGravity = row.GetProperty("noGravity").GetBoolean(), KnockBackResist = row.GetProperty("resist").GetSingle()
        };
        var state = new NpcStateUpdate(3, 3, 100, 100, row.GetProperty("incomingX").GetSingle(),
            row.GetProperty("incomingY").GetSingle(), 0, default, simulation);
        Assert.True(store.TrySpawn(0, in state, out var before));
        var executor = new RuntimeNpcDamageExecutor(store, expertMode: row.GetProperty("expert").GetBoolean(),
            lethalAdmission: UnexpectedDeathAdmission);
        var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, row.GetProperty("damage").GetInt32(),
            Critical: row.GetProperty("crit").GetBoolean(), KnockBack: 6f, HitDirection: row.GetProperty("direction").GetInt32());
        Assert.True(executor.TryApply(in request, out var result));
        Assert.Equal(row.GetProperty("result").GetInt32(), result.ResolvedDamage);
        Assert.Equal(0, result.LifeLost); Assert.False(result.Lethal); Assert.False(result.DeathIntercepted);
        Assert.True(store.TryGet(before.Handle, out var current));
        Assert.Equal(row.GetProperty("life").GetInt32(), current.Simulation.Life);
        Assert.Equal(row.GetProperty("justHit").GetBoolean(), current.Simulation.JustHit);
        Assert.Equal(row.GetProperty("vx").GetSingle(), current.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), current.VelocityY);
        Assert.True(current.Simulation.Immortal);
    }

    private static bool UnexpectedDeathAdmission(in NpcSnapshot _) =>
        throw new InvalidOperationException("An immortal source strike cannot prepare NPC death or item allocation.");
}
