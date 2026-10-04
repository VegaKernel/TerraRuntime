using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private void ApplyPlayerLuckFactors(PlayerLuckFactorsRuntimeCommand command)
    {
        if (!command.Factors.IsFinite || lanternsUp is null ||
            !membership.TryGet(command.Connection, out RuntimePlayerMember? member)) return;
        float luck = CalculatePlayerLuck(member, command.Factors);
        if (!float.IsFinite(luck) || !member.TryAdvanceRevision()) return;
        member.LuckComponents = command.Factors;
        member.Luck = luck;
        events?.PlayerLuckFactorsUpdated(command.Connection, command.Factors);
    }

    private float CalculatePlayerLuck(RuntimePlayerMember member, in VanillaPlayerLuckComponents1458 factors) =>
        VanillaPlayerLuckFacts1458.Recalculate(factors, transferProfiles.HasUsedGalaxyPearl(member.Connection),
            lanternsUp == true, HasNaturalSpawnBuffSnapshot(member.Slot.Value, VanillaBuffIds.Stinky));

    private void RecalculatePlayerLuck(RuntimePlayerMember? member)
    {
        if (member is null || lanternsUp is null) return;
        float luck = CalculatePlayerLuck(member, member.LuckComponents ?? default);
        if (float.IsFinite(luck)) member.Luck = luck;
    }

    internal void TickPlayerLuck()
    {
        if (lanternsUp is null) return;
        foreach (RuntimePlayerMember member in membership.Members)
        {
            // Player.Update returns through UpdateDead before UpdateLuck.
            if (member.IsDead) continue;
            VanillaPlayerLuckComponents1458 old = member.LuckComponents ?? default;
            // Source remote UpdateLuckFactors; ordinary runtime time progression owns dayRate=1.
            VanillaPlayerLuckComponents1458 next = VanillaPlayerLuckFacts1458.AdvanceRemoteFactors(old, 1);
            float luck = CalculatePlayerLuck(member, next);
            if ((!member.LuckComponents.HasValue || old == next) && member.Luck == luck) continue;
            if (!float.IsFinite(luck) || !member.TryAdvanceRevision()) continue;
            if (member.LuckComponents.HasValue) member.LuckComponents = next;
            member.Luck = luck;
        }
    }
}
