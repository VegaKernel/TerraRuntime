using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class ServerRuntimeState : IRuntimePlayerSnapshotLookup, IRuntimePlayerSlotSnapshotLookup
{
    private readonly PlayerAuthority _players;
    private readonly RuntimeServerPlayerStateStore? _serverPlayerStates;
    private readonly RuntimeServerPlayerCommandService? _serverPlayerCommands;
    private readonly IRuntimeServerPlayerEventSink? _serverPlayerEvents;
    private readonly VanillaServerPlayerDryPhysicsStepper? _serverPlayerDryPhysics;
    private readonly PlayerStateSnapshot[] _serverPlayerSnapshots =
        new PlayerStateSnapshot[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly PlayerHandle[] _serverPlayerLiquidOwners =
        new PlayerHandle[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly VanillaLiquidContactState[] _serverPlayerLiquidContacts =
        new VanillaLiquidContactState[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly NpcAuthority _npcs;
    private readonly ProjectileAuthority _projectiles;
    private readonly WorldItemAuthority _worldItems;
    private readonly WorldTileAuthority _worldTileAuthority;
    private readonly WorldTileStore? _worldTiles;
    private readonly RuntimeWorldClock? _worldClock;
    private readonly RuntimeWorldProgressionMutations _worldProgression;
    private int lastWorkerResult;
}
