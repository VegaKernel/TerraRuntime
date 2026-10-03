using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownCommercePresence1458Tests
{
    public static IEnumerable<object[]> ResidentUnlocks()
    {
        // Chest.SetupShop 1.4.5.8 NPC.AnyNPCs predicates and their dependent items.
        foreach ((int vendor, int resident, int item) in new[] {
            (54, 441, 3242), (124, 369, 2295), (227, 124, 5344),
            (228, 108, 2999), (208, 229, 3369), (229, 208, 1337), (353, 208, 1984) })
        for (int state = 0; state < 6; state++)
            yield return [vendor, resident, item, state, state is 0 or 3];
    }

    [Theory]
    [MemberData(nameof(ResidentUnlocks))]
    public void Vendor_unlocks_follow_active_physical_npc_presence(
        int vendor, int resident, int item, int state, bool expectedPresent)
    {
        var fixture = new Fixture(vendor, resident, state <= 2);
        if (state is 1 or 2)
        {
            Assert.True(fixture.Npcs.TryGetActive(1, out NpcSnapshot old));
            Assert.True(fixture.Npcs.TryDespawn(old.Handle));
            if (state == 2)
                fixture.Spawn(1, 3);
        }
        else if (state is 3 or 5)
        {
            fixture.Spawn(state == 3 ? (byte)4 : (byte)200, resident);
        }

        RuntimeTownShopSession1458 session = fixture.Resolve();
        Assert.Equal(expectedPresent, session.Offers.Any(offer => offer.Item.Value == item));
    }

    [Theory]
    [InlineData(0, .84f)]
    [InlineData(1, .89f)]
    [InlineData(2, .89f)]
    [InlineData(3, .84f)]
    public void Happiness_neighbours_require_a_live_matching_resident(int state, float expectedPrice)
    {
        var fixture = new Fixture(17, 18, true);
        if (state != 0)
        {
            Assert.True(fixture.Npcs.TryGetActive(1, out NpcSnapshot old));
            Assert.True(fixture.Npcs.TryDespawn(old.Handle));
            if (state is 2 or 3)
                fixture.Spawn(1, state == 2 ? 3 : 18);
        }
        // The source counts one active Nurse in the <25-tile house, zero after despawn
        // or replacement by a non-town Zombie, and one after a live Nurse occupies it again.
        Assert.Equal(expectedPrice, fixture.Resolve().PriceAdjustment);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Pylon_population_does_not_count_a_replaced_resident(int state, bool expectedPylon)
    {
        var fixture = new Fixture(17, 18, true);
        if (state != 0)
        {
            Assert.True(fixture.Npcs.TryGetActive(1, out NpcSnapshot old));
            Assert.True(fixture.Npcs.TryDespawn(old.Handle));
            if (state is 2 or 3)
                fixture.Spawn(1, state == 2 ? 3 : 18);
        }
        Assert.Equal(expectedPylon, fixture.Resolve().Offers.Any(offer => offer.Item.Value == 4876));
    }

    [Theory]
    [InlineData(916, 100, 0f, true)]
    [InlineData(915, 100, 0f, false)]
    [InlineData(1084, 100, 0f, true)]
    [InlineData(1085, 100, 0f, false)]
    [InlineData(1000, 38, 0f, true)]
    [InlineData(1000, 37, 0f, false)]
    [InlineData(1000, 161, 0f, true)]
    [InlineData(1000, 162, 0f, false)]
    [InlineData(1004, 100, 99f, true)]
    [InlineData(1004, 100, 100f, false)]
    public void Pylon_population_keeps_source_rectangle_and_strict_home_distance(
        int homeX, int homeY, float distance, bool expectedPylon)
    {
        var fixture = new Fixture(17, 18, true, homeX, homeY);
        Assert.True(fixture.Npcs.TryGetActive(1, out NpcSnapshot old));
        Assert.True(fixture.Npcs.TryDespawn(old.Handle));
        fixture.Spawn(1, 18, (homeX + distance) * 16f - 9f, homeY * 16f - 20f);
        Assert.Equal(expectedPylon, fixture.Resolve().Offers.Any(offer => offer.Item.Value == 4876));
    }

    private sealed class Fixture
    {
        private readonly RuntimeTownCommerceResolver1458 resolver;

        public Fixture(int vendor, int resident, bool retainedResident, int homeX = 1004, int homeY = 100)
        {
            var tiles = new WorldTileStore(new WorldDimensions(2000, 500));
            var persisted = new List<WorldTownNpc> {
                new(vendor, "Vendor", 16000f, 1600f, false, 1000, 100, null, false) };
            if (retainedResident)
                persisted.Add(new(resident, "Resident", homeX * 16f, homeY * 16f,
                    false, homeX, homeY, null, false));
            var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], persisted.ToArray(), []), [], tiles.Dimensions);
            Npcs = new RuntimeNpcStore();
            Assert.True(town.TryReserveRuntimeSlots(Npcs));
            var facts = RuntimeTownCommerceWorldFacts1458.FromMetadata(new WorldFileRuntimeMetadata {
                DayTime = true, Time = 27000, MoonPhase = 1, WorldSurface = 200, RockLayer = 300,
                HardMode = true, DownedMechBossAny = true });
            if (vendor == 227)
            {
                // Painter's Mechanic predicate is restricted to the graveyard branch.
                for (int x = 990; x < 1018; x++)
                    tiles.SetInitialPopulationTile(x, 110, new WorldTile { Type = 85, Flags = WorldTileFlags.Active });
            }
            resolver = new RuntimeTownCommerceResolver1458(tiles, town, Npcs, in facts);
        }

        public RuntimeNpcStore Npcs { get; }

        public void Spawn(byte slot, int type, float x = 16064f, float y = 1600f)
        {
            var update = new NpcStateUpdate(type, checked((short)type), x, y, 0f, 0f,
                255, default, NpcSimulationState.Initial);
            Assert.True(Npcs.TrySpawn(slot, in update, out _));
        }

        public RuntimeTownShopSession1458 Resolve()
        {
            var player = new RuntimeTownCommercePlayer1458(15990f, 1579f, 400, 200, 0);
            var items = new RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
            Assert.True(resolver.TryResolve(items, in player, 0, null, out RuntimeTownShopSession1458 session));
            return session;
        }
    }
}
