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
    private readonly RuntimeProjectileStore _projectiles;
    private readonly RuntimeProjectileStateExecutor _projectileExecutor;
    private readonly IProjectileStateStepper? _projectileStepper;
    private readonly RuntimeNpcProjectileReflectionPass _projectileReflections;
    private readonly RuntimeProjectileReplicationRegistry? _projectileReplication;
    private readonly RuntimeNpcReplicationRegistry? _npcReplication;
    private readonly RuntimeWorldItemReplicationRegistry? _worldItemReplication;
    private readonly RuntimeWorldItemInstancedLeaseStore _instancedItemLeases;
    private readonly RuntimeNpcNetworkCombatPipeline _npcCombat;
    private readonly short[] _expiredInstancedItemSlots = new short[RuntimeWorldItemStore.VanillaCapacity];
    private readonly RuntimeTownNpcStateStore? _townNpcs;
    private readonly RuntimeWorldProgressionMutations? _worldProgression;
    private readonly RuntimeTownNpcRescueService1458? _townRescue;
    private readonly RuntimePurificationPowderNpcInteraction1458? _purificationPowderNpcInteractions;
    private readonly RuntimeMysticFrogCatchService1458? _mysticFrogCatch;
    private readonly RuntimeTownCommerceResolver1458? _townCommerce;
    private readonly VanillaHousingValidator1458? _housingValidator;
    private readonly RuntimeTownNpcMoveInCoordinator1458? _townMoveIn;
    private readonly RuntimeTownNpcSchedule1458? _townSchedule;
    private readonly RuntimeTownNpcCombat1458? _townCombat;
    private readonly RuntimeTownNpcShimmerService1458? _townShimmer;
    private readonly VanillaTownSpawnPlayerFacts1458[] _townSpawnPlayers = new VanillaTownSpawnPlayerFacts1458[MaxPlayerSlots];
    private readonly RuntimeTownPlayerBounds1458[] _townPlayerBounds = new RuntimeTownPlayerBounds1458[MaxPlayerSlots];
    private readonly bool _townInitialRaining;
    private readonly bool _townInitialEclipse;
    private readonly bool _townInitialInvasionActive;
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
