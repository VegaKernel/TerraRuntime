using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class MiningToolCombat1458Tests
{
    // Captured independently by executing every original Item.SetDefaults in TerrariaServer 1.4.5.8.
    // Each row is item, damage, knockback, base class critical + item critical, useTime, useAnimation.
    public static IEnumerable<object[]> OriginalTools =>
    [
        new object[] { 1, 5, 2f, 4, 13, 20 },
        new object[] { 7, 7, 5.5f, 4, 20, 30 },
        new object[] { 10, 5, 4.5f, 4, 19, 27 },
        new object[] { 45, 20, 6f, 4, 15, 30 },
        new object[] { 103, 9, 3f, 4, 15, 20 },
        new object[] { 104, 24, 6f, 4, 19, 45 },
        new object[] { 122, 12, 2f, 4, 18, 23 },
        new object[] { 196, 2, 5.5f, 4, 25, 37 },
        new object[] { 204, 20, 7f, 4, 16, 30 },
        new object[] { 217, 20, 7f, 4, 14, 27 },
        new object[] { 367, 26, 7.5f, 4, 14, 27 },
        new object[] { 654, 7, 5.5f, 4, 20, 30 },
        new object[] { 657, 4, 5.5f, 4, 23, 33 },
        new object[] { 660, 10, 5.5f, 4, 19, 29 },
        new object[] { 776, 10, 5f, 4, 13, 25 },
        new object[] { 777, 15, 5f, 4, 10, 25 },
        new object[] { 778, 20, 5f, 4, 8, 25 },
        new object[] { 787, 26, 7.5f, 4, 14, 27 },
        new object[] { 797, 23, 6f, 4, 19, 40 },
        new object[] { 798, 12, 3.5f, 4, 14, 22 },
        new object[] { 799, 22, 6f, 4, 15, 32 },
        new object[] { 882, 4, 2f, 4, 16, 25 },
        new object[] { 922, 7, 5.5f, 4, 20, 30 },
        new object[] { 990, 35, 4.75f, 4, 7, 25 },
        new object[] { 991, 33, 5f, 4, 13, 35 },
        new object[] { 992, 39, 6f, 4, 10, 35 },
        new object[] { 993, 43, 7f, 4, 8, 35 },
        new object[] { 1188, 12, 5f, 4, 12, 25 },
        new object[] { 1195, 17, 5f, 4, 9, 25 },
        new object[] { 1202, 27, 5f, 4, 7, 25 },
        new object[] { 1222, 36, 5.5f, 4, 12, 35 },
        new object[] { 1223, 41, 6.5f, 4, 9, 35 },
        new object[] { 1224, 44, 7.5f, 4, 7, 35 },
        new object[] { 1230, 40, 5f, 4, 7, 25 },
        new object[] { 1233, 70, 7f, 4, 7, 30 },
        new object[] { 1234, 80, 8f, 4, 14, 35 },
        new object[] { 1294, 34, 5.5f, 4, 6, 16 },
        new object[] { 1305, 72, 7.25f, 4, 7, 23 },
        new object[] { 1320, 8, 3f, 4, 11, 19 },
        new object[] { 1506, 32, 5.25f, 4, 8, 24 },
        new object[] { 1507, 60, 7f, 4, 8, 28 },
        new object[] { 1917, 7, 2.5f, 4, 16, 20 },
        new object[] { 2176, 45, 6f, 4, 4, 12 },
        new object[] { 2320, 24, 6f, 4, 14, 24 },
        new object[] { 2341, 16, 3f, 4, 13, 22 },
        new object[] { 2516, 4, 5.5f, 4, 23, 33 },
        new object[] { 2746, 4, 5.5f, 4, 23, 33 },
        new object[] { 2776, 80, 5.5f, 4, 6, 12 },
        new object[] { 2781, 80, 5.5f, 4, 6, 12 },
        new object[] { 2786, 80, 5.5f, 4, 6, 12 },
        new object[] { 3466, 80, 5.5f, 4, 6, 12 },
        new object[] { 3481, 10, 5.5f, 4, 21, 27 },
        new object[] { 3482, 8, 4.5f, 4, 17, 25 },
        new object[] { 3485, 7, 2f, 4, 15, 19 },
        new object[] { 3487, 9, 5.5f, 4, 25, 28 },
        new object[] { 3488, 7, 4.5f, 4, 18, 26 },
        new object[] { 3491, 6, 2f, 4, 19, 21 },
        new object[] { 3493, 8, 5.5f, 4, 19, 29 },
        new object[] { 3494, 6, 4.5f, 4, 19, 28 },
        new object[] { 3497, 6, 2f, 4, 12, 19 },
        new object[] { 3499, 6, 5.5f, 4, 21, 31 },
        new object[] { 3500, 4, 4.5f, 4, 20, 28 },
        new object[] { 3503, 5, 2f, 4, 14, 21 },
        new object[] { 3505, 4, 5.5f, 4, 23, 33 },
        new object[] { 3506, 3, 4.5f, 4, 21, 30 },
        new object[] { 3509, 4, 2f, 4, 15, 23 },
        new object[] { 3511, 9, 5.5f, 4, 19, 29 },
        new object[] { 3512, 6, 4.5f, 4, 18, 26 },
        new object[] { 3515, 6, 2f, 4, 11, 19 },
        new object[] { 3517, 9, 5.5f, 4, 23, 28 },
        new object[] { 3518, 7, 4.5f, 4, 18, 26 },
        new object[] { 3521, 6, 2f, 4, 17, 20 },
        new object[] { 3522, 60, 7f, 4, 7, 28 },
        new object[] { 3523, 60, 7f, 4, 7, 28 },
        new object[] { 3524, 60, 7f, 4, 7, 28 },
        new object[] { 3525, 60, 7f, 4, 7, 28 },
        new object[] { 4059, 8, 4f, 4, 14, 18 },
        new object[] { 4317, 30, 7f, 4, 11, 27 },
        new object[] { 5095, 27, 5f, 14, 15, 15 },
        new object[] { 5295, 20, 5f, 4, 12, 24 },
    ];

    [Theory]
    [MemberData(nameof(OriginalTools))]
    public void Complete_invariant_tool_family_matches_original_defaults(
        int id, int damage, float knockBack, int crit, int useTime, int animation)
    {
        Assert.True(VanillaItemCombatCatalog.TryGetDirectMelee(new ItemTypeId(id), out var tool));
        Assert.Equal(damage, tool.BaseDamage);
        Assert.Equal(knockBack, tool.BaseKnockBack);
        Assert.Equal(crit, tool.BaseCrit);
        Assert.Equal(useTime, tool.UseTimeTicks);
        Assert.Equal(animation, tool.AnimationTicks);
    }

    [Theory]
    [MemberData(nameof(OriginalTools))]
    public void Real_npc_hit_rejects_forged_damage_then_uses_tool_authority_and_retains_stack(
        int id, int damage, float knockBack, int crit, int useTime, int animation)
    {
        _ = (knockBack, crit, useTime, animation);
        using var fixture = new Fixture();
        ConnectionHandle owner = fixture.SpawnPlayer(id);
        fixture.Equip(owner, id);
        NpcSnapshot npc = fixture.SpawnNpc();
        var wire = new TerrariaNpcDamageState(npc.Handle.Slot,
            RuntimeNpcPacketProjection.ToProtocolGeneration(npc.Handle.Generation), short.MaxValue, 0f, 2, 0);
        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(owner, wire));
        Assert.True(fixture.Npcs.TryGet(npc.Handle, out var unchanged));
        Assert.Equal(1000, unchanged.Simulation.Life);
        Assert.Equal(1, fixture.State.RejectedClientNpcDamage);
        wire = wire with { Damage = 1 };
        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(owner, wire));
        Assert.Equal(1, fixture.State.AppliedClientNpcDamage);
        Assert.True(fixture.Npcs.TryGet(npc.Handle, out var hit));
        Assert.InRange(1000 - hit.Simulation.Life, Math.Max(1, (int)Math.Round(damage * .85f)), (int)Math.Round(damage * 1.15f) * 2);
        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(owner, wire));
        Assert.Equal(1, fixture.State.AppliedClientNpcDamage);
        Assert.Equal(2, fixture.State.RejectedClientNpcDamage);
        Assert.True(fixture.State.TryCapturePlayerInventoryItem(owner.Player, 0, out var held));
        Assert.Equal((short)1, held.Stack);
        Assert.Equal(new ItemTypeId(id), held.ItemType);
    }

    [Theory]
    [MemberData(nameof(OriginalTools))]
    public void Real_pvp_hit_uses_same_tool_family_and_rejects_forged_claim_before_hp_mutation(
        int id, int damage, float knockBack, int crit, int useTime, int animation)
    {
        _ = (knockBack, crit, useTime, animation);
        using var fixture = new Fixture();
        ConnectionHandle owner = fixture.SpawnPlayer(id);
        ConnectionHandle target = fixture.SpawnPlayer(id + 10000);
        fixture.Equip(owner, id);
        var reason = new TerrariaPlayerDeathReasonState(owner.Player.Slot.Value, -1, -1, -1, 0, 0, 0, null);
        var wire = new TerrariaPlayerHurtState(target.Player.Slot.Value, reason, short.MaxValue, 2, 2, -1);
        fixture.State.Apply(new ClientPlayerPvpHitRuntimeCommand(owner, wire));
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out var unchanged));
        Assert.Equal((short)1000, unchanged.Life);
        fixture.State.Apply(new ClientPlayerPvpHitRuntimeCommand(owner, wire with { Damage = 1 }));
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out var hit));
        Assert.InRange(1000 - hit.Life, Math.Max(1, (int)Math.Round(damage * .85f)), (int)Math.Round(damage * 1.15f) * 2);
        fixture.State.Apply(new ClientPlayerPvpHitRuntimeCommand(owner, wire with { Damage = 1 }));
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out var repeated));
        Assert.Equal(hit.Life, repeated.Life);
    }

    [Fact]
    public void Lucy_axe_item_critical_contribution_is_added_once_and_pvp_keeps_source_ten_percent()
    {
        Assert.True(VanillaItemCombatCatalog.TryGetDirectMelee(new ItemTypeId(5095), out var tool));
        var prefix = VanillaCombatPrefixModifiers.Identity;
        var attacker = VanillaPlayerCombatSnapshot.Baseline;
        // Player.Update adds selected Item.crit to meleeCrit before GetWeaponCrit returns it: 4 + 10.
        var pve = VanillaDirectMeleeCombatMath.Resolve(in tool, in prefix, in attacker, 0, 14, pvp: false);
        Assert.Equal(14, pve.CritChance);
        Assert.True(pve.Critical);
        Assert.False(VanillaDirectMeleeCombatMath.Resolve(in tool, in prefix, in attacker, 0, 15, false).Critical);
        Assert.Equal(10, VanillaDirectMeleeCombatMath.Resolve(in tool, in prefix, in attacker, 0, 14, true).CritChance);
    }

    [Theory]
    [InlineData(5283)] // Ash Wood Hammer has a Remix-dependent ItemVariant.
    [InlineData(579)] // Drax uses a drill projectile/noMelee.
    [InlineData(2774)] // Solar Flare Drill.
    public void Unowned_variant_and_projectile_tool_families_remain_unadmitted(int id) =>
        Assert.False(VanillaItemCombatCatalog.TryGetDirectMelee(new ItemTypeId(id), out _));

    [Fact]
    public void Lethal_tool_hit_runs_death_once_and_stale_connection_cannot_hit_replacement()
    {
        using var fixture = new Fixture();
        ConnectionHandle owner = fixture.SpawnPlayer(42);
        fixture.Equip(owner, 1);
        NpcSnapshot npc = fixture.SpawnNpc(life: 1);
        var wire = new TerrariaNpcDamageState(npc.Handle.Slot,
            RuntimeNpcPacketProjection.ToProtocolGeneration(npc.Handle.Generation), 1, 0f, 2, 0);
        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(owner, wire));
        Assert.False(fixture.Npcs.TryGet(npc.Handle, out _));
        Assert.Equal(1, fixture.State.AppliedClientNpcDamage);
        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(owner, wire));
        Assert.Equal(1, fixture.State.AppliedClientNpcDamage);
        fixture.State.Apply(new PlayerDisconnectRuntimeCommand(owner));
        fixture.Release(owner);
        ConnectionHandle replacement = fixture.SpawnPlayer(43);
        Assert.Equal(owner.Player.Slot, replacement.Player.Slot);
        Assert.NotEqual(owner.Player.Generation, replacement.Player.Generation);
        fixture.Equip(replacement, 1);
        NpcSnapshot next = fixture.SpawnNpc();
        var nextWire = wire with { NpcSlot = next.Handle.Slot, Generation = RuntimeNpcPacketProjection.ToProtocolGeneration(next.Handle.Generation) };
        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(owner, nextWire));
        Assert.True(fixture.Npcs.TryGet(next.Handle, out var untouched));
        Assert.Equal(1000, untouched.Simulation.Life);
        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(replacement, nextWire));
        Assert.Equal(2, fixture.State.AppliedClientNpcDamage);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(2);
        private readonly Dictionary<PlayerHandle, PlayerJoinSession> sessions = [];
        public RuntimeNpcStore Npcs { get; } = new(capacity: 2);
        public ServerRuntimeState State { get; }

        public Fixture() => State = new ServerRuntimeState(npcs: Npcs);

        public ConnectionHandle SpawnPlayer(long id)
        {
            Assert.True(slots.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            sessions.Add(session.Handle, session);
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
            var owner = new ConnectionHandle(GameCommandSourceId.FromConnection(id), session.Handle);
            State.Apply(new PlayerSpawnRuntimeCommand(owner, session, new PlayerSpawnCommitRequest(session.Slot, 10, 10, 0, 0, 0, 0, 0)));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            State.Apply(new PlayerEquipmentRuntimeCommand(owner, new PlayerEquipmentCommitRequest(owner.Player.Slot, VanillaPlayerItemSlotCatalog.ArmorStart, 0, 0, 0, 0)));
            State.Apply(new PlayerHealthRuntimeCommand(owner, new PlayerHealthCommitRequest(owner.Player.Slot, 1000, 1000)));
            State.Apply(new PlayerPvpToggleRuntimeCommand(owner, true));
            State.Apply(new PlayerMovementRuntimeCommand(owner, new PlayerMovementCommitRequest(
                owner.Player.Slot, 0, 0, 0, 0, 0, 100f, 100f, false, 0f, 0f, false, 0, false, 0f, 0f, 0f, 0f, false, 0f, 0f)));
            return owner;
        }

        public void Equip(ConnectionHandle owner, int id) => State.Apply(new PlayerEquipmentRuntimeCommand(owner,
            new PlayerEquipmentCommitRequest(owner.Player.Slot, 0, 1, 0, checked((short)id), 0)));

        public NpcSnapshot SpawnNpc(int life = 1000)
        {
            var state = new NpcStateUpdate(1, 1, 110f, 100f, 0f, 0f, 255, default,
                NpcSimulationState.Initial with { Life = life, LifeMax = life, DefenseOverride = 0, Scale = 1f });
            Assert.True(Npcs.TrySpawn(0, in state, out var npc));
            return npc;
        }

        public void Release(ConnectionHandle owner)
        {
            sessions[owner.Player].Dispose();
            sessions.Remove(owner.Player);
        }

        public void Dispose()
        {
            foreach (var session in sessions.Values) session.Dispose();
        }
    }
}
