using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    internal bool IsGeneralPveImmune(PlayerHandle player, long tick) =>
        damageImmunity.IsPveImmune(player, VanillaPlayerImmunityChannel1458.General, tick);

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

        bool? extraAccessory = states.TryGetAppearance(player, out var appearance)
            ? (appearance.DifficultyFlags & VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag) != 0 : null;
        var context = new VanillaPlayerCombatEquipmentContext(extraAccessory, combatEquipmentExpertMode, combatEquipmentMasterMode)
        { LocalVanityArmor = combatEquipmentLocalVanityArmor };
        Span<PlayerEquipmentCommitRequest> equipment = stackalloc PlayerEquipmentCommitRequest[
            VanillaPlayerItemSlotCatalog.ArmorCount + VanillaPlayerItemSlotCatalog.FunctionalArmorCount * VanillaPlayerItemSlotCatalog.LoadoutCount];
        int count = 0;
        for (short slot = VanillaPlayerItemSlotCatalog.ArmorStart;
             slot < VanillaPlayerItemSlotCatalog.VanityArmorEndExclusive;
             slot++)
        {
            if (!states.TryGetItem(player, slot, out ServerPlayerItemState item))
                return false;
            if (item.IsEmpty)
                continue;
            if (item.ItemType.Value > short.MaxValue || item.Prefix.Value > byte.MaxValue)
                return false;

            equipment[count++] = new PlayerEquipmentCommitRequest(
                player.Slot,
                slot,
                item.Stack,
                checked((byte)item.Prefix.Value),
                checked((short)item.ItemType.Value),
                item.ItemFlags);
        }

        for (int loadout = 0; loadout < VanillaPlayerItemSlotCatalog.LoadoutCount; loadout++)
        for (int index = 0; index < VanillaPlayerItemSlotCatalog.FunctionalArmorCount; index++)
        {
            short slot = checked((short)(VanillaPlayerItemSlotCatalog.LoadoutArmorStart +
                loadout * VanillaPlayerItemSlotCatalog.LoadoutStride + index));
            if (!states.TryGetItem(player, slot, out var item)) return false;
            if (item.IsEmpty || (item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) == 0) continue;
            if (item.ItemType.Value > short.MaxValue || item.Prefix.Value > byte.MaxValue) return false;
            equipment[count++] = new(player.Slot, slot, item.Stack, checked((byte)item.Prefix.Value),
                checked((short)item.ItemType.Value), item.ItemFlags);
        }
        return VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment[..count], in context, out snapshot);
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
        out PlayerStateSnapshot committed,
        NpcContactBuffSource1458? contactBuffs = null) =>
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
            out committed, contactBuffs: contactBuffs);

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

    internal PlayerDamageCommitResult TryCommitAuthoritativeNpcProjectileDamageWithStatus(
        long tick, NpcHandle sourceNpc, ProjectileHandle projectile, ProjectileTypeId projectileType,
        PlayerHandle target, int damage, int hitDirection, VanillaPlayerImmunityChannel1458 immunityChannel,
        bool expertMode, bool masterMode, in ProjectilePlayerHurt1458 status,
        out PlayerStateSnapshot committed) =>
        TryCommitAuthoritativeDamage(tick, target, DamageSource.FromNpcProjectile(sourceNpc, projectile),
            projectileType, damage, hitDirection, immunityChannel, expertMode, masterMode,
            out committed, projectileStatus: status);

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
        bool critical = false,
        NpcContactBuffSource1458? contactBuffs = null,
        ProjectilePlayerHurt1458? projectileStatus = null)
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
            : VanillaCombatDamagePipeline.TryResolvePlayerDamage(in attack, in targetCombat, immune, out final,
                expertMode || projectileStatus is { GoodWorld: true },
                masterMode || expertMode && projectileStatus is { GoodWorld: true });
        if (!resolved ||
            final.Damage <= 0)
        {
            return PlayerDamageCommitResult.Rejected;
        }

        return CommitResolvedDamage(tick, in current, source, projectileType, in final, hitDirection,
            immunityChannel, pvp, damageOverTime: false, out committed, contactBuffs, targetCombat, expertMode, masterMode, projectileStatus);
    }

    private PlayerDamageCommitResult CommitResolvedDamage(
        long tick, in PlayerStateSnapshot current, DamageSource source, ProjectileTypeId projectileType,
        in FinalDamageToHp final, int hitDirection, VanillaPlayerImmunityChannel1458 immunityChannel,
        bool pvp, bool damageOverTime, out PlayerStateSnapshot committed,
        NpcContactBuffSource1458? contactBuffs = null, VanillaPlayerCombatSnapshot targetCombat = default,
        bool expertMode = false, bool masterMode = false, ProjectilePlayerHurt1458? projectileStatus = null,
        List<ResolvedDamagePublication>? deferredPublications = null, PlayerBuffState? preparedProjectileBuffs = null)
    {
        PlayerHandle target = current.Player;
        committed = default;
        short nextLife = ResolveLifeAfterDamage(in current, in final);
        var vitals = new ServerPlayerVitalsState(nextLife, current.MaxLife, current.Mana, current.MaxMana);
        if (!states.TrySetVitals(target, in vitals, out PlayerStateSnapshot afterVitals))
            return PlayerDamageCommitResult.Rejected;

        committed = afterVitals;
        bool moved = false;
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
            moved = true;
        }

        if (pvp)
            damageImmunity.RecordPvp(target, tick + 8);
        else if (!damageOverTime)
            damageImmunity.RecordPve(target, immunityChannel,
                tick + VanillaIncomingPlayerDamageFacts1458.ResolvePveImmunityTicks(final.Damage));
        // StatusFromNPC follows successful, nonlethal Hurt. Adopt every status before the first observer
        // can disconnect or replace this generation, and keep RNG after the accepted-damage boundary.
        bool buffsApplied = contactBuffs is { } status && !committed.IsDead &&
            ApplyNpcContactBuffs(target, in status, in targetCombat, expertMode, masterMode);
        if (preparedProjectileBuffs is not null && !committed.IsDead)
        {
            int slot = target.Slot.Value;
            if (npcContactBuffOwners[slot] != target) npcContactDebuffEffects[slot] = default;
            npcContactBuffOwners[slot] = target;
            npcContactBuffStates[slot] = preparedProjectileBuffs;
            buffsApplied = true;
        }
        else if (projectileStatus is { } projectileSource && !committed.IsDead)
            buffsApplied |= ApplyProjectileStatus(target, in targetCombat, in projectileSource, final.Damage, expertMode, masterMode);
        bool died = !current.IsDead && committed.IsDead;
        bool deathBuffsCleared = false;
        if (died)
        {
            horizontalIntents.Remove(target);
            jumpIntents.Remove(target);
            jumpStates.Remove(target);
            movementIntents.Remove(target);
            deathBuffsCleared = ResetPlayerBuffs(target, publish: false);
        }
        var publication = new ResolvedDamagePublication(committed, source, projectileType,
            damageOverTime ? 10 : final.Damage, hitDirection, moved, buffsApplied || deathBuffsCleared, died,
            buffsApplied || deathBuffsCleared ? CaptureBuffTypes(target) : []);
        if (deferredPublications is not null) deferredPublications.Add(publication);
        else PublishResolvedDamage(publication);
        return PlayerDamageCommitResult.Committed;
    }

    private static short ResolveLifeAfterDamage(in PlayerStateSnapshot current, in FinalDamageToHp final) =>
        checked((short)Math.Max(0, current.Life - final.Damage));

    private sealed record ResolvedDamagePublication(PlayerStateSnapshot Committed, DamageSource Source,
        ProjectileTypeId ProjectileType, int Damage, int HitDirection, bool Moved, bool BuffsChanged, bool Died,
        BuffTypeId[] BuffTypes);

    private void PublishResolvedDamage(ResolvedDamagePublication publication, Func<bool>? additionalGuard = null)
    {
        var committed = publication.Committed;
        var target = committed.Player;
        bool Current() => states.TryGet(target, out var current) && current == committed &&
            (additionalGuard?.Invoke() ?? true);
        if (!Current()) return;
        if (publication.Moved) events?.ServerPlayerMoved(in committed);
        if (!Current()) return;
        if (publication.BuffsChanged) events?.ServerPlayerBuffTypesUpdated(target, publication.BuffTypes);
        if (!Current()) return;
        var vitals = new ServerPlayerVitalsState(committed.Life, committed.MaxLife, committed.Mana, committed.MaxMana);
        events?.ServerPlayerVitalsUpdated(target, in vitals);
        if (publication.Died && Current())
            events?.ServerPlayerDied(target, publication.Source, publication.ProjectileType,
                publication.Damage, publication.HitDirection);
    }
}
