using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

/// <summary>
/// Generation-safe authoritative-thread player lookup across connection-owned and runtime-controlled players.
/// It is the single aggregation boundary used by simulation code that must not know which authority owns a player.
/// </summary>
internal sealed class RuntimePlayerSnapshotLookup : IRuntimePlayerSnapshotLookup, IRuntimePlayerSlotSnapshotLookup
{
    private readonly PlayerAuthority players;
    private readonly ServerPlayerAuthority? serverPlayers;

    public RuntimePlayerSnapshotLookup(PlayerAuthority players, ServerPlayerAuthority? serverPlayers)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.serverPlayers = serverPlayers;
    }

    public bool TryGetPlayer(PlayerHandle player, out PlayerStateSnapshot snapshot)
    {
        if (players.TryCapture(player, out snapshot))
            return true;

        if (serverPlayers is not null && serverPlayers.TryGet(player, out snapshot))
            return true;

        snapshot = default;
        return false;
    }

    public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
    {
        if (players.TryGet(slot, out RuntimePlayerMember? player))
            return TryGetPlayer(player.Connection.Player, out snapshot);

        if (serverPlayers is not null)
            return serverPlayers.TryGet(slot, out snapshot);

        snapshot = default;
        return false;
    }
}
