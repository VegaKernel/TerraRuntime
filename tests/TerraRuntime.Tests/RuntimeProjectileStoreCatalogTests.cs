using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileStoreCatalogTests
{
    [Fact]
    public void Store_rejects_none_and_unknown_projectile_types_before_commit()
    {
        var sink = new RecordingSink();
        var store = new RuntimeProjectileStore(capacity: 4, commitSink: sink);
        ProjectileStateUpdate none = CreateUpdate(new ProjectileTypeId(0));
        ProjectileStateUpdate unknown = CreateUpdate(new ProjectileTypeId(VanillaProjectileIds.Count));
        ProjectileStateUpdate valid = CreateUpdate(VanillaProjectileIds.WoodenArrowFriendly);

        Assert.False(store.TrySpawn(0, in none, out _));
        Assert.False(store.TrySpawn(1, in unknown, out _));
        Assert.True(store.TrySpawn(2, in valid, out ProjectileSnapshot created));

        Assert.Equal(1, store.ActiveCount);
        Assert.Equal(VanillaProjectileIds.WoodenArrowFriendly, created.Type);
        Assert.Single(sink.Commits);
        Assert.Equal(ProjectileStateCommitKind.Spawn, sink.Commits[0].Kind);
        Assert.Equal(created, sink.Commits[0].Snapshot);
    }

    [Fact]
    public void Invalid_type_update_does_not_advance_revision_or_publish_commit()
    {
        var sink = new RecordingSink();
        var store = new RuntimeProjectileStore(capacity: 4, commitSink: sink);
        ProjectileStateUpdate valid = CreateUpdate(VanillaProjectileIds.WoodenArrowFriendly);
        Assert.True(store.TrySpawn(1, in valid, out ProjectileSnapshot created));
        ProjectileStateUpdate invalid = valid with
        {
            Type = new ProjectileTypeId(VanillaProjectileIds.Count),
            PositionX = 999f
        };

        Assert.False(store.TryUpdate(created.Handle, in invalid, out _));

        Assert.True(store.TryGet(created.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal(new ProjectileRevision(1), unchanged.Revision);
        Assert.Equal(valid.PositionX, unchanged.PositionX);
        Assert.Single(sink.Commits);
    }

    private static ProjectileStateUpdate CreateUpdate(ProjectileTypeId type) =>
        new(
            Type: type,
            Spawner: 3,
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 1f,
            VelocityY: 2f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 10,
            KnockBack: 1f,
            OriginalDamage: 10);

    private sealed class RecordingSink : IProjectileStateCommitSink
    {
        public List<(ProjectileStateCommitKind Kind, ProjectileSnapshot Snapshot)> Commits { get; } = [];

        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
            Commits.Add((kind, snapshot));
    }
}
