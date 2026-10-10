using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ArrowNpcKernelSource1458Tests
{
    [Fact]
    public void Configured_local_kernel_hp_status_point_rng_and_literal_28_are_retained()
    {
        using var source = Read("ArrowKernelNpc1458");
        Assert.Equal(32, source.RootElement.GetArrayLength());
        int rows = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            // Retain original owner255 boundary rows without claiming an ordinary human App caller.
            if (row.GetProperty("actorOwner").GetInt32() != 0)
                continue;
            Replay(row);
            rows++;
        }
        Assert.Equal(24, rows);
    }

    [Fact]
    public void Actual_fire_proc_and_lava_immunity_keep_status_then_damage_publications_and_rng()
    {
        using var source = Read("ArrowFireProcNpc1458");
        Assert.Equal(3, source.RootElement.GetArrayLength());
        foreach (var row in source.RootElement.EnumerateArray())
            Replay(row);
    }

    private static void Replay(JsonElement row)
    {
        using var f = new ArrowNpcKernelSourceSupport1458.Fixture(row.GetProperty("seed").GetInt32(),
            row.GetProperty("shotType").GetInt32(), row.GetProperty("targetType").GetInt32(), 1000);
        AssertRandom(f.Random, row.GetProperty("beforeRandom"));
        f.Pass.Tick();
        Assert.Equal(1, f.Pass.CommittedHits);
        Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var after));
        Assert.Equal(row.GetProperty("after").GetProperty("npcLife").GetInt32(), after.Simulation.Life);
        var buffs = new TerrariaNpcBuffEntryState[20];
        Assert.True(f.Status.TryCopyWireBuffs(after.Handle, buffs, out int count));
        Assert.Equal(row.GetProperty("after").GetProperty("buffType").EnumerateArray().Count(v => v.GetInt32() != 0), count);
        for (int index = 0; index < count; index++)
        {
            Assert.Equal(new TerrariaNpcBuffEntryState(
                checked((ushort)row.GetProperty("after").GetProperty("buffType")[index].GetInt32()),
                checked((ushort)row.GetProperty("after").GetProperty("buffTime")[index].GetInt32())), buffs[index]);
        }
        int penetration = row.GetProperty("after").GetProperty("penetration").GetInt32();
        if (penetration > 0 || penetration == -1)
        {
            Assert.True(f.Shots.TryGetLifecycle(f.Shot.Handle, out var life));
            Assert.Equal(penetration, life.PenetrateOverride ?? row.GetProperty("before").GetProperty("penetration").GetInt32());
        }
        // Source private active=true/penetration0 is not a whole Update/Kill assertion.
        var frames = f.Drain().Where(frame => frame[2] is 28 or 54).Select(Convert.ToHexString).ToArray();
        Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(v => v.GetString()).ToArray(), frames);
        AssertRandom(f.Random, row.GetProperty("afterRandom"));
    }

    private static JsonDocument Read(string name)
    {
        using var resource = typeof(ArrowNpcKernelSource1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void AssertRandom(VanillaUnifiedRandom1458 random, JsonElement expected)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(v => v.GetInt32()).ToArray(),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", fields)!.GetValue(random)!);
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", fields)!.GetValue(random)!);
        Assert.Equal(expected.GetProperty("next").GetInt32(), random.Clone().Next());
    }
}
