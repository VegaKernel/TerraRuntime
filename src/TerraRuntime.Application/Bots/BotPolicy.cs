using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Application.Bots;

internal static class BotPolicy
{
    internal static RuntimeBotActionLimits AttackActionLimits => new(180, 0);
    internal static RuntimeBotActionLimits RecoveryActionLimits => new(120, 0);
    internal static RuntimeBotActionLimits CollectionActionLimits => new(1_800, 300);
    internal static RuntimeBotActionLimits MiningActionLimits => new(1_800, 300);
    internal const float ReturnArrivalDistancePixels = 96f;
    internal const float GuardRadiusPixels = 640f;
    internal const float GuardRadiusSquared = GuardRadiusPixels * GuardRadiusPixels;
    internal const float StuckTeleportMinimumDistancePixels = 320f;
    internal const float StuckTeleportMinimumDistanceSquared = StuckTeleportMinimumDistancePixels * StuckTeleportMinimumDistancePixels;
    internal const float HardTeleportDistancePixels = 1_280f;
    internal const float HardTeleportDistanceSquared = HardTeleportDistancePixels * HardTeleportDistancePixels;
    internal const float ProgressDistancePixels = 8f;
    internal const long StuckTicks = 180;
    internal const long TeleportCooldownTicks = 120;
    internal const long TelemetryPeriodTicks = 6;
    internal const int MaximumNpcSlots = 256;
    internal const short StarterAmmoStack = 999;
    internal const byte MeleeWeaponSlot = 0;
    internal const byte BowWeaponSlot = 1;
    internal const byte GunWeaponSlot = 2;
    internal const byte ControlUseItemFlag = 1 << 5;
    // Tactical switch thresholds belong to bot policy. Weapon timing, launch velocity, projectile motion,
    // and damage below continue to come from the verified 1.4.5.8 catalogs.
    internal const float ConservativeMeleeCenterDistancePixels = 64f;
    internal const float AutomaticGunDistancePixels = 256f;
    internal const float AutomaticGunTargetSpeedPixelsPerTick = 3f;
    internal const int MaximumPredictiveAimTicks = 120;
    internal const float MinimumPlayerEscortOffsetPixels = 64f;
    internal const float EscortWanderRadiusPixels = 12f;
    internal const long EscortWanderPeriodTicks = 360;
    internal const float FlightAscentThresholdPixels = 32f;
    internal const float ObstacleClimbTargetPixels = 96f;
    internal const long FlightDecisionHoldTicks = 45;
    internal const long GuardTargetLockTicks = 90;
    internal const float GuardMeleeApproachDistancePixels = 46f;
    internal const float GuardMeleeReleaseDistancePixels = 68f;
    internal const float GuardBowMinimumDistancePixels = 150f;
    internal const float GuardBowMaximumDistancePixels = 280f;
    internal const float GuardGunMinimumDistancePixels = 220f;
    internal const float GuardGunMaximumDistancePixels = 380f;
    internal const float GuardRepositionPixels = 48f;
    internal const long GuardRepositionPeriodTicks = 150;
    internal const short MaximumVanillaPermanentLife = 500;
    internal const short MaximumVanillaPermanentMana = 200;
    internal const short StarterPotionStack = 30;
    internal const byte MirrorSlot = 5;
    // Item 50 SetDefaults: useTime/useAnimation=90. ItemCheck recalls at useTime/2.
    internal const long MirrorUseTicks = 90;

    internal static void UpdateProgress(BotState bot, float distanceSquared, long tick)
    {
        float distance = MathF.Sqrt(Math.Max(0f, distanceSquared));
        if (!float.IsFinite(bot.LastDistance) || bot.LastDistance - distance >= ProgressDistancePixels ||
            distanceSquared < StuckTeleportMinimumDistanceSquared)
        {
            bot.LastDistance = distance;
            bot.LastProgressTick = tick;
            bot.IsStuck = false;
        }
        else if (distance < bot.LastDistance)
        {
            bot.LastDistance = distance;
        }
    }

    internal static bool ShouldTeleport(BotState bot, float distanceSquared, long tick)
    {
        bool hardDistance = distanceSquared >= HardTeleportDistanceSquared;
        bool stalledFarAway = distanceSquared >= StuckTeleportMinimumDistanceSquared && tick - bot.LastProgressTick >= StuckTicks;
        bot.IsStuck = stalledFarAway;
        return tick >= bot.TeleportCooldownUntil && (hardDistance || stalledFarAway);
    }

    internal static void CommitTeleport(BotState bot, long tick)
    {
        bot.TeleportCount++;
        bot.TeleportCooldownUntil = tick + TeleportCooldownTicks;
        bot.LastProgressTick = tick;
        bot.LastDistance = 0f;
        bot.IsStuck = false;
    }

    internal static RuntimeBotAttackKind ResolveAttackKind(
        RuntimeBotWeaponPolicy policy,
        in PlayerStateSnapshot self,
        in BotGuardTarget target)
    {
        if (policy == RuntimeBotWeaponPolicy.Melee)
            return RuntimeBotAttackKind.Melee;
        if (policy == RuntimeBotWeaponPolicy.Gun)
            return RuntimeBotAttackKind.Gun;
        if (policy == RuntimeBotWeaponPolicy.Bow)
            return RuntimeBotAttackKind.Bow;

        float sourceCenterX = self.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float sourceCenterY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float distanceSquared = DistanceSquared(sourceCenterX, sourceCenterY, target.CenterX, target.CenterY);
        if (target.Npc.IsAssigned &&
            distanceSquared <= ConservativeMeleeCenterDistancePixels * ConservativeMeleeCenterDistancePixels)
        {
            return RuntimeBotAttackKind.Melee;
        }

        float targetSpeedSquared = target.VelocityX * target.VelocityX + target.VelocityY * target.VelocityY;
        return distanceSquared >= AutomaticGunDistancePixels * AutomaticGunDistancePixels ||
               targetSpeedSquared >= AutomaticGunTargetSpeedPixelsPerTick * AutomaticGunTargetSpeedPixelsPerTick
            ? RuntimeBotAttackKind.Gun
            : RuntimeBotAttackKind.Bow;
    }

    internal static byte ResolveWeaponSlot(RuntimeBotWeaponPolicy policy, RuntimeBotAttackKind attack)
    {
        // Explicit policies place their only weapon in slot 0. Automatic owns the three visible hotbar slots and
        // therefore publishes the actual selected slot as it switches between melee/bow/gun.
        if (policy != RuntimeBotWeaponPolicy.Automatic)
            return MeleeWeaponSlot;

        return attack switch
        {
            RuntimeBotAttackKind.Melee => MeleeWeaponSlot,
            RuntimeBotAttackKind.Bow => BowWeaponSlot,
            RuntimeBotAttackKind.Gun => GunWeaponSlot,
            _ => MeleeWeaponSlot
        };
    }

    internal static bool IsBuffActive(BotState bot, BuffTypeId buff, long tick) =>
        bot.ActiveBuffs.TryGetValue(buff, out long until) && until > tick;

    internal static void ExpireBuffs(BotState bot, long tick)
    {
        if (bot.ActiveBuffs.Count == 0)
            return;
        foreach (BuffTypeId buff in bot.ActiveBuffs.Where(pair => pair.Value <= tick).Select(pair => pair.Key).ToArray())
            bot.ActiveBuffs.Remove(buff);
    }

    internal static bool IsBuffUsefulForWeapon(RuntimeBotWeaponPolicy policy, BuffTypeId buff)
    {
        if (buff == VanillaBuffIds.Wrath)
            return policy is RuntimeBotWeaponPolicy.Automatic or RuntimeBotWeaponPolicy.Melee or RuntimeBotWeaponPolicy.Bow or RuntimeBotWeaponPolicy.Gun;
        return buff == VanillaBuffIds.Archery && policy is RuntimeBotWeaponPolicy.Automatic or RuntimeBotWeaponPolicy.Bow;
    }

    internal static bool IsRequiredAmmo(BotState bot, ItemTypeId itemType)
    {
        RuntimeBotWeaponPolicy policy = bot.Configuration.WeaponPolicy;
        if (policy == RuntimeBotWeaponPolicy.Automatic)
            return itemType == bot.Loadout.ArrowAmmo || itemType == bot.Loadout.BulletAmmo;
        return TryResolveRangedLoadout(bot, policy, out _, out ItemTypeId requiredAmmo, out _, out _) &&
               itemType == requiredAmmo;
    }

    internal static bool TryResolveRangedLoadout(
        BotState bot,
        RuntimeBotWeaponPolicy policy,
        out ItemTypeId weaponItem,
        out ItemTypeId ammoItem,
        out VanillaProjectileWeaponCombatDefinition weapon,
        out VanillaProjectileAmmoCombatDefinition ammo)
    {
        weaponItem = policy == RuntimeBotWeaponPolicy.Gun ? bot.Loadout.GunWeapon : bot.Loadout.BowWeapon;
        ammoItem = policy == RuntimeBotWeaponPolicy.Gun ? bot.Loadout.BulletAmmo : bot.Loadout.ArrowAmmo;
        if (policy == RuntimeBotWeaponPolicy.Melee || !Enum.IsDefined(policy) ||
            !VanillaProjectileWeaponCombatCatalog.TryGetWeapon(weaponItem, out weapon))
        {
            weapon = default;
            ammo = default;
            return false;
        }

        return weapon.AmmoFamily switch
        {
            VanillaProjectileAmmoFamily.Arrow => VanillaProjectileWeaponCombatCatalog.TryGetArrowAmmo(ammoItem, out ammo),
            VanillaProjectileAmmoFamily.Bullet => VanillaProjectileWeaponCombatCatalog.TryGetBulletAmmo(ammoItem, out ammo),
            _ => FailAmmo(out ammo)
        };
    }

    internal static bool TryResolveRangedLoadout(
        BotState bot,
        RuntimeBotAttackKind attack,
        out ItemTypeId weaponItem,
        out ItemTypeId ammoItem,
        out VanillaProjectileWeaponCombatDefinition weapon,
        out VanillaProjectileAmmoCombatDefinition ammo) =>
        TryResolveRangedLoadout(
            bot,
            attack switch
            {
                RuntimeBotAttackKind.Bow => RuntimeBotWeaponPolicy.Bow,
                RuntimeBotAttackKind.Gun => RuntimeBotWeaponPolicy.Gun,
                _ => RuntimeBotWeaponPolicy.Melee
            },
            out weaponItem,
            out ammoItem,
            out weapon,
            out ammo);

    internal static bool FailAmmo(out VanillaProjectileAmmoCombatDefinition ammo)
    {
        ammo = default;
        return false;
    }

    internal static float DistanceSquared(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        return dx * dx + dy * dy;
    }

    internal static bool RectanglesIntersect(
        float leftA, float topA, float widthA, float heightA,
        float leftB, float topB, float widthB, float heightB) =>
        leftA < leftB + widthB && leftA + widthA > leftB &&
        topA < topB + heightB && topA + heightA > topB;

}
