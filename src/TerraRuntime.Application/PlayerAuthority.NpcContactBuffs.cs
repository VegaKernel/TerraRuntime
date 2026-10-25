using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Buffs;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal readonly record struct NpcContactBuffSource1458(int Type, int NetId, float Ai1, bool GoodWorld, Random Random);

internal sealed partial class PlayerAuthority
{
    private NpcContactBuff1458[] ApplyNpcContactBuffs(RuntimePlayerMember target,
        in VanillaPlayerCombatSnapshot combat, in NpcContactBuffSource1458 source, bool contactOnly = false)
    {
        if (target.IsDead || target.GodMode ||
            transferProfiles.CaptureBuffState(target.Connection) is not { } before) return [];
        var owner = target.CaptureSnapshot();
        Span<NpcContactBuff1458> effects = stackalloc NpcContactBuff1458[VanillaNpcContactBuffs1458.MaximumEffects];
        // Main.Difficulty raises the mode by one in For the Worthy; this is also the source
        // expert/master predicate for StatusFromNPC and DetermineBuffTimeToAdd.
        bool statusExpert = expertMode || source.GoodWorld;
        bool statusMaster = masterMode || expertMode && source.GoodWorld;
        int count;
        if (contactOnly)
        {
            effects[0] = new(new BuffTypeId(48), 1800);
            count = 1;
        }
        else count = VanillaNpcContactBuffs1458.Evaluate(source.Type, source.NetId, source.Ai1,
            statusExpert, before.IsNpcContactFrozen, source.GoodWorld, source.Random, effects);
        if (!membership.TryGet(target.Connection, out var current) || !ReferenceEquals(current, target) ||
            target.CaptureSnapshot() != owner) return [];
        var next = before.Clone();
        int applied = 0;
        for (int i = 0; i < count; i++)
        {
            var effect = effects[i];
            int duration = VanillaNpcContactBuffs1458.ResolveDuration(effect.Type, effect.DurationTicks,
                statusExpert, statusMaster, resistCold: false);
            if (!next.TryApplyNpcContactBuff(effect.Type, duration, combat.IsImmuneToNpcContactBuff(effect.Type))) continue;
            effects[applied++] = new(effect.Type, next.GetDuration(effect.Type));
        }
        if (applied == 0 || !transferProfiles.TryAdoptBuffState(target.Connection, before, next)) return [];
        return effects[..applied].ToArray();
    }

    internal bool TryApplySlimeContactBuff(in IncomingPlayerCombatCapture1458 capture,
        in NpcContactBuffSource1458 source)
    {
        if (!VanillaNpcContactBuffs1458.IsSlimeContactOnly(source.Type, source.Ai1) ||
            capture.Member is not { } member || !capture.IsAlive || !IsCurrentIncomingCombat(in capture) ||
            !member.TryAdvanceRevision()) return false;
        var effects = ApplyNpcContactBuffs(member, capture.Combat, source, contactOnly: true);
        PublishNpcContactBuffs(member, effects);
        return effects.Length > 0;
    }

    private void PublishNpcContactBuffs(RuntimePlayerMember member, ReadOnlySpan<NpcContactBuff1458> effects)
    {
        foreach (var effect in effects)
        {
            if (!membership.TryGet(member.Connection, out var current) || !ReferenceEquals(member, current) ||
                member.IsDead || transferProfiles.GetBuffDuration(member.Connection, effect.Type) != effect.DurationTicks) return;
            events?.PlayerNpcContactBuffApplied(member.Connection.Player, effect.Type, effect.DurationTicks);
        }
        if (effects.Length == 0 || !membership.TryGet(member.Connection, out var live) || !ReferenceEquals(member, live) || member.IsDead)
            return;
        if (transferProfiles.CaptureBuffState(member.Connection) is not { } state) return;
        var request = new PlayerBuffTypesCommitRequest(member.Slot, state.CaptureTypes());
        events?.PlayerBuffTypesUpdated(member.Connection, in request);
    }

    private void AdvanceNpcContactBuffs()
    {
        // An ordinary player with no contact effect needs neither a census snapshot nor 44-slot clones.
        // Keep the callback-safe census only when this clock actually owns work.
        bool hasEffects = false;
        foreach (var member in membership.Members)
            if (transferProfiles.HasOwnedNpcContactBuffs(member.Connection)) { hasEffects = true; break; }
        if (!hasEffects) return;
        foreach (var member in membership.Members.ToArray())
        {
            if (!transferProfiles.HasOwnedNpcContactBuffs(member.Connection)) continue;
            if (transferProfiles.CaptureBuffState(member.Connection) is not { HasOwnedNpcContactBuffs: true } before) continue;
            var next = before.Clone();
            bool changed = member.IsDead ? next.ClearNonPersistentOnDeath() : next.TickNpcContactBuffs();
            if (!member.TryAdvanceRevision() || !transferProfiles.TryAdoptBuffState(member.Connection, before, next) || !changed) continue;
            var request = new PlayerBuffTypesCommitRequest(member.Slot, next.CaptureTypes());
            events?.PlayerBuffTypesUpdated(member.Connection, in request);
        }
    }
}
