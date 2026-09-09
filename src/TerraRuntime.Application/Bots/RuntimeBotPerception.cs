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

/// <summary>Bounded detached perception plus the retained target-lock selection policy; no world mutation.</summary>
internal sealed class RuntimeBotPerception(
    BotState bot, ServerPlayerAuthority serverPlayers, PlayerAuthority players,
    RuntimePlayerSnapshotLookup playerSnapshots, NpcAuthority npcs, RuntimeBotInventory inventory,
    RuntimeBotNavigation navigation, IEnumerable<BotState> bots, WorldRuntimeIdentity world, WorldTileStore worldTiles,
    RuntimeBotMining mining)
{
    private readonly NpcSnapshot[] npcBuffer = new NpcSnapshot[MaximumNpcSlots];

    public RuntimeBotObservationSnapshot Capture(long tick, RuntimeBotActionExecutor executor, bool forDecision = true)
    {
        _ = serverPlayers.TryGet(bot.Player, out var self);
        PlayerStateSnapshot? target = null;
        if (bot.Configuration.Mode != RuntimeBotMode.Idle && bot.Configuration.Target.IsAssigned &&
            playerSnapshots.TryGetPlayer(bot.Configuration.Target.Player, out var candidate) &&
            candidate.Player == bot.Configuration.Target.Player && candidate.HasHealth && candidate.Life > 0 && !candidate.IsDead)
            target = candidate;
        BotGuardTarget? guard = null;
        bool recovery = forDecision && bot.Configuration.Mode != RuntimeBotMode.Mining && !self.IsDead && target is PlayerStateSnapshot recoveryTarget &&
            navigation.ObserveRecovery(self, recoveryTarget, tick);
        bot.PvpEnabled = target?.Hostile ?? false;
        if (forDecision && !self.IsDead && !recovery && bot.Configuration.Mode == RuntimeBotMode.Guard &&
            target is PlayerStateSnapshot protectedPlayer &&
            TryFindGuardTarget(bot, self, protectedPlayer, tick, out var selected)) guard = selected;
        return new(bot.Id, world, checked(++bot.ObservationRevision), bot.GoalGeneration, tick,
            self, bot.Configuration, target, guard, inventory.FindUsefulItem(self), recovery,
            executor.Current, executor.RecentResult)
        {
            Inventory = inventory.CaptureSummary(),
            LastPickedItem = bot.LastPickedItem,
            MiningTarget = forDecision ? mining.Observe(self, tick) : null,
            Underground = worldTiles.WorldSurfaceTiles is double surface ? self.PositionY > surface * 16d : null
        };
    }
    private bool TryFindGuardTarget(
        BotState bot,
        in PlayerStateSnapshot self,
        in PlayerStateSnapshot protectedPlayer,
        long tick,
        out BotGuardTarget target)
    {
        if (tick < bot.GuardTargetLockUntilTick &&
            TryResolveLockedGuardTarget(bot, in protectedPlayer, out target))
        {
            return true;
        }

        target = default;
        float protectedCenterX = protectedPlayer.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float protectedCenterY = protectedPlayer.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float bestDistanceSquared = float.PositiveInfinity;

        int npcCount = npcs.CopyActive(npcBuffer);
        for (int i = 0; i < npcCount; i++)
        {
            NpcSnapshot npc = npcBuffer[i];
            if (!VanillaNpcChaseability1458.CanBeChasedBy(in npc) || npc.Simulation.Life <= 0 ||
                !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition) ||
                definition.Role == NpcArchetypeRole.Town || definition.Damage <= 0 ||
                !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox))
            {
                continue;
            }

            float centerX = npc.PositionX + hitbox.Width * 0.5f;
            float centerY = npc.PositionY + hitbox.Height * 0.5f;
            float distanceSquared = DistanceSquared(protectedCenterX, protectedCenterY, centerX, centerY);
            float score = ScoreGuardTarget(bot, npc.Handle, default, distanceSquared);
            if (distanceSquared > GuardRadiusSquared || score >= bestDistanceSquared)
            {
                continue;
            }
            bestDistanceSquared = score;
            target = new BotGuardTarget(
                npc.Handle,
                default,
                centerX,
                centerY,
                npc.VelocityX,
                npc.VelocityY,
                hitbox.Width,
                hitbox.Height);
        }

        // PvP is admitted only when both the protected target and the opponent have vanilla hostile enabled.
        if (bot.PvpEnabled)
        {
            foreach (RuntimePlayerMember candidate in players.Members)
            {
                PlayerHandle candidateHandle = candidate.Connection.Player;
                if (candidateHandle == protectedPlayer.Player || candidateHandle == bot.Player ||
                    !candidate.Hostile || candidate.IsDead || !candidate.HasHealth || candidate.Life <= 0)
                {
                    continue;
                }

                float centerX = candidate.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
                float centerY = candidate.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
                float distanceSquared = DistanceSquared(protectedCenterX, protectedCenterY, centerX, centerY);
                float score = ScoreGuardTarget(bot, default, candidateHandle, distanceSquared);
                if (distanceSquared > GuardRadiusSquared || score >= bestDistanceSquared)
                {
                    continue;
                }
                bestDistanceSquared = score;
                target = new BotGuardTarget(
                    default,
                    candidateHandle,
                    centerX,
                    centerY,
                    candidate.VelocityX,
                    candidate.VelocityY,
                    PlayerAuthority.VanillaBasePlayerWidth,
                    PlayerAuthority.VanillaBasePlayerHeight);
            }
        }

        if (!float.IsFinite(bestDistanceSquared))
        {
            bot.LockedGuardNpc = default;
            bot.LockedGuardPlayer = default;
            bot.GuardTargetLockUntilTick = 0;
            return false;
        }

        bot.LockedGuardNpc = target.Npc;
        bot.LockedGuardPlayer = target.Player;
        bot.GuardTargetLockUntilTick = tick + GuardTargetLockTicks;
        return true;
    }

    private float ScoreGuardTarget(BotState bot, NpcHandle npc, PlayerHandle player, float distanceSquared)
    {
        // Soft assignment, not exclusive ownership: one boss can still be attacked by the whole squad.
        // Immediate threats within melee reach of the protected player override spreading out.
        if (distanceSquared <= 80f * 80f) return distanceSquared;
        int assigned = 0;
        foreach (BotState other in bots)
            if (other != bot && other.Configuration.Mode == RuntimeBotMode.Guard &&
                other.Configuration.Target.Player == bot.Configuration.Target.Player &&
                serverPlayers.TryGet(other.Player, out var ally) && !ally.IsDead &&
                ((npc.IsAssigned && other.LockedGuardNpc == npc) ||
                 (player.IsAssigned && other.LockedGuardPlayer == player))) assigned++;
        return distanceSquared + assigned * 240f * 240f;
    }

    private bool TryResolveLockedGuardTarget(
        BotState bot,
        in PlayerStateSnapshot protectedPlayer,
        out BotGuardTarget target)
    {
        target = default;
        float protectedCenterX = protectedPlayer.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float protectedCenterY = protectedPlayer.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;

        if (bot.LockedGuardNpc.IsAssigned && npcs.TryCapture(bot.LockedGuardNpc, out NpcSnapshot npc) &&
            VanillaNpcChaseability1458.CanBeChasedBy(in npc) && npc.Simulation.Life > 0 &&
            VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition) &&
            definition.Role != NpcArchetypeRole.Town && definition.Damage > 0 &&
            definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            float centerX = npc.PositionX + hitbox.Width * 0.5f;
            float centerY = npc.PositionY + hitbox.Height * 0.5f;
            if (DistanceSquared(protectedCenterX, protectedCenterY, centerX, centerY) <= GuardRadiusSquared)
            {
                target = new BotGuardTarget(
                    npc.Handle,
                    default,
                    centerX,
                    centerY,
                    npc.VelocityX,
                    npc.VelocityY,
                    hitbox.Width,
                    hitbox.Height);
                return true;
            }
        }

        if (bot.PvpEnabled && bot.LockedGuardPlayer.IsAssigned)
        {
            foreach (RuntimePlayerMember candidate in players.Members)
            {
                if (candidate.Connection.Player != bot.LockedGuardPlayer || !candidate.Hostile || candidate.IsDead ||
                    !candidate.HasHealth || candidate.Life <= 0)
                {
                    continue;
                }
                float centerX = candidate.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
                float centerY = candidate.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
                if (DistanceSquared(protectedCenterX, protectedCenterY, centerX, centerY) > GuardRadiusSquared)
                    return false;
                target = new BotGuardTarget(
                    default,
                    candidate.Connection.Player,
                    centerX,
                    centerY,
                    candidate.VelocityX,
                    candidate.VelocityY,
                    PlayerAuthority.VanillaBasePlayerWidth,
                    PlayerAuthority.VanillaBasePlayerHeight);
                return true;
            }
        }

        return false;
    }

}
