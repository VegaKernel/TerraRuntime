using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class NpcDeathPreludeUnpublished1458Tests
{
    [Fact]
    public void Admission_adopts_credit_and_immutable_join_view_before_publication()
    {
        var owner = new RuntimeNpcDeathPrelude1458();
        var oldJoin = owner.CaptureJoinFrames();
        var preview = DeathPreview(owner);
        Assert.Empty(owner.CaptureBestiary().Kills);
        Assert.True(owner.TryAdoptUnpublished(preview, owner.Revision, out var publication));
        Assert.NotNull(publication);
        Assert.Single(owner.CaptureBestiary().Kills);
        Assert.Equal(1, owner.CaptureBanners().KillCounts.Sum());
        Assert.NotSame(oldJoin, owner.CaptureJoinFrames());
        Assert.Single(oldJoin);
        var adopted = owner.CaptureJoinFrames();
        Assert.True(publication.TryPublish());
        Assert.False(publication.TryPublish());
        Assert.Same(adopted, owner.CaptureJoinFrames());
        Assert.Equal(preview.Revision, owner.Revision);
    }

    [Fact]
    public void Stale_admission_preserves_newer_credit_and_does_not_create_publication()
    {
        var owner = new RuntimeNpcDeathPrelude1458();
        var stale = DeathPreview(owner);
        var accepted = DeathPreview(owner);
        Assert.True(owner.TryAdoptUnpublished(accepted, 1, out _));
        var join = owner.CaptureJoinFrames();
        Assert.False(owner.TryAdoptUnpublished(stale, 1, out var rejected));
        Assert.Null(rejected);
        Assert.False(owner.TryAdoptUnpublished(owner, owner.Revision, out _));
        Assert.Same(join, owner.CaptureJoinFrames());
        Assert.Equal(1, owner.CaptureBanners().KillCounts.Sum());
    }

    [Fact]
    public void Late_publication_cannot_restore_credit_after_a_subsequent_death()
    {
        var owner = new RuntimeNpcDeathPrelude1458();
        Assert.True(owner.TryAdoptUnpublished(DeathPreview(owner), owner.Revision, out var first));
        Assert.True(owner.TryAdoptUnpublished(DeathPreview(owner), owner.Revision, out var second));
        var join = owner.CaptureJoinFrames();
        Assert.True(first!.TryPublish());
        Assert.True(second!.TryPublish());
        Assert.False(first.TryPublish());
        Assert.Equal(2, owner.CaptureBanners().KillCounts.Sum());
        Assert.Equal(2, Assert.Single(owner.CaptureBestiary().Kills).KillCount);
        Assert.Same(join, owner.CaptureJoinFrames());
    }

    private static RuntimeNpcDeathPrelude1458 DeathPreview(RuntimeNpcDeathPrelude1458 owner)
    {
        var preview = owner.CreatePreview();
        var npc = new NpcSnapshot(new(0, new(1)), new(1), 3, 3, 100, 100, 0, 0, 0,
            default, NpcSimulationState.Initial);
        var context = new RuntimeNpcDeathPreludeContext1458(true, false, false, false, false, true,
            false, false, default, HasOwnInteractions: true);
        Assert.True(preview.TryApply(in npc, in context, new VanillaUnifiedRandom1458(0), out bool loot));
        Assert.True(loot);
        return preview;
    }
}
