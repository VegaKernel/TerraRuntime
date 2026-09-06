using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ServerRuntimeState
{
    public void Tick()
    {
        _runtime.Players.AdvanceCombatTick(Updates);
        _runtime.WorldTileAuthority.AdvanceTo(Updates);
        _runtime.WorldTileAuthority.TickLiquids();

        _runtime.Npcs.CommitPending();
        _runtime.Bots?.Tick();
        _runtime.ServerPlayers?.TickPhysics(_runtime.PlayerSnapshots);
        _runtime.Npcs.TickSimulation();
        _runtime.NpcPlayerCombat.Tick(Updates);
        if (_runtime.Projectiles.TryTickState())
        {
            ReadOnlySpan<RuntimeProjectileExplosionEvent> explosions = _runtime.Projectiles.PendingExplosions;
            _runtime.Npcs.TickProjectileInteractions(explosions);
            _runtime.ProjectilePlayerCombat.Tick(explosions);
            _runtime.WorldTileAuthority.TickProjectileTileExplosions(_runtime.Projectiles.PendingTileExplosions);
            _runtime.Projectiles.ApplyReflections();
        }
        _runtime.WorldItems.TickPlayerReservations(Updates);
        _runtime.WorldItems.TickInstancedLeases();

        _runtime.WorldClock?.Tick();
        _runtime.Updates.Advance();
    }

    private bool IsTileActorFree(int tileX, int tileY)
    {
        if (_runtime.WorldTiles is null)
            return false;
        if ((uint)tileX >= (uint)_runtime.WorldTiles.Dimensions.WidthTiles || (uint)tileY >= (uint)_runtime.WorldTiles.Dimensions.HeightTiles)
            return false;
        return _runtime.TallGateOccupancy?.IsActorFree(tileX, tileY) == true;
    }

    internal bool IsTileActorFreeForTesting(int tileX, int tileY) => IsTileActorFree(tileX, tileY);
}
