using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class ServerRuntimeState : IRuntimePlayerSnapshotLookup, IRuntimePlayerSlotSnapshotLookup
{
    private const int MaxPlayerSlots = 256;

    private readonly PlayerAuthority _players;
    private readonly VanillaNpcTargetCandidate[] _npcTargetCandidates =
        new VanillaNpcTargetCandidate[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly RuntimeNpcStore _npcs;
    private readonly RuntimeNpcAiStateExecutor _npcAiExecutor;
    private readonly RuntimeNpcActorControlRegistry _npcActorControls;
    private readonly RuntimeNpcActorControlCommandService _npcActorCommands;
    private readonly RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper> _npcPresentationBehaviors;
    private readonly RuntimeArchetypeBehaviorRegistry<INpcAiStateStepper> _npcArchetypeBehaviors;
    private readonly RuntimeNpcBehaviorQueries _npcBehaviorQueries;
    private readonly RuntimeNpcArchetypeRegistry _npcArchetypes;
    private readonly RuntimeNpcArchetypeIdentityStore _npcArchetypeIdentities;
    private readonly RuntimeNpcArchetypeSpawner _npcArchetypeSpawner;
    private readonly RuntimeNpcShopCatalogRegistry _npcShops;
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
    private readonly INpcAiStateStepper _npcAiStepper;
    private readonly VanillaNpcTargetingAiStepper? _vanillaNpcTargetingAiStepper;
    private readonly VanillaNpcCheckActiveAiStepper? _vanillaNpcCheckActiveAiStepper;
    private readonly ProjectileAuthority _projectiles;
    private readonly RuntimeNpcReplicationRegistry? _npcReplication;
    private readonly RuntimeWorldItemReplicationRegistry? _worldItemReplication;
    private readonly RuntimeWorldItemInstancedLeaseStore _instancedItemLeases;
    private readonly RuntimeNpcNetworkCombatPipeline _npcCombat;
    private readonly short[] _expiredInstancedItemSlots = new short[RuntimeWorldItemStore.VanillaCapacity];
    private readonly TownNpcAuthority _townNpcAuthority;
    private readonly RuntimeMysticFrogCatchService1458? _mysticFrogCatch;
    private readonly RuntimeTileManipulationReplicationRegistry? _tileManipulationReplication;
    private readonly RuntimeObjectPlacementCommandProcessor? _objectPlacementProcessor;
    private readonly RuntimeWorldItemStore _worldItems;
    private readonly IWorldItemSpawnRandom _worldItemSpawnRandom;
    private readonly WorldTileStore? _worldTiles;
    private readonly VanillaWorldTileMutationService? _tileMutations;
    private readonly RuntimeWorldClock? _worldClock;
    private readonly bool _expertMode;
    private readonly bool _masterMode;
    private readonly PlayerTileEditBudget _tileEditBudget = new(MaxPlayerSlots);
    private int lastWorkerResult;
}
