from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text()
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one anchor, found {count}: {old[:100]!r}")
    p.write_text(text.replace(old, new, 1))


def replace_between(path: str, start: str, end: str, replacement: str) -> None:
    p = Path(path)
    text = p.read_text()
    i = text.find(start)
    j = text.find(end, i)
    if i < 0 or j < 0:
        raise SystemExit(f"{path}: block anchors not found")
    p.write_text(text[:i] + replacement + text[j:])

# Town spawn facts carry the persisted Truffle unlock.
replace_once(
    'src/TerraRuntime.Core/Npcs/VanillaTownNpcSpawnEligibility1458.cs',
    '''    bool PartyGirlRollSucceeded)\n{\n    public bool IsValid =>''',
    '''    bool PartyGirlRollSucceeded)\n{\n    /// <summary>Persisted TerrariaServer 1.4.5.8 NPC.unlockedTruffleSpawn state.</summary>\n    public bool UnlockedTruffleSpawn { get; init; }\n\n    public bool IsValid =>''')

replace_once(
    'src/TerraRuntime/RuntimeTownNpcWorldFactsProjection1458.cs',
    '''            BestiaryCompletionPercent: 0f,\n            PartyGirlRollSucceeded: false);''',
    '''            BestiaryCompletionPercent: 0f,\n            PartyGirlRollSucceeded: false)\n        {\n            UnlockedTruffleSpawn = metadata.UnlockedTruffleSpawn\n        };''')

# Complete the source-backed Truffle housing gate.
replace_once(
    'src/TerraRuntime/VanillaHousingValidator1458.cs',
    '''    private readonly WorldTileStore tiles;\n\n    public VanillaHousingValidator1458(WorldTileStore tiles) =>\n        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));''',
    '''    private readonly WorldTileStore tiles;\n    private bool truffleUnlocked;\n\n    public VanillaHousingValidator1458(WorldTileStore tiles) =>\n        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));\n\n    internal void SetTruffleUnlocked(bool unlocked) => truffleUnlocked = unlocked;''')

replace_between(
    'src/TerraRuntime/VanillaHousingValidator1458.cs',
    '''    private bool PassesSpecialNpcCondition(\n''',
    '''    private int CalculateBaseRoomScore''',
    '''    private bool PassesSpecialNpcCondition(\n        NpcTypeId npcType,\n        int roomY2,\n        int startX,\n        int endX,\n        int startY,\n        int endY)\n    {\n        if (npcType != VanillaNpcIds.Truffle)\n            return true;\n\n        double worldSurface = tiles.WorldSurfaceTiles ?? Math.Max(1d, tiles.Dimensions.HeightTiles / 3d);\n        if (!truffleUnlocked && roomY2 > worldSurface && worldSurface > 30d)\n            return false;\n\n        int mushroomTiles = 0;\n        for (int x = startX + 1; x < endX; x++)\n        {\n            for (int y = startY + 2; y < endY + 2; y++)\n            {\n                WorldTile tile = tiles.Get(x, y);\n                if (tile.IsActive && tile.Type is 70 or 71 or 72 or 528 && ++mushroomTiles >= 100)\n                    return true;\n            }\n        }\n\n        return false;\n    }\n\n''')

replace_once(
    'src/TerraRuntime/RuntimeTownHouseCandidateIndex1458.cs',
    '''    public int CandidateCount => candidates.Count;\n\n    public void Scan(int tileBudget)''',
    '''    public int CandidateCount => candidates.Count;\n\n    public void SetTruffleUnlocked(bool unlocked) => validator.SetTruffleUnlocked(unlocked);\n\n    public void Scan(int tileBudget)''')

# Progression journal persists NPC.unlockedTruffleSpawn without rewriting unrelated header bytes.
replace_once(
    'src/TerraRuntime.World/RuntimeWorldProgressionMutations.cs',
    '''    public bool UnlockSlimeBlueSpawn { get; init; }\n\n    public bool HasAny => CompletedMask != 0 || UnlockSlimeBlueSpawn;''',
    '''    public bool UnlockSlimeBlueSpawn { get; init; }\n\n    public bool UnlockTruffleSpawn { get; init; }\n\n    public bool HasAny => CompletedMask != 0 || UnlockSlimeBlueSpawn || UnlockTruffleSpawn;''')
replace_once(
    'src/TerraRuntime.World/RuntimeWorldProgressionMutations.cs',
    '''    private bool baselineSlimeBlueSpawnUnlocked;\n    private bool unlockSlimeBlueSpawn;''',
    '''    private bool baselineSlimeBlueSpawnUnlocked;\n    private bool unlockSlimeBlueSpawn;\n    private bool baselineTruffleSpawnUnlocked;\n    private bool unlockTruffleSpawn;''')
replace_once(
    'src/TerraRuntime.World/RuntimeWorldProgressionMutations.cs',
    '''    public RuntimeWorldProgressionMutationSnapshot CaptureSnapshot() =>\n        new(completedMask) { UnlockSlimeBlueSpawn = unlockSlimeBlueSpawn };''',
    '''    public void SetTruffleSpawnBaseline(bool unlocked)\n    {\n        if (unlocked)\n            baselineTruffleSpawnUnlocked = true;\n    }\n\n    public bool IsTruffleSpawnUnlocked => baselineTruffleSpawnUnlocked || unlockTruffleSpawn;\n\n    public bool MarkTruffleSpawnUnlocked()\n    {\n        if (IsTruffleSpawnUnlocked)\n            return false;\n\n        unlockTruffleSpawn = true;\n        return true;\n    }\n\n    public RuntimeWorldProgressionMutationSnapshot CaptureSnapshot() =>\n        new(completedMask)\n        {\n            UnlockSlimeBlueSpawn = unlockSlimeBlueSpawn,\n            UnlockTruffleSpawn = unlockTruffleSpawn\n        };''')

# Extend the lossless header patcher to the exact Truffle bool after the four preceding town unlocks.
replace_once(
    'src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs',
    '''        int slimeBlueUnlockOffset = -1;\n        bool persistedSlimeBlueUnlock = false;\n        if (mutations.UnlockSlimeBlueSpawn &&\n            !TryLocateSlimeBlueSpawnUnlock(ref reader, out slimeBlueUnlockOffset, out persistedSlimeBlueUnlock))\n        {\n            return WorldFileProgressionHeaderPatchResult.InvalidHeader;\n        }\n\n        patchedHeader = sourceHeader.ToArray();\n        if (mutations.IsCompleted(VanillaWorldProgressionId.KingSlime) && !persistedDownedSlimeKing)\n            patchedHeader[downedSlimeKingOffset] = 1;\n        if (mutations.UnlockSlimeBlueSpawn && !persistedSlimeBlueUnlock)\n            patchedHeader[slimeBlueUnlockOffset] = 1;''',
    '''        int slimeBlueUnlockOffset = -1;\n        int truffleUnlockOffset = -1;\n        bool persistedSlimeBlueUnlock = false;\n        bool persistedTruffleUnlock = false;\n        if ((mutations.UnlockSlimeBlueSpawn || mutations.UnlockTruffleSpawn) &&\n            !TryLocateTownSpawnUnlocks(\n                ref reader,\n                out slimeBlueUnlockOffset,\n                out persistedSlimeBlueUnlock,\n                out truffleUnlockOffset,\n                out persistedTruffleUnlock))\n        {\n            return WorldFileProgressionHeaderPatchResult.InvalidHeader;\n        }\n\n        patchedHeader = sourceHeader.ToArray();\n        if (mutations.IsCompleted(VanillaWorldProgressionId.KingSlime) && !persistedDownedSlimeKing)\n            patchedHeader[downedSlimeKingOffset] = 1;\n        if (mutations.UnlockSlimeBlueSpawn && !persistedSlimeBlueUnlock)\n            patchedHeader[slimeBlueUnlockOffset] = 1;\n        if (mutations.UnlockTruffleSpawn && !persistedTruffleUnlock)\n            patchedHeader[truffleUnlockOffset] = 1;''')
replace_once(
    'src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs',
    '''    private static bool TryLocateSlimeBlueSpawnUnlock(\n        ref HeaderPrefixReader reader,\n        out int offset,\n        out bool persisted)\n    {\n        offset = -1;\n        persisted = false;''',
    '''    private static bool TryLocateTownSpawnUnlocks(\n        ref HeaderPrefixReader reader,\n        out int slimeBlueOffset,\n        out bool persistedSlimeBlue,\n        out int truffleOffset,\n        out bool persistedTruffle)\n    {\n        slimeBlueOffset = -1;\n        persistedSlimeBlue = false;\n        truffleOffset = -1;\n        persistedTruffle = false;''')
replace_once(
    'src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs',
    '''        offset = reader.Offset;\n        return reader.TryReadBool(out persisted);\n    }''',
    '''        slimeBlueOffset = reader.Offset;\n        if (!reader.TryReadBool(out persistedSlimeBlue) || !reader.TrySkipBools(4))\n            return false;\n\n        truffleOffset = reader.Offset;\n        return reader.TryReadBool(out persistedTruffle);\n    }''')

# Move-in coordinator updates both in-memory housing semantics and the save mutation journal.
replace_once(
    'src/TerraRuntime/RuntimeTownNpcMoveInCoordinator1458.cs',
    '''    private readonly IRuntimeTownNpcArrivalSink1458? arrivals;''',
    '''    private readonly IRuntimeTownNpcArrivalSink1458? arrivals;\n    private readonly RuntimeWorldProgressionMutations? progression;''')
replace_once(
    'src/TerraRuntime/RuntimeTownNpcMoveInCoordinator1458.cs',
    '''        RuntimeNpcReplicationRegistry? replication = null,\n        IRuntimeTownNpcArrivalSink1458? arrivals = null)''',
    '''        RuntimeNpcReplicationRegistry? replication = null,\n        IRuntimeTownNpcArrivalSink1458? arrivals = null,\n        RuntimeWorldProgressionMutations? progression = null)''')
replace_once(
    'src/TerraRuntime/RuntimeTownNpcMoveInCoordinator1458.cs',
    '''        this.houses = houses;\n        this.worldFacts = worldFacts;\n        this.replication = replication;\n        this.arrivals = arrivals;''',
    '''        this.houses = houses;\n        houses.SetTruffleUnlocked(worldFacts.UnlockedTruffleSpawn || townNpcs.ContainsNpcType(VanillaNpcIds.Truffle));\n        this.worldFacts = worldFacts;\n        this.replication = replication;\n        this.arrivals = arrivals;\n        this.progression = progression;\n        progression?.SetTruffleSpawnBaseline(worldFacts.UnlockedTruffleSpawn);''')
replace_once(
    'src/TerraRuntime/RuntimeTownNpcMoveInCoordinator1458.cs',
    '''            replication?.TryPublishTownHome(in home);\n            var arrival = new RuntimeTownNpcArrival1458(''',
    '''            if (type == VanillaNpcIds.Truffle)\n            {\n                houses.SetTruffleUnlocked(true);\n                progression?.MarkTruffleSpawnUnlocked();\n            }\n\n            replication?.TryPublishTownHome(in home);\n            var arrival = new RuntimeTownNpcArrival1458(''')

# Authoritative packet-40 path now mirrors SetTalkNPC -> shopping settings + shop resolution.
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''    private readonly short[] _playerTalkNpcSlots = new short[MaxPlayerSlots];\n    private readonly RuntimePlayerInventoryStore _playerInventory = new();''',
    '''    private readonly short[] _playerTalkNpcSlots = new short[MaxPlayerSlots];\n    private readonly RuntimeTownShopSession1458?[] _townShopSessions = new RuntimeTownShopSession1458?[MaxPlayerSlots];\n    private readonly RuntimePlayerInventoryStore _playerInventory = new();''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''    private readonly RuntimeTownNpcStateStore? _townNpcs;\n    private readonly VanillaHousingValidator1458? _housingValidator;''',
    '''    private readonly RuntimeTownNpcStateStore? _townNpcs;\n    private readonly RuntimeTownCommerceResolver1458? _townCommerce;\n    private readonly VanillaHousingValidator1458? _housingValidator;''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        RuntimeTownNpcStateStore? townNpcs = null,\n        VanillaTownSpawnWorldFacts1458? townSpawnWorldFacts = null,\n        bool townInitialRaining = false,''',
    '''        RuntimeTownNpcStateStore? townNpcs = null,\n        VanillaTownSpawnWorldFacts1458? townSpawnWorldFacts = null,\n        RuntimeTownCommerceWorldFacts1458? townCommerceWorldFacts = null,\n        bool townInitialRaining = false,''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        _townNpcs = townNpcs;\n        _housingValidator = worldTiles is not null && townNpcs is not null''',
    '''        _townNpcs = townNpcs;\n        _townCommerce = worldTiles is not null && townCommerceWorldFacts is RuntimeTownCommerceWorldFacts1458 commerceFacts\n            ? new RuntimeTownCommerceResolver1458(worldTiles, townNpcs, _npcs, in commerceFacts)\n            : null;\n        _housingValidator = worldTiles is not null && townNpcs is not null''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''                _townMoveIn = new RuntimeTownNpcMoveInCoordinator1458(\n                    townNpcs, _npcs, houseIndex, in facts, npcReplication);''',
    '''                RuntimeWorldProgressionMutations progression = RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles);\n                progression.SetTruffleSpawnBaseline(facts.UnlockedTruffleSpawn);\n                _townMoveIn = new RuntimeTownNpcMoveInCoordinator1458(\n                    townNpcs, _npcs, houseIndex, in facts, npcReplication, progression: progression);''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''    internal RuntimeNpcShopCatalogRegistry NpcShops => _npcShops;\n\n    internal RuntimeNpcArchetypeRegistry NpcArchetypes => _npcArchetypes;''',
    '''    internal RuntimeNpcShopCatalogRegistry NpcShops => _npcShops;\n\n    internal bool TryGetPlayerTownShopSession(PlayerHandle player, out RuntimeTownShopSession1458? session)\n    {\n        if (!player.IsAssigned ||\n            !_players.TryGetValue(player.Slot.Value, out RuntimePlayerState? state) ||\n            state.Connection.Player != player ||\n            _townShopSessions[player.Slot.Value] is not RuntimeTownShopSession1458 current)\n        {\n            session = null;\n            return false;\n        }\n\n        session = current;\n        return true;\n    }\n\n    internal RuntimeNpcArchetypeRegistry NpcArchetypes => _npcArchetypes;''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        _playerTalkNpcSlots[command.Connection.Player.Slot.Value] = command.State.NpcSlot;\n        _npcReplication?.TryPublishNpcTalk(command.Connection, command.State.NpcSlot);''',
    '''        byte playerSlot = command.Connection.Player.Slot.Value;\n        _playerTalkNpcSlots[playerSlot] = command.State.NpcSlot;\n        _townShopSessions[playerSlot] = null;\n        if (command.State.NpcSlot != TerrariaNpcTalkCodec.NoNpc &&\n            _townCommerce is not null &&\n            _players.TryGetValue(playerSlot, out RuntimePlayerState? playerState))\n        {\n            var commercePlayer = new RuntimeTownCommercePlayer1458(\n                playerState.PositionX,\n                playerState.PositionY,\n                playerState.HasHealth ? playerState.MaxLife : 100,\n                playerState.HasMana ? playerState.MaxMana : 20,\n                playerState.Team);\n            if (_townCommerce.TryResolve(\n                    command.Connection,\n                    _playerInventory,\n                    in commercePlayer,\n                    command.State.NpcSlot,\n                    _worldClock,\n                    out RuntimeTownShopSession1458 session))\n            {\n                _townShopSessions[playerSlot] = session;\n            }\n        }\n\n        _npcReplication?.TryPublishNpcTalk(command.Connection, command.State.NpcSlot);''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        _playerTalkNpcSlots[connection.Player.Slot.Value] = TerrariaNpcTalkCodec.NoNpc;\n        _players.Remove(connection.Player.Slot.Value);''',
    '''        _playerTalkNpcSlots[connection.Player.Slot.Value] = TerrariaNpcTalkCodec.NoNpc;\n        _townShopSessions[connection.Player.Slot.Value] = null;\n        _players.Remove(connection.Player.Slot.Value);''')

replace_once(
    'src/TerraRuntime/TerrariaServerHost.cs',
    '''            townSpawnWorldFacts: RuntimeTownNpcWorldFactsProjection1458.FromMetadata(world.RuntimeMetadata),\n            townInitialRaining:''',
    '''            townSpawnWorldFacts: RuntimeTownNpcWorldFactsProjection1458.FromMetadata(world.RuntimeMetadata),\n            townCommerceWorldFacts: RuntimeTownCommerceWorldFacts1458.FromMetadata(world.RuntimeMetadata),\n            townInitialRaining:''')

# Bilingual documentation stops claiming the source-backed Truffle gate is missing.
for path, old, new in [
    ('docs/en/town-npc-housing-shops.md',
     'Truffle assignment currently fails closed because its complete mushroom-scene/unlock condition is not yet runtime-owned.',
     'Truffle assignment follows the pinned 1.4.5.8 gate: a first move-in requires a functional surface room unless `Main.NoFunctionalSurface`, every accepted room needs at least 100 active mushroom tiles (`70`, `71`, `72`, `528`) inside the source-tested bounds, and the successful unlock is journaled into the lossless `.wld` header patch path.'),
    ('docs/ru/town-npc-housing-shops.md',
     'Назначение Truffle сейчас fail-closed, потому что полное условие mushroom scene/unlock ещё не принадлежит runtime.',
     'Назначение Truffle следует pinned-условию 1.4.5.8: до первого вселения нужна функциональная surface-комната, если только не действует `Main.NoFunctionalSurface`; в source-tested bounds требуется минимум 100 активных mushroom tiles (`70`, `71`, `72`, `528`), а успешный unlock журналируется в lossless `.wld` header patch path.')
]:
    p = Path(path)
    text = p.read_text()
    if old not in text:
        # Russian wording has changed a few times; append a precise replacement note rather than silently skipping.
        if path.endswith('/ru/town-npc-housing-shops.md'):
            text += '\n\nTruffle housing 1.4.5.8: до первого вселения нужна surface-комната (кроме `Main.NoFunctionalSurface`), минимум 100 mushroom tiles `70/71/72/528`; unlock сохраняется в `.wld`.\n'
            p.write_text(text)
            continue
        raise SystemExit(f'{path}: documentation anchor drifted')
    p.write_text(text.replace(old, new, 1))

for path, addition in [
    ('docs/en/town-npc-housing-shops.md', '''\n\n### Authoritative talk-to-shop mirror\n\nPacket 40 now mirrors the server side of `Player.SetTalkNPC`: after authenticating the player slot, the authoritative game thread resolves the live NPC, snapshots packet-5 inventory/vitals/team state, scans the pinned `169x124` SceneMetrics window around the player, computes source-shaped housing crowding and numeric happiness, and resolves the ordinary `Chest.SetupShop` catalog or supported special shop into an immutable per-player session. Closing the conversation clears the session, and disconnect cannot leak it across a reused player generation.\n\nThe mirror is deliberately honest about still-unowned inputs. `LoveStruck`, live wind/weather, Golfer score, full Bestiary/Fairy Torch state, Artisan Bread and Traveling Merchant `travelShop` data are represented as explicit missing-fact flags rather than fabricated defaults being advertised as parity.\n'''),
    ('docs/ru/town-npc-housing-shops.md', '''\n\n### Authoritative talk-to-shop mirror\n\nPacket 40 теперь повторяет серверную часть `Player.SetTalkNPC`: после проверки authenticated player slot authoritative game thread разрешает live NPC, снимает packet-5 inventory/vitals/team state, сканирует pinned `169x124` SceneMetrics вокруг игрока, считает source-shaped housing crowding и числовой happiness, затем собирает обычный `Chest.SetupShop` или поддержанный special shop в immutable per-player session. Закрытие разговора очищает session, disconnect не даёт ей протечь в переиспользованный player generation.\n\nНе принадлежащие runtime факты не подменяются выдумками: `LoveStruck`, live wind/weather, Golfer score, полный Bestiary/Fairy Torch state, Artisan Bread и Traveling Merchant `travelShop` отмечаются явными missing-fact flags.\n''')
]:
    p = Path(path)
    text = p.read_text()
    if '### Authoritative talk-to-shop mirror' not in text:
        p.write_text(text.rstrip() + addition)

Path('tests/TerraRuntime.Tests/VanillaTruffleHousing1458Tests.cs').write_text(r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTruffleHousing1458Tests
{
    [Fact]
    public void Source_mushroom_threshold_is_required_for_truffle_room()
    {
        WorldTileStore enough = CreateRoom(top: 40, mushroomTiles: 100, worldSurface: 80d);
        VanillaHousingPlacement accepted = new VanillaHousingValidator1458(enough).Validate(25, 45, VanillaNpcIds.Truffle);
        Assert.NotEqual(VanillaHousingValidationResult.SpecialNpcConditionFailed, accepted.Result);

        WorldTileStore shortByOne = CreateRoom(top: 40, mushroomTiles: 99, worldSurface: 80d);
        Assert.Equal(
            VanillaHousingValidationResult.SpecialNpcConditionFailed,
            new VanillaHousingValidator1458(shortByOne).Validate(25, 45, VanillaNpcIds.Truffle).Result);
    }

    [Fact]
    public void Persisted_unlock_allows_below_surface_room_but_keeps_mushroom_gate()
    {
        WorldTileStore tiles = CreateRoom(top: 100, mushroomTiles: 100, worldSurface: 80d);
        var locked = new VanillaHousingValidator1458(tiles);
        Assert.Equal(
            VanillaHousingValidationResult.SpecialNpcConditionFailed,
            locked.Validate(25, 105, VanillaNpcIds.Truffle).Result);

        var unlocked = new VanillaHousingValidator1458(tiles);
        unlocked.SetTruffleUnlocked(true);
        Assert.NotEqual(
            VanillaHousingValidationResult.SpecialNpcConditionFailed,
            unlocked.Validate(25, 105, VanillaNpcIds.Truffle).Result);
    }

    [Fact]
    public void No_functional_surface_matches_source_exception()
    {
        WorldTileStore tiles = CreateRoom(top: 70, mushroomTiles: 100, worldSurface: 30d);
        Assert.NotEqual(
            VanillaHousingValidationResult.SpecialNpcConditionFailed,
            new VanillaHousingValidator1458(tiles).Validate(25, 75, VanillaNpcIds.Truffle).Result);
    }

    [Fact]
    public void World_projection_and_progression_journal_keep_truffle_unlock_explicit()
    {
        var metadata = new WorldFileRuntimeMetadata { UnlockedTruffleSpawn = true };
        VanillaTownSpawnWorldFacts1458 facts = RuntimeTownNpcWorldFactsProjection1458.FromMetadata(metadata);
        Assert.True(facts.UnlockedTruffleSpawn);

        var mutations = new RuntimeWorldProgressionMutations();
        mutations.SetTruffleSpawnBaseline(false);
        Assert.True(mutations.MarkTruffleSpawnUnlocked());
        RuntimeWorldProgressionMutationSnapshot snapshot = mutations.CaptureSnapshot();
        Assert.True(snapshot.UnlockTruffleSpawn);
        Assert.True(snapshot.HasAny);
    }

    private static WorldTileStore CreateRoom(int top, int mushroomTiles, double worldSurface)
    {
        var tiles = new WorldTileStore(new WorldDimensions(160, 160));
        Assert.True(tiles.TryAttachWorldSurface(worldSurface));
        const int left = 20;
        const int right = 31;
        int bottom = top + 9;
        for (int x = left; x <= right; x++)
        for (int y = top; y <= bottom; y++)
        {
            bool boundary = x == left || x == right || y == top || y == bottom;
            tiles.Set(x, y, new WorldTile
            {
                Type = boundary ? (ushort)1 : (ushort)0,
                Wall = 1,
                Flags = boundary ? WorldTileFlags.Active : WorldTileFlags.None
            });
        }
        Place(tiles, 22, top + 6, 15);
        Place(tiles, 24, top + 6, 14);
        Place(tiles, 26, top + 3, 4);
        Place(tiles, 28, top + 5, 10);

        int written = 0;
        for (int x = 40; x < 50 && written < mushroomTiles; x++)
        for (int y = top; y < top + 10 && written < mushroomTiles; y++)
        {
            Place(tiles, x, y, (ushort)(written % 4 switch { 0 => 70, 1 => 71, 2 => 72, _ => 528 }));
            written++;
        }
        Assert.Equal(mushroomTiles, written);
        return tiles;
    }

    private static void Place(WorldTileStore tiles, int x, int y, ushort type) =>
        tiles.Set(x, y, new WorldTile { Type = type, Wall = 1, Flags = WorldTileFlags.Active });
}
''')

print('town commerce + Truffle housing integration applied')
