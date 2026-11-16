using System.Reflection;
using System.Text.Json;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringDartTrap1458Tests
{
    [Theory]
    [InlineData(185)]
    [InlineData(186)]
    [InlineData(187)]
    [InlineData(188)]
    public void Raw_unproved_trap_actors_cannot_advance_physics_children_or_shared_entropy(int type)
    {
        using var data = JsonDocument.Parse("{\"seed\":1458,\"mode\":0,\"remix\":false,\"style\":2,\"orientation\":1}");
        var fixture = new Fixture(data.RootElement, 65, 136);
        var input = new ProjectileStateUpdate(new(type), 255, 1052, 1042, 5, 0, default, 0,
            (short)(type == 186 ? 60 : 40), 2, 0);
        Assert.True(fixture.Shots.TrySpawn(0, in input, out var before));
        Assert.True(fixture.Shots.TryGetLifecycle(before.Handle, out var life));
        var random = fixture.Random.Clone();
        Assert.True(fixture.Actors.TryTickState());
        Assert.True(fixture.Shots.TryGet(before.Handle, out var after)); Assert.Equal(before, after);
        Assert.True(fixture.Shots.TryGetLifecycle(before.Handle, out var afterLife)); Assert.Equal(life, afterLife);
        Assert.Equal(1, fixture.Shots.ActiveCount); Assert.True(random.HasSameState(fixture.Random));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Emitter_refuses_a_stale_clock_or_unqualified_self_displacement_without_partial_child(bool saturated)
    {
        using var data = JsonDocument.Parse("{\"seed\":1458,\"mode\":0,\"remix\":false,\"style\":2,\"orientation\":1}");
        var fixture = new Fixture(data.RootElement, 65, 136);
        Assert.True(fixture.Fire(65, 65));
        for (int i = 0; i < 5; i++) Assert.True(fixture.Actors.TryTickState());
        Assert.True(fixture.Shots.TryGetActive(0, out var emitter));
        Assert.True(fixture.Shots.TryGetLifecycle(emitter.Handle, out var life));
        if (saturated)
        {
            var filler = new ProjectileStateUpdate(new(98), 255, 1000, 1000, 0, 0, default, 0, 20, 2, 0);
            for (ushort slot = 1; slot < 1000; slot++) Assert.True(fixture.Shots.TrySpawn(slot, in filler, out _));
        }
        else
        {
            int reads = 0;
            typeof(TerraRuntime.Application.ProjectileAuthority).GetField("tickProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(fixture.Actors, (Func<long>)(() => ++reads));
        }
        var random = fixture.Random.Clone();
        Assert.True(fixture.Actors.TryTickState());
        Assert.True(fixture.Shots.TryGet(emitter.Handle, out var after)); Assert.Equal(emitter, after);
        Assert.True(fixture.Shots.TryGetLifecycle(emitter.Handle, out var afterLife)); Assert.Equal(life, afterLife);
        Assert.Equal(saturated ? 1000 : 1, fixture.Shots.ActiveCount);
        Assert.True(random.HasSameState(fixture.Random));
    }

    public static IEnumerable<object[]> ContactFlights() => Rows("WiringTrapFlameContactFlight1458");
    public static IEnumerable<object[]> LowerSlots() => Rows("WiringTrapLowerSlot1458");
    public static IEnumerable<object[]> RemainingCircuits() => Rows("WiringRemainingCircuit1458");
    [Theory, MemberData(nameof(ContactFlights))]
    public void Flame_subupdate_contacts_and_entropy_match_original_Update(string observation) => CompareRemaining(observation, false);
    [Theory, MemberData(nameof(LowerSlots))]
    public void Earlier_physical_child_slots_wait_until_next_source_census(string observation) => CompareRemaining(observation, false, true);
    [Theory, MemberData(nameof(RemainingCircuits))]
    public void Remaining_trap_switch_plate_timer_and_cooldown_match_original(string observation) =>
        Real_world_switch_plate_timer_wire_dart_and_shared_cooldown_match_original_timeline(observation);

    [Theory]
    [InlineData(185, ProjectileSimulationTerminationReason.None, false)]
    [InlineData(187, ProjectileSimulationTerminationReason.None, false)]
    [InlineData(188, ProjectileSimulationTerminationReason.None, true)]
    [InlineData(188, ProjectileSimulationTerminationReason.WorldBounds, false)]
    [InlineData(188, ProjectileSimulationTerminationReason.BehaviorKill, false)]
    [InlineData(188, ProjectileSimulationTerminationReason.LifetimeExpired, true)]
    public void Flame_entropy_marker_requires_the_source_actor_and_executed_AI(int type, ProjectileSimulationTerminationReason reason, bool accepted)
    {
        var state = new ProjectileStateUpdate(new(type), 255, 1052, 1042, 5, 0, default, 0, 40, 2, 0);
        var proposed = new ProjectileSimulationStepResult(state, reason == ProjectileSimulationTerminationReason.None ? 10 : 0,
            TerminationReason: reason)
        { WiringFlameSourceUpdate = true };
        Assert.Equal(accepted, RuntimeProjectileStateExecutor.TryNormalizeTermination(in proposed, out _));
    }

    public static IEnumerable<object[]> RemainingNpcDeaths() => Rows("WiringRemainingTrapNpcDeath1458");
    [Theory, MemberData(nameof(RemainingNpcDeaths))]
    public void Remaining_trap_lethal_contacts_complete_existing_joint_death_once(string observation) =>
        Lethal_dart_contacts_complete_existing_joint_death_once(observation);

    public static IEnumerable<object[]> RemainingNpcHits() => Rows("WiringRemainingTrapNpcHit1458");
    [Theory, MemberData(nameof(RemainingNpcHits))]
    public void Remaining_trap_npc_contact_matches_original(string observation) =>
        Dart_actual_npc_contacts_match_original_damage_immunity_status_and_entropy(observation);

    public static IEnumerable<object[]> RemainingFlights() => Rows("WiringRemainingTrapFlight1458");
    public static IEnumerable<object[]> RemainingRooms() => Rows("WiringRemainingTrapRoom1458");

    [Theory, MemberData(nameof(RemainingFlights))]
    public void Remaining_traps_match_original_full_flight_and_children(string observation) => CompareRemaining(observation, false);

    [Theory, MemberData(nameof(RemainingRooms))]
    public void Remaining_traps_match_original_enclosed_bounces_and_lifetime(string observation) => CompareRemaining(observation, true);

    private static void CompareRemaining(string observation, bool room, bool lowerSlot = false)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var fixture = new Fixture(row, 65, 136);
        if (row.GetProperty("barrier").GetBoolean())
        {
            if (room)
            {
                for (int x = 20; x < 108; x++) for (int y = 20; y < 108; y++)
                    if (x == 20 || x == 107 || y == 20 || y == 107)
                        fixture.Tiles.Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            }
            else
            {
                int o = row.GetProperty("orientation").GetInt32();
                int dx = o == 0 ? -1 : o == 1 ? 1 : 0, dy = o >= 4 ? 1 : o >= 2 ? -1 : 0;
                for (int n = -3; n <= 3; n++) fixture.Tiles.Set(65 + dx * 10 + (dx == 0 ? n : 0),
                    65 + dy * 10 + (dy == 0 ? n : 0), new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            }
        }
        TerraRuntime.Contracts.Runtime.NpcHandle target = default;
        bool contactScene = row.TryGetProperty("targetType", out var targetType);
        if (contactScene)
        {
            int type = targetType.GetInt32();
            Assert.True(fixture.Npcs.TrySpawnVanilla(new(type, (short)type, 1050, 1040, 0, 0, 255, default,
                TerraRuntime.Contracts.Runtime.NpcSimulationState.Initial with
                {
                    Life = 1000,
                    LifeMax = 1000,
                    DefenseOverride = 0,
                    HitboxOverride = new(row.GetProperty("targetWidth").GetInt32(), 42),
                    MoneyValue = 0,
                    ExtraMoneyValue = 0,
                    Midas = false,
                    Immortal = false
                }), out var npc));
            target = npc.Handle;
        }
        var placeholders = new List<TerraRuntime.Contracts.Runtime.ProjectileHandle>();
        if (lowerSlot)
        {
            using var prepared = fixture.Shots.CreateWorldSpawnPreparation();
            var dummy = new ProjectileStateUpdate(new(98), 255, 10, 10, 0, 0, default, 0, 20, 2, 0);
            for (int n = 0; n < 3; n++) { Assert.True(prepared.TryStage(in dummy, false, out var shot)); placeholders.Add(shot.Handle); }
            Assert.True(prepared.TryAdoptUnpublished(out _));
        }
        Assert.True(fixture.Fire(65, 65));
        foreach (var handle in placeholders) Assert.True(fixture.Shots.TryRemove(handle, out _));
        var buffBuffer = new TerrariaNpcBuffEntryState[20];
        foreach (var point in row.GetProperty("frames").EnumerateArray())
        {
            try
            {
                Assert.True(fixture.Actors.TryTickState(contactScene ? fixture.Combat : null));
                if (contactScene)
                {
                    Assert.True(fixture.Npcs.TryGet(target, out var npc));
                    var expected = point.GetProperty("target"); Assert.Equal(expected.GetProperty("life").GetInt32(), npc.Simulation.Life);
                    Span<TerrariaNpcBuffEntryState> buffs = buffBuffer;
                    Assert.True(fixture.Status.TryCopyWireBuffs(target, buffs, out int buffCount));
                    var types = expected.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                    var times = expected.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                    Assert.Equal(types.Count(x => x != 0), buffCount); int n = 0;
                    for (int slot = 0; slot < types.Length; slot++) if (types[slot] != 0)
                    { Assert.Equal(types[slot], buffs[n].BuffType); Assert.Equal(times[slot], buffs[n++].Duration); }
                }
                EqualProjectiles(point.GetProperty("projectiles"), fixture.Shots);
                foreach (var item in point.GetProperty("projectiles").EnumerateArray())
                {
                    Assert.True(fixture.Shots.TryGetActive(item.GetProperty("whoAmI").GetUInt16(), out var shot));
                    Assert.True(fixture.Shots.TryGetLifecycle(shot.Handle, out var life));
                    var ai = item.GetProperty("ai"); var local = item.GetProperty("localAI");
                    Assert.Equal(ai[0].GetSingle(), shot.Ai.Ai0); Assert.Equal(ai[1].GetSingle(), shot.Ai.Ai1);
                    Assert.Equal(local[0].GetSingle(), life.LocalAi.Ai0); Assert.Equal(local[1].GetSingle(), life.LocalAi.Ai1);
                }
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var rng = point.GetProperty("rng");
                Assert.Equal(rng.GetProperty("inext").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(fixture.Random)!);
                Assert.Equal(rng.GetProperty("SeedArray").EnumerateArray().Select(v => v.GetInt32()),
                    (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(fixture.Random)!);
            }
            catch (Exception error)
            {
                var active = new TerraRuntime.Contracts.Runtime.ProjectileSnapshot[fixture.Shots.Capacity];
                int count = fixture.Shots.CopyActive(active);
                string slots = string.Join(",", active.Take(count).Select(x => $"{x.Handle.Slot}:{x.Type.Value}"));
                throw new Xunit.Sdk.XunitException($"active={slots} style={row.GetProperty("style")} orientation={row.GetProperty("orientation")} room={room} tick={point.GetProperty("tick")}: {error}");
            }
        }
        if (!contactScene) Assert.Equal(0, fixture.Shots.ActiveCount);
    }
}
