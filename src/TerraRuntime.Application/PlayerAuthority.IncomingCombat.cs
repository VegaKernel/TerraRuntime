using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal enum IncomingHumanCombatPolicy1458 : byte
{
    EquipmentComponent,
    PhaseOwned
}

internal readonly record struct IncomingPlayerCombatCapture1458(
    RuntimePlayerMember? Member,
    PlayerStateSnapshot Player,
    RuntimePlayerItemPhase1458? ItemPhase,
    ulong InventorySerial,
    bool GodMode,
    VanillaPlayerCombatSnapshot Combat,
    IncomingPlayerVitalsCapture1458? Vitals = null,
    bool PhaseOwnedHuman = false)
{
    internal bool IsAlive => !Player.IsDead &&
        (Vitals is { } owned ? owned.Health.Life > 0 :
            PhaseOwnedHuman && GodMode && Player.NpcLifeCurrent == true && Player.NpcHealth is { Life: > 0 } ||
            Player.HasHealth && Player.Life > 0);
}

internal readonly record struct IncomingPlayerVitalsCapture1458(
    PlayerNpcHealthState1458 Health, int BaseMaximum, int DerivedMaximum);

internal sealed partial class PlayerAuthority
{
    private readonly IncomingHumanCombatPolicy1458 incomingHumanCombatPolicy;

    internal bool TryCaptureIncomingCombat(PlayerHandle player, out IncomingPlayerCombatCapture1458 capture)
    {
        capture = default;
        if (!membership.TryGet(player, out var member))
        {
            // Server-owned actors retain their separate equipment/component policy.
            if (!TryCaptureCombatTarget(player.Slot.Value, out var server) || server.Player != player ||
                !TryCaptureCombatSnapshot(player, out var serverCombat)) return false;
            capture = new(null, server, null, inventory.Serial, server.GodMode, serverCombat);
            return true;
        }

        RuntimePlayerItemPhase1458? phase = member.ItemPhase;
        if ((member.Revision == ulong.MaxValue || member.ProjectileUseInputRevision == ulong.MaxValue ||
            inventory.Serial == ulong.MaxValue) && !member.GodMode) return false;
        VanillaPlayerCombatSnapshot combat;
        IncomingPlayerVitalsCapture1458? vitals = null;
        if (member.GodMode)
            combat = default; // Hurt avoids damage before using derived mitigation. This does not mint a phase.
        else if (incomingHumanCombatPolicy == IncomingHumanCombatPolicy1458.PhaseOwned)
        {
            if (phase?.DerivedCombat is not { } derived || !IsValidDerivedCombat(in derived)) return false;
            if (member.NpcLifeCurrent != true || member.NpcHealth is not { } health ||
                !IsValidIncomingHealth(in health) || member.BaseLifeMax is not (>= 20 and <= short.MaxValue) ||
                member.DerivedLifeMax is not (>= 1)) return false;
            vitals = new(health, member.BaseLifeMax.Value, member.DerivedLifeMax.Value);
            combat = derived;
        }
        else if (!TryCaptureCombatSnapshot(player, out combat))
            return false;

        capture = new(member, member.CaptureSnapshot(), phase, inventory.Serial, member.GodMode, combat, vitals,
            incomingHumanCombatPolicy == IncomingHumanCombatPolicy1458.PhaseOwned);
        return true;
    }

    // Keep the legacy component eligibility before its weapon-specific LegacyFallback decision.
    internal bool IsIncomingTargetAlive(in PlayerStateSnapshot player)
    {
        if (player.IsDead) return false;
        bool reportedAlive = player.HasHealth && player.Life > 0;
        if (incomingHumanCombatPolicy != IncomingHumanCombatPolicy1458.PhaseOwned ||
            !membership.TryGet(player.Player, out var member)) return reportedAlive;
        return player.NpcLifeCurrent == true && player.NpcHealth is { Life: > 0 } ||
            member.GodMode && reportedAlive;
    }

    private static bool IsValidIncomingHealth(in PlayerNpcHealthState1458 health) =>
        health.Life is >= 0 and <= short.MaxValue &&
        (health.RegenTime is not { } time || float.IsFinite(time) && time >= 0f);

    private static bool TryResolveIncomingLifeAfterDamage(in IncomingPlayerCombatCapture1458 capture, int damage, out short life)
    {
        int next = Math.Max(0, (capture.Vitals?.Health.Life ?? capture.Player.Life) - damage);
        life = 0;
        if (next > short.MaxValue) return false;
        life = (short)next;
        return true;
    }

    private static void AdoptIncomingLife(RuntimePlayerMember target, in IncomingPlayerCombatCapture1458 capture, short life)
    {
        short previousReportedLife = target.Life;
        target.Life = life;
        if (capture.Vitals is { } vitals)
        {
            // Hurt resets only the regeneration timer; unknown accumulated count stays unknown.
            target.NpcHealth = vitals.Health with { Life = life, RegenTime = 0f };
            target.NpcLifeCurrent = true;
            target.HasHealth = true;
            target.MaxLife = (short)vitals.BaseMaximum;
        }
        else ApplyNpcHealthHurt(target, previousReportedLife);
    }

    internal bool IsCurrentIncomingCombat(in IncomingPlayerCombatCapture1458 capture)
    {
        if (capture.Member is null)
            return TryCaptureCombatTarget(capture.Player.Player.Slot.Value, out var server) && server == capture.Player;
        return membership.TryGet(capture.Player.Player, out var member) && ReferenceEquals(member, capture.Member) &&
            member.CaptureSnapshot() == capture.Player && member.ItemPhase == capture.ItemPhase && member.GodMode == capture.GodMode &&
            (capture.Vitals is not { } vitals || member.NpcLifeCurrent == true && member.NpcHealth == vitals.Health &&
                member.BaseLifeMax == vitals.BaseMaximum && member.DerivedLifeMax == vitals.DerivedMaximum) &&
            inventory.Serial == capture.InventorySerial;
    }
}
