using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    internal void TickHealthContext(WorldTileStore tiles)
    {
        int count = states.CopySnapshots(snapshots);
        for (int index = 0; index < count; index++)
        {
            PlayerStateSnapshot player = snapshots[index];
            bool outOfRange = VanillaPlayerHealthContext1458.IsRemoteOutOfRange(
                player.PositionX, player.PositionY,
                (int)PlayerAuthority.VanillaBasePlayerWidth, (int)PlayerAuthority.VanillaBasePlayerHeight,
                tiles.Dimensions.WidthTiles, tiles.Dimensions.HeightTiles);
            // The server-owned buff owner currently admits Moon Leech and lava On Fire, neither of which
            // changes statLifeMax2. A new buff writer must also extend this represented health context.
            int? next = VanillaPlayerHealthContext1458.Resolve(
                player.DerivedLifeMax, player.BaseLifeMax, lifeforceSlots: 0,
                outOfRange, ghost: false, player.IsDead);
            ref LavaState lava = ref lavaStates[player.Player.Slot.Value];
            var represented = new PlayerDebuffSnapshot1458(
                lava.Owner == player.Player && lava.BurningTicks > 0, false, false);
            var debuffs = VanillaPlayerHealthContext1458.ResolveDebuffs(
                player.Debuffs, represented, outOfRange, ghost: false, player.IsDead);
            states.TrySetHealthContext(in player, next, debuffs);
        }
    }
}
