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
            : type == VanillaBuffIds.MoonLeech ? serverPlayers?.GetMoonLeechDuration(player) ?? 0 : 0;

    internal bool HasMoonLeech(byte slot) =>
        transferProfiles.HasBuff(slot, VanillaBuffIds.MoonLeech) ||
        (serverPlayers is not null && serverPlayers.TryGet(new PlayerSlotId(slot), out var player) &&
         serverPlayers.GetMoonLeechDuration(player.Player) > 0);

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

        if (activePlayer is not null && !activePlayer.TryAdvanceRevision())
        {
            RejectedBuffSnapshots++;
            return;
        }

        if (!transferProfiles.TrySetBuffTypes(command.Connection, request.BuffTypes))
        {
            RejectedBuffSnapshots++;
            return;
        }

        AppliedBuffSnapshots++;
        events?.PlayerBuffTypesUpdated(command.Connection, in request);
    }
}
