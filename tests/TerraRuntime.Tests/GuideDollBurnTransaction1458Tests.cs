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
    public static IEnumerable<object[]> WholeBatchCases()
    {
        using var stream = typeof(GuideDollBurnTransaction1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.guide-doll-batch-checkpoint-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(WholeBatchCases))]
    public void Whole_batch_matches_original_multi_Guide_and_later_town_deaths(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var f = new Fixture(row.GetProperty("seed").GetInt32(), row.GetProperty("injured").GetBoolean(),
            andrew: row.GetProperty("andrew").GetBoolean(), guides: row.GetProperty("guides").GetInt32(),
            merchants: row.GetProperty("merchants").GetInt32(), spread: row.GetProperty("spread").GetBoolean());
        var doll = f.Doll((short)row.GetProperty("stack").GetInt32());
        Assert.True(f.Batch(in doll));
        var active = new NpcSnapshot[f.Npcs.Capacity]; int count = f.Npcs.CopyActive(active);
        var expected = row.GetProperty("alive"); Assert.Equal(expected.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            var n = active[i]; var source = expected[i];
            Assert.Equal(source.GetProperty("slot").GetInt32(), n.Handle.Slot);
            Assert.Equal(source.GetProperty("type").GetInt32(), n.Type);
            Assert.Equal(source.GetProperty("life").GetInt32(), n.Simulation.Life);
            Assert.Equal(source.GetProperty("target").GetInt32(), n.Target);
            Assert.Equal(source.GetProperty("x").GetSingle(), n.PositionX);
            Assert.Equal(source.GetProperty("y").GetSingle(), n.PositionY);
        }
        var items = new WorldItemSnapshot[f.Items.Capacity]; count = f.Items.CopyActive(items);
        var drops = row.GetProperty("drops"); Assert.Equal(drops.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            var actual = items[i]; var drop = drops[i];
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
        Assert.Equal("item:Remove:267", f.Events.First());
        var actualCredits = f.Prelude.CaptureBestiary().Kills.ToDictionary(static e => e.PersistentId, static e => e.KillCount);
        var sourceCredits = row.GetProperty("bestiary");
        Assert.Equal(sourceCredits.EnumerateObject().Count(), actualCredits.Count);
        foreach (var credit in sourceCredits.EnumerateObject()) Assert.Equal(credit.Value.GetInt32(), actualCredits[credit.Name]);
    }

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
    public void Unknown_late_Guide_name_rejects_entire_batch_after_first_detached_death()
    {
        var f = new Fixture(5, true, andrew: true, guides: 2, merchants: 1); var doll = f.Doll(5);
        var before = new NpcSnapshot[f.Npcs.Capacity]; int count = f.Npcs.CopyActive(before);
        var random = f.Random.Clone();
        f.NameQuery = handle => handle.Slot == 0 ? "Andrew" : null;
        Assert.False(f.Batch(in doll)); Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var item));
        Assert.Equal(doll, item);
        var after = new NpcSnapshot[f.Npcs.Capacity]; Assert.Equal(count, f.Npcs.CopyActive(after));
        Assert.Equal(before[..count], after[..count]); Assert.True(f.Random.HasSameState(random));
        Assert.Empty(f.Prelude.CaptureBestiary().Kills); Assert.Empty(f.Events);
    }

    [Fact]
    public void Selected_unknown_life_in_second_death_keeps_first_Guide_cap_doll_and_rng_uncommitted()
    {
        // Actual seed5 source row is Cap(first Guide), Cap(second Guide), Heart(second Guide).
        // The first source death can be previewed with unknown life because it selects no heart.
        var f = new Fixture(5, true, knownLife: false, andrew: true, guides: 2); var doll = f.Doll();
        var before = new NpcSnapshot[f.Npcs.Capacity]; int count = f.Npcs.CopyActive(before); var random = f.Random.Clone();
        Assert.False(f.Batch(in doll)); Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var item)); Assert.Equal(doll, item);
        var after = new NpcSnapshot[f.Npcs.Capacity]; Assert.Equal(count, f.Npcs.CopyActive(after));
        Assert.Equal(before[..count], after[..count]); Assert.Equal(1, f.Items.ActiveCount);
        Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Events); Assert.Empty(f.Prelude.CaptureBestiary().Kills);
    }

    [Fact]
    public void Foreign_item_reservation_rejects_whole_batch_and_release_allows_exact_retry()
    {
        var f = new Fixture(5, true, andrew: true, guides: 2); var doll = f.Doll();
        Assert.True(f.Items.TryReserveDropSlot(out var held)); var random = f.Random.Clone();
        Assert.False(f.Batch(in doll)); Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var item)); Assert.Equal(doll, item);
        Assert.True(f.Npcs.TryGet(f.Guide.Handle, out var retained)); Assert.Equal(f.Guide, retained);
        Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Events); Assert.Empty(f.Prelude.CaptureBestiary().Kills);
        Assert.True(f.Items.TryReleaseDropReservation(in held));
        Assert.True(f.Batch(in doll));
        var active = new WorldItemSnapshot[f.Items.Capacity]; int count = f.Items.CopyActive(active);
        Assert.Equal(new short[] { 867, 867, 58 }, active[..count].Select(static i => i.ItemNetId));
    }

    [Fact]
    public void Item_mutation_between_fork_and_allocation_capture_cannot_change_accepted_source_slots()
    {
        var f = new Fixture(5, true, andrew: true, guides: 2); var doll = f.Doll(); var random = f.Random.Clone();
        int reads = 0;
        f.Lookup.BeforeRead = () =>
        {
            // First read is Wall eligibility, second is the census; views are captured after the fork.
            if (++reads == 3) Assert.True(f.Items.TryAllocate(new(0, 0, 0, 0, 1, 0,
                WorldItemOwnershipMode.None, 2, false, 0, 0, 255, 0, 255, 0), out _));
        };
        Assert.False(f.Batch(in doll)); Assert.True(reads >= 3);
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var item)); Assert.Equal(doll, item);
        Assert.True(f.Npcs.TryGet(f.Guide.Handle, out var retained)); Assert.Equal(f.Guide, retained);
        Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Prelude.CaptureBestiary().Kills);
        Assert.DoesNotContain("item:Remove:267", f.Events);
    }

    [Fact]
    public void Unsupported_late_conversation_peer_rejects_before_any_live_victim_or_doll_mutation()
    {
        var f = new Fixture(5, true, guides: 2, merchants: 1); var doll = f.Doll(5);
        Assert.True(f.Npcs.TryGetActive(2, out var merchant));
        var update = new NpcStateUpdate(merchant.Type, merchant.NetId, merchant.PositionX, merchant.PositionY,
            0, 0, merchant.Target, new(3, 12, 10, 7), merchant.Simulation);
        Assert.True(f.Npcs.TryUpdate(merchant.Handle, in update, out _));
        Assert.True(f.Npcs.TrySpawn(10, new(17, 17, 1000, 800, 0, 0, 255, default, NpcSimulationState.Initial), out _));
        f.Events.Clear(); var before = new NpcSnapshot[f.Npcs.Capacity]; int count = f.Npcs.CopyActive(before);
        var random = f.Random.Clone();
        Assert.False(f.Batch(in doll)); Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var item)); Assert.Equal(doll, item);
        var after = new NpcSnapshot[f.Npcs.Capacity]; Assert.Equal(count, f.Npcs.CopyActive(after));
        Assert.Equal(before[..count], after[..count]); Assert.True(f.Random.HasSameState(random));
        Assert.Empty(f.Prelude.CaptureBestiary().Kills); Assert.Empty(f.Events);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Batch_final_dependency_guard_rejects_changed_player_or_NPC_table(bool npcPool)
    {
        var f = new Fixture(5, true, guides: 2, merchants: 1); var doll = f.Doll(5); var random = f.Random.Clone();
        int captures = 0;
        f.DuringContext = () =>
        {
            if (++captures != 2) return;
            if (npcPool) Assert.True(f.Npcs.TrySpawn(10, new(17, 17, 1000, 800, 0, 0, 255, default,
                NpcSimulationState.Initial), out _));
            else f.Lookup.Value = f.Lookup.Value with { Revision = new(2) };
        };
        Assert.False(f.Batch(in doll)); Assert.True(captures >= 2);
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var item)); Assert.Equal(doll, item);
        Assert.True(f.Npcs.TryGet(f.Guide.Handle, out var retained)); Assert.Equal(f.Guide, retained);
        Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Prelude.CaptureBestiary().Kills);
        Assert.DoesNotContain("item:Remove:267", f.Events);
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
        // Keep the previously captured broad source outcomes; the new batch must reproduce
        // them rather than redefining vanilla behavior to match an old admission fence.
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

    [Fact]
    public void Town_specific_reward_presence_matches_actual_official_registry_for_every_town_identity()
    {
        using var stream = typeof(GuideDollBurnTransaction1458Tests).Assembly
            .GetManifestResourceStream("TownSpecificLootRegistry1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var facts = JsonDocument.Parse(gzip);
        Assert.Equal(40, facts.RootElement.GetArrayLength());
        foreach (var row in facts.RootElement.EnumerateArray())
            Assert.Equal(row.GetProperty("rules").GetArrayLength() != 0,
                TerraRuntime.Gameplay.Npcs.VanillaTownNpcLootFacts1458.HasRegisteredSpecificRules(
                    new(row.GetProperty("id").GetInt32())));
    }

    public static IEnumerable<object[]> TownRewardBatches()
    {
        var assembly = typeof(GuideDollBurnTransaction1458Tests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(static name => name.EndsWith("town-batches.json.gz", StringComparison.Ordinal)))!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
            if (row.TryGetProperty("legacy", out var legacy) && legacy.GetBoolean()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(TownRewardBatches))]
    public void Previously_fenced_reward_victim_matches_actual_original_whole_burn(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        int type = row.GetProperty("id").GetInt32(); string mode = row.GetProperty("mode").GetString()!;
        var f = new Fixture(5, true, andrew: true); var doll = f.Doll(2);
        Assert.True(f.Npcs.TrySpawn(1, new(type, (short)type, 800, 800, 0, 0, 255, default,
            NpcSimulationState.Initial with { DirectionX = 1 }), out _));
        string victimName = mode == "matching" ? type == 178 ? "Whitney" : "Jim"
            : mode == "other" ? "NotTheMatchingName" : "";
        f.NameQuery = handle => handle.Slot == 0 ? "Andrew" : victimName;
        f.Events.Clear(); Assert.True(f.Batch(in doll));
        var active = new NpcSnapshot[f.Npcs.Capacity]; int count = f.Npcs.CopyActive(active);
        var survivors = row.GetProperty("alive"); Assert.Equal(survivors.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(survivors[i].GetProperty("slot").GetInt32(), active[i].Handle.Slot);
            Assert.Equal(survivors[i].GetProperty("type").GetInt32(), active[i].Type);
            Assert.Equal(survivors[i].GetProperty("life").GetInt32(), active[i].Simulation.Life);
            Assert.Equal(survivors[i].GetProperty("x").GetSingle(), active[i].PositionX);
            Assert.Equal(survivors[i].GetProperty("y").GetSingle(), active[i].PositionY);
        }
        var items = new WorldItemSnapshot[f.Items.Capacity]; count = f.Items.CopyActive(items);
        var drops = row.GetProperty("drops"); Assert.Equal(drops.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            var actual = items[i]; var source = drops[i];
            Assert.Equal(source.GetProperty("slot").GetInt16(), actual.Handle.Slot);
            Assert.Equal(source.GetProperty("id").GetInt16(), actual.ItemNetId);
            Assert.Equal(source.GetProperty("stack").GetInt16(), actual.Stack);
            Assert.Equal(source.GetProperty("prefix").GetByte(), actual.Prefix);
            Assert.Equal(source.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(source.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(source.GetProperty("vx").GetSingle(), actual.VelocityX);
            Assert.Equal(source.GetProperty("vy").GetSingle(), actual.VelocityY);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
        Assert.Equal("item:Remove:267", f.Events.First());
        var credits = f.Prelude.CaptureBestiary().Kills.ToDictionary(static e => e.PersistentId, static e => e.KillCount);
        Assert.Equal(row.GetProperty("bestiary").EnumerateObject().Count(), credits.Count);
        foreach (var credit in row.GetProperty("bestiary").EnumerateObject())
            Assert.Equal(credit.Value.GetInt32(), credits[credit.Name]);
    }

    [Theory]
    [InlineData(178)] [InlineData(227)]
    public void Selected_unknown_named_town_reward_rejects_before_any_Guide_or_doll_commit(int type)
    {
        var f = new Fixture(5, true, andrew: true); var doll = f.Doll(2);
        Assert.True(f.Npcs.TrySpawn(1, new(type, (short)type, 800, 800, 0, 0, 255, default,
            NpcSimulationState.Initial with { DirectionX = 1 }), out var town));
        f.NameQuery = handle => handle.Slot == 0 ? "Andrew" : null;
        var random = f.Random.Clone(); f.Events.Clear();
        ulong preludeRevision = f.Prelude.Revision;
        Assert.False(f.Burn(in doll));
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var current)); Assert.Equal(doll, current);
        Assert.True(f.Npcs.TryGet(f.Guide.Handle, out var guide)); Assert.Equal(f.Guide, guide);
        Assert.True(f.Npcs.TryGet(town.Handle, out var currentTown)); Assert.Equal(town, currentTown);
        Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Events);
        Assert.Equal(preludeRevision, f.Prelude.Revision);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Late_name_callback_cannot_invalidate_a_prechecked_direct_death_owner(int owner)
    {
        var f = new Fixture(5, true, andrew: true, withClock: true); var doll = f.Doll();
        var random = f.Random.Clone(); int captures = 0; bool armed = false, changed = false;
        f.DuringContext = () => { if (++captures == 2) armed = true; };
        f.NameQuery = _ =>
        {
            if (armed && !changed)
            {
                changed = true;
                if (owner == 0) Assert.True(f.Progression.MarkCompleted(VanillaWorldProgressionId.Skeletron));
                else if (owner == 1) Assert.True(f.Prelude.TryRegisterScriptedKill(in f.Guide));
                else if (owner == 2) f.Clock!.Tick();
                else f.Tiles.Set(160, 250, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            }
            return f.GuideName;
        };
        Assert.False(f.Burn(in doll)); Assert.True(changed);
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var current)); Assert.Equal(doll, current);
        Assert.True(f.Npcs.TryGet(f.Guide.Handle, out var guide)); Assert.Equal(f.Guide, guide);
        Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Events);
    }

    private sealed class Lookup(bool injured, bool known) : IRuntimePlayerSlotSnapshotLookup
    {
        public Action? BeforeRead;
        public PlayerStateSnapshot Value = default(PlayerStateSnapshot) with
        {
            Player = new(new(0), new(1)), Revision = new(1), PositionX = 1000, PositionY = 1000,
            NpcHealth = known ? new(injured ? 50 : 100, 0, 0, true) : null,
            NpcLifeCurrent = known, DerivedLifeMax = 100
        };
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        { if (slot.Value == 0) BeforeRead?.Invoke(); player = Value; return slot.Value == 0; }
    }

    private sealed class Fixture : IWorldItemStateCommitSink, INpcStateCommitSink
    {
        public readonly List<string> Events = [];
        public readonly RuntimeNpcStore Npcs;
        public readonly RuntimeWorldItemStore Items;
        public readonly VanillaUnifiedRandom1458 Random;
        public readonly Lookup Lookup;
        public readonly NpcSnapshot Guide;
        public readonly RuntimeNpcDeathPrelude1458 Prelude = new();
        public readonly RuntimeWorldProgressionMutations Progression = new();
        public readonly RuntimeWorldClock? Clock;
        public readonly WorldTileStore Tiles;
        public Action? DuringContext;
        public string? GuideName;
        public Func<NpcHandle, string?>? NameQuery;
        private readonly RuntimeNpcNetworkCombatPipeline pipeline;
        public Fixture(int seed, bool injured, bool knownLife = true, bool andrew = false,
            int guides = 1, int merchants = 0, bool spread = false, bool withClock = false)
        {
            GuideName = andrew ? "Andrew" : "";
            Random = new(seed); Npcs = new(commitSink: this); Items = new(this); Lookup = new(injured, knownLife);
            Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(Random));
            for (int i = 0; i < guides; i++)
            {
                Assert.True(Npcs.TrySpawn((byte)i, new(22, 22, spread ? 800 + i * 50 : 800, 800, 0, 0, 255, default,
                    NpcSimulationState.Initial with { DirectionX = 1 }), out var guide));
                if (i == 0) Guide = guide;
            }
            for (int i = guides; i < guides + merchants; i++)
                Assert.True(Npcs.TrySpawn((byte)i, new(17, 17, spread ? 800 + i * 50 : 800, 800, 0, 0, 255, default,
                    NpcSimulationState.Initial with { DirectionX = 1 }), out _));
            var tiles = Tiles = new WorldTileStore(new(400, 400));
            Clock = withClock ? new(0, true, VanillaMoonPhase.Full, 0, 1) : null;
            for (int x = 150; x <= 170; x++) for (int y = 247; y <= 258; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = x is 150 or 170 || y == 258
                    ? new WorldTile { Type = 1, Flags = WorldTileFlags.Active }
                    : new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Lava };
            bool? Context() { DuringContext?.Invoke(); return false; }
            pipeline = new(Npcs, Items, Lookup, new PlayerAuthority(null, null), () => 0, null,
                new(Items), null, Clock, Progression, false, false, worldTiles: tiles, lootRandom: Random,
                deathPrelude: Prelude, npcSpecificLowTiles: Context, requireOwnedPlayerHealth: true,
                townNameSource: handle => NameQuery is null ? GuideName : NameQuery(handle),
                townLootLanguage: TerraRuntime.Gameplay.Npcs.VanillaTownNpcLootLanguage1458.English);
            Events.Clear();
        }
        public WorldItemSnapshot Doll(short stack = 1)
        {
            Assert.True(Items.TryAllocate(new(2560, 4000, 0, 0, stack, 0, WorldItemOwnershipMode.None,
                267, false, 0, 0, 255, 0, 255, 0), out var doll)); Events.Clear(); return doll;
        }
        public bool Burn(in WorldItemSnapshot doll)
        {
            return pipeline.TryBurnGuideDollBatch(in doll, out _);
        }
        public bool Batch(in WorldItemSnapshot doll) => pipeline.TryBurnGuideDollBatch(in doll, out _);
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item) =>
            Events.Add($"item:{kind}:{item.ItemNetId}");
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) =>
            Events.Add($"npc:{kind}:{npc.Type}");
    }
}
