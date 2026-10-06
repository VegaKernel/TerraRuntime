using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ProjectileNpcStatusAddition1458Tests
{
    [Fact]
    public void Intrinsic_selection_and_nonaging_addition_match_actual_StatusNPC_calls()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ProjectileNpcStatusSelection1458");
        Assert.NotNull(stream);
        using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        using var data = JsonDocument.Parse(gzip);
        int compared = 0;
        Span<TerrariaNpcBuffEntryState> actual = stackalloc TerrariaNpcBuffEntryState[20];
        foreach (var row in data.RootElement.EnumerateArray())
        {
            string profile = row.GetProperty("profile").GetString()!;
            if (profile.StartsWith("whole-", StringComparison.Ordinal)) continue;
            int projectile = row.GetProperty("type").GetInt32();
            BuffTypeId type = projectile == 54 ? VanillaBuffIds.Poisoned : VanillaBuffIds.OnFire;
            var (store, before) = Create(profile == "immune" ? VanillaNpcIds.LavaSlime : VanillaNpcIds.Zombie);
            int publications = 0;
            var status = new RuntimeNpcBuffStatus1458(store, _ => publications++);
            if (profile == "long-existing") Assert.True(status.TryApply(before.Handle, type, 1000));
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            Assert.True(VanillaProjectileNpcStatus1458.TrySelect(new(projectile), random.Next, out var addition));
            RuntimeNpcBuffAdditionPlan1458 plan;
            Assert.True(addition is { } selected
                ? status.TryPlanAddition(in before, selected.Type, selected.Duration, out plan)
                : status.CaptureAddition(in before, out plan));
            Assert.True(plan.IsCurrent);
            Assert.Equal(0, publications);
            Assert.True(status.TryAdoptAddition(plan, in before));
            Assert.True(status.TryGetDebuffs(before.Handle, out var flags));
            Assert.Equal(default, flags);
            status.PublishAddition(plan);
            status.PublishAddition(plan);
            Assert.Equal(row.GetProperty("frames").GetArrayLength(), publications);
            Assert.True(status.TryCopyWireBuffs(before.Handle, actual, out int count));
            int[] expectedTypes = row.GetProperty("buffTypes").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            int[] expectedDurations = row.GetProperty("buffDurations").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            Assert.Equal(expectedTypes.Count(x => x != 0), count);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(expectedTypes[i], actual[i].BuffType);
                Assert.Equal(expectedDurations[i], actual[i].Duration);
            }
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
            Assert.True(store.TryGet(before.Handle, out var after));
            Assert.Equal(before, after);
            compared++;
        }
        Assert.Equal(36, compared);
    }

    [Fact]
    public void Unsupported_producer_rejects_before_random_draws()
    {
        var random = new VanillaUnifiedRandom1458(1458);
        var checkpoint = random.Clone();
        Assert.False(VanillaProjectileNpcStatus1458.TrySelect(VanillaProjectileIds.WoodenArrowFriendly, random.Next, out _));
        Assert.True(random.HasSameState(checkpoint));
    }

    [Fact]
    public void No_effect_plan_retains_status_flags_and_timer_dependency()
    {
        var (store, before) = Create(VanillaNpcIds.Zombie);
        var status = new RuntimeNpcBuffStatus1458(store);
        Assert.True(status.CaptureAddition(in before, out var plan));
        Assert.True(status.TryApply(before.Handle, VanillaBuffIds.OnFire, 180));
        Assert.False(plan.IsCurrent);
        Assert.False(status.TryAdoptAddition(plan, in before));

        Assert.True(status.CaptureAddition(in before, out plan));
        status.BeginWorldTick(); // Flags change without a slot-duration/revision write.
        Assert.False(plan.IsCurrent);
        Assert.False(status.TryAdoptAddition(plan, in before));
    }

    [Fact]
    public void Accepted_damage_revision_adopts_without_aging_then_publishes_once()
    {
        var (store, before) = Create(VanillaNpcIds.Zombie);
        int publications = 0;
        bool callbackEntered = false;
        RuntimeNpcBuffAdditionPlan1458? retained = null;
        RuntimeNpcBuffStatus1458? status = null;
        status = new(store, _ =>
        {
            publications++;
            if (!callbackEntered)
            {
                callbackEntered = true;
                status!.PublishAddition(retained!);
            }
        });
        Assert.True(status.TryPlanAddition(in before, VanillaBuffIds.Poisoned, 600, out retained));
        var damage = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 1);
        Assert.True(damage.TryApplyUnpublished(in request, out _, out var accepted, out _, out _));
        Assert.True(status.TryAdoptAddition(retained, in accepted));
        Assert.False(status.TryAdoptAddition(retained, in accepted));
        status.PublishAddition(retained);
        Assert.Equal(1, publications);
        Assert.True(status.TryGetDebuffs(before.Handle, out var flags));
        Assert.False(flags.Poisoned);
        Span<TerrariaNpcBuffEntryState> buffs = stackalloc TerrariaNpcBuffEntryState[20];
        Assert.True(status.TryCopyWireBuffs(before.Handle, buffs, out int count));
        Assert.Equal(1, count);
        Assert.Equal(600, buffs[0].Duration);
    }

    [Fact]
    public void Replacement_and_later_actor_write_prevent_stale_adoption_or_publication()
    {
        var (store, before) = Create(VanillaNpcIds.Zombie);
        int publications = 0;
        var status = new RuntimeNpcBuffStatus1458(store, _ => publications++);
        Assert.True(status.TryPlanAddition(in before, VanillaBuffIds.OnFire, 180, out var plan));
        Assert.True(status.TryAdoptAddition(plan, in before));
        Assert.True(store.TryDespawn(before.Handle));
        Assert.True(store.TrySpawn(0, new(3, 3, 800, 800, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 45, LifeMax = 45 }), out var replacement));
        status.PublishAddition(plan);
        Assert.Equal(0, publications);
        Assert.False(status.TryAdoptAddition(plan, in replacement));
        Assert.True(status.CaptureAddition(in replacement, out plan));
        var damage = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(replacement.Handle, DamageSource.Environment, 1);
        Assert.True(damage.TryApplyUnpublished(in request, out _, out _, out _, out _));
        Assert.True(damage.TryApplyUnpublished(in request, out _, out var twice, out _, out _));
        Assert.False(status.TryAdoptAddition(plan, in twice));
    }

    [Fact]
    public void Full_debuff_table_and_longer_existing_status_do_not_publish_or_shorten()
    {
        var (store, before) = Create(VanillaNpcIds.Zombie);
        int publications = 0;
        var status = new RuntimeNpcBuffStatus1458(store, _ => publications++);
        Assert.True(status.EnsureGeneration(before.Handle));
        var slots = new RuntimeNpcBuffSlots1458();
        for (int i = 0; i < 20; i++) slots[i] = new((ushort)VanillaBuffIds.Stinky.Value, 100);
        SetSlots(status, slots);
        Assert.True(status.TryPlanAddition(in before, VanillaBuffIds.Poisoned, 600, out var plan));
        Assert.False(plan.Changed);
        Assert.True(status.TryAdoptAddition(plan, in before));
        status.PublishAddition(plan);
        Assert.Equal(0, publications);
        slots[0] = new((ushort)VanillaBuffIds.OnFire.Value, 1000);
        SetSlots(status, slots);
        Assert.True(status.TryPlanAddition(in before, VanillaBuffIds.OnFire, 180, out plan));
        Assert.False(plan.Changed);
        Assert.Equal(1000, plan.Slots[0].Duration);
    }

    [Fact]
    public void Accepted_addition_stamp_rejects_status_and_actor_changes_including_no_effect_plans()
    {
        var (store, before) = Create(VanillaNpcIds.Zombie);
        var status = new RuntimeNpcBuffStatus1458(store);
        Assert.True(status.TryPlanAddition(in before, VanillaBuffIds.Poisoned, 600, out var changed));
        Assert.True(status.TryAdoptAddition(changed, in before));
        Assert.True(status.IsAcceptedAdditionCurrent(changed));
        Assert.True(status.CaptureAddition(in before, out var noEffect));
        Assert.True(status.TryAdoptAddition(noEffect, in before));
        Assert.True(status.IsAcceptedAdditionCurrent(noEffect));
        var altered = changed.Slots;
        altered[0] = new((ushort)VanillaBuffIds.Poisoned.Value, 599);
        SetSlots(status, altered);
        Assert.False(status.IsAcceptedAdditionCurrent(changed));
        Assert.False(status.IsAcceptedAdditionCurrent(noEffect));
        SetSlots(status, changed.Slots);
        Assert.True(status.IsAcceptedAdditionCurrent(changed));
        var damage = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 1);
        Assert.True(damage.TryApplyUnpublished(in request, out _, out _, out _, out _));
        Assert.False(status.IsAcceptedAdditionCurrent(changed));
    }

    [Fact]
    public void Foreign_owner_unknown_table_and_status_revision_saturation_reject()
    {
        var (store, before) = Create(VanillaNpcIds.Zombie);
        var status = new RuntimeNpcBuffStatus1458(store);
        var foreign = new RuntimeNpcBuffStatus1458(store);
        Assert.True(status.CaptureAddition(in before, out var plan));
        Assert.False(foreign.TryAdoptAddition(plan, in before));
        var slots = new RuntimeNpcBuffSlots1458();
        slots[0] = new(999, 100);
        SetSlots(status, slots);
        Assert.False(status.CaptureAddition(in before, out _));
        SetSlots(status, default, ulong.MaxValue);
        Assert.False(status.CaptureAddition(in before, out _));
    }

    private static (RuntimeNpcStore Store, NpcSnapshot Before) Create(NpcTypeId type)
    {
        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawn(0, new(type.Value, checked((short)type.Value), 800, 800, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 45, LifeMax = 45, LifeRegenCounter = 0 }), out var before));
        return (store, before);
    }

    private static void SetSlots(RuntimeNpcBuffStatus1458 status, RuntimeNpcBuffSlots1458 slots, ulong? revision = null)
    {
        var entries = (Array)typeof(RuntimeNpcBuffStatus1458).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(status)!;
        object entry = entries.GetValue(0)!;
        entry.GetType().GetField("Slots", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(entry, slots);
        if (revision is { } value)
            entry.GetType().GetField("Revision", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(entry, value);
        entries.SetValue(entry, 0);
    }
}
