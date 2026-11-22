using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private const float GeyserDrift = .001f;
    private const int GeyserPhaseLimit = 60;

    private ProjectileActorSimulationResult TrySimulateGeyser(in ProjectileSnapshot initial)
    {
        if (stepper is not VanillaProjectileWorldStateStepper || worldTiles is null ||
            initial.Spawner != byte.MaxValue || initial.OriginalDamage != 0 || initial.KnockBack != 2f ||
            initial.Damage != VanillaWiringTrap1458.Damage(initial.Type) ||
            initial.VelocityX != 0 || MathF.Abs(initial.VelocityY) is not (8f or GeyserDrift) ||
            initial.Ai.Ai0 is not (0f or 1f) || initial.Ai.Ai1 < 0 || initial.Ai.Ai1 >= GeyserPhaseLimit)
            return ProjectileActorSimulationResult.Rejected;

        long tick = tickProvider();
        if (!projectiles.TryPrepareSimulation(in initial, out var actor) || actor is null ||
            !actor.CombatTrusted || actor.TrustedOwner.IsAssigned || actor.SourceNpc.IsAssigned ||
            actor.Lifecycle.NpcProjectile || actor.Lifecycle.Reflected || tickProvider() != tick || !actor.IsCurrent)
            return ProjectileActorSimulationResult.Rejected;

        // All source geometry and entropy reads follow the last external clock callback.
        var life = actor.Lifecycle;
        int height = life.GeyserHeight == 0 ? VanillaWiringTrap1458.GeyserInitialHeight : life.GeyserHeight;
        bool worldBounds = initial.PositionX <= 0 || initial.PositionY <= 0 ||
            initial.PositionX + VanillaWiringTrap1458.GeyserWidth >= worldTiles.Dimensions.WidthTiles * 16f ||
            initial.PositionY + height >= worldTiles.Dimensions.HeightTiles * 16f;
        var random = projectileRandom.Clone();
        var next = ResolveGeyserStep(worldTiles, in initial, ref life, height, worldBounds, random, out var origin);
        bool terminal = life.TimeLeft <= 0;
        if (!actor.TryStageMotion(in next, in life) ||
            !(terminal ? actor.TryQueueWiringTrapDespawn(origin) : actor.TryQueuePublication()) ||
            !actor.TryAdoptUnpublished(out var accepted, out _))
            return ProjectileActorSimulationResult.Rejected;

        projectileRandom.CopyStateFrom(random);
        ExceptionDispatchInfo? failure = null;
        if (!terminal)
        {
            try
            {
                toolActorWorld?.ApplyGeyserTileCuts(in accepted, life.GeyserHeight);
                bool current = projectiles.TryGet(accepted.Handle, out var afterCuts) && afterCuts == accepted &&
                    activeOrdinaryArrowCombat?.TickOwnedWiringTrapContact(in accepted) != false;
                if (current && projectiles.TryGet(accepted.Handle, out var afterNpc))
                    groundCombatPlayerCombat?.TickOwnedWiringTrapContact(in afterNpc);
            }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        }

        try
        {
            if (worldBounds && replication is not null)
                replication.TryPublishGeyserBounds(actor);
            else
                while (actor.TryPublishNext()) { }
        }
        catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        failure?.Throw();
        return ProjectileActorSimulationResult.Applied;
    }

    private static ProjectileStateUpdate ResolveGeyserStep(WorldTileStore tiles, in ProjectileSnapshot shot,
        ref ProjectileLifecycleState life, int height, bool worldBounds, VanillaUnifiedRandom1458 random,
        out ProjectileKillOrigin? origin)
    {
        float x = shot.PositionX, y = shot.PositionY, velocity = shot.VelocityY;
        int direction = Math.Sign(velocity);
        var ai = shot.Ai;
        var local = life.LocalAi;
        int remaining = life.TimeLeft;
        bool killed = worldBounds;
        if (!worldBounds)
        {
            if (ai.Ai0 == 0)
            {
                bool opens = !VanillaWorldSolidCollision.Intersects(tiles, x, y + (direction < 0 ? height - 48 : 0),
                    VanillaWiringTrap1458.GeyserWidth, 48) &&
                    !VanillaWorldCollision.TryGetWetContact(tiles, x, y + (direction < 0 ? height - 20 : 0),
                        VanillaWiringTrap1458.GeyserWidth, 20, out _);
                if (opens)
                {
                    ai = ai with { Ai0 = 1, Ai1 = 0 };
                    remaining = GeyserPhaseLimit;
                }
                ai = ai with { Ai1 = ai.Ai1 + 1 };
                killed = ai.Ai1 >= GeyserPhaseLimit;
                // AI_126 evaluates scale/fade-in offers on dedicated servers even though Dust.NewDust is disabled.
                for (int offer = 0; offer < 6; offer++) random.Next(5);
            }

            if (ai.Ai0 == 1)
            {
                velocity = direction * GeyserDrift;
                int span = 16;
                float bottom = y + height;
                while (span < VanillaWiringTrap1458.GeyserMaximumHeight &&
                    !VanillaWorldSolidCollision.Intersects(tiles, x, direction < 0 ? y + (height - span - 16) : y,
                        VanillaWiringTrap1458.GeyserWidth, span + 16))
                    span += 16;
                height = span;
                if (direction < 0) y = bottom - height;
                ai = ai with { Ai1 = ai.Ai1 + 1 };
                killed = ai.Ai1 >= GeyserPhaseLimit;
                bool first = local.Ai0 == 0;
                if (first) local = local with { Ai0 = 1 };
                ConsumeGeyserBeamOffers(random, direction < 0, first);
            }
        }

        origin = killed && !worldBounds ? new ProjectileKillOrigin(x, y) : null;
        if (!killed)
        {
            y += velocity;
            if (--remaining == 0) origin = new ProjectileKillOrigin(x, y);
        }
        life = life with
        {
            TimeLeft = killed ? 0 : remaining,
            GeyserHeight = (ushort)height,
            LocalAi = local,
            OldVelocityX = shot.VelocityX,
            OldVelocityY = shot.VelocityY
        };
        return new ProjectileStateUpdate(shot.Type, shot.Spawner, x, y, shot.VelocityX, velocity, ai,
            shot.BannerIdToRespondTo, shot.Damage, shot.KnockBack, shot.OriginalDamage);
    }

    private static void ConsumeGeyserBeamOffers(VanillaUnifiedRandom1458 random, bool upward, bool startup)
    {
        // AI_126 still evaluates its explicit dust arguments in dedicated mode. Nothing is rendered or allocated.
        int burst = (startup ? 60 : 0) + (upward ? 0 : 9);
        for (int sample = 0; sample < burst; sample++)
        {
            random.Next(3);
            random.NextDouble();
            if (upward) random.Next(4);
        }
        for (int sample = 0; sample < 6; sample++)
            if (random.Next(3) < 2) random.Next(6);
        for (int sample = 0; sample < 6; sample++)
        {
            if ((float)random.NextDouble() < .5f) continue;
            random.Next(3);
            random.NextDouble();
            if (upward) random.Next(4);
        }
    }
}
