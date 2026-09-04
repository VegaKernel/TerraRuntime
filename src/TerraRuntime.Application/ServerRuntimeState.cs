using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class ServerRuntimeState : IRuntimePlayerSnapshotLookup, IRuntimePlayerSlotSnapshotLookup
{
    private readonly PlayerAuthority _players;
    private readonly ServerPlayerAuthority? _serverPlayers;
    private readonly NpcAuthority _npcs;
    private readonly ProjectileAuthority _projectiles;
    private readonly RuntimeProjectilePlayerCombatPass _projectilePlayerCombat;
    private readonly RuntimeNpcPlayerCombatPass _npcPlayerCombat;
    private readonly WorldItemAuthority _worldItems;
    private readonly WorldTileAuthority _worldTileAuthority;
    private readonly WorldTileStore? _worldTiles;
    private readonly RuntimeWorldClock? _worldClock;
    private readonly RuntimeWorldProgressionMutations _worldProgression;
    private int lastWorkerResult;
}
