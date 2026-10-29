using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class MaggotSiblingPhase1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var raw = typeof(MaggotSiblingPhase1458Tests).Assembly.GetManifestResourceStream("MaggotSiblingPhaseLinux1458.v1")!;
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(x => new object[] { x.Clone() }).ToArray();
    }

    [Theory, MemberData(nameof(Cases))]
    public void Zero_damage_sibling_overlap_keeps_source_motion_and_entropy_through_protection_expiry(JsonElement row)
    {
        using var host = new RangedNpcPhaseFixture1458(row);
        var input = row.GetProperty("input");
        var before = input.GetProperty("npc");
        var actor = host.SpawnObserved(before, 1023, 1600);
        host.AssertBirth(row.GetProperty("birth"));
        actor = host.ImportObserved(actor.Handle, before);
        host.ImportStatus(actor.Handle, before);
        var other = input.GetProperty("activeNpcPeers")[1].GetProperty("state");
        var sibling = host.SpawnObserved(other, 1023, 1600, 1);
        sibling = host.ImportObserved(sibling.Handle, other);
        host.ImportStatus(sibling.Handle, other);
        Assert.Equal(0, actor.Simulation.DamageOverride);
        Assert.Equal(0, sibling.Simulation.DamageOverride);
        RangedNpcPhaseFixture1458.AssertRandom(input.GetProperty("rng"), host.Random);
        var checkpoints = row.GetProperty("checkpoints").EnumerateArray().ToDictionary(x => x.GetProperty("index").GetInt32());
        for (int i = 0; i < 128; i++)
        {
            var actual = host.Step(actor.Handle);
            if (!checkpoints.TryGetValue(i, out var checkpoint)) continue;
            var expected = checkpoint.GetProperty("after");
            MartianChildAi1458Tests.AssertActor(expected.GetProperty("npc"), in actual);
            Assert.Equal(5, actual.Simulation.Life);
            Assert.Equal(0, actual.Simulation.DamageOverride);
            RangedNpcPhaseFixture1458.AssertRandom(expected.GetProperty("rng"), host.Random);
            Assert.Equal(sibling, host.Current(sibling.Handle));
        }
        var final = host.Current(actor.Handle);
        Assert.Equal(0, final.Simulation.CatchableNpcTempImmunityCounter);
        Assert.False(final.Simulation.Friendly);
        Assert.False(host.RandomInputImported);
    }
}
