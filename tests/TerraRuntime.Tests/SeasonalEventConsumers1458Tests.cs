using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SeasonalEventConsumers1458Tests
{
    [Fact]
    public void Real_NPC_death_pickups_use_live_season_and_expire_loaded_Today_flags_at_dawn()
    {
        foreach (bool christmas in new[] { false, true })
        {
            var clock = Clock(halloween: !christmas, christmas: christmas);
            using var fixture = new DropFixture(clock, savedHalloween: !christmas, savedChristmas: christmas);
            short seasonalHeart = (short)(christmas ? VanillaBossRecoveryItemIds1458.CandyCane.Value :
                VanillaBossRecoveryItemIds1458.CandyApple.Value);
            var first = fixture.KillGuaranteedHeartActor();
            Assert.Contains(first, item => item.ItemNetId == seasonalHeart);
            Assert.DoesNotContain(first, item => item.ItemNetId == 58);
            clock.Tick();
            Assert.True(clock.DayTime);
            Assert.False(clock.HalloweenActive);
            Assert.False(clock.ChristmasActive);
            var second = fixture.KillGuaranteedHeartActor();
            Assert.Contains(second, item => item.ItemNetId == 58);
            Assert.DoesNotContain(second, item => item.ItemNetId is 1734 or 1867);
        }
    }

    [Fact]
    public void Composed_client_NewItem_heart_and_star_substitutions_follow_same_live_clock()
    {
        foreach (bool christmas in new[] { false, true })
        {
            var clock = Clock(halloween: !christmas, christmas: christmas);
            using var fixture = new DropFixture(clock, savedHalloween: !christmas, savedChristmas: christmas);
            Assert.Equal(christmas ? 1867 : 1734, fixture.Create(58).ItemNetId);
            Assert.Equal(christmas ? 1868 : 1735, fixture.Create(184).ItemNetId);
            clock.Tick();
            Assert.Equal(58, fixture.Create(58).ItemNetId);
            Assert.Equal(184, fixture.Create(184).ItemNetId);
        }
    }

    [Fact]
    public void Unknown_scalar_clock_preserves_explicit_seasonal_component_facts()
    {
        var unknown = new RuntimeWorldClock(0, true, 0, 0, 0);
        Assert.False(unknown.HasOwnedSeasonalState);
        using var fixture = new DropFixture(unknown, savedHalloween: true, savedChristmas: false);
        Assert.Equal(1734, fixture.Create(58).ItemNetId);
        Assert.Contains(fixture.KillGuaranteedHeartActor(), item => item.ItemNetId == 1734);
    }

    [Fact]
    public void Reopened_holiday_shop_uses_owned_false_instead_of_stale_loaded_Halloween()
    {
        var tiles = new WorldTileStore(new(500, 300));
        Assert.True(tiles.TryAttachWorldSurface(100));
        var npcs = new RuntimeNpcStore();
        var town = new RuntimeTownNpcStateStore(new([], [
            new WorldTownNpc(19, "Arms Dealer", 1600, 1600, false, 100, 100, null, false)
        ], []), [], tiles.Dimensions);
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        var facts = RuntimeTownCommerceWorldFacts1458.FromMetadata(new()
        { ForceHalloweenForToday = true, WorldSurface = 100, RockLayer = 180, DayTime = true });
        var resolver = new RuntimeTownCommerceResolver1458(tiles, town, npcs, in facts);
        var inventory = new RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        var buyer = new RuntimeTownCommercePlayer1458(1600, 1600, 400, 200, 0);
        var clock = Clock(halloween: true, christmas: false);
        Assert.True(resolver.TryResolve(inventory, in buyer, 0, clock, out var before));
        Assert.Contains(before.Offers, offer => offer.Item.Value == 1736);
        clock.Tick();
        Assert.True(resolver.TryResolve(inventory, in buyer, 0, clock, out var after));
        Assert.DoesNotContain(after.Offers, offer => offer.Item.Value == 1736);
        var unknown = new RuntimeWorldClock(0, true, 0, 0, 0);
        Assert.True(resolver.TryResolve(inventory, in buyer, 0, unknown, out var component));
        Assert.Contains(component.Offers, offer => offer.Item.Value == 1736);
    }

    [Fact]
    public void Composed_Santa_move_in_reads_live_Christmas_and_new_Frost_milestone_without_stale_OR()
    {
        foreach (string profile in new[] { "active", "expired", "unknown", "new-active" })
        {
            var tiles = HousingWorld();
            var validator = new VanillaHousingValidator1458(tiles);
            var guideRoom = validator.Validate(25, 25, VanillaNpcIds.Guide);
            Assert.True(guideRoom.IsValid);
            var town = new RuntimeTownNpcStateStore(new([], [new WorldTownNpc(22, "Andrew", 400, 400,
                false, guideRoom.HomeTileX, guideRoom.HomeTileY, null, false)], []),
                [new(22, guideRoom.HomeTileX, guideRoom.HomeTileY)], tiles.Dimensions);
            var npcs = new RuntimeNpcStore();
            Assert.True(town.TryReserveRuntimeSlots(npcs));
            var metadata = new WorldFileRuntimeMetadata
            { ForceXMasForToday = profile != "new-active", DayTime = true, WorldSurface = 35, RockLayer = 65 };
            var spawnFacts = RuntimeTownNpcWorldFactsProjection1458.FromMetadata(metadata);
            Assert.False(spawnFacts.DownedFrost);
            var commerceFacts = RuntimeTownCommerceWorldFacts1458.FromMetadata(metadata);
            var progression = new RuntimeWorldProgressionMutations();
            Assert.True(progression.MarkCompleted(VanillaWorldProgressionId.FrostLegion));
            var authority = new TownNpcAuthority(new PlayerAuthority(null, tiles), npcs, new(), tiles,
                progression, town, spawnFacts, commerceFacts, null, null, false, false, null, false, false);
            RuntimeWorldClock clock = profile == "unknown" ? new(0, true, 0, 0, 0) :
                Clock(halloween: false, christmas: true, dayTime: profile != "expired");
            if (profile == "expired") clock.Tick();
            for (int tick = 0; tick < 7200; tick++) authority.TickLifecycle(clock);
            Assert.Equal(profile != "expired", town.ContainsNpcType(VanillaNpcIds.SantaClaus));
        }
    }

    private static RuntimeWorldClock Clock(bool halloween, bool christmas, bool dayTime = false) => new(
        dayTime ? 0 : RuntimeWorldClock.NightLength, dayTime, 0, 0, 1,
        forceHalloweenForToday: halloween, forceXMasForToday: christmas,
        forceHalloweenForever: false, forceXMasForever: false,
        seasonDateSource: static () => new DateTime(2026, 2, 2));

    private sealed class DropFixture : IDisposable, IWorldItemStateCommitSink
    {
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly RuntimeWorldItemStore Items;
        internal readonly ServerRuntimeState State;
        internal readonly ServerRuntimeComposition Graph;
        internal readonly ConnectionHandle Connection;
        private readonly PlayerJoinSession session;
        private readonly List<WorldItemSnapshot> drops = [];

        internal DropFixture(RuntimeWorldClock clock, bool savedHalloween, bool savedChristmas)
        {
            var tiles = new WorldTileStore(new(120, 100));
            Assert.True(tiles.TryAttachWorldSurface(35));
            Items = new(this);
            var facts = RuntimeTownCommerceWorldFacts1458.FromMetadata(new()
            { ForceHalloweenForToday = savedHalloween, ForceXMasForToday = savedChristmas,
                WorldSurface = 35, RockLayer = 65, GameMode = 0 }) with { UseLocalSeasonCalendar = false };
            var random = new VanillaUnifiedRandom1458(1458);
            State = new(npcs: Npcs, worldItems: Items, worldTiles: tiles, worldClock: clock,
                townCommerceWorldFacts: facts, naturalSpawnRandom: new SystemVanillaNpcRandom(random),
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random));
            Graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!;
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(14582), session.Handle);
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 50, 25, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 100, 400)));
            State.Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 200, 200)));
            Assert.True(Graph.Players.TryGet(Connection, out var player));
            // Explicit represented neutral combat maximum; this fixture does not execute Player.Update.
            player.DerivedLifeMax = 400;
            player.Zones = new(0, 0, 0, 0, 0, 0);
        }

        internal WorldItemSnapshot Create(short item)
        {
            var completion = new TaskCompletionSource<WorldItemSnapshot?>();
            State.Apply(new WorldItemAllocateRuntimeCommand(Connection,
                new(800, 400, 0, 0, 1, 0, WorldItemOwnershipMode.None, item, false, 0, 0), completion));
            Assert.True(completion.Task.IsCompletedSuccessfully);
            return Assert.IsType<WorldItemSnapshot>(completion.Task.Result);
        }

        internal WorldItemSnapshot[] KillGuaranteedHeartActor()
        {
            drops.Clear();
            // Existing Headless Horseman death owner always offers one healing Heart;
            // no new event boss, loot rule, AI or spawn selector is admitted by this test.
            var spawn = new NpcStateUpdate(315, 315, 800, 400, 0, 0, 0, default, NpcSimulationState.Initial);
            Assert.True(Npcs.TrySpawnVanilla(in spawn, out var npc));
            var pipeline = (RuntimeNpcNetworkCombatPipeline)typeof(NpcAuthority).GetField("combat",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Graph.Npcs)!;
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                pipeline.TryStrikeEnvironment(npc.Handle, 100000));
            return drops.ToArray();
        }

        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item, int? reservationOffer = null, GameCommandSourceId excludedSource = default)
        {
            if (kind == WorldItemStateCommitKind.Drop) drops.Add(item);
        }

        public void Dispose() => session.Dispose();
    }

    private static WorldTileStore HousingWorld()
    {
        var tiles = new WorldTileStore(new(120, 100));
        foreach (int left in new[] { 20, 60 })
        {
            for (int x = left; x <= left + 11; x++)
            for (int y = 20; y <= 29; y++)
            {
                bool edge = x == left || x == left + 11 || y is 20 or 29;
                tiles.Set(x, y, new WorldTile { Type = edge ? (ushort)1 : (ushort)0, Wall = 1,
                    Flags = edge ? WorldTileFlags.Active : WorldTileFlags.None });
            }
            foreach (var item in new[] { (X: 2, Y: 26, Type: 15), (X: 4, Y: 26, Type: 14),
                         (X: 6, Y: 23, Type: 4), (X: 8, Y: 25, Type: 10) })
                tiles.Set(left + item.X, item.Y, new WorldTile { Type = (ushort)item.Type, Wall = 1, Flags = WorldTileFlags.Active });
        }
        return tiles;
    }
}
