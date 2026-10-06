using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Gameplay.Worlds;

namespace TerraRuntime.Tests;

public sealed partial class InvasionDeathCredit1458Tests
{
    [Fact]
    public void Actual_death_retains_source_zero_maximum_and_signed_progress()
    {
        using var stream = typeof(InvasionDeathCredit1458Tests).Assembly.GetManifestResourceStream("InvasionDeathZeroStart1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        var row = Assert.Single(source.RootElement.EnumerateArray());
        var fixture = new PipelineFixture(27, 1, row.GetProperty("seed").GetInt32(), false);
        Assert.True(fixture.Owner.TryCapture(out var before));
        var residual = new InvasionTransition1458(before.State with { Size = 3, SizeStart = 0 }, default);
        Assert.True(fixture.Owner.TryAdopt(in before, in residual, out var prepared));
        Assert.True(VanillaInvasionLifecycle1458.TryCreditDeath(prepared.State, 27, out var expectedTransition));
        Assert.Equal(row.GetProperty("after").GetProperty("progress").GetInt32(), expectedTransition.State.Progress);
        Assert.Equal(row.GetProperty("after").GetProperty("maximum").GetInt32(), expectedTransition.State.ProgressMax);

        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
            fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));

        Assert.True(fixture.Owner.TryCapture(out var accepted));
        Assert.Equal(-2, accepted.State.Progress);
        Assert.Equal(0, accepted.State.ProgressMax);
        Assert.Equal(Convert.FromHexString(row.GetProperty("frames").EnumerateArray().Last().GetString()!), fixture.ProgressFrame);
        Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Next());
        Assert.False(fixture.Npcs.TryGet(fixture.Npc.Handle, out _));
        Assert.Equal(new[] { 78, 23 }, fixture.Events.TakeLast(2));
    }
}
