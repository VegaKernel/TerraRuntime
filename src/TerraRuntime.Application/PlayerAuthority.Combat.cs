using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private void ApplyPlayerPvpToggle(PlayerPvpToggleRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out RuntimePlayerMember? player) || !player.TryAdvanceRevision())
        {
            RejectedPvpToggles++;
            return;
        }

        player.Hostile = command.Hostile;
        AppliedPvpToggles++;
    }

    private void ApplyPlayerTeam(PlayerTeamRuntimeCommand command)
    {
        if (command.Team > 5 || !membership.TryGet(command.Connection, out RuntimePlayerMember? player) || !player.TryAdvanceRevision())
        {
            RejectedTeamChanges++;
            return;
        }

        player.Team = command.Team;
        AppliedTeamChanges++;
    }

    private void ApplyClientPvpHit(ClientPlayerPvpHitRuntimeCommand command)
    {
        PvpCombatResolveResult resolve = pvpCombat.ResolveClientItemHit(
            currentCombatTick,
            command.Connection,
            command.State,
            out AuthoritativePvpHit hit);
        if (resolve == PvpCombatResolveResult.LegacyFallback)
        {
            LegacyPvpFallbackHits++;
            return;
        }
        if (resolve != PvpCombatResolveResult.Accepted)
        {
            RejectedAuthoritativePvpHits++;
            return;
        }

        PlayerDamageCommitResult commitResult = TryCommitAuthoritativePvpDamage(
                currentCombatTick,
                hit.Attacker,
                hit.Target,
                hit.Context.Source,
                hit.Damage,
                hit.Critical,
                hit.HitDirection,
                out _);
        if (commitResult == PlayerDamageCommitResult.Rejected)
        {
            RejectedAuthoritativePvpHits++;
            return;
        }

        if (commitResult == PlayerDamageCommitResult.Committed)
            AppliedAuthoritativePvpHits++;
    }

    internal PlayerDamageCommitResult TryCommitAuthoritativePvpDamage(
        long tick,
        PlayerHandle attacker,
        PlayerHandle targetHandle,
        DamageSource sourceDamage,
        int damage,
        bool critical,
        int hitDirection,
        out PlayerStateSnapshot committed)
    {
        committed = default;
        if (!attacker.IsAssigned || !targetHandle.IsAssigned || attacker == targetHandle ||
            !sourceDamage.IsValid || sourceDamage.Player != attacker || damage <= 0 ||
            hitDirection is < -1 or > 1 ||
            !membership.TryGet(attacker, out RuntimePlayerMember? source) ||
            !membership.TryGet(targetHandle, out RuntimePlayerMember? target) ||
            !source.Hostile || !target.Hostile || source.IsDead || target.IsDead || !target.HasHealth || target.Life <= 0 ||
            (source.Team != 0 && source.Team == target.Team))
        {
            return PlayerDamageCommitResult.Rejected;
        }

        if (target.GodMode)
            return AvoidGodModeDamage(tick, targetHandle, target);

        if (!TryCaptureCombatSnapshot(targetHandle, out VanillaPlayerCombatSnapshot targetCombat))
            return PlayerDamageCommitResult.Rejected;

        bool immune = damageImmunity.IsPvpImmune(targetHandle, tick);
        var attack = new AuthoritativeAttackDamage(
            sourceDamage,
            damage,
            ArmorPenetration: 0,
            critical,
            KnockBack: 4.5f,
            hitDirection);
        if (!VanillaCombatDamagePipeline.TryResolvePvp(
                in attack,
                in targetCombat,
                immune,
                out FinalDamageToHp final,
                expertMode,
                masterMode) ||
            final.Damage <= 0 ||
            !target.TryAdvanceRevision())
        {
            return PlayerDamageCommitResult.Rejected;
        }

        target.Life = checked((short)Math.Max(0, target.Life - final.Damage));
        target.IsDead = target.Life <= 0;
        if (!final.Mitigation.NoKnockback && hitDirection != 0)
        {
            // Player.Hurt(pvp:true) uses this fixed vanilla impulse; weapon knockback is not an input here.
            target.VelocityX = 4.5f * hitDirection;
            target.VelocityY = -3.5f;
        }
        damageImmunity.RecordPvp(targetHandle, tick + 8);
        committed = target.CaptureSnapshot();
        var health = new PlayerHealthCommitRequest(target.Slot, target.Life, target.MaxLife);
        events?.PlayerAuthoritativeHealthUpdated(target.Connection, in health);
        return PlayerDamageCommitResult.Committed;
    }


    internal PlayerDamageCommitResult TryCommitAuthoritativeNpcContactDamage(
        long tick,
        NpcHandle sourceNpc,
        PlayerHandle targetHandle,
        int damage,
        int hitDirection,
        VanillaPlayerImmunityChannel1458 immunityChannel,
        out PlayerStateSnapshot committed) =>
        TryCommitAuthoritativePveDamage(
            tick,
            targetHandle,
            DamageSource.FromNpcContact(sourceNpc),
            damage,
            hitDirection,
            immunityChannel,
            out committed);

    internal PlayerDamageCommitResult TryCommitAuthoritativeNpcProjectileDamage(
        long tick,
        NpcHandle sourceNpc,
        ProjectileHandle projectile,
        PlayerHandle targetHandle,
        int damage,
        int hitDirection,
        VanillaPlayerImmunityChannel1458 immunityChannel,
        out PlayerStateSnapshot committed) =>
        TryCommitAuthoritativePveDamage(
            tick,
            targetHandle,
            DamageSource.FromNpcProjectile(sourceNpc, projectile),
            damage,
            hitDirection,
            immunityChannel,
            out committed);

    private PlayerDamageCommitResult TryCommitAuthoritativePveDamage(
        long tick,
        PlayerHandle targetHandle,
        DamageSource sourceDamage,
        int damage,
        int hitDirection,
        VanillaPlayerImmunityChannel1458 immunityChannel,
        out PlayerStateSnapshot committed)
    {
        committed = default;
        if (!targetHandle.IsAssigned || !sourceDamage.IsValid || damage <= 0 ||
            hitDirection is < -1 or > 1 ||
            !membership.TryGet(targetHandle, out RuntimePlayerMember? target) ||
            target.IsDead || !target.HasHealth || target.Life <= 0)
        {
            return PlayerDamageCommitResult.Rejected;
        }

        if (target.GodMode)
            return AvoidGodModeDamage(tick, targetHandle, target);

        if (!TryCaptureCombatSnapshot(targetHandle, out VanillaPlayerCombatSnapshot targetCombat))
            return PlayerDamageCommitResult.Rejected;

        bool immune = damageImmunity.IsPveImmune(targetHandle, immunityChannel, tick);
        var attack = new AuthoritativeAttackDamage(
            sourceDamage,
            damage,
            ArmorPenetration: 0,
            Critical: false,
            KnockBack: 4.5f,
            hitDirection);
        if (!VanillaCombatDamagePipeline.TryResolvePlayerDamage(
                in attack,
                in targetCombat,
                immune,
                out FinalDamageToHp final,
                expertMode,
                masterMode) ||
            final.Damage <= 0 ||
            !target.TryAdvanceRevision())
        {
            return PlayerDamageCommitResult.Rejected;
        }

        target.Life = checked((short)Math.Max(0, target.Life - final.Damage));
        target.IsDead = target.Life <= 0;
        if (!final.Mitigation.NoKnockback && hitDirection != 0)
        {
            target.VelocityX = 4.5f * hitDirection;
            target.VelocityY = -3.5f;
        }

        long until = tick + VanillaIncomingPlayerDamageFacts1458.ResolvePveImmunityTicks(final.Damage);
        damageImmunity.RecordPve(targetHandle, immunityChannel, until);

        committed = target.CaptureSnapshot();
        var health = new PlayerHealthCommitRequest(target.Slot, target.Life, target.MaxLife);
        events?.PlayerAuthoritativeHealthUpdated(target.Connection, in health);
        return PlayerDamageCommitResult.Committed;
    }

    private PlayerDamageCommitResult AvoidGodModeDamage(
        long tick,
        PlayerHandle targetHandle,
        RuntimePlayerMember target)
    {
        // Terraria applies several Hurt paths locally before packet 16/13 reach the server. Merely refusing
        // authoritative damage therefore leaves a brief local HP loss and knockback. Reassert both owner-only
        // states immediately; MISS remains a separate visual acknowledgement for observers.
        ReassertGodModeOwnerState(target, tick);

        events?.PlayerDamageAvoided(
            targetHandle,
            target.PositionX + VanillaBasePlayerWidth * 0.5f,
            target.PositionY + VanillaBasePlayerHeight * 0.5f,
            GodModeCombatText.Select(targetHandle, tick));
        return PlayerDamageCommitResult.AvoidedByGodMode;
    }

    private void ApplySetPlayerGodMode(SetPlayerGodModeRuntimeCommand command)
    {
        if (!membership.TryGet(command.Player, out RuntimePlayerMember? player) || !player.TryAdvanceRevision())
        {
            command.Completion.TrySetResult(false);
            return;
        }

        player.GodMode = command.Enabled;
        if (!command.Enabled)
            ClearGodModeMovementCorrection(command.Player);
        command.Completion.TrySetResult(true);
    }

    private void ApplyGetPlayerGodMode(GetPlayerGodModeRuntimeCommand command)
    {
        command.Completion.TrySetResult(
            membership.TryGet(command.Player, out RuntimePlayerMember? player) ? player.GodMode : null);
    }

}
