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

using static TerraRuntime.Application.Bots.BotPolicy;

namespace TerraRuntime.Application.Bots;

internal sealed class RuntimeBotCombat(
    BotState bot, ServerPlayerAuthority serverPlayers, NpcAuthority npcs, ProjectileAuthority projectiles,
    WorldTileStore worldTiles, RuntimeBotInventory inventory, IEnumerable<BotState> bots, WorldRuntimeIdentity world) : IRuntimeBotCombat
{
    public RuntimeBotActionResult Attack(in RuntimeBotObservationSnapshot observation)
    {
        if (!RuntimeBotObservationScope.IsCurrent(bot, world, observation) || observation.GuardTarget is not BotGuardTarget target)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.TargetUnavailable);
        if (observation.Tick < bot.NextAttackTick) return RuntimeBotActionResult.Pending();
        long before = bot.NextAttackTick;
        TryGuardAttack(bot, observation.Self, target, observation.Tick);
        return bot.NextAttackTick != before ? RuntimeBotActionResult.Success : RuntimeBotActionResult.Pending();
    }

    private void TryGuardAttack(BotState bot, in PlayerStateSnapshot self, in BotGuardTarget target, long tick)
    {
        RuntimeBotAttackKind attack = ResolveAttackKind(bot.Configuration.WeaponPolicy, in self, in target);
        if (attack == RuntimeBotAttackKind.Melee)
        {
            TryGuardMeleeAttack(bot, in self, in target, tick);
            return;
        }

        if (TryGuardRangedAttack(bot, in self, in target, attack, tick))
            return;

        if (bot.Configuration.WeaponPolicy == RuntimeBotWeaponPolicy.Automatic)
        {
            RuntimeBotAttackKind fallback = attack == RuntimeBotAttackKind.Gun
                ? RuntimeBotAttackKind.Bow
                : RuntimeBotAttackKind.Gun;
            _ = TryGuardRangedAttack(bot, in self, in target, fallback, tick);
        }
    }

    private bool TryGuardRangedAttack(
        BotState bot,
        in PlayerStateSnapshot self,
        in BotGuardTarget target,
        RuntimeBotAttackKind attack,
        long tick)
    {
        if (!TryResolveRangedLoadout(bot, attack, out _, out ItemTypeId ammoItem,
                out VanillaProjectileWeaponCombatDefinition weapon, out VanillaProjectileAmmoCombatDefinition ammo) ||
            !RuntimeBotInventory.TryFindAmmoSlot(serverPlayers, bot.ServerPlayerId, ammoItem, out short ammoSlot, out ServerPlayerItemState ammoState) ||
            !VanillaProjectileWeaponCombatCatalog.TryResolveProjectileType(in weapon, in ammo, out ProjectileTypeId projectileType) ||
            !TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(
                projectileType,
                out VanillaProjectileDefinition projectileDefinition))
        {
            return false;
        }

        if (!inventory.TryBuildBotCombatSnapshot(bot, tick, out VanillaPlayerCombatSnapshot combat))
            return false;
        VanillaCombatPrefixModifiers prefix = VanillaCombatPrefixModifiers.Identity;
        VanillaLaunchSpeedEnvelope speedEnvelope = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(
            in weapon, in ammo, in prefix, in combat);
        int damage = VanillaProjectileWeaponCombatCatalog.ResolveDamage(in weapon, in ammo, in prefix, in combat);
        float knockBack = VanillaProjectileWeaponCombatCatalog.ResolveKnockBack(in weapon, in ammo, in prefix, in combat);
        if (!speedEnvelope.IsValid || damage <= 0 || damage > short.MaxValue || !float.IsFinite(knockBack))
            return false;

        float speed = speedEnvelope.CanonicalMagnitude;
        if (weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow &&
            IsBuffActive(bot, VanillaBuffIds.Archery, tick) && speed < 20f)
        {
            speed = Math.Min(20f, speed * 1.2f);
        }

        float originX = self.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float originY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        if (!TryResolveProjectileLaunch(
                originX,
                originY,
                projectileType,
                in projectileDefinition,
                speed,
                in target,
                out float velocityX,
                out float velocityY))
        {
            return false;
        }

        byte weaponSlot = ResolveWeaponSlot(bot.Configuration.WeaponPolicy, attack);
        if (!serverPlayers.SetHeldItem(bot.ServerPlayerId, weaponSlot, useItem: true))
            return false;

        // Consume first, rollback if projectile allocation fails. This keeps the world-writer path duplication-safe.
        var consumed = ammoState.Stack == 1
            ? new ServerPlayerItemState(ammoSlot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
            : ammoState with { Stack = checked((short)(ammoState.Stack - 1)) };
        if (!serverPlayers.SetItem(bot.ServerPlayerId, in consumed))
        {
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, weaponSlot, useItem: false);
            return false;
        }

        var projectile = new ProjectileStateUpdate(
            projectileType,
            bot.Player.Slot.Value,
            originX,
            originY,
            velocityX,
            velocityY,
            default,
            BannerIdToRespondTo: 0,
            Damage: checked((short)damage),
            KnockBack: knockBack,
            OriginalDamage: 0);
        if (!projectiles.TrySpawnTrustedServerPlayerProjectile(bot.Player, in projectile, out _))
        {
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in ammoState))
                throw new InvalidOperationException("Bot ammo rollback failed after rejected trusted projectile spawn.");
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, weaponSlot, useItem: false);
            return false;
        }

        bot.NextAttackTick = tick + Math.Max(1, weapon.UseTimeTicks);
        bot.UseItemUntilTick = tick + Math.Max(1, weapon.AnimationTicks);
        _ = serverPlayers.PresentItemUse(bot.ServerPlayerId, velocityX, velocityY, Math.Max(1, weapon.AnimationTicks));
        return true;
    }

    internal bool SquadBlocksShot(BotState bot, float x, float y, float targetX, float targetY)
    {
        float dx = targetX - x, dy = targetY - y;
        float lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1f) return false;
        foreach (BotState other in bots)
        {
            if (other == bot || other.Configuration.Mode != RuntimeBotMode.Guard ||
                other.Configuration.Target.Player != bot.Configuration.Target.Player ||
                !serverPlayers.TryGet(other.Player, out var ally) || ally.IsDead) continue;
            float ax = ally.PositionX + 10f, ay = ally.PositionY + 21f;
            // In a shared spawn position only the later member yields, preventing a mutual deadlock.
            if (DistanceSquared(x, y, ax, ay) < 32f * 32f)
            {
                if (other.Id < bot.Id) return true;
                continue;
            }
            float t = ((ax - x) * dx + (ay - y) * dy) / lengthSquared;
            if (t > 0f && t < 1f && DistanceSquared(ax, ay, x + dx * t, y + dy * t) < 26f * 26f)
                return true;
        }
        return false;
    }

    private void TryGuardMeleeAttack(
        BotState bot,
        in PlayerStateSnapshot self,
        in BotGuardTarget target,
        long tick)
    {
        if (!target.Npc.IsAssigned ||
            !VanillaItemCombatCatalog.TryGetDirectMelee(
                bot.Loadout.MeleeWeapon,
                out VanillaDirectMeleeCombatDefinition weapon))
        {
            return;
        }

        float sourceCenterX = self.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float sourceCenterY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = target.CenterX - sourceCenterX;
        float dy = target.CenterY - sourceCenterY;
        float distanceSquared = dx * dx + dy * dy;
        if (distanceSquared > ConservativeMeleeCenterDistancePixels * ConservativeMeleeCenterDistancePixels)
            return;

        if (!inventory.TryBuildBotCombatSnapshot(bot, tick, out VanillaPlayerCombatSnapshot combat))
            return;
        VanillaResolvedDirectMeleeUse resolved = VanillaDirectMeleeCombatMath.Resolve(
            in weapon,
            VanillaCombatPrefixModifiers.Identity,
            combat,
            Random.Shared.Next(-15, 16),
            Random.Shared.Next(1, 101),
            pvp: false);
        int hitDirection = dx < 0f ? -1 : 1;
        byte weaponSlot = ResolveWeaponSlot(bot.Configuration.WeaponPolicy, RuntimeBotAttackKind.Melee);
        if (!serverPlayers.SetHeldItem(bot.ServerPlayerId, weaponSlot, useItem: true) ||
            !npcs.TryStrikeBotPlayerMelee(
                bot.Player,
                target.Npc,
                resolved.Damage,
                resolved.ArmorPenetration,
                resolved.Critical,
                resolved.KnockBack,
                hitDirection))
        {
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, weaponSlot, useItem: false);
            return;
        }

        bot.NextAttackTick = tick + Math.Max(1, resolved.UseTimeTicks);
        bot.UseItemUntilTick = tick + Math.Max(1, resolved.AnimationTicks);
    }

    private bool TryResolveProjectileLaunch(
        float originX,
        float originY,
        ProjectileTypeId projectileType,
        in VanillaProjectileDefinition definition,
        float launchSpeed,
        in BotGuardTarget target,
        out float velocityX,
        out float velocityY)
    {
        velocityX = 0f;
        velocityY = 0f;
        if (definition.AiStyle != VanillaProjectileAiStyles.Arrow ||
            !(launchSpeed > 0f) || !float.IsFinite(launchSpeed))
        {
            return false;
        }

        int subupdates = VanillaProjectileUpdateFacts.GetSubupdatesPerWorldTick(projectileType);
        bool gravity = !VanillaProjectileBehaviorProfileCatalog.SkipsBasicArrowGravity(projectileType);
        float projectileCenterX = originX + definition.Width * 0.5f;
        float projectileCenterY = originY + definition.Height * 0.5f;
        for (int ticks = 1; ticks <= MaximumPredictiveAimTicks; ticks++)
        {
            int steps = checked(ticks * subupdates);
            float predictedTargetX = target.CenterX + target.VelocityX * ticks;
            float predictedTargetY = target.CenterY + target.VelocityY * ticks;
            int gravitySteps = gravity ? Math.Max(0, steps - 14) : 0;
            float gravityDisplacement = gravitySteps * (gravitySteps + 1) * 0.05f;
            float requiredX = (predictedTargetX - projectileCenterX) / steps;
            float requiredY = (predictedTargetY - projectileCenterY - gravityDisplacement) / steps;
            float requiredLength = MathF.Sqrt(requiredX * requiredX + requiredY * requiredY);
            if (!(requiredLength > 0.001f) || !float.IsFinite(requiredLength))
                continue;

            float candidateVelocityX = requiredX / requiredLength * launchSpeed;
            float candidateVelocityY = requiredY / requiredLength * launchSpeed;
            if (!TrajectoryReachesTarget(
                    originX,
                    originY,
                    candidateVelocityX,
                    candidateVelocityY,
                    subupdates,
                    ticks,
                    gravity,
                    in definition,
                    in target))
            {
                continue;
            }

            velocityX = candidateVelocityX;
            velocityY = candidateVelocityY;
            return true;
        }

        return false;
    }

    private bool TrajectoryReachesTarget(
        float originX,
        float originY,
        float initialVelocityX,
        float initialVelocityY,
        int subupdates,
        int maximumTicks,
        bool gravity,
        in VanillaProjectileDefinition definition,
        in BotGuardTarget target)
    {
        float positionX = originX;
        float positionY = originY;
        float velocityX = initialVelocityX;
        float velocityY = initialVelocityY;
        float ai0 = 0f;
        int maximumSteps = checked(subupdates * maximumTicks);
        for (int step = 0; step < maximumSteps; step++)
        {
            if (gravity)
                ai0 += 1f;
            if (ai0 >= 15f)
            {
                ai0 = 15f;
                velocityY = Math.Min(16f, velocityY + 0.1f);
            }

            if (!definition.IgnoreWater &&
                VanillaWorldCollision.GetLiquidContacts(
                    worldTiles,
                    positionX,
                    positionY,
                    definition.Width,
                    definition.Height).Wet)
            {
                return false;
            }

            VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
                worldTiles,
                positionX + definition.CollisionOffsetX,
                positionY + definition.CollisionOffsetY,
                velocityX,
                velocityY,
                definition.CollisionWidth,
                definition.CollisionHeight,
                fallThrough: true,
                fall2: true);
            if (collision.VelocityX != velocityX || collision.VelocityY != velocityY)
                return false;

            positionX += velocityX;
            positionY += velocityY;
            float elapsedTicks = (step + 1f) / subupdates;
            float targetLeft = target.CenterX + target.VelocityX * elapsedTicks - target.Width * 0.5f;
            float targetTop = target.CenterY + target.VelocityY * elapsedTicks - target.Height * 0.5f;
            if (RectanglesIntersect(
                    positionX,
                    positionY,
                    definition.Width,
                    definition.Height,
                    targetLeft,
                    targetTop,
                    target.Width,
                    target.Height))
            {
                return true;
            }
        }

        return false;
    }

}
