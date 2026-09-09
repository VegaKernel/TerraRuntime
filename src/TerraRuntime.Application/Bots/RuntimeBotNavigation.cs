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

internal sealed class RuntimeBotNavigation(
    BotState bot, ServerPlayerAuthority serverPlayers, WorldTileStore worldTiles,
    RuntimeBotCombat combat, IEnumerable<BotState> bots, RuntimeBotResourceLeases leases,
    WorldRuntimeIdentity world) : IRuntimeBotNavigation
{
    public bool ObserveRecovery(in PlayerStateSnapshot self, in PlayerStateSnapshot target, long tick)
    {
        if (bot.MirrorStartedAtTick >= 0)
        {
            if (tick - bot.MirrorStartedAtTick < MirrorUseTicks) return true;
            return false;
        }
        float distance = DistanceSquared(self.PositionX, self.PositionY, target.PositionX, target.PositionY);
        UpdateProgress(bot, distance, tick);
        return ShouldTeleport(bot, distance, tick) &&
            serverPlayers.TryGetItem(bot.ServerPlayerId, MirrorSlot, out var item) &&
            item.ItemType == VanillaItemIds.MagicMirror && item.Stack == 1;
    }

    public void Stop()
    {
        if (bot.OwnsCurrentActor(serverPlayers))
            _ = serverPlayers.SetMovementIntent(bot.ServerPlayerId, ServerPlayerMovementIntent.Stop());
    }

    public void FinishRecovery(long tick)
    {
        if (bot.MirrorStartedAtTick >= 0 && tick - bot.MirrorStartedAtTick >= MirrorUseTicks) CancelRecovery();
    }

    public void CancelRecovery()
    {
        if (bot.MirrorStartedAtTick >= 0 && bot.OwnsCurrentActor(serverPlayers))
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, MeleeWeaponSlot, useItem: false);
        bot.MirrorStartedAtTick = -1;
    }

    public RuntimeBotActionResult Recover(in RuntimeBotObservationSnapshot observation)
    {
        if (!RuntimeBotObservationScope.IsCurrent(bot, world, observation))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.StaleDecision);
        if (observation.TargetPlayer is not PlayerStateSnapshot target)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.TargetUnavailable);
        bool active = UpdatePlayerStuckRecovery(bot, observation.Self, target, observation.Tick);
        if (active) Stop();
        return active ? RuntimeBotActionResult.Pending(observation.Tick - bot.MirrorStartedAtTick) : RuntimeBotActionResult.Success;
    }

    public RuntimeBotActionResult Follow(in RuntimeBotObservationSnapshot observation)
    {
        if (!RuntimeBotObservationScope.IsCurrent(bot, world, observation))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.StaleDecision);
        if (observation.TargetPlayer is not PlayerStateSnapshot target)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.TargetUnavailable);
        ResolvePlayerEscortDestination(bot, observation.Self, target, observation.Tick, out float x, out float y);
        if (!BotTraversal.ClearLeg(worldTiles, x, y, x, y))
        {
            x = target.PositionX + 10f;
            y = target.PositionY + 21f;
        }
        Move(observation, x, y, 20f);
        return RuntimeBotActionResult.Pending();
    }

    public RuntimeBotActionResult Engage(in RuntimeBotObservationSnapshot observation)
    {
        if (!RuntimeBotObservationScope.IsCurrent(bot, world, observation))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.StaleDecision);
        if (observation.TargetPlayer is not PlayerStateSnapshot target || observation.GuardTarget is not BotGuardTarget enemy)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.TargetUnavailable);
        var attack = ResolveAttackKind(bot.Configuration.WeaponPolicy, observation.Self, enemy);
        ResolveGuardMovementDestination(bot, observation.Self, target, enemy, attack, observation.Tick,
            out float x, out float y, out float stop);
        Move(observation, x, y, stop);
        return RuntimeBotActionResult.Pending();
    }

    public RuntimeBotActionResult Return(in RuntimeBotObservationSnapshot observation)
    {
        if (!RuntimeBotObservationScope.IsCurrent(bot, world, observation))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.StaleDecision);
        if (observation.TargetPlayer is not PlayerStateSnapshot target)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.TargetUnavailable);
        float distance = DistanceSquared(observation.Self.PositionX, observation.Self.PositionY, target.PositionX, target.PositionY);
        if (distance <= ReturnArrivalDistancePixels * ReturnArrivalDistancePixels) { Stop(); return RuntimeBotActionResult.Success; }
        _ = Follow(observation);
        return RuntimeBotActionResult.Pending(-MathF.Sqrt(distance));
    }

    public RuntimeBotActionResult Collect(in RuntimeBotObservationSnapshot observation)
    {
        if (!RuntimeBotObservationScope.IsCurrent(bot, world, observation))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.StaleDecision);
        if (observation.UsefulItem is not WorldItemSnapshot item)
        {
            Stop();
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.ItemUnavailable);
        }
        var owner = new RuntimeBotLeaseOwner(bot.Id, bot.Player, world);
        if (!leases.TryAcquire(owner, RuntimeBotResourceKey.ForItem(world, item.Handle), observation.Tick))
        {
            Stop();
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.PermissionDenied);
        }
        if (!item.TryGetItemType(out var itemType) || !VanillaBotItemDefinitionCatalog1458.TryGet(itemType, out var definition))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.UnsupportedAction);
        Move(observation, item.PositionX + definition.Width * .5f, item.PositionY + definition.Height * .5f, 0f);
        return RuntimeBotActionResult.Pending(-MathF.Sqrt(DistanceSquared(
            observation.Self.PositionX, observation.Self.PositionY, item.PositionX, item.PositionY)));
    }

    internal void Move(in RuntimeBotObservationSnapshot observation, float x, float y, float stop)
    {
        x = ClampWorldCenterX(x, PlayerAuthority.VanillaBasePlayerWidth * .5f);
        y = ClampWorldCenterY(y, PlayerAuthority.VanillaBasePlayerHeight * .5f);
        if (bot.Configuration.FlightEnabled) ResolveTraversal(bot, observation.Self, observation.Tick, ref x, ref y);
        _ = serverPlayers.SetMovementIntent(bot.ServerPlayerId, ServerPlayerMovementIntent.MoveTo(
            x, y, ServerPlayerMovementOptions.Default with
            {
                StopDistance = stop, AutoJumpObstacles = true, FlightEnabled = bot.Configuration.FlightEnabled
            }));
    }

    private void ResolveTraversal(BotState bot, in PlayerStateSnapshot self, long tick, ref float x, ref float y)
    {
        float selfX = self.PositionX + 10f;
        float selfY = self.PositionY + 21f;
        if (BotTraversal.ClearLeg(worldTiles, selfX, selfY, x, y))
        {
            bot.TraversalUntilTick = 0;
            return;
        }
        if (tick < bot.TraversalUntilTick &&
            DistanceSquared(selfX, selfY, bot.TraversalX, bot.TraversalY) > 24f * 24f &&
            BotTraversal.ClearLeg(worldTiles, selfX, selfY, bot.TraversalX, bot.TraversalY))
        {
            x = bot.TraversalX;
            y = bot.TraversalY;
            return;
        }
        if (tick < bot.NextTraversalSearchTick)
            return;
        bot.NextTraversalSearchTick = tick + 15;
        if (BotTraversal.TryDetour(worldTiles, selfX, selfY, x, y, out float nextX, out float nextY))
        {
            bot.TraversalX = x = nextX;
            bot.TraversalY = y = nextY;
            bot.TraversalUntilTick = tick + 90;
        }
    }

    private bool UpdatePlayerStuckRecovery(
        BotState bot,
        in PlayerStateSnapshot self,
        in PlayerStateSnapshot target,
        long tick)
    {
        float distanceSquared = DistanceSquared(self.PositionX, self.PositionY, target.PositionX, target.PositionY);
        UpdateProgress(bot, distanceSquared, tick);
        if (bot.MirrorStartedAtTick < 0)
        {
            if (!ShouldTeleport(bot, distanceSquared, tick) ||
                !serverPlayers.TryGetItem(bot.ServerPlayerId, MirrorSlot, out ServerPlayerItemState mirror) ||
                mirror.ItemType != VanillaItemIds.MagicMirror || mirror.Stack != 1 ||
                !serverPlayers.SetHeldItem(bot.ServerPlayerId, MirrorSlot, useItem: true))
                return false;
            bot.MirrorStartedAtTick = tick;
            bot.MirrorTeleported = false;
            bot.UseItemUntilTick = tick + MirrorUseTicks;
        }

        long elapsed = tick - bot.MirrorStartedAtTick;
        if (elapsed >= MirrorUseTicks)
        {
            bot.MirrorStartedAtTick = -1;
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, MeleeWeaponSlot, useItem: false);
            return false;
        }
        if (elapsed < MirrorUseTicks / 2 || bot.MirrorTeleported)
            return true;

        ResolveEscortDestination(bot, in target, tick, allowVerticalWander: bot.Configuration.FlightEnabled,
            out float destinationX, out float destinationY);
        destinationX = ClampWorldCenterX(destinationX, PlayerAuthority.VanillaBasePlayerWidth * 0.5f);
        destinationY = ClampWorldCenterY(destinationY, PlayerAuthority.VanillaBasePlayerHeight * 0.5f);
        if (BotTraversal.TryRecallLanding(worldTiles, destinationX, destinationY, out short floorX, out short floorY) &&
            serverPlayers.TryTeleportWithRecallPresentation(
                bot.ServerPlayerId,
                floorX,
                floorY))
        {
            CommitTeleport(bot, tick);
            bot.MirrorTeleported = true;
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, MirrorSlot, useItem: false);
        }
        return true;
    }

    private void ResolvePlayerEscortDestination(
        BotState bot,
        in PlayerStateSnapshot self,
        in PlayerStateSnapshot target,
        long tick,
        out float centerX,
        out float centerY)
    {
        // Walking is the normal state. Wings are a traversal capability: they become useful only when the protected
        // player is materially above the bot or a solid obstacle blocks the direct route. Keeping the ordinary target
        // on the target player's ground level also prevents the old permanent low hover caused by a negative formation
        // offset on every Follow tick.
        ResolveEscortDestination(bot, in target, tick, allowVerticalWander: false, out centerX, out centerY);
        // Formation is bot policy, not a demand to occupy a wall beside the followed player. Contract the
        // offset toward that player's free position before choosing walk/flight, using vanilla body collision.
        float formationX = centerX;
        float targetX = target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        for (int attempt = 0; attempt < 4 && VanillaWorldSolidCollision.Intersects(
                 worldTiles, centerX - PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
                 centerY - PlayerAuthority.VanillaBasePlayerHeight * 0.5f,
                 (int)PlayerAuthority.VanillaBasePlayerWidth, (int)PlayerAuthority.VanillaBasePlayerHeight); attempt++)
            centerX = formationX + (targetX - formationX) * ((attempt + 1) / 4f);
        if (!bot.Configuration.FlightEnabled)
        {
            bot.FlightDecisionUntilTick = 0;
            return;
        }

        float selfCenterY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float targetCenterY = target.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        bool targetRequiresAscent = targetCenterY < selfCenterY - FlightAscentThresholdPixels;
        bool routeBlocked = !VanillaWorldCanHit.HasLineOfSight(
            worldTiles,
            self.PositionX,
            self.PositionY,
            (int)PlayerAuthority.VanillaBasePlayerWidth,
            (int)PlayerAuthority.VanillaBasePlayerHeight,
            centerX - PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
            target.PositionY,
            (int)PlayerAuthority.VanillaBasePlayerWidth,
            (int)PlayerAuthority.VanillaBasePlayerHeight);

        if (targetRequiresAscent || routeBlocked)
            bot.FlightDecisionUntilTick = tick + FlightDecisionHoldTicks;
        if (!targetRequiresAscent && !routeBlocked && tick >= bot.FlightDecisionUntilTick)
            return;

        float phase = ((tick + bot.Personality.WanderPhaseTicks) % EscortWanderPeriodTicks) /
            (float)EscortWanderPeriodTicks * MathF.Tau;
        float airborneFormationY = targetCenterY + bot.Personality.EscortOffsetY +
            MathF.Cos(phase * 0.75f) * EscortWanderRadiusPixels;
        centerY = routeBlocked
            ? MathF.Min(airborneFormationY, selfCenterY - ObstacleClimbTargetPixels)
            : airborneFormationY;
    }

    private static void ResolveEscortDestination(
        BotState bot,
        in PlayerStateSnapshot target,
        long tick,
        bool allowVerticalWander,
        out float centerX,
        out float centerY)
    {
        float phase = ((tick + bot.Personality.WanderPhaseTicks) % EscortWanderPeriodTicks) /
            (float)EscortWanderPeriodTicks * MathF.Tau;
        float wanderX = MathF.Sin(phase) * EscortWanderRadiusPixels;
        float wanderY = allowVerticalWander
            ? MathF.Cos(phase * 0.75f) * EscortWanderRadiusPixels
            : 0f;
        centerX = target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f +
            bot.Personality.EscortOffsetX + wanderX;
        centerY = target.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f +
            (allowVerticalWander ? bot.Personality.EscortOffsetY : 0f) + wanderY;
    }

    private void ResolveGuardMovementDestination(
        BotState bot,
        in PlayerStateSnapshot self,
        in PlayerStateSnapshot protectedPlayer,
        in BotGuardTarget target,
        RuntimeBotAttackKind attack,
        long tick,
        out float centerX,
        out float centerY,
        out float stopDistance)
    {
        float selfCenterX = self.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float selfCenterY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = target.CenterX - selfCenterX;
        float dy = target.CenterY - selfCenterY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float minimum = attack switch
        {
            RuntimeBotAttackKind.Bow => GuardBowMinimumDistancePixels,
            RuntimeBotAttackKind.Gun => GuardGunMinimumDistancePixels,
            _ => 0f
        };
        float maximum = attack switch
        {
            RuntimeBotAttackKind.Bow => GuardBowMaximumDistancePixels,
            RuntimeBotAttackKind.Gun => GuardGunMaximumDistancePixels,
            _ => GuardMeleeReleaseDistancePixels
        };

        if (attack == RuntimeBotAttackKind.Melee || distance > maximum)
        {
            centerX = target.CenterX;
            centerY = target.CenterY;
            stopDistance = attack == RuntimeBotAttackKind.Melee ? GuardMeleeApproachDistancePixels : maximum * 0.88f;
            if (attack == RuntimeBotAttackKind.Melee)
            {
                int members = 0, rank = 0;
                foreach (BotState ally in bots)
                    if (ally.Configuration.Mode == RuntimeBotMode.Guard &&
                        ally.Configuration.Target.Player == bot.Configuration.Target.Player &&
                        serverPlayers.TryGet(ally.Player, out var state) && !state.IsDead)
                    {
                        members++;
                        if (ally.Id < bot.Id) rank++;
                    }
                if (members > 1)
                {
                    float offset = rank % 2 == 0 ? -24f : 24f;
                    float candidateX = target.CenterX + offset;
                    if (BotTraversal.ClearLeg(worldTiles, candidateX, target.CenterY, candidateX, target.CenterY))
                    {
                        centerX = candidateX;
                        stopDistance = 20f;
                    }
                }
            }
            return;
        }

        if (distance < minimum && distance > 0.001f)
        {
            float inv = 1f / distance;
            centerX = target.CenterX - dx * inv * (minimum + GuardRepositionPixels);
            centerY = selfCenterY;
            stopDistance = 18f;
            return;
        }

        bool clearShot = VanillaWorldCanHit.HasLineOfSight(worldTiles,
            self.PositionX, self.PositionY, (int)PlayerAuthority.VanillaBasePlayerWidth, (int)PlayerAuthority.VanillaBasePlayerHeight,
            target.CenterX - target.Width * .5f, target.CenterY - target.Height * .5f, (int)target.Width, (int)target.Height);
        if (clearShot && !combat.SquadBlocksShot(bot, selfCenterX, selfCenterY, target.CenterX, target.CenterY))
        {
            centerX = selfCenterX;
            centerY = selfCenterY;
            stopDistance = 14f;
            bot.GuardRepositionUntilTick = 0;
            return;
        }

        // Reposition only to recover a blocked shot. A destination anchored to the current body every tick
        // never becomes reachable and made bots continuously strafe even with a clear, comfortable firing lane.
        if (tick >= bot.GuardRepositionUntilTick)
        {
            float side = (((tick + bot.Personality.WanderPhaseTicks) / GuardRepositionPeriodTicks) & 1L) == 0 ? -1f : 1f;
            bot.GuardRepositionX = selfCenterX + side * GuardRepositionPixels;
            bot.GuardRepositionY = selfCenterY - (bot.Configuration.FlightEnabled ? ObstacleClimbTargetPixels : 0f);
            bot.GuardRepositionUntilTick = tick + GuardRepositionPeriodTicks;
        }
        centerX = bot.GuardRepositionX;
        centerY = bot.GuardRepositionY;
        stopDistance = 14f;

        // Never wander away from the protected player just to look clever.
        float protectedCenterX = protectedPlayer.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        if (MathF.Abs(centerX - protectedCenterX) > GuardRadiusPixels)
            centerX = selfCenterX;
    }

    private float ClampWorldCenterX(float centerX, float halfWidth) =>
        Math.Clamp(centerX, halfWidth, worldTiles.Dimensions.WidthTiles * 16f - halfWidth);

    private float ClampWorldCenterY(float centerY, float halfHeight) =>
        Math.Clamp(centerY, halfHeight, worldTiles.Dimensions.HeightTiles * 16f - halfHeight);

}
