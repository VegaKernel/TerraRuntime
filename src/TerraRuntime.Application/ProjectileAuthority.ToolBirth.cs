using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;
using ProjectileDefinitions = TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog;

namespace TerraRuntime.Application;

internal sealed class ToolBirthPreparation1458(PlayerToolAttemptPreparation player,
    ToolDefinition1458 tool, ProjectileStateUpdate state,
    RuntimeProjectileStore.SourceOwnerTypeQueryPreparation? uniqueness)
{
    internal PlayerToolAttemptPreparation Player { get; } = player;
    internal ToolDefinition1458 Tool { get; } = tool;
    internal ProjectileStateUpdate State { get; } = state;
    internal bool IsCurrent => Player.IsCurrent &&
        (uniqueness is null || !uniqueness.Contains && uniqueness.IsCurrentOwned);

    // Root's joint participant reserves the exact Core birth generation first.
    // Staging this clock does not adopt or publish either owner.
    internal bool TryStagePlayer(ProjectileHandle preparedHandle) =>
        IsCurrent && Player.TryStageToolBirth(Tool, preparedHandle);
}

// One-shot publication for an already adopted/trusted birth. No lazy spawning,
// player clock write, RNG draw or generation reassignment is allowed here.
internal sealed class ToolBirthPublication1458(PlayerAuthority players, ConnectionHandle connection,
    RuntimeProjectileStore store, RuntimeProjectileStore.SpawnPreparation spawn,
    RuntimeProjectileReplicationRegistry.SpawnBirthJournalPreparation journal, ProjectileSnapshot birth)
{
    private bool published;
    internal bool TryPublish()
    {
        if (published) return false;
        published = true; // Consume before reentry, including stale/replaced ownership.
        if (!players.TryGet(connection, out _) ||
            !store.TryGet(birth.Handle, out var current) || current != birth ||
            !store.TryGetCombatTrustedOwner(birth.Handle, out var owner) || owner != connection.Player ||
            !journal.TryPublish(store)) return false;
        using var observer = journal.EnterObserverPublication();
        return spawn.TryPublish(in birth);
    }
}

internal sealed partial class ProjectileAuthority
{
    internal bool HasAdoptedToolBirth(ConnectionHandle connection, in TerrariaProjectileUpdateState report,
        ProjectileHandle handle) => replication is not null &&
        replication.WireIdentities.TryResolve(report.Key, out var resolved) && resolved == handle &&
        projectiles.TryGet(handle, out var current) && current.Type.Value == report.ProjectileType &&
        projectiles.TryGetCombatTrustedOwner(handle, out var owner) && owner == connection.Player;

    internal bool TryCommitPendingToolBirth(ConnectionHandle connection)
    {
        if (!TryCommitPendingToolBirth(connection, out var publication) || publication is null) return false;
        publication.TryPublish();
        return true;
    }

    // Joint phase drain retains this typed publication until ALL clock/world/HP
    // participants have adopted. No observer is invoked by this overload.
    internal bool TryCommitPendingToolBirth(ConnectionHandle connection, out ToolBirthPublication1458? pendingPublication)
    {
        pendingPublication = null;
        if (replication is null || !TryPrepareToolBirth(connection, out var plan) || plan is null ||
            plan.Player.Before.Input?.ShootReport is not { } report ||
            replication.WireIdentities.TryResolve(report.Key, out _) ||
            !projectiles.TryPrepareToolSpawn(plan.State, out var spawn) || spawn is null)
            return false;
        var preview = spawn.PreparedBirth;
        if (!TryPrepareToolActorBirth(plan, in preview, out var actorBirth) || actorBirth is null ||
            !plan.TryStagePlayer(preview.Handle) ||
            !replication.TryPrepareSpawnBirthJournal(new[] { preview }, new[] { report.Key },
                connection.Source, out var publication) || publication is null ||
            !publication.IsCurrentOwned || !spawn.IsCurrent || !plan.IsCurrent ||
            !actorBirth.TrySealForAdoption())
            return false;
        // After this complete preflight all writes are callback-free. The known
        // source query may change only by this exact owned birth itself.
        if (!spawn.TryCommitUnpublished(out var born)) return false;
        if (born.Handle != preview.Handle || !plan.Player.TryAdoptUnpublished() ||
            !projectiles.TryMarkCombatTrusted(born.Handle, connection.Player) ||
            !actorBirth.TryAdoptUnpublished(in born) ||
            !publication.TryAdoptBaselines(projectiles) ||
            !plan.Player.TryRebaseOtherToolInputs())
            throw new InvalidOperationException("Validated tool birth lost joint ownership before publication.");
        AppliedSpawns++;
        PromotedClientProjectileSpawns++;
        pendingPublication = new(players, connection, projectiles, spawn, publication, born);
        return true;
    }

    internal bool TryPrepareToolBirth(ConnectionHandle connection, out ToolBirthPreparation1458? preparation)
    {
        preparation = null;
        if (!players.TryPrepareToolAttempt(connection, out var player) || player is null ||
            !player.Capture.HasCurrentSourceStage ||
            player.Before is not { ShootEligible: true, ToolStagePending: true,
                SourceStagePose: { } pose, Input.ShootReport: { } packet } ||
            player.Capture.Actor.Player.IsDead ||
            pose.Player.HasMount || pose.Width != PlayerAuthority.VanillaBasePlayerWidth ||
            pose.Height != PlayerAuthority.VanillaBasePlayerHeight ||
            pose.GravityDirection is not (1f or -1f) ||
            player.Capture.Phase.DerivedCombat is not { } combat ||
            !VanillaToolCatalog1458.TryResolve(player.Capture.Selected.ItemType,
                player.Capture.Selected.Prefix, player.Capture.World.RemixWorld,
                player.Capture.World.WindowsItemPrefixArithmetic, out var tool) ||
            !VanillaToolCatalog1458.TryGetLaunchStats(in tool, combat.MeleeDamage,
                combat.MeleeAnimationMultiplier, false, false,
                player.Capture.World.WindowsItemPrefixArithmetic, out var launch) ||
            !ProjectileDefinitions.TryGet(launch.Projectile, out var definition) ||
            packet.Key.Spawner != connection.Player.Slot.Value ||
            packet.OriginalDamage != 0 || packet.BannerIdToRespondTo != 0 ||
            BitConverter.SingleToInt32Bits(packet.Ai0) != 0 ||
            BitConverter.SingleToInt32Bits(packet.Ai1) != 0 ||
            BitConverter.SingleToInt32Bits(packet.Ai2) != 0 ||
            launch.Damage is < 0 or > short.MaxValue)
            return false;
        // Admitted equipment has no kbGlove producer and buff14 is outside the
        // represented phase. A future such consumer must project its own facts.
        float length = MathF.Sqrt(packet.VelocityX * packet.VelocityX + packet.VelocityY * packet.VelocityY);
        if (!float.IsFinite(length) || length <= 0f ||
            !float.IsFinite(launch.Speed) || launch.Speed <= 0f ||
            MathF.Abs(length - launch.Speed) > 0.002f) return false;
        var state = new ProjectileStateUpdate(launch.Projectile, connection.Player.Slot.Value,
            pose.Player.PositionX + pose.Width * 0.5f - definition.Width * 0.5f,
            pose.Player.PositionY + pose.Height * 0.5f - definition.Height * 0.5f,
            packet.VelocityX, packet.VelocityY, new(0f, 0f, 0f),
            0, (short)launch.Damage, launch.KnockBack, 0);
        if (!MatchesBulletReport(in packet, in state)) return false;
        RuntimeProjectileStore.SourceOwnerTypeQueryPreparation? uniqueness = null;
        if (tool.Shoot == 1052 &&
            (!projectiles.TryPrepareSourceOwnerTypeQuery(connection.Player.Slot.Value,
                launch.Projectile, out uniqueness) || uniqueness is null || uniqueness.Contains))
            return false;
        preparation = new(player, tool, state, uniqueness);
        return preparation.IsCurrent;
    }
}
