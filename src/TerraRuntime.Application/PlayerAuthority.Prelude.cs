using TerraRuntime.Contracts.Runtime;
namespace TerraRuntime.Application;
internal sealed partial class PlayerAuthority
{
    internal bool TryGetPreludePlayerName(PlayerHandle player, out string name)
    {
        name = "";
        if (!membership.TryGet(player.Slot, out var member) || member.Connection.Player != player) return false;
        name = transferProfiles.GetName(member.Connection) ?? ""; return true;
    }
}
