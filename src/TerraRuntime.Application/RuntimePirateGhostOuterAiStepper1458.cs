using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

// The complete verified AI122 Update, rather than a generic state-only 23 after each move.
internal sealed class RuntimePirateGhostOuterAiStepper1458(
    INpcAiStateStepper inner, RuntimeNpcStore npcs, VanillaNpcTargetingAiStepper targeting,
    VanillaNpcWorldMotionAiStepper motion, RuntimeNpcNetworkCombatPipeline combat,
    RuntimeNpcBuffStatus1458 status, INpcRawPlayerSlotLookup1458 rawPlayers,
    Func<VanillaNpcTargetCandidate[]> capturePlayers) : INpcAiStateStepper, INpcAiStateStepperWrapper, INpcAiPrepass1458
{
    private readonly INpcAiPrepass1458? ordinaryPrepass = NpcAiStateStepperComposition.FindCapability<INpcAiPrepass1458>(inner);
    public INpcAiStateStepper InnerStepper => inner;
    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) => inner.TryStepState(in npc, out next);
    public bool TryRunPrepass(in NpcSnapshot npc, out bool consumed)
    {
        consumed = false;
        if (npc.TypeIdentity != VanillaNpcIds.PirateGhost)
            return ordinaryPrepass is null || ordinaryPrepass.TryRunPrepass(in npc, out consumed);
        consumed = true;
        // Nonempty status/regen/liquid phases need their own complete source chronology.
        if (npc.Simulation.LifeRegenCounter != 0 || npc.Simulation.HostileContactImmunity != 0 ||
            !HasEmptyStatus(npc.Handle) || !npcs.TryCaptureDeathMutationSerial(out var beforeSerial)) return false;
        var players = capturePlayers();
        var peers = new NpcSnapshot[npcs.Capacity];
        int count = npcs.CopyActive(peers);
        targeting.SetCandidates(players);
        targeting.SetNpcPeers(peers.AsSpan(0, count));
        if (!targeting.TryPlanPirateGhost(in npc, out var ai, out var fade, windowsVectorArithmetic: OperatingSystem.IsWindows()) ||
            !motion.TryApplyWorldMotion(in npc, in ai, out var proposed) ||
            !rawPlayers.TryCapture(proposed.Target < byte.MaxValue ? (byte)proposed.Target : (byte)0, out var raw) ||
            !npcs.TryCaptureDeathMutationSerial(out var afterSerial) || beforeSerial != afterSerial) return false;
        var handle = npc.Handle;
        bool CaptureCurrentPlayers() => capturePlayers().AsSpan().SequenceEqual(players);
        // This retained owner read follows all external phase/death providers. No further
        // provider is sampled between this tail and the existing direct NPC/RNG guards.
        bool RetainedPhaseIsCurrent() => rawPlayers.IsCurrent(in raw) && HasEmptyStatus(handle);
        return combat.TryCommitPirateGhostPhase(in npc, in proposed, fade, players,
            CaptureCurrentPlayers, RetainedPhaseIsCurrent);
    }
    private bool HasEmptyStatus(NpcHandle handle)
    {
        Span<TerrariaNpcBuffEntryState> buffs = stackalloc TerrariaNpcBuffEntryState[RuntimeNpcBuffStatus1458.Capacity];
        return status.TryCopyWireBuffs(handle, buffs, out var count) && count == 0 &&
            status.TryGetDebuffs(handle, out var flags) && flags == default &&
            status.TryGetStinky(handle, out var stinky) && !stinky;
    }
}
