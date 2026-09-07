using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
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
