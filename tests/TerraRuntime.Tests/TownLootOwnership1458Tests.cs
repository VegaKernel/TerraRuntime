using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed partial class TownLootSource1458Tests
{
    private static JsonElement Select(int id, bool batch, string mode = "matching", bool hard = false)
    {
        foreach (var values in Rows(batch ? "town-batches" : "town-deaths"))
        {
            using var json = JsonDocument.Parse((string)values[0]); var row = json.RootElement;
            if (row.GetProperty("id").GetInt32() == id && row.GetProperty("mode").GetString() == mode &&
                row.GetProperty("hard").GetBoolean() == hard && row.GetProperty("seed").GetInt32() == 5 &&
                row.GetProperty("injured").GetBoolean()) return row.Clone();
        }
        throw new InvalidOperationException("Missing selected independent original row");
    }

    [Theory]
    [InlineData(178, false)] [InlineData(227, false)]
    [InlineData(178, true)] [InlineData(227, true)]
    public void Unknown_named_reward_owner_rejects_without_npc_item_credit_or_rng_mutation(int id, bool batch)
    {
        var f = new Fixture(Select(id, batch), batch); f.NameOverride = _ => null;
        Reject(f, batch);
    }

    [Theory]
    [InlineData(178)] [InlineData(227)]
    public void Nonempty_name_without_owned_server_locale_is_unknown_before_lethal_commit(int id)
    {
        var f = new Fixture(Select(id, false), false, language: null);
        Reject(f, false);
    }

    [Theory]
    [InlineData(178)] [InlineData(227)]
    public void Known_empty_name_bypasses_localized_predicate_without_fake_English_locale(int id)
    {
        var row = Select(id, false, "empty"); var f = new Fixture(row, false, language: null);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrikeEnvironment(f.Victim.Handle, 9999, 10, 1));
        Check(row, f);
    }

    [Theory]
    [InlineData(178, false)] [InlineData(227, false)]
    [InlineData(178, true)] [InlineData(227, true)]
    public void Changed_resident_name_cannot_publish_retained_reward_plan(int id, bool batch)
    {
        var f = new Fixture(Select(id, batch), batch); int captures = 0;
        f.NameOverride = h => h == f.Victim.Handle
            ? ++captures == 1 ? id == 178 ? "Whitney" : "Jim" : "NotTheMatchingName"
            : f.Residents.CaptureResidentName(h);
        Reject(f, batch); Assert.True(captures >= 2);
    }

    [Fact]
    public void Changed_Hardmode_owner_cannot_publish_Princess_reward_or_hit_effect_rng()
    {
        var f = new Fixture(Select(663, false, "empty", true), false); int reads = 0;
        f.WorldRead = () => { if (++reads == 2) f.World = f.World with { HardMode = false }; };
        Reject(f, false); Assert.True(reads >= 2);
    }

    [Theory]
    [InlineData(178)] [InlineData(227)] [InlineData(208)] [InlineData(663)]
    public void Held_item_claim_rejects_whole_named_or_stack_reward_batch_and_exact_retry_matches_source(int id)
    {
        var row = Select(id, true, id is 178 or 227 ? "matching" : "empty", id == 663);
        var f = new Fixture(row, true);
        Assert.True(f.Items.TryReserveDropSlot(out var held));
        var doll = Reject(f, true);
        Assert.True(f.Items.TryReleaseDropReservation(in held));
        Assert.True(f.Pipeline.TryBurnGuideDollBatch(in doll, out int walls)); Assert.Equal(1, walls);
        Check(row, f);
    }

    [Theory]
    [InlineData(22, "Andrew")] [InlineData(178, "Whitney")] [InlineData(227, "Jim")]
    public void Named_source_predicates_need_case_exact_name_and_owned_English_locale(int id, string name)
    {
        Assert.True(VanillaTownNpcLootRules1458.CaptureNamedEligibility(new(id), name, VanillaTownNpcLootLanguage1458.English));
        Assert.False(VanillaTownNpcLootRules1458.CaptureNamedEligibility(new(id), name.ToLowerInvariant(), VanillaTownNpcLootLanguage1458.English));
        Assert.Null(VanillaTownNpcLootRules1458.CaptureNamedEligibility(new(id), name, null));
        Assert.Null(VanillaTownNpcLootRules1458.CaptureNamedEligibility(new(id), null, VanillaTownNpcLootLanguage1458.English));
        Assert.False(VanillaTownNpcLootRules1458.CaptureNamedEligibility(new(id), "", null));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Unchanged_name_callback_cannot_invalidate_a_prechecked_global_world_owner(bool batch)
    {
        var f = new Fixture(Select(178, batch), batch); int reads = 0; bool armed = false, changed = false;
        f.WorldRead = () => { if (++reads == 2) armed = true; };
        f.NameOverride = h =>
        {
            if (armed && !changed && h == f.Victim.Handle)
            { changed = true; f.World = f.World with { HardMode = !f.World.HardMode }; }
            return f.Residents.CaptureResidentName(h);
        };
        Reject(f, batch); Assert.True(changed);
    }

    private static WorldItemSnapshot Reject(Fixture f, bool batch)
    {
        WorldItemSnapshot doll = default;
        if (batch) doll = f.Doll();
        var random = f.Random.Clone(); var before = new NpcSnapshot[f.Npcs.Capacity]; int count = f.Npcs.CopyActive(before);
        if (batch) Assert.False(f.Pipeline.TryBurnGuideDollBatch(in doll, out _));
        else Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(f.Victim.Handle, 9999, 10, 1));
        var after = new NpcSnapshot[f.Npcs.Capacity]; Assert.Equal(count, f.Npcs.CopyActive(after));
        Assert.Equal(before[..count], after[..count]); Assert.True(f.Random.HasSameState(random));
        Assert.Empty(f.Events); Assert.Empty(f.Prelude.CaptureBestiary().Kills);
        if (batch) { Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var retained)); Assert.Equal(doll, retained); }
        else Assert.Equal(0, f.Items.ActiveCount);
        return doll;
    }
}
