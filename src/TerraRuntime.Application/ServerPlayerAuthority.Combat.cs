using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    internal PlayerDamageCommitResult TryCommitAuthoritativePvpDamage(
        long tick, in PlayerStateSnapshot attacker, PlayerHandle target, DamageSource source,
        int damage, bool critical, int hitDirection, bool expertMode, bool masterMode,
        out PlayerStateSnapshot committed)
    {
        committed = default;
        if (!attacker.Player.IsAssigned || attacker.Player == target || attacker.IsDead || !attacker.Hostile ||
            !source.IsValid || source.Player != attacker.Player ||
            source.Kind is not (DamageSourceKind.PlayerItem or DamageSourceKind.PlayerProjectile) ||
            !states.TryGet(target, out PlayerStateSnapshot victim) || !victim.Hostile ||
            (attacker.Team != 0 && attacker.Team == victim.Team))
            return PlayerDamageCommitResult.Rejected;
        return TryCommitAuthoritativeDamage(tick, target, source, default, damage, hitDirection,
            default, expertMode, masterMode, out committed, pvp: true, critical);
    }

    internal bool TryCaptureCombatSnapshot(PlayerHandle player, out VanillaPlayerCombatSnapshot snapshot)
    {
        snapshot = default;
        if (!states.TryGet(player, out PlayerStateSnapshot state) || state.Player != player)
            return false;

        Span<PlayerEquipmentCommitRequest> equipment = stackalloc PlayerEquipmentCommitRequest[
            VanillaPlayerItemSlotCatalog.BaselineFunctionalArmorCount];
        int count = 0;
        for (short slot = VanillaPlayerItemSlotCatalog.ArmorStart;
             slot < VanillaPlayerItemSlotCatalog.BaselineFunctionalArmorEndExclusive;
             slot++)
        {
            if (!states.TryGetItem(player, slot, out ServerPlayerItemState item))
                return false;
            if (item.IsEmpty)
                continue;
            if (item.Stack != 1 || item.ItemType.Value > short.MaxValue || item.Prefix.Value > byte.MaxValue)
                return false;

            equipment[count++] = new PlayerEquipmentCommitRequest(
                player.Slot,
                slot,
                item.Stack,
                checked((byte)item.Prefix.Value),
                checked((short)item.ItemType.Value),
                item.ItemFlags);
        }

        return VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment[..count], out snapshot);
    }

    internal PlayerDamageCommitResult TryCommitAuthoritativeNpcContactDamage(
        long tick,
        NpcHandle sourceNpc,
        PlayerHandle target,
        int damage,
        int hitDirection,
        VanillaPlayerImmunityChannel1458 immunityChannel,
        bool expertMode,
        bool masterMode,
        out PlayerStateSnapshot committed) =>
        TryCommitAuthoritativeDamage(
            tick,
            target,
            DamageSource.FromNpcContact(sourceNpc),
            default,
            damage,
            hitDirection,
            immunityChannel,
            expertMode,
            masterMode,
            out committed);

    internal PlayerDamageCommitResult TryCommitAuthoritativeNpcProjectileDamage(
        long tick,
        NpcHandle sourceNpc,
        ProjectileHandle projectile,
        ProjectileTypeId projectileType,
        PlayerHandle target,
        int damage,
        int hitDirection,
        VanillaPlayerImmunityChannel1458 immunityChannel,
        bool expertMode,
        bool masterMode,
        out PlayerStateSnapshot committed) =>
        TryCommitAuthoritativeDamage(
            tick,
            target,
            DamageSource.FromNpcProjectile(sourceNpc, projectile),
            projectileType,
            damage,
            hitDirection,
            immunityChannel,
            expertMode,
            masterMode,
            out committed);

    internal PlayerDamageCommitResult TryCommitAuthoritativeEnvironmentProjectileDamage(
        long tick, ProjectileHandle projectile, ProjectileTypeId projectileType, PlayerHandle target,
        int damage, int hitDirection, VanillaPlayerImmunityChannel1458 immunityChannel, bool expertMode, bool masterMode,
        out PlayerStateSnapshot committed) =>
        TryCommitAuthoritativeDamage(tick, target, DamageSource.FromEnvironmentProjectile(projectile), projectileType,
            damage, hitDirection, immunityChannel, expertMode, masterMode, out committed);

    private PlayerDamageCommitResult TryCommitAuthoritativeDamage(
        long tick,
        PlayerHandle target,
        DamageSource source,
        ProjectileTypeId projectileType,
        int damage,
        int hitDirection,
        VanillaPlayerImmunityChannel1458 immunityChannel,
        bool expertMode,
        bool masterMode,
        out PlayerStateSnapshot committed,
        bool pvp = false,
        bool critical = false)
    {
        committed = default;
        if (!target.IsAssigned || !source.IsValid || damage <= 0 || damage > short.MaxValue ||
            hitDirection is < -1 or > 1 || (masterMode && !expertMode) ||
            !states.TryGet(target, out PlayerStateSnapshot current) || current.Player != target ||
            current.IsDead || !current.HasHealth || current.Life <= 0)
        {
            return PlayerDamageCommitResult.Rejected;
        }

        if (current.GodMode)
            return PlayerDamageCommitResult.AvoidedByGodMode;
        if (!TryCaptureCombatSnapshot(target, out VanillaPlayerCombatSnapshot targetCombat))
            return PlayerDamageCommitResult.Rejected;

        bool immune = pvp ? damageImmunity.IsPvpImmune(target, tick) : damageImmunity.IsPveImmune(target, immunityChannel, tick);
        var attack = new AuthoritativeAttackDamage(
            source,
            damage,
            ArmorPenetration: 0,
            Critical: critical,
            KnockBack: 4.5f,
            hitDirection);
        FinalDamageToHp final;
        bool resolved = pvp
            ? VanillaCombatDamagePipeline.TryResolvePvp(in attack, in targetCombat, immune, out final, expertMode, masterMode)
            : VanillaCombatDamagePipeline.TryResolvePlayerDamage(in attack, in targetCombat, immune, out final, expertMode, masterMode);
        if (!resolved ||
            final.Damage <= 0)
        {
            return PlayerDamageCommitResult.Rejected;
        }

        return CommitResolvedDamage(tick, in current, source, projectileType, in final, hitDirection,
            immunityChannel, pvp, damageOverTime: false, out committed);
    }

    private PlayerDamageCommitResult CommitResolvedDamage(
        long tick, in PlayerStateSnapshot current, DamageSource source, ProjectileTypeId projectileType,
        in FinalDamageToHp final, int hitDirection, VanillaPlayerImmunityChannel1458 immunityChannel,
        bool pvp, bool damageOverTime, out PlayerStateSnapshot committed)
    {
        PlayerHandle target = current.Player;
        committed = default;
        short nextLife = checked((short)Math.Max(0, current.Life - final.Damage));
        var vitals = new ServerPlayerVitalsState(nextLife, current.MaxLife, current.Mana, current.MaxMana);
        if (!states.TrySetVitals(target, in vitals, out PlayerStateSnapshot afterVitals))
            return PlayerDamageCommitResult.Rejected;

        committed = afterVitals;
        if (!final.Mitigation.NoKnockback && hitDirection != 0 &&
            states.TrySetMotion(
                target,
                afterVitals.PositionX,
                afterVitals.PositionY,
                4.5f * hitDirection,
                -3.5f,
                afterVitals.ControlFlags,
                out PlayerStateSnapshot afterMotion))
        {
            committed = afterMotion;
            events?.ServerPlayerMoved(in afterMotion);
        }

        if (pvp)
            damageImmunity.RecordPvp(target, tick + 8);
        else if (!damageOverTime)
            damageImmunity.RecordPve(target, immunityChannel,
                tick + VanillaIncomingPlayerDamageFacts1458.ResolvePveImmunityTicks(final.Damage));
        var committedVitals = new ServerPlayerVitalsState(
            committed.Life,
            committed.MaxLife,
            committed.Mana,
            committed.MaxMana);
        events?.ServerPlayerVitalsUpdated(target, in committedVitals);
        if (!current.IsDead && committed.IsDead)
        {
            horizontalIntents.Remove(target);
            jumpIntents.Remove(target);
            jumpStates.Remove(target);
            movementIntents.Remove(target);
            // Player.UpdateLifeRegen calls KillMe(ByOther(8), 10, 0), not Hurt's per-tick HP loss.
            events?.ServerPlayerDied(target, source, projectileType, damageOverTime ? 10 : final.Damage, hitDirection);
            ResetLavaState(target);
        }
        return PlayerDamageCommitResult.Committed;
    }
}
