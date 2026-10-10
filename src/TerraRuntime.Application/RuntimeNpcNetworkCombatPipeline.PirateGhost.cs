using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    // AI122's outer Update owns motion/CheckActive and, at alpha255, a real 9999 death.
    // Source's negative post-fatal life has no runtime representation; the retained inactive wire does.
    internal bool TryCommitPirateGhostPhase(in NpcSnapshot before, in NpcStateUpdate motion,
        bool fadeStrike, ReadOnlySpan<VanillaNpcTargetCandidate> players, Func<bool> captureCurrentPlayers, Func<bool> retainedPhaseIsCurrent)
    {
        if (before.TypeIdentity != VanillaNpcIds.PirateGhost || before.Simulation.Life <= 0 ||
            !npcs.TryGet(before.Handle, out var live) || live != before ||
            !npcs.TryCaptureDeathMutationSerial(out ulong serial) || serial > ulong.MaxValue - 2 ||
            before.Revision.Value == ulong.MaxValue || !RuntimeNpcStore.IsValid(in motion) ||
            !VanillaPirateGhostNpcCatalog1458.Definition.TryResolveHitbox(motion.Simulation, out var body)) return false;
        var finished = motion with { Simulation = motion.Simulation with
        {
            SpriteDirection = motion.VelocityX > 0 ? 1 : motion.VelocityX < 0 ? -1 : motion.Simulation.SpriteDirection,
            JustHit = false
        } };
        if (fadeStrike)
        {
            var strike = new NpcDamageRequest(before.Handle, DamageSource.Environment, 9999, KnockBack: 0, HitDirection: 0);
            if (before.Simulation.Immortal == true || before.Simulation.DontTakeDamage ||
                !TerraRuntime.Core.Npcs.VanillaNpcDamageResolver.TryResolve(
                    before.Simulation.DefenseOverride ?? VanillaPirateGhostNpcCatalog1458.Definition.Defense,
                    in strike, out _, out var damageValue) || before.Simulation.Life > damageValue) return false;
            var dead = before with { PositionX = finished.PositionX, PositionY = finished.PositionY,
                VelocityX = finished.VelocityX, VelocityY = finished.VelocityY, Target = finished.Target,
                Ai = finished.Ai, Simulation = finished.Simulation with { Life = 0, JustHit = false } };
            if (!TryPrepareDeathPlan(in dead)) { CancelPendingDeathPlan(); return false; }
            if (!captureCurrentPlayers() || !IsCurrentDeathOwner(before.Handle, before.Revision) ||
                !retainedPhaseIsCurrent() ||
                !npcs.TryGet(before.Handle, out live) || live != before ||
                !npcs.TryCaptureDeathMutationSerial(out var guardedSerial) || guardedSerial != serial ||
                pendingDeathPlan?.CanAdoptUnpublished() != true || plannedCaptainBirth is null ||
                !IsCaptainInvasionCurrent()) { CancelPendingDeathPlan(); return false; }
            var deadUpdate = new NpcStateUpdate(dead.Type, dead.NetId, dead.PositionX, dead.PositionY,
                dead.VelocityX, dead.VelocityY, dead.Target, dead.Ai, dead.Simulation);
            if (!npcs.TryUpdateUnpublished(before.Handle, in deadUpdate, out var committed))
                throw new InvalidOperationException("Validated Ghost motion/death could not be adopted.");
            AdoptCaptainOwners(in committed);
            var wire = new TerrariaNpcDamageState(committed.Handle.Slot,
                RuntimeNpcPacketProjection.ToProtocolGeneration(committed.Handle.Generation), 9999, 0, 1, 0);
            PublishCaptainDeath(in committed, in wire, default);
            return true;
        }
        if (!VanillaZombieCheckActive.TryStep(finished.PositionX, finished.PositionY, body.Width, body.Height,
            finished.Simulation.TimeLeft, players, out var active)) return false;
        var historical = before with { PositionX = finished.PositionX, PositionY = finished.PositionY,
            VelocityX = finished.VelocityX, VelocityY = finished.VelocityY, Target = finished.Target,
            Ai = finished.Ai, Simulation = finished.Simulation };
        finished = finished with { Simulation = finished.Simulation with
        { TimeLeft = active.TimeLeft, Life = active.ShouldDespawn ? 0 : finished.Simulation.Life } };
        var projected = historical with { Simulation = finished.Simulation };
        TerraRuntime.Core.Npcs.RuntimeNpcStore.TerminalRemoval? terminal = null;
        if (active.ShouldDespawn && !npcs.TryPrepareProjectedTerminalRemoval(in projected, out terminal)) return false;
        byte[]? historicalWire = null;
        byte[]? baselineWire = null;
        byte[]? despawnWire = null;
        if (npcReplication is not null &&
            !npcReplication.TryPrepareGhostMotionFrames(in historical, active.ShouldDespawn,
                out historicalWire, out baselineWire, out despawnWire)) return false;
        if (!captureCurrentPlayers() || !retainedPhaseIsCurrent() || !npcs.TryGet(before.Handle, out live) || live != before ||
            !npcs.TryCaptureDeathMutationSerial(out var finalSerial) || finalSerial != serial) return false;
        if (!npcs.TryUpdateUnpublished(before.Handle, in finished, out var accepted))
            throw new InvalidOperationException("Validated Ghost motion could not be adopted.");
        if (terminal is not null)
        {
            if (!terminal.TryAdoptUnpublished()) throw new InvalidOperationException("Validated Ghost CheckActive could not retire.");
            npcReplication?.RetirePreparedDeathBaseline(accepted.Handle);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
            if (terminal.IsAcceptedCurrent)
            {
                try { npcReplication?.PublishRetainedGhostMotion(historicalWire!); }
                catch (Exception e) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e); }
            }
            try { terminal.TryPublish(); }
            catch (Exception e) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e); }
            interactions.Forget(before.Handle);
            failure?.Throw();
        }
        else npcReplication?.AdoptGhostMotionBaseline(in accepted, baselineWire!, despawnWire!);
        return true;
    }
}
