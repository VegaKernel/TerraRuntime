using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GlobalNpcLootDeath1458Tests
{
    public static IEnumerable<object[]> OriginalDeaths() => GlobalNpcLoot1458Tests.Rows("whole");

    [Theory, MemberData(nameof(OriginalDeaths))]
    public void Actual_lethal_commit_matches_original_global_specific_money_healing_and_next_rng(string json)
    {
        using var document = JsonDocument.Parse(json);
        var fixture = new Fixture(document.RootElement);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100_000));
        var items = new WorldItemSnapshot[fixture.Items.Capacity];
        int count = fixture.Items.CopyActive(items);
        var expected = document.RootElement.GetProperty("drops");
        Assert.Equal(expected.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            var original = expected[i]; var actual = items[i];
            Assert.Equal((original.GetProperty("id").GetInt32(), original.GetProperty("stack").GetInt32(), original.GetProperty("prefix").GetInt32()),
                ((int)actual.ItemNetId, (int)actual.Stack, (int)actual.Prefix));
            Assert.Equal((original.GetProperty("x").GetSingle(), original.GetProperty("y").GetSingle(),
                original.GetProperty("vx").GetSingle(), original.GetProperty("vy").GetSingle()),
                (actual.PositionX, actual.PositionY, actual.VelocityX, actual.VelocityY));
        }
        Assert.Equal(document.RootElement.GetProperty("next").GetInt32(), fixture.Random.Next());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Context_changed_during_preview_rejects_without_damage_drops_or_live_rng(bool worldChange)
    {
        string json = (string)OriginalDeaths().First()[0];
        using var document = JsonDocument.Parse(json);
        var fixture = new Fixture(document.RootElement);
        var before = fixture.Random.Clone();
        fixture.ChangeOnRead = worldChange ? 1 : 2;
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100_000));
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var retained));
        Assert.Equal(fixture.Npc, retained);
        Assert.True(before.HasSameState(fixture.Random));
        Assert.Equal(0, fixture.Items.ActiveCount);
    }

    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup
    {
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly NpcSnapshot Npc;
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;
        internal int ChangeOnRead;
        private int reads;
        private PlayerStateSnapshot player;
        private RuntimeNpcGlobalLootWorldFacts1458 world;

        internal Fixture(JsonElement row)
        {
            var context = GlobalNpcLoot1458Tests.Context(row); var p = row.GetProperty("profile");
            Random = new(row.GetProperty("seed").GetInt32());
            bool injured = row.GetProperty("injured").GetBoolean();
            player = default(PlayerStateSnapshot) with
            {
                Player = new(new(0), new(1)), Revision = new(1), PositionX = context.PositionX, PositionY = context.PositionY,
                Luck = row.GetProperty("luck").GetSingle(), Zones = context.Zones,
                HasHealth = true, Life = (short)(injured ? 100 : 400), MaxLife = 400, DerivedLifeMax = 400,
                HasMana = true, Mana = (short)(injured ? 20 : 200), MaxMana = 200
            };
            world = new(context.WorldWidth, context.WorldHeight, context.Difficulty, context.HardMode, context.RemixWorld,
                context.Halloween, context.Christmas, context.RockLayer, context.WorldSurface, context.SkeletronDowned, context.AnyMechDowned);
            var state = NpcSimulationState.Initial with
            {
                Life = context.NpcLifeMax, LifeMax = context.NpcLifeMax,
                DamageOverride = context.NpcDamage, DefenseOverride = context.NpcDefense,
                Friendly = context.NpcFriendly, MoneyValue = context.NpcValue, ExtraMoneyValue = 0,
                HitboxOverride = new(47, 35), SpawnedFromStatue = p.GetProperty("Statue").GetBoolean()
            };
            Assert.True(Npcs.TrySpawnVanilla(new(context.NpcType.Value, checked((short)context.NpcType.Value),
                context.PositionX, context.PositionY, 0, 0, checked((ushort)context.NpcTarget),
                new(0, p.GetProperty("Held").GetSingle(), 0, 0), state), out Npc));
            bool expert = context.Difficulty >= 2f, master = context.Difficulty >= 3f;
            bool downed = context.AnyMechDowned == true;
            Pipeline = new(Npcs, Items, this, new PlayerAuthority(null, null), static () => 0, null,
                new(Items), null, new RuntimeWorldClock(0, true, 0, 0, 1), new(), expert, master,
                lootRandom: Random,
                seasonalItemContext: () => new(context.Halloween == true, context.Christmas == true, false),
                mechanicalLootBaseline: new(0, context.HardMode, downed, downed, downed), lootRemixWorld: context.RemixWorld,
                globalLootWorldSource: () => world, requireOwnedPlayerHealth: true);
        }

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            if (slot.Value == 0 && ++reads == 2)
            {
                if (ChangeOnRead == 1) world = world with { Halloween = !world.Halloween };
                if (ChangeOnRead == 2) player = player with { Revision = new(2), Zones = default(PlayerZoneSnapshot1458) };
            }
            snapshot = player;
            return slot.Value == 0;
        }

    }
}
