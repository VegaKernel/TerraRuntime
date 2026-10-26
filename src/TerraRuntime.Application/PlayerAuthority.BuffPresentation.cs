using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool TryApplyProjectileBuff(in ProjectilePlayerBuffApplication application)
    {
        if (application.Type != VanillaBuffIds.MoonLeech || application.DurationTicks <= 0) return false;
        if (!membership.TryGet(application.Target, out RuntimePlayerMember? member))
            return serverPlayers?.TryApplyMoonLeech(application.Target, application.DurationTicks) ?? false;
        // The client runs the same projectile AI for its own buff. Moon Leech is not a PvP relay buff;
        // server state is replaced by subsequent packet-50 snapshots, without sending packet 55.
        return transferProfiles.TryApplyMoonLeech(member.Connection, application.DurationTicks);
    }

    internal int GetBuffDuration(PlayerHandle player, BuffTypeId type) =>
        membership.TryGet(player, out RuntimePlayerMember? member)
            ? transferProfiles.GetBuffDuration(member.Connection, type)
            : type == VanillaBuffIds.MoonLeech ? serverPlayers?.GetMoonLeechDuration(player) ?? 0
            : serverPlayers?.GetNpcContactBuffDuration(player, type) ?? 0;

    // Spawn-only flags follow remote Player.UpdateBuffs; this does not admit combat modifiers.
    internal bool HasNaturalSpawnBuffSnapshot(byte slot, BuffTypeId type) =>
        membership.TryGet(new PlayerSlotId(slot), out RuntimePlayerMember? member) &&
        transferProfiles.GetBuffDuration(member.Connection, type) > 0;

    internal bool HasMoonLeech(byte slot) =>
        transferProfiles.HasBuff(slot, VanillaBuffIds.MoonLeech) ||
        (serverPlayers is not null && serverPlayers.TryGet(new PlayerSlotId(slot), out var player) &&
         serverPlayers.GetMoonLeechDuration(player.Player) > 0);

    private bool IsExplainedQuickManaBuffAcknowledgement(RuntimePlayerMember member,
        ReadOnlySpan<BuffTypeId> incoming)
    {
        if (!inventory.HasOwnedManaPotionQuantity(member.Connection)) return false;
        var buffs = transferProfiles.CaptureBuffState(member.Connection);
        return buffs is not null && buffs.CountActive(new(94)) > 0 &&
            incoming.SequenceEqual(buffs.CaptureTypes());
    }

    private void ApplyPlayerBuffTypes(PlayerBuffTypesRuntimeCommand command)
    {
        PlayerBuffTypesCommitRequest request = command.Request;
        if (!command.Connection.IsAssigned || command.Connection.Player.Slot != request.PlayerSlot)
        {
            RejectedBuffSnapshots++;
            return;
        }

        if (membership.TryGet(request.PlayerSlot, out RuntimePlayerMember? activePlayer) &&
            activePlayer.Connection != command.Connection)
        {
            RejectedBuffSnapshots++;
            return;
        }

        if (activePlayer is not null && transferProfiles.CaptureBuffState(command.Connection) is
            { HasOwnedNpcContactBuffs: true } contactState)
        {
            // Presence-only client reports cannot renew an earned timer or discard a server effect.
            // Other local cures/removal transactions need their own causal authority before admission.
            if (request.BuffTypes.Span.SequenceEqual(contactState.CaptureTypes())) AppliedBuffSnapshots++;
            else RejectedBuffSnapshots++;
            return;
        }
        if (activePlayer is not null && TryHandleQuickBuffSnapshot(activePlayer, in request))
            return;
        if (activePlayer is not null &&
            (IsExplainedSelectedPotionBuffAcknowledgement(activePlayer, request.BuffTypes.Span) ||
             TryExplainDeniedSelectedBuffAcknowledgement(activePlayer, request.BuffTypes.Span)))
        {
            AppliedBuffSnapshots++;
            return;
        }
        if (activePlayer is not null && TryHandleOwnedFoodSnapshot(activePlayer, in request))
            return;
        if (activePlayer is not null && TryApplySelectedBuffSubsetRemoval(activePlayer, in request))
            return;
        if (activePlayer is not null && IsKnownDeniedSelectedBuffIncrement(activePlayer, request.BuffTypes.Span))
        {
            RejectedBuffSnapshots++;
            return;
        }
        if (activePlayer is not null && !activePlayer.TryAdvanceRevision())
        {
            RejectedBuffSnapshots++;
            return;
        }

        bool explainedQuickMana = activePlayer is not null &&
            (IsExplainedQuickManaBuffAcknowledgement(activePlayer, request.BuffTypes.Span) ||
             IsExplainedSelectedPotionBuffAcknowledgement(activePlayer, request.BuffTypes.Span));
        if (!explainedQuickMana && !transferProfiles.TrySetBuffTypes(command.Connection, request.BuffTypes))
        {
            RejectedBuffSnapshots++;
            return;
        }

        AppliedBuffSnapshots++;
        RecalculatePlayerLuck(activePlayer);
        events?.PlayerBuffTypesUpdated(command.Connection, in request);
    }
    private bool TryHandleOwnedFoodSnapshot(RuntimePlayerMember member, in PlayerBuffTypesCommitRequest request)
    {
        if (transferProfiles.CaptureBuffState(member.Connection) is not { HasOwnedFoodSlots: true } previous) return false;
        var incoming = request.BuffTypes.Span;
        if (incoming.SequenceEqual(previous.CaptureTypes()))
        {
            AppliedBuffSnapshots++;
            return true;
        }
        if (previous.TryPrepareSourceSubsetRemoval(incoming, out var next, out bool ambiguous))
        {
            if (ambiguous || next is null || !member.TryAdvanceRevision() ||
                !transferProfiles.TryAdoptBuffState(member.Connection, previous, next)) RejectedBuffSnapshots++;
            else
            {
                AppliedBuffSnapshots++;
                RecalculatePlayerLuck(member);
                events?.PlayerBuffTypesUpdated(member.Connection, in request);
            }
            return true;
        }
        // A changed presence-only report cannot select or renew an owned duplicate.
        foreach (var type in previous.CaptureEffectTypes())
            if (type.Value is 26 or 206 or 207 or 121 or 122 or 123 && incoming.Contains(type))
            {
                RejectedBuffSnapshots++;
                return true;
            }
        return false; // Removing all earned food may retain the existing unrelated-import policy.
    }

}
