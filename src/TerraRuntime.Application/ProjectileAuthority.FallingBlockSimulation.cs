using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private ProjectileActorSimulationResult TrySimulateFallingBlockActor(ProjectileSnapshot initial)
    {
        if (!VanillaFallingBlock1458.TryGetTile(initial.Type, out _) ||
            initial.Type.Value == 31 && initial.Ai.Ai0 == 2f || !FallingBlocks.Contains(initial.Handle))
            return ProjectileActorSimulationResult.NotApplicable;
        if (initial.Spawner != byte.MaxValue || initial.Ai.Ai0 is not (0f or 1f) ||
            initial.Ai.Ai1 != 0f || initial.Ai.Ai2 != 0f ||
            toolActorWorld is null || worldTiles is null || stepper is not VanillaProjectileWorldStateStepper concrete ||
            !projectiles.TryGet(initial.Handle, out var current) || current != initial ||
            !projectiles.TryGetLifecycle(initial.Handle, out var life) || life.TimeLeft <= 0 || life.Reflected ||
            !VanillaFallingBlock1458.TryGetDedicatedAiRandomBound(initial.Type, out int dustBound))
            return ProjectileActorSimulationResult.Rejected;

        bool bounds = initial.PositionX <= 0f || initial.PositionY <= 0f ||
            initial.PositionX + 10f >= worldTiles.Dimensions.WidthTiles * 16f ||
            initial.PositionY + 10f >= worldTiles.Dimensions.HeightTiles * 16f;
        if (bounds)
        {
            var body = ToolUpdate(initial, initial.PositionX, initial.PositionY,
                initial.VelocityX, initial.VelocityY, initial.Ai);
            var ended = life with { TimeLeft = 0 };
            if (!projectiles.TryPrepareSimulation(in initial, out var outside) || outside is null ||
                !outside.TryStageMotion(in body, in ended) ||
                !outside.TryQueueFallingDespawn(new(initial.PositionX, initial.PositionY)) || !outside.IsCurrent ||
                !FallingBlocks.Contains(initial.Handle) || !outside.TryAdoptUnpublished(out _, out bool expired) || !expired)
                return ProjectileActorSimulationResult.Rejected;
            FallingBlocks.Forget(initial.Handle);
            if (replication is not null) replication.TryPublishFallingBlockActor(outside, false, true);
            else while (outside.TryPublishNext()) { }
            return ProjectileActorSimulationResult.Applied;
        }

        if (!toolActorWorld.TryBeginFallingActor(in initial, projectileRandom, out var world) || world is null)
            return ProjectileActorSimulationResult.Rejected;
        using (world)
        {
            var wind = concrete.CaptureGemStaffWind();
            var random = world.Random;
            // Dedicated Dust.NewDust returns before its own allocation RNG. The
            // AI010 caller test is nevertheless exactly one Main draw per update.
            _ = random.Next(dustBound);
            float x = initial.PositionX, y = initial.PositionY;
            float vx = initial.VelocityX, vy = Math.Min(10f, initial.VelocityY + .41f);
            bool tileCollide = initial.Ai.Ai0 != 0f;
            bool netUpdate = !tileCollide;
            var ai = initial.Ai with { Ai0 = 1f };
            var phaseLife = life with
            {
                OldVelocityX = initial.VelocityX, OldVelocityY = initial.VelocityY,
                LocalAi = life.LocalAi with { Ai1 = 0f }
            };
            var aiBody = ToolUpdate(initial, x, y, vx, vy, ai);
            if (!world.TrySetSourceActorView(in aiBody, in phaseLife, true) ||
                !world.TryStageAiTiles(tileCollide, in aiBody, in phaseLife, ref y) ||
                !concrete.TryCaptureFallingTerrain(world.Terrain, initial.Type, x, y,
                    initial.VelocityX, initial.VelocityY, vx, vy, tileCollide,
                    in life, in wind, out var terrain)) return ProjectileActorSimulationResult.Rejected;
            for (int draw = 0; draw < terrain.ShimmerColourDraws; draw++) random.Next(6);
            vx = terrain.VelocityX; vy = terrain.VelocityY;
            phaseLife = phaseLife with { Liquid = terrain.Liquid, SourceWetCount = terrain.WetCount };
            ProjectileKillOrigin? killOrigin = null;
            bool terminal = terrain.HasImpact;
            if (terrain.HasImpact)
            {
                var impact = ToolUpdate(initial, x, y, vx, vy, ai);
                if (!world.TrySetSourceActorView(in impact, in phaseLife, true) ||
                    !world.TryStageCuts(in impact, true, life.NpcProjectile))
                    return ProjectileActorSimulationResult.Rejected;
                // Generic impact advances once before Kill and once again in
                // the common inactive tail. Kill/packet29/landing use the first.
                x += vx; y += vy;
                killOrigin = new(x, y);
                var killing = ToolUpdate(initial, x, y, vx, vy, ai);
                var killingLife = phaseLife with { TimeLeft = 0 };
                var killed = initial with { PositionX = x, PositionY = y, VelocityX = vx, VelocityY = vy, Ai = ai };
                if (!world.TrySetSourceActorView(in killing, in killingLife, true) ||
                    !world.TryStageLanding(in killed)) return ProjectileActorSimulationResult.Rejected;
            }
            x += terrain.MovementX; y += terrain.MovementY;
            var after = ToolUpdate(initial, x, y, vx, vy, ai);
            if (!terminal)
            {
                // Source Damage/CutTiles still sees the positive pre-time-- body.
                if (!world.TrySetSourceActorView(in after, in phaseLife, true) ||
                    !world.TryStageCuts(in after, false, life.NpcProjectile))
                    return ProjectileActorSimulationResult.Rejected;
                terminal = life.TimeLeft == 1;
                if (terminal)
                {
                    killOrigin = new(x, y);
                    var killingLife = phaseLife with { TimeLeft = 0 };
                    var killed = initial with { PositionX = x, PositionY = y, VelocityX = vx, VelocityY = vy, Ai = ai };
                    if (!world.TrySetSourceActorView(in after, in killingLife, true) ||
                        !world.TryStageLanding(in killed)) return ProjectileActorSimulationResult.Rejected;
                }
            }
            var finalLife = phaseLife with { TimeLeft = terminal ? 0 : life.TimeLeft - 1 };
            if (!world.TrySeal() || !projectiles.TryPrepareSimulation(in initial, out var publication) || publication is null ||
                !publication.TryStageMotion(in after, in finalLife) ||
                !publication.TryStageSourceDirection(vx < 0f ? -1 : 1) ||
                !(terminal ? publication.TryQueueFallingDespawn(killOrigin!.Value) : publication.TryQueuePublication()) ||
                !world.TryBindSourceActor(publication) || !world.IsCurrent || !publication.IsCurrent ||
                !FallingBlocks.Contains(initial.Handle) || !concrete.IsGemStaffWindCurrent(in wind))
                return ProjectileActorSimulationResult.Rejected;
            if (!publication.TryAdoptUnpublished(out _, out bool expired) || expired != terminal ||
                !world.TryAdoptUnpublished())
                throw new InvalidOperationException("A prepared falling actor lost joint ownership.");
            if (terminal) FallingBlocks.Forget(initial.Handle);
            ExceptionDispatchInfo? first = null;
            Drain(false);
            try
            {
                if (replication is not null) replication.TryPublishFallingBlockActor(publication, !terminal && netUpdate);
                else while (publication.TryPublishNext()) { }
            }
            catch (Exception error) { first ??= ExceptionDispatchInfo.Capture(error); }
            Drain(true);
            first?.Throw();
            return ProjectileActorSimulationResult.Applied;

            void Drain(bool afterKill)
            {
                while (true)
                {
                    try { if (!world.TryPublishNext(afterKill)) break; }
                    catch (Exception error) { first ??= ExceptionDispatchInfo.Capture(error); }
                }
            }
        }
    }
}
