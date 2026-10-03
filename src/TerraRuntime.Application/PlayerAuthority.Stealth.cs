using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // Player.Hurt resets stealth only after its rejection/immunity/dodge gates.
    // These calls belong to accepted authoritative PvE/PvP commits, not packet16 snapshots.
    private void ResetStealthAfterAcceptedHurt(RuntimePlayerMember member)
    {
        if (member.Stealth == 1f)
            return;
        member.Stealth = 1f;
        events?.PlayerStealthUpdated(member.Connection, 1f);
    }

    private void ApplyPlayerStealth(PlayerStealthRuntimeCommand command)
    {
        if (!TerrariaPlayerStealthCodec1458.IsValid(command.Stealth) ||
            !membership.TryGet(command.Connection, out RuntimePlayerMember? member) ||
            !member.TryAdvanceRevision())
            return;
        member.Stealth = command.Stealth;
        events?.PlayerStealthUpdated(command.Connection, command.Stealth);
    }
}
