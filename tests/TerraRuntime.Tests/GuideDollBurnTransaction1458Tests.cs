using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GuideDollBurnTransaction1458Tests
{
    [Fact]
    public void Loaded_Andrew_identity_is_bound_to_exact_Guide_generation()
    {
        var npcs = new RuntimeNpcStore();
        var update = new NpcStateUpdate(22, 22, 800, 800, 0, 0, 255, default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawn(0, in update, out var guide));
        var identities = new RuntimeTownNpcStateStore(new([], [new(22, "Andrew", 800, 800,
            true, 0, 0, null, false)], []), [], new(400, 400));
        identities.BindRuntimeNames(npcs);
        Assert.Equal("Andrew", identities.CaptureResidentName(guide.Handle));
        Assert.True(npcs.TryDespawn(guide.Handle));
        Assert.True(npcs.TrySpawn(0, in update, out var replacement));
        Assert.NotEqual(guide.Handle.Generation, replacement.Handle.Generation);
        Assert.Null(identities.CaptureResidentName(guide.Handle));
        Assert.Null(identities.CaptureResidentName(replacement.Handle));
        identities.BindRuntimeNames(npcs); // Repeated composition setup cannot grant the stale identity to a reused slot.
        Assert.Null(identities.CaptureResidentName(replacement.Handle));
    }

    [Fact]
    public void Andrew_Green_Cap_has_original_world_drop_only_defaults_and_no_natural_prefix()
    {
        Assert.True(TerraRuntime.Gameplay.Items.VanillaNpcSpecificDropCatalog1458.TryGet(
            VanillaItemIds.GreenCap, out var definition));
        Assert.Equal(28, definition.RuntimeDefaults.Width); Assert.Equal(20, definition.RuntimeDefaults.Height);
        Assert.Equal(9999, definition.RuntimeDefaults.MaximumStack);
        Assert.Equal(VanillaItemPrefixFamily.None, definition.WorldDrop!.Value.PrefixFamily);
        Assert.Null(definition.UseTiming); Assert.Null(definition.Placement); Assert.Null(definition.PickTool);
    }

    public static IEnumerable<object[]> SingleGuideCases()
    {
        using var stream = typeof(GuideDollBurnTransaction1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.guide-doll-burn-checkpoint-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
            if (row.GetProperty("guides").GetInt32() == 1 && !row.GetProperty("merchant").GetBoolean())
                yield return [row.GetRawText()];
    }

    [Fact]
    public void Independent_vanilla_broad_stack_goldens_retain_all_Guide_and_later_victim_outcomes()
    {
        // These source outcomes remain evidence of the unimplemented shared multi-death lane.
        // Runtime refusal tests do not redefine vanilla's broad-stack behavior.
        using var stream = typeof(GuideDollBurnTransaction1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.guide-doll-burn-checkpoint-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip); int examined = 0;
        foreach (var row in document.RootElement.EnumerateArray())
        {
            int guides = row.GetProperty("guides").GetInt32(), stack = row.GetProperty("stack").GetInt32();
            bool merchant = row.GetProperty("merchant").GetBoolean();
            if (guides < 2 && !(guides == 1 && merchant && stack > 1)) continue;
            examined++;
            Assert.True(row.GetProperty("dollAir").GetBoolean());
            var alive = row.GetProperty("alive").EnumerateArray().ToArray();
            Assert.DoesNotContain(alive, n => n.GetProperty("type").GetInt32() == 22);
            Assert.Single(alive, n => n.GetProperty("type").GetInt32() == 113);
            Assert.Equal(merchant && stack <= guides ? 1 : 0,
                alive.Count(n => n.GetProperty("type").GetInt32() == 17));
        }
        Assert.Equal(416, examined);
    }

    [Theory, MemberData(nameof(SingleGuideCases))]
    public void Accepted_single_Guide_matches_actual_whole_burn_drops_Wall_and_next_rng(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var f = new Fixture(row.GetProperty("seed").GetInt32(), row.GetProperty("injured").GetBoolean(),
            andrew: row.GetProperty("andrew").GetBoolean());
        var doll = f.Doll((short)row.GetProperty("stack").GetInt32());
        Assert.True(f.Burn(in doll));
        Assert.False(f.Items.TryGetActive(doll.Handle.Slot, out var remaining) && remaining.Handle == doll.Handle);
        Assert.False(f.Npcs.TryGet(f.Guide.Handle, out _));
        Assert.Equal("item:Remove:267", f.Events[0]);
        var expected = row.GetProperty("alive")[0];
        Assert.True(f.Npcs.TryGetActive((byte)expected.GetProperty("slot").GetInt32(), out var wall));
        Assert.Equal(113, wall.Type); Assert.Equal(255, wall.Target);
        Assert.Equal(expected.GetProperty("x").GetSingle(), wall.PositionX);
        Assert.Equal(expected.GetProperty("y").GetSingle(), wall.PositionY);
        var buffer = new WorldItemSnapshot[f.Items.Capacity]; int count = f.Items.CopyActive(buffer);
        var drops = row.GetProperty("drops"); Assert.Equal(drops.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            var actual = buffer[i]; var drop = drops[i];
            Assert.Equal(drop.GetProperty("slot").GetInt16(), actual.Handle.Slot);
            Assert.Equal(drop.GetProperty("id").GetInt16(), actual.ItemNetId);
            Assert.Equal(drop.GetProperty("stack").GetInt16(), actual.Stack);
            Assert.Equal(drop.GetProperty("prefix").GetByte(), actual.Prefix);
            Assert.Equal(drop.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(drop.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(drop.GetProperty("vx").GetSingle(), actual.VelocityX);
            Assert.Equal(drop.GetProperty("vy").GetSingle(), actual.VelocityY);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
    }

    [Fact]
    public void Selected_unknown_current_life_rejects_before_doll_Guide_and_random_mutation()
    {
        var f = new Fixture(5, true, knownLife: false); var doll = f.Doll(); var random = f.Random.Clone();
        Assert.False(f.Burn(in doll));
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var current)); Assert.Equal(doll, current);
        Assert.True(f.Npcs.TryGet(f.Guide.Handle, out var guide)); Assert.Equal(f.Guide, guide);
        Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Events);
    }

    [Fact]
    public void Burn_removal_frees_exact_source_slot_before_selected_heart_in_full_item_table()
    {
        var f = new Fixture(5, true); var doll = f.Doll();
        for (int i = 1; i < f.Items.Capacity; i++)
            Assert.True(f.Items.TryAllocate(new(0, 0, 0, 0, 9999, 0, WorldItemOwnershipMode.None, 1,
                false, 0, 0, 255, 0, 255, 0), out _));
        f.Events.Clear(); Assert.True(f.Burn(in doll));
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var heart));
        Assert.Equal(58, heart.ItemNetId); Assert.NotEqual(doll.Handle.Generation, heart.Handle.Generation);
        Assert.Equal(400, f.Items.ActiveCount); Assert.Equal(328503735, f.Random.Next());
        Assert.Equal("item:Remove:267", f.Events[0]);
        Assert.Equal("item:Drop:58", f.Events[1]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Stale_doll_revision_or_generation_cannot_gain_whole_death_authority(bool reuse)
    {
        var f = new Fixture(5, true); var doll = f.Doll();
        if (reuse) { Assert.True(f.Items.TryRemove(doll.Handle.Slot, out _)); f.Doll(); }
        else Assert.True(f.Items.TryAdvanceMotion(doll.Handle, doll.PositionX, doll.PositionY + 1, 0, 0, out _));
        var random = f.Random.Clone(); f.Events.Clear();
        Assert.False(f.Burn(in doll)); Assert.True(f.Npcs.TryGet(f.Guide.Handle, out var guide));
        Assert.Equal(f.Guide, guide); Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Events);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Changed_death_dependency_rejects_before_source_removal(bool npcPool)
    {
        var f = new Fixture(5, true); var doll = f.Doll(); var random = f.Random.Clone();
        bool changed = false; int captures = 0;
        f.DuringContext = () =>
        {
            if (++captures < 2 || changed) return;
            changed = true;
            if (npcPool) Assert.True(f.Npcs.TrySpawn(10, new(17, 17, 900, 900, 0, 0, 255, default,
                NpcSimulationState.Initial), out _));
            else f.Lookup.Value = f.Lookup.Value with { Revision = new(2) };
        };
        Assert.False(f.Burn(in doll)); Assert.True(changed);
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var current)); Assert.Equal(doll, current);
        Assert.True(f.Npcs.TryGet(f.Guide.Handle, out var guide)); Assert.Equal(f.Guide, guide);
        Assert.True(f.Random.HasSameState(random)); Assert.DoesNotContain("item:Remove:267", f.Events);
    }

    [Fact]
    public void Unknown_Wall_spawn_context_rejects_without_consuming_doll_or_Guide()
    {
        var f = new Fixture(0, false); var doll = f.Doll(); var random = f.Random.Clone();
        f.Npcs.SetVanillaSpawnContextSource(() => new(float.NaN, 1, false));
        Assert.False(f.Burn(in doll)); Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.True(f.Npcs.TryGet(f.Guide.Handle, out var guide)); Assert.Equal(f.Guide, guide);
        Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Events);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Unknown_or_changed_owned_Guide_name_rejects_before_whole_burn(bool changed)
    {
        var f = new Fixture(0, false, andrew: true); var doll = f.Doll(); var random = f.Random.Clone();
        if (!changed) f.GuideName = null;
        else
        {
            int captures = 0;
            f.DuringContext = () => { if (++captures == 2) f.GuideName = ""; };
        }
        Assert.False(f.Burn(in doll)); Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var current));
        Assert.Equal(doll, current); Assert.True(f.Npcs.TryGet(f.Guide.Handle, out var guide));
        Assert.Equal(f.Guide, guide); Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Events);
    }

    private sealed class Lookup(bool injured, bool known) : IRuntimePlayerSlotSnapshotLookup
    {
        public PlayerStateSnapshot Value = default(PlayerStateSnapshot) with
        {
            Player = new(new(0), new(1)), Revision = new(1), PositionX = 1000, PositionY = 1000,
            NpcHealth = known ? new(injured ? 50 : 100, 0, 0, true) : null,
            NpcLifeCurrent = known, DerivedLifeMax = 100
        };
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        { player = Value; return slot.Value == 0; }
    }

    private sealed class Fixture : IWorldItemStateCommitSink, INpcStateCommitSink
    {
        public readonly List<string> Events = [];
        public readonly RuntimeNpcStore Npcs;
        public readonly RuntimeWorldItemStore Items;
        public readonly VanillaUnifiedRandom1458 Random;
        public readonly Lookup Lookup;
        public readonly NpcSnapshot Guide;
        public Action? DuringContext;
        public string? GuideName;
        private readonly RuntimeNpcNetworkCombatPipeline pipeline;
        public Fixture(int seed, bool injured, bool knownLife = true, bool andrew = false)
        {
            GuideName = andrew ? "Andrew" : "";
            Random = new(seed); Npcs = new(commitSink: this); Items = new(this); Lookup = new(injured, knownLife);
            Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(Random));
            Assert.True(Npcs.TrySpawn(0, new(22, 22, 800, 800, 0, 0, 255, default,
                NpcSimulationState.Initial with { DirectionX = 1 }), out Guide));
            var tiles = new WorldTileStore(new(400, 400));
            for (int x = 150; x <= 170; x++) for (int y = 247; y <= 258; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = x is 150 or 170 || y == 258
                    ? new WorldTile { Type = 1, Flags = WorldTileFlags.Active }
                    : new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Lava };
            bool? Context() { DuringContext?.Invoke(); return false; }
            pipeline = new(Npcs, Items, Lookup, new PlayerAuthority(null, null), () => 0, null,
                new(Items), null, null, new(), false, false, worldTiles: tiles, lootRandom: Random,
                npcSpecificLowTiles: Context, requireOwnedPlayerHealth: true, guideNameSource: _ => GuideName);
            Events.Clear();
        }
        public WorldItemSnapshot Doll(short stack = 1)
        {
            Assert.True(Items.TryAllocate(new(2560, 4000, 0, 0, stack, 0, WorldItemOwnershipMode.None,
                267, false, 0, 0, 255, 0, 255, 0), out var doll)); Events.Clear(); return doll;
        }
        public bool Burn(in WorldItemSnapshot doll)
        {
            Assert.True(Npcs.TryCaptureDeathMutationSerial(out ulong serial));
            if (!pipeline.TryStrikeGuideDoll(in doll, in Guide, serial, out var spawn)) return false;
            if (spawn is { } wall) Assert.True(Npcs.TrySpawnIntent(in wall, out _));
            return true;
        }
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item) =>
            Events.Add($"item:{kind}:{item.ItemNetId}");
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) =>
            Events.Add($"npc:{kind}:{npc.Type}");
    }
}
