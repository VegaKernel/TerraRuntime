using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class UndeadLootDeath1458Tests
{
    public static IEnumerable<object[]> OriginalAdmittedDeaths()
    {
        foreach (var data in UndeadLoot1458Tests.Rows("death"))
        {
            using var document = JsonDocument.Parse((string)data[0]);
            var profile = document.RootElement.GetProperty("profile");
            int type = profile.GetProperty("Type").GetInt32();
            if (!VanillaNpcDefinitionCatalog.TryGet(new(type), new(type), out _)) continue;
            // Empty open Void Bag storage has no runtime owner; positive bank4 contents do.
            if (profile.GetProperty("LowTiles").GetBoolean() &&
                profile.GetProperty("SickleCase").GetInt32() == 2) continue;
            yield return data;
        }
    }

    [Theory, MemberData(nameof(OriginalAdmittedDeaths))]
    public void Actual_admitted_actor_death_matches_original_hit_items_money_heals_and_rng(string json) =>
        AssertOriginalDeath(json);

    internal static void AssertOriginalDeath(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var fixture = new Fixture(document.RootElement);
        if (fixture.Interacted)
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed,
                fixture.Pipeline.TryStrikeServerPlayerMelee(fixture.Player, fixture.Npc.Handle, 100_000, 0, false, 0, 1));
        else
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100_000));
        Assert.False(fixture.Npcs.TryGet(fixture.Npc.Handle, out _));
        var actual = new WorldItemSnapshot[fixture.Items.Capacity];
        int count = fixture.Items.CopyActive(actual);
        var expected = document.RootElement.GetProperty("drops");
        Assert.Equal(expected.GetArrayLength(), count);
        for (int index = 0; index < count; index++)
        {
            var item = expected[index]; var state = actual[index];
            Assert.Equal((item.GetProperty("id").GetInt32(), item.GetProperty("stack").GetInt32(),
                item.GetProperty("prefix").GetInt32()),
                ((int)state.ItemNetId, (int)state.Stack, (int)state.Prefix));
            Assert.Equal((item.GetProperty("x").GetSingle(), item.GetProperty("y").GetSingle(),
                item.GetProperty("vx").GetSingle(), item.GetProperty("vy").GetSingle()),
                (state.PositionX, state.PositionY, state.VelocityX, state.VelocityY));
        }
        Assert.Equal(document.RootElement.GetProperty("next").GetInt32(), fixture.Random.Next());
    }

    internal static void AssertCurrentLifeBoundary(string phase)
    {
        string json = (string)OriginalAdmittedDeaths().First(data =>
        {
            using var parsed = JsonDocument.Parse((string)data[0]);
            var row = parsed.RootElement; var profile = row.GetProperty("profile");
            return profile.GetProperty("Type").GetInt32() == 3 && !profile.GetProperty("Statue").GetBoolean() &&
                profile.GetProperty("SickleCase").GetInt32() == 0 &&
                row.GetProperty("drops").EnumerateArray().Any(item => item.GetProperty("id").GetInt32() == 58);
        })[0];
        using var document = JsonDocument.Parse(json);
        using var fixture = new Fixture(document.RootElement, phase);
        var beforeRandom = fixture.Random.Clone();
        var result = fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100_000);
        if (phase != "unknown")
        {
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, result);
            var items = new WorldItemSnapshot[fixture.Items.Capacity];
            int count = fixture.Items.CopyActive(items);
            Assert.Contains(items.Take(count), item => item.ItemNetId == 58);
            Assert.Equal(document.RootElement.GetProperty("next").GetInt32(), fixture.Random.Next());
            return;
        }
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, result);
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var after));
        Assert.Equal(fixture.Npc, after);
        Assert.True(beforeRandom.HasSameState(fixture.Random));
        Assert.Equal(0, fixture.Items.ActiveCount);
    }

    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup, IDisposable
    {
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly NpcSnapshot Npc;
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;
        internal readonly bool Interacted;
        internal PlayerHandle Player => session.Handle;
        private readonly PlayerJoinSession session;
        private readonly PlayerStateSnapshot player;

        internal Fixture(JsonElement row, string phase = "report")
        {
            var context = UndeadLoot1458Tests.Context(row); var profile = row.GetProperty("profile");
            Interacted = profile.TryGetProperty("Interacted", out var interacted) && interacted.GetBoolean();
            Random = new(row.GetProperty("seed").GetInt32());
            // Missing world in the original report-only fixture selects the source outside early return.
            // In-world cases distinguish an owned continuous phase from genuinely missing clock/profile facts.
            var tiles = phase is "owned" or "unknown" ? new WorldTileStore(new WorldDimensions(100, 100)) : null;
            var authority = new PlayerAuthority(null, tiles);
            if (phase == "owned")
            {
                authority.SetNpcHealthWorldFacts(() => new(false, false));
                authority.SetNpcHealthGrapplingFacts(_ => false);
            }
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9191), session.Handle);
            authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
                new(session.Slot, 40, 30, 0, 0, 0, 0, 0)));
            bool injured = row.GetProperty("injured").GetBoolean();
            authority.TryApply(new PlayerHealthRuntimeCommand(connection,
                new(session.Slot, (short)(injured ? 100 : 400), 400)));
            if (phase == "unknown")
            {
                Assert.True(authority.TryGet(connection, out var member));
                member.NpcHealth = new(injured ? 100 : 400, null, null);
            }
            if (phase != "report") authority.TickHealthContext();
            Assert.True(authority.TryCaptureCombatTarget(session.Slot.Value, out var reported));
            player = default(PlayerStateSnapshot) with
            {
                Player = session.Handle, Revision = new(1), PositionX = context.PositionX, PositionY = context.PositionY,
                Luck = row.GetProperty("luck").GetSingle(), Zones = context.Zones,
                HasHealth = true, Life = (short)(injured ? 100 : 400), MaxLife = 400, DerivedLifeMax = 400,
                // The original capture supplies life immediately before the death callback.
                NpcLifeCurrent = reported.NpcLifeCurrent,
                NpcHealth = reported.NpcHealth,
                HasMana = true, Mana = (short)(injured ? 20 : 200), MaxMana = 200
            };
            int bag = profile.GetProperty("SickleCase").GetInt32();
            if (bag == 1) Equipment(0, VanillaNpcSpecificDropItemIds.Sickle);
            if (bag is 2 or 3) Equipment(0, VanillaNpcLootPredicateItemIds1458.OpenVoidBag);
            if (bag == 3) Equipment(VanillaPlayerItemSlotCatalog.Bank4Start, VanillaNpcSpecificDropItemIds.Sickle);
            var simulation = NpcSimulationState.Initial with
            {
                Life = context.NpcLifeMax, LifeMax = context.NpcLifeMax,
                DamageOverride = context.NpcDamage, DefenseOverride = context.NpcDefense,
                Friendly = context.NpcFriendly, MoneyValue = context.NpcValue, ExtraMoneyValue = 0,
                HitboxOverride = new(47, 35), SpawnedFromStatue = profile.GetProperty("Statue").GetBoolean()
            };
            Assert.True(Npcs.TrySpawnVanilla(new(context.NpcType.Value, checked((short)context.NpcType.Value),
                context.PositionX, context.PositionY, 0, 0, checked((ushort)context.NpcTarget), default, simulation), out Npc));
            var world = new RuntimeNpcGlobalLootWorldFacts1458(context.WorldWidth, context.WorldHeight,
                context.Difficulty, context.HardMode, context.RemixWorld, context.Halloween, context.Christmas,
                context.RockLayer, context.WorldSurface, context.SkeletronDowned, context.AnyMechDowned);
            bool downed = context.AnyMechDowned == true;
            bool extraGel = profile.GetProperty("ExtraGel").GetBoolean();
            int mode = profile.GetProperty("Mode").GetInt32();
            Pipeline = new(Npcs, Items, this, authority, static () => 0, null, new(Items), null,
                new RuntimeWorldClock(0, true, 0, 0, 1, getGoodWorld: profile.GetProperty("Good").GetBoolean()),
                new(), mode is 1 or 2, mode == 2,
                lootRandom: Random,
                seasonalItemContext: () => new(context.Halloween == true, context.Christmas == true, extraGel),
                mechanicalLootBaseline: new(0, context.HardMode, downed, downed, downed),
                lootRemixWorld: context.RemixWorld, globalLootWorldSource: () => world,
                npcSpecificLowTiles: () => profile.GetProperty("LowTiles").GetBoolean(),
                requireOwnedPlayerHealth: true, npcSpecificDropExtraGel: extraGel,
                npcSpecificGoodWorld: profile.GetProperty("Good").GetBoolean(), onlyShimmerOceanWorlds: extraGel);

            void Equipment(short slot, ItemTypeId item) => authority.TryApply(new PlayerEquipmentRuntimeCommand(
                connection, new(session.Slot, slot, 1, 0, checked((short)item.Value), 0)));
        }

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = player;
            return slot == session.Slot;
        }

        public void Dispose() => session.Dispose();
    }
}
