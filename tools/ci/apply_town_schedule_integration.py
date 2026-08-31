from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one anchor, found {count}")
    p.write_text(text.replace(old, new), encoding="utf-8")


state = "src/TerraRuntime/ServerRuntimeState.cs"
replace_once(state,
'''    private readonly RuntimeNpcReplicationRegistry? _npcReplication;
    private readonly RuntimeTownNpcStateStore? _townNpcs;
    private readonly VanillaHousingValidator1458? _housingValidator;
    private readonly RuntimeTileManipulationReplicationRegistry? _tileManipulationReplication;''',
'''    private readonly RuntimeNpcReplicationRegistry? _npcReplication;
    private readonly RuntimeTownNpcStateStore? _townNpcs;
    private readonly VanillaHousingValidator1458? _housingValidator;
    private readonly RuntimeTownNpcMoveInCoordinator1458? _townMoveIn;
    private readonly RuntimeTownNpcSchedule1458? _townSchedule;
    private readonly VanillaTownSpawnPlayerFacts1458[] _townSpawnPlayers = new VanillaTownSpawnPlayerFacts1458[MaxPlayerSlots];
    private readonly RuntimeTownPlayerBounds1458[] _townPlayerBounds = new RuntimeTownPlayerBounds1458[MaxPlayerSlots];
    private readonly bool _townInitialRaining;
    private readonly bool _townInitialEclipse;
    private readonly bool _townInitialInvasionActive;
    private readonly RuntimeTileManipulationReplicationRegistry? _tileManipulationReplication;''')
replace_once(state,
'''        RuntimeProjectileReplicationRegistry? projectileReplication = null,
        RuntimeNpcReplicationRegistry? npcReplication = null,
        RuntimeTownNpcStateStore? townNpcs = null,
        RuntimeTileManipulationReplicationRegistry? tileManipulationReplication = null,''',
'''        RuntimeProjectileReplicationRegistry? projectileReplication = null,
        RuntimeNpcReplicationRegistry? npcReplication = null,
        RuntimeTownNpcStateStore? townNpcs = null,
        VanillaTownSpawnWorldFacts1458? townSpawnWorldFacts = null,
        bool townInitialRaining = false,
        bool townInitialEclipse = false,
        bool townInitialInvasionActive = false,
        RuntimeTileManipulationReplicationRegistry? tileManipulationReplication = null,''')
replace_once(state,
'''        _housingValidator = worldTiles is not null && townNpcs is not null
            ? new VanillaHousingValidator1458(worldTiles)
            : null;
        _tileManipulationReplication = tileManipulationReplication;''',
'''        _housingValidator = worldTiles is not null && townNpcs is not null
            ? new VanillaHousingValidator1458(worldTiles)
            : null;
        _townInitialRaining = townInitialRaining;
        _townInitialEclipse = townInitialEclipse;
        _townInitialInvasionActive = townInitialInvasionActive;
        if (worldTiles is not null && townNpcs is not null && _housingValidator is not null)
        {
            _townSchedule = new RuntimeTownNpcSchedule1458(townNpcs, _npcs, worldTiles);
            if (townSpawnWorldFacts is VanillaTownSpawnWorldFacts1458 facts)
            {
                var houseIndex = new RuntimeTownHouseCandidateIndex1458(worldTiles, _housingValidator);
                _townMoveIn = new RuntimeTownNpcMoveInCoordinator1458(
                    townNpcs, _npcs, houseIndex, in facts, npcReplication);
            }
        }
        _tileManipulationReplication = tileManipulationReplication;''')
replace_once(state,
'''        LastNpcAiTick = _npcAiExecutor.Tick(_npcAiStepper);
        AppliedNpcDespawns += _npcs.DespawnExpired();''',
'''        LastNpcAiTick = _npcAiExecutor.Tick(_npcAiStepper);
        TickTownNpcLifecycle();
        AppliedNpcDespawns += _npcs.DespawnExpired();''')
replace_once(state,
'''        Updates++;
    }

    private void TickServerPlayerPhysics()''',
'''        Updates++;
    }

    private void TickTownNpcLifecycle()
    {
        if (_townMoveIn is null && _townSchedule is null)
            return;

        int spawnPlayerCount = 0;
        int boundsCount = 0;
        Span<RuntimePlayerInventoryItem> inventory = stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        foreach (RuntimePlayerState player in _players.Values)
        {
            long coinValue = 0;
            bool bullet = false;
            bool bomb = false;
            bool dye = false;
            inventory.Clear();
            if (_playerInventory.TryCopyInventory(player.Connection, inventory))
            {
                foreach (RuntimePlayerInventoryItem item in inventory)
                {
                    if (item.IsEmpty)
                        continue;
                    coinValue = Math.Min(5_000L, coinValue + VanillaTownNpcSpawnItemFacts1458.GetCoinValue(item.ItemType, item.Stack));
                    bullet |= VanillaTownNpcSpawnItemFacts1458.CountsForArmsDealer(item.ItemType);
                    bomb |= VanillaTownNpcSpawnItemFacts1458.CountsForDemolitionist(item.ItemType);
                    dye |= VanillaTownNpcSpawnItemFacts1458.CountsForDyeTrader(item.ItemType);
                }
            }

            _townSpawnPlayers[spawnPlayerCount++] = new VanillaTownSpawnPlayerFacts1458(
                Active: true,
                MaxLife: player.HasHealth ? player.MaxLife : (short)100,
                CoinValue: coinValue,
                HasBulletAmmoOrWeapon: bullet,
                HasDemolitionistBomb: bomb,
                HasDyeTraderItem: dye);
            _townPlayerBounds[boundsCount++] = new RuntimeTownPlayerBounds1458(
                player.PositionX, player.PositionY, VanillaBasePlayerWidth, VanillaBasePlayerHeight);
        }

        if (_townMoveIn is not null)
        {
            var moveInConditions = new RuntimeTownNpcMoveInConditions1458(
                DayTime: _worldClock?.DayTime ?? true,
                Eclipse: _townInitialEclipse,
                InvasionActive: _townInitialInvasionActive,
                WorldUpdateRate: 1);
            _townMoveIn.Tick(in moveInConditions, _townSpawnPlayers.AsSpan(0, spawnPlayerCount));
        }

        if (_townSchedule is not null)
        {
            var scheduleConditions = new RuntimeTownNpcScheduleConditions1458(
                DayTime: _worldClock?.DayTime ?? true,
                Raining: _townInitialRaining,
                Eclipse: _townInitialEclipse,
                SlimeRain: _worldClock?.SlimeRainActive ?? false,
                StormingAboveSurface: false);
            _townSchedule.Tick(in scheduleConditions, _townPlayerBounds.AsSpan(0, boundsCount));
        }
    }

    private void TickServerPlayerPhysics()''')

host = "src/TerraRuntime/TerrariaServerHost.cs"
replace_once(host,
'''            projectileReplication: projectileReplication,
            npcReplication: npcReplication,
            townNpcs: townNpcStore,
            tileManipulationReplication: tileManipulationReplication,''',
'''            projectileReplication: projectileReplication,
            npcReplication: npcReplication,
            townNpcs: townNpcStore,
            townSpawnWorldFacts: RuntimeTownNpcWorldFactsProjection1458.FromMetadata(world.RuntimeMetadata),
            townInitialRaining: world.RuntimeMetadata.Raining,
            townInitialEclipse: world.RuntimeMetadata.Eclipse,
            townInitialInvasionActive: world.RuntimeMetadata.InvasionType > 0,
            tileManipulationReplication: tileManipulationReplication,''')

housing = "src/TerraRuntime/VanillaHousingValidator1458.cs"
replace_once(housing,
'''    public VanillaHousingValidator1458(WorldTileStore tiles) =>
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public VanillaHousingPlacement Validate(''',
'''    public VanillaHousingValidator1458(WorldTileStore tiles) =>
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    internal static bool IsPotentialRoomAnchorType(int type) =>
        Contains(ChairTypes, type) ||
        Contains(TableTypes, type) ||
        Contains(TorchTypes, type) ||
        Contains(DoorTypes, type);

    private static bool Contains(ReadOnlySpan<int> values, int value)
    {
        foreach (int candidate in values)
        {
            if (candidate == value)
                return true;
        }
        return false;
    }

    public VanillaHousingPlacement Validate(''')

print("town schedule integration applied")
