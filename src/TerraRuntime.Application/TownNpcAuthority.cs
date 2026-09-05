using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Owns the world-scoped town-NPC lifecycle and interaction services. All methods are invoked by the authoritative
/// world loop; extracting this owner does not introduce a second writer for NPC, player, housing or progression state.
/// </summary>
internal sealed class TownNpcAuthority
{
    private const int MaxPlayerSlots = byte.MaxValue + 1;

    private readonly PlayerAuthority players;
    private readonly RuntimeTownNpcStateStore? townNpcs;
    private readonly RuntimeTownNpcRescueService1458? rescue;
    private readonly RuntimePurificationPowderNpcInteraction1458? purificationPowderInteractions;
    private readonly RuntimeTownCommerceResolver1458? commerce;
    private readonly VanillaHousingValidator1458? housingValidator;
    private readonly RuntimeTownNpcMoveInCoordinator1458? moveIn;
    private readonly RuntimeTownNpcSchedule1458? schedule;
    private readonly RuntimeTownNpcCombat1458? combat;
    private readonly RuntimeTownNpcShimmerService1458? shimmer;
    private readonly RuntimeNpcReplicationRegistry? npcReplication;
    private readonly VanillaTownSpawnPlayerFacts1458[] spawnPlayers = new VanillaTownSpawnPlayerFacts1458[MaxPlayerSlots];
    private readonly RuntimeTownPlayerBounds1458[] playerBounds = new RuntimeTownPlayerBounds1458[MaxPlayerSlots];
    private readonly bool initialRaining;
    private readonly bool initialEclipse;
    private readonly bool initialInvasionActive;

    public TownNpcAuthority(
        PlayerAuthority players,
        RuntimeNpcStore npcs,
        RuntimeProjectileStore projectiles,
        WorldTileStore? worldTiles,
        RuntimeWorldProgressionMutations progression,
        RuntimeTownNpcStateStore? townNpcs,
        VanillaTownSpawnWorldFacts1458? townSpawnWorldFacts,
        RuntimeTownCommerceWorldFacts1458? townCommerceWorldFacts,
        RuntimeTownNpcCombatWorldFacts1458? townCombatWorldFacts,
        RuntimeNpcReplicationRegistry? npcReplication,
        bool initialRaining,
        bool initialEclipse,
        bool initialInvasionActive,
        bool expertMode,
        bool masterMode)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        ArgumentNullException.ThrowIfNull(npcs);
        ArgumentNullException.ThrowIfNull(projectiles);
        this.townNpcs = townNpcs;
        this.npcReplication = npcReplication;
        this.initialRaining = initialRaining;
        this.initialEclipse = initialEclipse;
        this.initialInvasionActive = initialInvasionActive;
        ArgumentNullException.ThrowIfNull(progression);

        rescue = townNpcs is not null && worldTiles is not null
            ? new RuntimeTownNpcRescueService1458(npcs, townNpcs, progression)
            : null;
        purificationPowderInteractions = townNpcs is not null && worldTiles is not null && rescue is not null
            ? new RuntimePurificationPowderNpcInteraction1458(
                npcs,
                projectiles,
                townNpcs,
                rescue,
                progression,
                townSpawnWorldFacts?.InfectedSeed ?? false)
            : null;
        commerce = worldTiles is not null && townCommerceWorldFacts is RuntimeTownCommerceWorldFacts1458 commerceFacts
            ? new RuntimeTownCommerceResolver1458(worldTiles, townNpcs, npcs, in commerceFacts)
            : null;
        combat = worldTiles is not null &&
            townNpcs is not null &&
            townCombatWorldFacts is RuntimeTownNpcCombatWorldFacts1458 combatFacts
                ? new RuntimeTownNpcCombat1458(
                    townNpcs,
                    npcs,
                    projectiles,
                    worldTiles,
                    in combatFacts,
                    progression,
                    expertMode,
                    masterMode)
                : null;
        housingValidator = worldTiles is not null && townNpcs is not null
            ? new VanillaHousingValidator1458(worldTiles)
            : null;

        if (worldTiles is null || townNpcs is null || housingValidator is null)
            return;

        schedule = new RuntimeTownNpcSchedule1458(townNpcs, npcs, worldTiles);
        shimmer = new RuntimeTownNpcShimmerService1458(npcs, townNpcs, worldTiles, npcReplication);
        if (townSpawnWorldFacts is not VanillaTownSpawnWorldFacts1458 facts)
            return;

        var houseIndex = new RuntimeTownHouseCandidateIndex1458(worldTiles, housingValidator);
        RuntimeWorldProgressionMutations configuredProgression = progression;
        configuredProgression.SetTruffleSpawnBaseline(facts.UnlockedTruffleSpawn);
        configuredProgression.SetSlimeYellowSpawnBaseline(facts.UnlockedSlimeYellowSpawn);

        RuntimeTownRescueFacts1458 rescuedBaseline = RuntimeTownRescueFacts1458.None;
        if (facts.SavedGoblin) rescuedBaseline |= RuntimeTownRescueFacts1458.Goblin;
        if (facts.SavedWizard) rescuedBaseline |= RuntimeTownRescueFacts1458.Wizard;
        if (facts.SavedMechanic) rescuedBaseline |= RuntimeTownRescueFacts1458.Mechanic;
        if (facts.SavedStylist) rescuedBaseline |= RuntimeTownRescueFacts1458.Stylist;
        if (facts.SavedAngler) rescuedBaseline |= RuntimeTownRescueFacts1458.Angler;
        if (facts.SavedBartender) rescuedBaseline |= RuntimeTownRescueFacts1458.Bartender;
        if (facts.SavedGolfer) rescuedBaseline |= RuntimeTownRescueFacts1458.Golfer;
        if (facts.SavedTaxCollector) rescuedBaseline |= RuntimeTownRescueFacts1458.TaxCollector;
        configuredProgression.SetTownRescueBaseline(rescuedBaseline);

        moveIn = new RuntimeTownNpcMoveInCoordinator1458(
            townNpcs,
            npcs,
            houseIndex,
            in facts,
            npcReplication,
            progression: configuredProgression);
    }

    public void SetMeleeDamageSink(IRuntimeTownNpcMeleeDamageSink1458 sink) => combat?.SetMeleeDamageSink(sink);

    public void TickShimmer() => shimmer?.Tick();

    public void TickProjectileInteractions() => purificationPowderInteractions?.Tick();

    public void TickLifecycle(RuntimeWorldClock? worldClock)
    {
        if (moveIn is null && schedule is null && combat is null)
            return;

        int spawnPlayerCount = 0;
        int boundsCount = 0;
        Span<RuntimePlayerInventoryItem> inventory =
            stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        foreach (RuntimePlayerMember player in players.Members)
        {
            long coinValue = 0;
            bool bullet = false;
            bool bomb = false;
            bool dye = false;
            inventory.Clear();
            if (players.TryCopyInventory(player.Connection, inventory))
            {
                foreach (RuntimePlayerInventoryItem item in inventory)
                {
                    if (item.IsEmpty)
                        continue;

                    coinValue = Math.Min(
                        5_000L,
                        coinValue + VanillaTownNpcSpawnItemFacts1458.GetCoinValue(item.ItemType, item.Stack));
                    bullet |= VanillaTownNpcSpawnItemFacts1458.CountsForArmsDealer(item.ItemType);
                    bomb |= VanillaTownNpcSpawnItemFacts1458.CountsForDemolitionist(item.ItemType);
                    dye |= VanillaTownNpcSpawnItemFacts1458.CountsForDyeTrader(item.ItemType);
                }
            }

            spawnPlayers[spawnPlayerCount++] = new VanillaTownSpawnPlayerFacts1458(
                Active: true,
                MaxLife: player.HasHealth ? player.MaxLife : (short)100,
                CoinValue: coinValue,
                HasBulletAmmoOrWeapon: bullet,
                HasDemolitionistBomb: bomb,
                HasDyeTraderItem: dye);
            playerBounds[boundsCount++] = new RuntimeTownPlayerBounds1458(
                player.PositionX,
                player.PositionY,
                PlayerAuthority.VanillaBasePlayerWidth,
                PlayerAuthority.VanillaBasePlayerHeight);
        }

        if (moveIn is not null)
        {
            var moveInConditions = new RuntimeTownNpcMoveInConditions1458(
                DayTime: worldClock?.DayTime ?? true,
                Eclipse: initialEclipse,
                InvasionActive: initialInvasionActive,
                WorldUpdateRate: 1);
            moveIn.Tick(
                in moveInConditions,
                spawnPlayers.AsSpan(0, spawnPlayerCount),
                playerBounds.AsSpan(0, boundsCount));
        }

        if (schedule is not null)
        {
            var scheduleConditions = new RuntimeTownNpcScheduleConditions1458(
                DayTime: worldClock?.DayTime ?? true,
                Raining: initialRaining,
                Eclipse: initialEclipse,
                SlimeRain: worldClock?.SlimeRainActive ?? false,
                StormingAboveSurface: false);
            schedule.Tick(in scheduleConditions, playerBounds.AsSpan(0, boundsCount));
        }

        combat?.Tick();
    }

    public void ApplyTalk(ConnectionHandle connection, short npcSlot, RuntimeWorldClock? worldClock)
    {
        if (!players.IsCurrent(connection) || !TerrariaNpcTalkCodec.IsValidNpcSlot(npcSlot))
            return;

        byte playerSlot = connection.Player.Slot.Value;
        if (npcSlot != TerrariaNpcTalkCodec.NoNpc)
            rescue?.TryRescueTalk(npcSlot, out _);
        if (!players.TrySetTalkNpc(connection, npcSlot))
            return;

        if (npcSlot != TerrariaNpcTalkCodec.NoNpc &&
            commerce is not null &&
            players.TryGet(playerSlot, out RuntimePlayerMember? playerState))
        {
            Span<RuntimePlayerInventoryItem> inventory =
                stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
            var commercePlayer = new RuntimeTownCommercePlayer1458(
                playerState.PositionX,
                playerState.PositionY,
                playerState.HasHealth ? playerState.MaxLife : 100,
                playerState.HasMana ? playerState.MaxMana : 20,
                playerState.Team);
            if (players.TryCopyInventory(connection, inventory) &&
                commerce.TryResolve(
                    inventory,
                    in commercePlayer,
                    npcSlot,
                    worldClock,
                    out RuntimeTownShopSession1458 session))
            {
                players.TrySetTownShopSession(connection, session);
            }
        }

        npcReplication?.TryPublishNpcTalk(connection, npcSlot);
    }

    public void ApplyHome(ConnectionHandle connection, in TerrariaNpcHomeState state)
    {
        if (!players.IsCurrent(connection) ||
            townNpcs is null ||
            housingValidator is null ||
            !state.TryGetStatus(out TerrariaNpcHomeStatus status))
        {
            return;
        }

        RuntimeTownNpcHomeCommit commit = default;
        bool applied = status switch
        {
            TerrariaNpcHomeStatus.Homeless => townNpcs.TryKickOut(state.NpcSlot, out commit),
            TerrariaNpcHomeStatus.None => townNpcs.TryAssignRoom(
                state.NpcSlot,
                state.HomeTileX,
                state.HomeTileY,
                housingValidator,
                out commit,
                out _),
            // Status 2 is server-authored GetHouseholdStatus state, not a client room-move request.
            TerrariaNpcHomeStatus.HasRoom => false,
            _ => false
        };

        if (applied)
            npcReplication?.TryPublishTownHome(in commit);
    }
}
