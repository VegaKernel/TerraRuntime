using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private ServerPlayerAuthority? teleporterServerPlayers;
    private readonly HashSet<PlayerHandle> wiringTeleportingPlayers = [];
    private readonly HashSet<NpcHandle> wiringTeleportingNpcs = [];
    private TerraRuntime.Core.Projectiles.RuntimeProjectileStore? teleporterProjectiles;

    internal void BindWiringTeleporters(ServerPlayerAuthority? ownedPlayers, TerraRuntime.Core.Projectiles.RuntimeProjectileStore projectiles)
    { teleporterServerPlayers = ownedPlayers; teleporterProjectiles = projectiles; }

    internal void TickWiringTeleportPresentation()
    {
        if (worldItemSpawnRandom is not SystemWorldItemSpawnRandom random) return;
        players.TickWiringTeleportPresentation(random.SourceRandom);
        teleporterServerPlayers?.TickWiringTeleportPresentation(random.SourceRandom);
    }

    private void TryTeleportWiringPair(WorldWiringTeleporter1458 first, WorldWiringTeleporter1458 second, bool blockPlayers)
    {
        if (tiles is null || worldItemSpawnRandom is not SystemWorldItemSpawnRandom adapter ||
            !npcAuthority.OwnsWiringRandom(worldItemSpawnRandom)) return;
        // Source asymmetric overlap exclusion compares adjusted tile coordinates, not pixel distances.
        if (first.PixelX < second.PixelX + 48 && first.PixelX > second.PixelX - 48 &&
            first.PixelY > second.PixelY - 48 && first.PixelY < second.PixelY) return;
        var images = new WorldTile[6]; var versions = new long[6];
        for (int i = 0; i < 6; i++)
        {
            var point = i < 3 ? first : second;
            if ((uint)point.Left >= (uint)tiles.Dimensions.WidthTiles || point.Left + 2 >= tiles.Dimensions.WidthTiles ||
                (uint)point.Row >= (uint)tiles.Dimensions.HeightTiles) return;
            images[i] = tiles.Get(point.Left + i % 3, point.Row);
            if (!images[i].IsActive || images[i].Type != 235 || images[i].FrameX != i % 3 * 18 || images[i].FrameY != 0 ||
                images[i].HasActuator || images[i].IsActuated || images[i].LiquidAmount != 0) return;
            versions[i] = tiles.GetSectionVersion(TerrariaSectionGeometry.FromTile(tiles.Dimensions, point.Left + i % 3, point.Row));
        }
        bool Current()
        {
            for (int i = 0; i < 6; i++)
            {
                var point = i < 3 ? first : second;
                if (!images[i].Equals(tiles.Get(point.Left + i % 3, point.Row)) || versions[i] !=
                    tiles.GetSectionVersion(TerrariaSectionGeometry.FromTile(tiles.Dimensions, point.Left + i % 3, point.Row))) return false;
            }
            return npcAuthority.OwnsWiringRandom(worldItemSpawnRandom);
        }
        bool RebasePadVersions()
        {
            for (int i = 0; i < 6; i++)
            {
                var point = i < 3 ? first : second;
                if (!images[i].Equals(tiles.Get(point.Left + i % 3, point.Row))) return false;
                versions[i] = tiles.GetSectionVersion(TerrariaSectionGeometry.FromTile(tiles.Dimensions, point.Left + i % 3, point.Row));
            }
            return true;
        }
        // Original teleporting flags are shared, and every nested pair clears all flags.
        var movedPlayers = wiringTeleportingPlayers;
        var movedNpcs = wiringTeleportingNpcs;
        try
        {
            for (int side = 0; side < 2; side++)
            {
                var from = side == 0 ? first : second; var to = side == 0 ? second : first;
                int dx = to.PixelX - from.PixelX, dy = to.PixelY - from.PixelY;
                if (!(wiring?.PlayerTeleportationBlocked ?? blockPlayers))
                for (int slot = 0; slot < 255; slot++)
                {
                    PlayerStateSnapshot player;
                    if (players.TryGet(new PlayerSlotId((byte)slot), out var member)) player = member.CaptureSnapshot();
                    else if (teleporterServerPlayers?.TryGet(new PlayerSlotId((byte)slot), out player) != true) continue;
                    if (player.HasMount && player.MountType >= VanillaPlayerMountHitbox1458.MountTypeCount) continue;
                    var (bodyWidth, bodyHeight) = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (20f, 42f);
                    int width = (int)bodyWidth, height = (int)bodyHeight;
                    if (player.IsDead || movedPlayers.Contains(player.Player) ||
                        !VanillaWiringTeleport1458.Touches(from.PixelX, from.PixelY, player.PositionX, player.PositionY, width, height) ||
                        teleporterProjectiles is null || PlayerAuthority.CaptureNpcHealthGrappling(teleporterProjectiles, player.Player) != false ||
                        !DryBody(player.PositionX, player.PositionY, width, height) || !DryBody(player.PositionX + dx, player.PositionY + dy, width, height)) continue;
                    movedPlayers.Add(player.Player);
                    bool committed = member is not null
                        ? players.TryWiringTeleport(in player, dx, dy, adapter.SourceRandom, Current)
                        : teleporterServerPlayers!.TryWiringTeleport(in player, dx, dy, adapter.SourceRandom, Current);
                    if (!committed) movedPlayers.Remove(player.Player);
                    else if (!RebasePadVersions()) return;
                }
                for (int slot = 0; slot < Math.Min(200, npcs.Capacity); slot++)
                    if (npcs.TryGetActive((byte)slot, out var npc) && movedNpcs.Add(npc.Handle))
                    {
                        if (!npcAuthority.TryWiringTeleport(in npc, from.PixelX, from.PixelY, dx, dy, Current)) movedNpcs.Remove(npc.Handle);
                        else if (!RebasePadVersions()) return;
                    }
            }
        }
        finally { movedPlayers.Clear(); movedNpcs.Clear(); }
    }
    private bool DryBody(float x, float y, int width, int height)
    {
        if (tiles is null || !float.IsFinite(x) || !float.IsFinite(y) || x < 0 || y < 0 ||
            x + width > tiles.Dimensions.WidthTiles * 16f || y + height > tiles.Dimensions.HeightTiles * 16f) return false;
        for (int tx = (int)x / 16; tx <= (int)(x + width - 1) / 16; tx++)
        for (int ty = (int)y / 16; ty <= (int)(y + height - 1) / 16; ty++)
            if (tiles.Get(tx, ty).LiquidAmount != 0) return false;
        return true;
    }

}
