using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class AnkhEquipmentPhase1458Tests
{
    public static IEnumerable<object[]> Cleanup() => AnkhEquipmentImmunity1458Tests.Rows("whole-update-cleanup");

    [Theory, MemberData(nameof(Cleanup))]
    public void Source_remote_Update_cure_preserves_this_frame_effects_and_forward_slot_compaction(int seed, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var row = doc.RootElement;
        using var f = new Fixture(seed);
        f.Equipment((short)row.GetProperty("type").GetInt32());
        f.Restore(row.GetProperty("before").GetProperty("player"));
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            f.Move();
            f.State.Tick();
            Assert.NotNull(f.Member.ItemPhase);
            var expected = step.GetProperty("after").GetProperty("player");
            var buffs = f.Buffs;
            // This comparison is the source remote buff/effect boundary. It
            // does not claim unknown wire regeneration clocks as constructor0.
            AnkhEquipmentImmunity1458Tests.Slots(expected, buffs);
            Assert.Equal(expected.GetProperty("noItems").GetBoolean(), buffs.HasNoItemsEffect);
            Assert.Equal(expected.GetProperty("poisoned").GetBoolean(), buffs.CaptureDebuffFlags().Poisoned);
        }
    }

    [Fact]
    public void An_equipment_change_after_detached_cure_cannot_adopt_buff_hp_or_entropy()
    {
        using var doc = JsonDocument.Parse((string)Cleanup().First(r => (int)r[0] == 1458 &&
            ((string)r[1]).Contains("\"type\":1612,\"scene\":\"adjacent\"", StringComparison.Ordinal))[1]);
        using var f = new Fixture(1458);
        f.Equipment(1612); f.State.Tick();
        Assert.NotNull(f.Member.ItemPhase);
        f.Restore(doc.RootElement.GetProperty("before").GetProperty("player"));
        var beforeBuffs = f.Buffs;
        beforeBuffs.CaptureSlots(out var types, out var durations);
        var phase = f.Member.ItemPhase;
        var health = f.Member.NpcHealth;
        var random = f.Random.Clone();
        int reads = 0;
        f.Players.SetNpcHealthWorldFacts(() => {
            if (++reads == 2) f.Equipment(0);
            return new(false, false);
        });
        Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.Equal(2, reads);
        Assert.Equal(phase, f.Member.ItemPhase);
        Assert.Equal(health, f.Member.NpcHealth);
        Assert.True(random.HasSameState(f.Random));
        f.Buffs.CaptureSlots(out var afterTypes, out var afterDurations);
        Assert.Equal(types, afterTypes); Assert.Equal(durations, afterDurations);
    }

    [Fact]
    public void Existing_unowned_affliction_and_unknown_mask_fences_remain_explicit()
    {
        using var f = new Fixture(1458);
        f.Equipment(1612);
        var unsupportedTypes = new BuffTypeId[44]; var unsupportedTimes = new int[44];
        unsupportedTypes[0] = new(156); unsupportedTimes[0] = 120;
        f.Profiles.RestoreBuffState(f.Connection,
            PlayerBuffState.FromSlots(unsupportedTypes, unsupportedTimes));
        var checkpoint = f.Random.Clone();
        f.State.Tick();
        Assert.Null(f.Member.ItemPhase);
        Assert.True(checkpoint.HasSameState(f.Random));
        Assert.Equal(120, f.Buffs.GetDuration(new(156)));
        var invalid = VanillaPlayerCombatSnapshot.Baseline with {
            BuffImmunity = (VanillaPlayerBuffImmunity1458)32768 };
        Assert.False(VanillaPlayerCombatBuffs1458.TryApply(in invalid, [], out _));
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryGetAccessoryImmunity(new(1613), out _));
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryGetAccessoryImmunity(new(1921), out _));
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryGetAccessoryImmunity(new(5355), out _));
    }

    [Fact]
    public void Source_derived_immunity_retires_when_the_next_accepted_phase_removes_the_accessory()
    {
        using var f = new Fixture(1458);
        f.Equipment(1612); f.State.Tick();
        Assert.NotNull(f.Member.ItemPhase);
        var before = f.Member.ItemPhase.Value.DerivedCombat!.Value;
        Assert.True(before.IsImmuneToNpcContactBuff(new(20)));
        f.Equipment(0);
        // An ingress report is not a replacement source UpdateEquips phase.
        Assert.Equal(before, f.Member.ItemPhase!.Value.DerivedCombat);
        f.State.Tick();
        Assert.NotNull(f.Member.ItemPhase);
        Assert.False(f.Member.ItemPhase.Value.DerivedCombat!.Value.IsImmuneToNpcContactBuff(new(20)));
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal readonly ConnectionHandle Connection;
        internal readonly VanillaUnifiedRandom1458 Random;
        private readonly PlayerJoinSession session;
        private readonly bool grounded;
        internal RuntimePlayerMember Member { get { Assert.True(Players.TryGet(Connection, out var member)); return member; } }
        internal RuntimePlayerTransferProfileStore Profiles => (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority)
            .GetField("transferProfiles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Players)!;
        internal PlayerBuffState Buffs => Profiles.CaptureBuffState(Connection)!;

        internal Fixture(int seed, int height = 300, bool grounded = false, bool emptyTickBeforeJoin = false)
        {
            this.grounded = grounded;
            var tiles = new WorldTileStore(new WorldDimensions(400, height));
            if (grounded) for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectiles = new RuntimeProjectileStore();
            State = new(worldTiles: tiles, projectiles: projectiles, playerUpdateRandomSeed: new(seed));
            if (emptyTickBeforeJoin) State.Tick();
            var permission = new RuntimeToolBuildingPermission1458(tiles);
            Assert.True(permission.TrySetSourceEvent(false, null)); State.BindToolBuildingPermission(permission);
            Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!).Players;
            Random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority).GetField("playerUpdateRandom",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Players)!;
            Players.SetPlayerUpdateWorldFacts(new(400, height, 80, false, false)
                { WindowsItemPrefixArithmetic = false, JourneyMode = false });
            Players.SetRemotePlayerEnvironment(new(false, false), projectiles);
            var pool = new PlayerSlotPool(1); Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(9180), session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 100, 100)));
            State.Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, (short)(grounded ? 20 : 0), 20)));
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 50, 52, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerAppearanceRuntimeCommand(Connection, new(session.Slot, 0, 0, 0, 0,
                "Equipment", 0, 0, 0, default, default, default, default, default, default, default, 0, 0, 0)));
            if (grounded) for (short slot = 0; slot < 59; slot++) State.Apply(new PlayerEquipmentRuntimeCommand(Connection, new(session.Slot, slot, 0, 0, 0, 0)));
            State.Apply(new PlayerEquipmentRuntimeCommand(Connection, new(session.Slot, 0, 1, 0, 24, 0)));
            Move();
        }
        internal void Equipment(short type) => State.Apply(new PlayerEquipmentRuntimeCommand(Connection,
            new(session.Slot, 62, (short)(type == 0 ? 0 : 1), 0, type, 0)));
        internal void Move(bool? held = null, float velocityX = 0f, float velocityY = 0f, bool local = false, bool success = false, bool both = false) => State.Apply(new PlayerMovementRuntimeCommand(Connection,
            new(session.Slot, grounded ? (byte)((both ? 12 : 8) | (held != false ? 32 : 0)) : (byte)32,
                (byte)(16 | (velocityX != 0f || velocityY != 0f ? 4 : 0)), 0, success ? (byte)64 : (byte)0, 0,
                grounded ? 1600 : 736, grounded ? 1606 : 822, velocityX != 0f || velocityY != 0f, velocityX, velocityY, false, 0, false, 0, 0, 0, 0, false, 0, 0))
                { ItemUseCaller = local ? RuntimeItemUseCaller1458.LocalOwnerProjection : RuntimeItemUseCaller1458.DedicatedRemote });
        internal void SetSlot(short slot, short type, short stack, byte prefix = 0) =>
            State.Apply(new PlayerEquipmentRuntimeCommand(Connection, new(session.Slot, slot, stack, prefix, type, 0)));
        internal void Restore(JsonElement source) => Profiles.RestoreBuffState(Connection,
            PlayerBuffState.FromSlots(source.GetProperty("buffType").EnumerateArray().Select(x => new BuffTypeId(x.GetInt32())).ToArray(),
                source.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()).ToArray()));
        public void Dispose() => session.Dispose();
    }
}
