using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class TownNpcDeathHitEffect1458Tests
{
    public static IEnumerable<object[]> Rows(string name)
    {
        using var stream = typeof(TownNpcDeathHitEffect1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }
    public static IEnumerable<object[]> HitEffects() => Rows("TownHitEffects1458");
    public static IEnumerable<object[]> WholeBatches() => Rows("TownEmptyVictimBatch1458");

    [Theory, MemberData(nameof(HitEffects))]
    public void Dedicated_choices_match_actual_original_HitEffect_for_all_forty_town_identities(string raw)
    {
        using var document = JsonDocument.Parse(raw); var row = document.RootElement;
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var rolls = new Rolls(random, 0);
        if (row.GetProperty("lethal").GetBoolean())
            VanillaTownNpcDeathHitEffect1458.ConsumeLethalChoices(new(row.GetProperty("id").GetInt32()), rolls);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        Assert.Empty(row.GetProperty("drops").EnumerateArray());
        Assert.Equal(1, row.GetProperty("npcCount").GetInt32());
        Assert.Equal(0, row.GetProperty("projectileCount").GetInt32());
    }

    [Theory, MemberData(nameof(WholeBatches))]
    public void Whole_original_two_Guide_burn_with_Angler_or_pet_preserves_drops_credits_Wall_order_and_rng(string raw)
    {
        using var document = JsonDocument.Parse(raw); var row = document.RootElement; var f = new Fixture(row, true);
        var doll = f.Doll();
        Assert.True(f.Pipeline.TryBurnGuideDollBatch(in doll, out int walls)); Assert.Equal(1, walls);
        Check(row, f);
        var survivors = new NpcSnapshot[f.Npcs.Capacity]; int count = f.Npcs.CopyActive(survivors);
        var expected = row.GetProperty("alive"); Assert.Equal(expected.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(expected[i].GetProperty("slot").GetInt32(), survivors[i].Handle.Slot);
            Assert.Equal(expected[i].GetProperty("type").GetInt32(), survivors[i].Type);
            Assert.Equal(expected[i].GetProperty("life").GetInt32(), survivors[i].Simulation.Life);
            Assert.Equal(expected[i].GetProperty("x").GetSingle(), survivors[i].PositionX);
            Assert.Equal(expected[i].GetProperty("y").GetSingle(), survivors[i].PositionY);
        }
        Assert.Equal("item:Remove:267", f.Events.First());
        int first = f.Events.IndexOf("npc:Despawn:22"), wall = f.Events.IndexOf("npc:Spawn:113");
        int last = f.Events.LastIndexOf("npc:Despawn:22");
        Assert.True(first < wall && wall < last);
        Assert.True(last < f.Events.IndexOf("npc:Despawn:" + row.GetProperty("id").GetInt32()));
    }

    private static void Check(JsonElement row, Fixture f)
    {
        var items = new WorldItemSnapshot[f.Items.Capacity]; int count = f.Items.CopyActive(items);
        Equal(row.GetProperty("drops"), items[..count].Select(static i => new WorldItemDropStateUpdate(
            i.PositionX, i.PositionY, i.VelocityX, i.VelocityY, i.Stack, i.Prefix,
            WorldItemOwnershipMode.None, i.ItemNetId, false, 0, 0)).ToList());
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
        var credits = f.Prelude.CaptureBestiary().Kills.ToDictionary(static x => x.PersistentId, static x => x.KillCount);
        Assert.Equal(row.GetProperty("bestiary").EnumerateObject().Count(), credits.Count);
        foreach (var pair in row.GetProperty("bestiary").EnumerateObject()) Assert.Equal(pair.Value.GetInt32(), credits[pair.Name]);
    }

    private static void Equal(JsonElement expected, List<WorldItemDropStateUpdate> actual)
    {
        Assert.Equal(expected.GetArrayLength(), actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            var e = expected[i]; var a = actual[i];
            Assert.Equal((e.GetProperty("id").GetInt32(), e.GetProperty("stack").GetInt32(), e.GetProperty("prefix").GetInt32()),
                ((int)a.ItemNetId, (int)a.Stack, (int)a.Prefix));
            Assert.Equal((e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(),
                e.GetProperty("vx").GetSingle(), e.GetProperty("vy").GetSingle()),
                (a.PositionX, a.PositionY, a.VelocityX, a.VelocityY));
        }
    }

    internal sealed class Rolls(VanillaUnifiedRandom1458 rng, float luck) : INpcLootRollSource
    {
        public int NextInt32(int min, int max) => rng.Next(min, max);
        public int RollLuck(int range)
        {
            if (luck > 0 && (float)rng.NextDouble() < luck) return rng.Next(rng.Next(range / 2, range));
            if (luck < 0 && (float)rng.NextDouble() < -luck) return rng.Next(rng.Next(range, range * 2));
            return rng.Next(range);
        }
    }

    internal sealed class Fixture : IRuntimePlayerSlotSnapshotLookup, INpcStateCommitSink, IWorldItemStateCommitSink
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeWorldItemStore Items;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeNpcDeathPrelude1458 Prelude = new();
        internal readonly RuntimeWorldProgressionMutations Progression = new();
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;
        internal readonly NpcSnapshot Victim;
        internal readonly RuntimeTownNpcStateStore Residents;
        internal readonly List<string> Events = [];
        internal PlayerStateSnapshot Player;
        internal Func<NpcHandle, string?>? NameOverride = null;
        internal RuntimeNpcGlobalLootWorldFacts1458 World;
        internal Fixture(JsonElement row, bool batch)
        {
            int id = row.GetProperty("id").GetInt32(); bool injured = row.GetProperty("injured").GetBoolean();
            Random = new(row.GetProperty("seed").GetInt32()); Npcs = new(commitSink: this); Items = new(this);
            Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(Random));
            Player = default(PlayerStateSnapshot) with { Player = new(new(0), new(1)), Revision = new(1),
                PositionX = 1000, PositionY = 1000, Luck = batch ? 0 : row.GetProperty("luck").GetSingle(),
                Zones = default(PlayerZoneSnapshot1458), HasHealth = true, DerivedLifeMax = 100,
                NpcHealth = new(injured ? 50 : 100, 0, 0, true), NpcLifeCurrent = true,
                HasMana = true, Mana = 200, MaxMana = 200 };
            World = new(400, 400, 1, row.GetProperty("hard").GetBoolean(), false, false, false, 200, 140, true, true);
            var records = new List<WorldTownNpc>();
            if (batch) for (int i = 0; i < 2; i++)
            {
                Assert.True(Npcs.TrySpawn((byte)i, new(22, 22, 800 + 50 * i, 800, 0, 0, 255, default,
                    NpcSimulationState.Initial with { DirectionX = 1 }), out _));
                records.Add(new(22, "Andrew", 800 + 50 * i, 800, true, 0, 0, null, false));
            }
            var b = row.GetProperty("body"); var state = NpcSimulationState.Initial with
            { Life = b.GetProperty("lifeMax").GetInt32(), LifeMax = b.GetProperty("lifeMax").GetInt32(),
                DamageOverride = b.GetProperty("damage").GetInt32(), DefenseOverride = b.GetProperty("defense").GetInt32(),
                MoneyValue = b.GetProperty("value").GetSingle(), Friendly = b.GetProperty("friendly").GetBoolean(),
                HitboxOverride = new(b.GetProperty("width").GetInt32(), b.GetProperty("height").GetInt32()), DirectionX = 1 };
            int slot = batch ? 2 : 0;
            Assert.True(Npcs.TrySpawn((byte)slot, new(id, (short)id, 800 + 50 * slot, 800, 0, 0, 255, default, state), out Victim));
            string mode = row.GetProperty("mode").GetString()!;
            string name = mode == "matching" ? id == 178 ? "Whitney" : "Jim" : mode == "other" ? "NotTheMatchingName" : "";
            records.Add(new(id, name, 800 + 50 * slot, 800, true, 0, 0, null, false));
            Residents = new(new([], records.ToArray(), []), [], new(400, 400)); Residents.BindRuntimeNames(Npcs);
            var tiles = new WorldTileStore(new(400, 400));
            if (batch) for (int x = 150; x <= 170; x++) for (int y = 247; y <= 258; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = x is 150 or 170 || y == 258
                    ? new WorldTile { Type = 1, Flags = WorldTileFlags.Active }
                    : new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Lava };
            Pipeline = new(Npcs, Items, this, new PlayerAuthority(null, null), () => 0, null, new(Items), null,
                null, Progression, false, false, worldTiles: tiles, lootRandom: Random, deathPrelude: Prelude,
                globalLootWorldSource: () => World, requireOwnedPlayerHealth: true,
                guideNameSource: h => NameOverride is null ? Residents.CaptureResidentName(h) : NameOverride(h));
            if (!batch && row.GetProperty("interacted").GetBoolean())
                Assert.True(Pipeline.Interactions.TryMark(Victim.Handle, Player.Player));
            Events.Clear();
        }
        internal WorldItemSnapshot Doll()
        {
            Assert.True(Items.TryAllocate(new(2560, 4000, 0, 0, 3, 0, WorldItemOwnershipMode.None, 267,
                false, 0, 0, 255, 0, 255, 0), out var doll)); Events.Clear(); return doll;
        }
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot value) { value = Player; return slot.Value == 0; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) => Events.Add($"npc:{kind}:{npc.Type}");
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item) => Events.Add($"item:{kind}:{item.ItemNetId}");
    }
}
