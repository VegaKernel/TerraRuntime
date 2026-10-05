using TerraRuntime.Core.Worlds;
using TerraRuntime.Core.Players;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Npcs;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SlimerDeath1458Tests
{
    private static IEnumerable<object[]> Rows()
    {
        using var stream = typeof(SlimerDeath1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.slimer-tenth-coupled-official.json.gz")!;
        using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }
    public static IEnumerable<object[]> OriginalDeaths() => Rows();

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
        Assert.False(fixture.Npcs.TryGet(fixture.Npc.Handle, out _));
        Assert.Equal(1, fixture.Npcs.ActiveCount);
        Assert.True(fixture.Npcs.TryGetActive(1, out var child));
        Assert.Equal((81, -2), (child.Type, (int)child.NetId));
        var randomBeforeDuplicate = fixture.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100_000));
        Assert.True(randomBeforeDuplicate.HasSameState(fixture.Random));
        Assert.Equal(1, fixture.Npcs.ActiveCount); Assert.Equal(count, fixture.Items.ActiveCount);
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

    [Fact]
    public void Peer_pool_changed_during_admission_rejects_without_parent_damage_child_items_or_random()
    {
        using var document = JsonDocument.Parse((string)OriginalDeaths().First()[0]);
        var fixture = new Fixture(document.RootElement) { ChangeOnRead = 3 };
        var before = fixture.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100_000));
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var retained)); Assert.Equal(fixture.Npc, retained);
        Assert.Equal(2, fixture.Npcs.ActiveCount); Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.True(before.HasSameState(fixture.Random));
        Assert.True(fixture.Npcs.TryGetActive(4, out var peer)); Assert.Equal(3, peer.Type);
    }

    [Fact]
    public void GoodWorld_unshared_spawn_random_rejects_before_lethal_mutation_or_any_draw()
    {
        string json = (string)OriginalDeaths().First(r => r[0].ToString()!.Contains("True"))[0];
        using var document = JsonDocument.Parse(json); var fixture = new Fixture(document.RootElement);
        var separate = new VanillaUnifiedRandom1458(0); var beforeSeparate = separate.Clone();
        fixture.Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(separate));
        var before = fixture.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100_000));
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var retained)); Assert.Equal(fixture.Npc, retained);
        Assert.Equal(1, fixture.Npcs.ActiveCount); Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.True(before.HasSameState(fixture.Random)); Assert.True(beforeSeparate.HasSameState(separate));
    }

    [Fact]
    public void Invalid_owned_spawn_context_rejects_before_parent_damage_items_child_and_random()
    {
        using var document = JsonDocument.Parse((string)OriginalDeaths().First()[0]);
        var fixture = new Fixture(document.RootElement);
        fixture.Npcs.SetVanillaSpawnContextSource(static () => new(float.NaN, 1, false));
        var before = fixture.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100_000));
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var retained)); Assert.Equal(fixture.Npc, retained);
        Assert.Equal(1, fixture.Npcs.ActiveCount); Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.True(before.HasSameState(fixture.Random));
    }

    [Fact]
    public void Spawn_context_changed_after_retained_preview_rejects_before_parent_damage_or_random()
    {
        using var document = JsonDocument.Parse((string)OriginalDeaths().First()[0]);
        var fixture = new Fixture(document.RootElement); int reads = 0;
        fixture.Npcs.SetVanillaSpawnContextSource(() => new(++reads == 1 ? 1f : 2f, 1, false));
        var before = fixture.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100_000));
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var retained)); Assert.Equal(fixture.Npc, retained);
        Assert.Equal(1, fixture.Npcs.ActiveCount); Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.True(before.HasSameState(fixture.Random));
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
            var context = NpcSpecificLoot1458Tests.Context(row); var p = row.GetProperty("profile");
            Random = new(row.GetProperty("seed").GetInt32());
            bool injured = row.GetProperty("injured").GetBoolean();
            player = default(PlayerStateSnapshot) with
            {
                Player = new(new(0), new(1)), Revision = new(1), PositionX = context.PositionX, PositionY = context.PositionY,
                Luck = row.GetProperty("luck").GetSingle(), Zones = context.Zones,
                HasHealth = true, Life = (short)(injured ? 100 : 400), MaxLife = 400,
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
            var spawn = new VanillaNpcSpawnContext(context.Difficulty!.Value, 1, p.GetProperty("Good").GetBoolean())
            { HardMode = context.HardMode, DownedPlantera = false };
            Npcs.SetVanillaSpawnContextSource(() => spawn);
            Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(Random));
            bool expert = context.Difficulty >= 2f, master = context.Difficulty >= 3f;
            bool downed = context.AnyMechDowned == true;
            Pipeline = new(Npcs, Items, this, new PlayerAuthority(null, null), static () => 0, null,
                new(Items), null, new RuntimeWorldClock(0, true, 0, 0, 1), new(), expert, master,
                lootRandom: Random,
                seasonalItemContext: () => new(context.Halloween == true, context.Christmas == true, false),
                mechanicalLootBaseline: new(0, context.HardMode, downed, downed, downed), lootRemixWorld: context.RemixWorld,
                globalLootWorldSource: () => world);
        }

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            if (slot.Value == 0 && ++reads == 2)
            {
                if (ChangeOnRead == 1) world = world with { Halloween = !world.Halloween };
                if (ChangeOnRead == 3)
                {
                    var peer = new NpcStateUpdate(3, 3, 0f, 0f, 0f, 0f, 255, default, NpcSimulationState.Initial);
                    Assert.True(Npcs.TrySpawn(4, in peer, out _));
                }
                if (ChangeOnRead == 2) player = player with { Revision = new(2), Zones = default(PlayerZoneSnapshot1458) };
            }
            snapshot = player;
            return slot.Value == 0;
        }

    }
}
