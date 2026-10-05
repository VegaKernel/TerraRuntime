using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class PlayerNpcLifeProvenance1458Tests
{
    [Theory]
    [InlineData(0, "alive")] [InlineData(20, "alive")] [InlineData(24, "alive")]
    [InlineData(39, "alive")] [InlineData(113, "alive")]
    [InlineData(0, "dead")] [InlineData(0, "ghost")] [InlineData(0, "outside")]
    public void Unowned_alive_phase_retires_the_report_while_source_early_return_and_dead_life_remain_known(int buff, string mode)
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(100, buff); f.Phase(mode);
        Assert.True(f.Member.NpcLifeCurrent); short report = f.Member.Life;
        ulong revision = f.Member.Revision;
        f.Players.TickHealthContext();
        Assert.Equal(mode != "alive", f.Member.NpcLifeCurrent);
        Assert.Equal(mode == "alive" ? null : mode == "dead" ? 0 : report, f.Member.CaptureSnapshot().NpcLife);
        Assert.True(f.Member.HasHealth); Assert.Equal(report, f.Member.Life);
        if (mode is "ghost" or "outside") Assert.Equal(revision, f.Member.Revision);
        else Assert.True(f.Member.Revision > revision);
        f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Session.Slot, 99, 100)));
        Assert.True(f.Member.CaptureSnapshot().NpcLifeCurrent);
        Assert.Equal(99, f.Member.Life);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)] [InlineData(null)]
    public async Task Transfer_retains_nullable_report_provenance_and_the_next_phase_invalidates_it(bool? known)
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(100, 0);
        var completion = new TaskCompletionSource<RuntimePlayerTransferState?>();
        f.Players.TryApply(new PlayerTransferDetachRuntimeCommand(f.Connection, completion));
        var transfer = Assert.IsType<RuntimePlayerTransferState>(await completion.Task);
        transfer = transfer with { Player = transfer.Player with { NpcLifeCurrent = known } };
        var destination = new PlayerAuthority(null, f.Tiles); var attached = new TaskCompletionSource<bool>();
        destination.TryApply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 40, 30, true, false, attached));
        Assert.True(await attached.Task); Assert.True(destination.TryGet(f.Connection, out var member));
        Assert.Equal(known, member.NpcLifeCurrent);
        destination.TickHealthContext(); Assert.NotEqual(true, member.NpcLifeCurrent);
    }

    [Fact]
    public void Source_owned_spawn_preserves_known_life_but_saturated_revision_retires_projection()
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(100, 0);
        f.Players.TryApply(new PlayerRespawnRuntimeCommand(f.Connection, new(f.Session.Slot, 40, 30, 0, 0, 0, 0, 0)));
        Assert.True(f.Member.NpcLifeCurrent);
        Assert.Equal(f.Member.Life, f.Member.CaptureSnapshot().NpcLife);
        f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Session.Slot, 100, 100)));
        f.Member.Revision = ulong.MaxValue; f.Players.TickHealthContext();
        Assert.Null(f.Member.CaptureSnapshot().NpcLifeCurrent);
    }
}
