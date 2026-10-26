using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using Xunit;

namespace TerraRuntime.Tests.Tools1458.ActorRuntime;

internal static partial class ToolActorBoundaryChecks1458
{
    // Real membership/inventory/phase and real retained world producer. This
    // extension runs inside the existing grouped actor Fact, not a new matrix.
    private static void WorldBombPreparedTokenCurrentness()
    {
        using (var f = new ToolActorRuntimeFixture1458(false))
        {
            ReadyBombHuman(f);
            var actor = BirthBombForToken(f);
            Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var life));
            Assert.True(f.Shots.TryPrepareSimulation(in actor, out var entry)); Assert.NotNull(entry);
            var update = BombForToken(actor);
            Assert.True(entry!.TryStageWorldToolBombKill(in update, life with { TimeLeft = 0 }));
            Assert.True(entry.TryAdoptUnpublished(out var explosion, out bool expired)); Assert.False(expired);
            life = entry.Lifecycle;
            Assert.True(f.Players.TryPrepareWorldToolBombDamage(in explosion, in life, out var token)); Assert.NotNull(token);
            Assert.True(token!.ValidateExternalOwners()); Assert.True(token.IsCurrentOwned);
            Assert.False(token.IsRandomDistinctFrom(WorldBombPlayerRandom(f.Players)!));
            var before = f.Member.CaptureSnapshot(); var inventory = f.InventoryImage();
            var playerRandom = WorldBombPlayerRandom(f.Players)!.Clone(); var main = f.Random.Clone();
            f.Players.SetCombatEquipmentWorldModes(true, true);
            Assert.False(token.ValidateExternalOwners()); Assert.False(token.IsCurrentOwned);
            Assert.False(token.TrySealForJointAdoption()); Assert.False(token.TryAdoptUnpublished());
            Assert.Equal(before, f.Member.CaptureSnapshot()); SameInventory(inventory, f.InventoryImage());
            Assert.True(f.Random.HasSameState(main)); Assert.True(WorldBombPlayerRandom(f.Players)!.HasSameState(playerRandom));
        }
        using (var f = new ToolActorRuntimeFixture1458(false, aliasPlayerRandom: true))
        {
            ReadyBombHuman(f);
            var actor = BirthBombForToken(f);
            Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var life));
            var update = BombForToken(actor);
            Assert.True(f.Shots.TryPrepareSimulation(in actor, out var preview)); Assert.NotNull(preview);
            Assert.True(preview!.TryStageWorldToolBombKill(in update, life with { TimeLeft = 0 }));
            var explosion = preview.Snapshot; var shadowLife = preview.Lifecycle;
            Assert.True(f.Players.TryPrepareWorldToolBombDamage(in explosion, in shadowLife, out var token)); Assert.NotNull(token);
            Assert.False(token!.IsRandomDistinctFrom(f.Random));
            Assert.True(ReferenceEquals(WorldBombPlayerRandom(f.Players), f.Random));
            var before = f.Member.CaptureSnapshot(); var inventory = f.InventoryImage(); var main = f.Random.Clone();
            f.Sink.Events.Clear();
            Assert.True(f.Authority.TryTickState(f.Composition.Npcs.ProjectileNpcCombat));
            Assert.Equal(1, f.Authority.LastTick.Rejected); Assert.Equal(0, f.Authority.LastTick.Applied);
            Assert.True(f.Shots.TryGet(actor.Handle, out var current)); Assert.Equal(actor, current);
            Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var currentLife)); Assert.Equal(life, currentLife);
            Assert.False(currentLife.WorldToolBombKillInProgress); Assert.Empty(f.Sink.Events);
            Assert.Equal(before, f.Member.CaptureSnapshot()); SameInventory(inventory, f.InventoryImage());
            Assert.True(f.Random.HasSameState(main));
        }
    }

    private static void ReadyBombHuman(ToolActorRuntimeFixture1458 f)
    {
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        f.Move(false); f.State.Tick();
        Assert.True(f.Players.TryCaptureIncomingCombat(f.Connection.Player, out var incoming));
        Assert.True(incoming.IsAlive); Assert.False(incoming.GodMode);
    }

    private static ProjectileSnapshot BirthBombForToken(ToolActorRuntimeFixture1458 f)
    {
        var member = f.Member;
        var update = new ProjectileStateUpdate(new(75), byte.MaxValue,
            member.PositionX - 1f, member.PositionY + 10f, 0, 0, default, 0, 0, 0, 0);
        var overlay = new ToolWorldTileOverlay1458(f.Tiles);
        Assert.True(f.Composition.WorldTileAuthority.TryCreateToolMutationPlan(overlay, out var plan)); Assert.NotNull(plan);
        ProjectileSnapshot actor;
        using (plan!)
        {
            Assert.True(plan.TryStageSourceProjectile(f.Shots, in update, true, out actor));
            Assert.True(plan.TrySeal()); Assert.True(plan.TryAdoptWholeUnpublished(out var publication)); Assert.NotNull(publication);
            Assert.True(publication!.TryPublishThrough(plan.CapturePublicationOffset()));
        }
        Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var life));
        Assert.True(f.Shots.TryPrepareSimulation(in actor, out var setup)); Assert.NotNull(setup);
        Assert.True(setup!.TryStageMotion(in update, life with { TimeLeft = 3 }));
        Assert.True(setup.TryAdoptUnpublished(out actor, out bool expired)); Assert.False(expired);
        return actor;
    }

    private static ProjectileStateUpdate BombForToken(in ProjectileSnapshot actor) =>
        new(actor.Type, actor.Spawner, actor.PositionX - 53f, actor.PositionY - 53f,
            actor.VelocityX, actor.VelocityY, actor.Ai, actor.BannerIdToRespondTo, 60, 8, 0);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateRandom")]
    private static extern ref VanillaUnifiedRandom1458? WorldBombPlayerRandom(PlayerAuthority owner);
}
