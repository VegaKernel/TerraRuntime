using Xunit;
using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Network;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Players;

using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

internal static class FoodConsumerBoundaryChecks
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "npcs")]
    private static extern ref RuntimeNpcStore RootNpcs(NpcAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "projectiles")]
    private static extern ref RuntimeProjectileStore RootShots(ProjectileAuthority owner);
    private static int CheckFundedNpcContact(short food, short ammunition, bool noItems = false)
    {
        bool paidFood = food != 0;
        using var f = new FoodConsumerFixture("empty-selected", runtimeTick: true);
        var c = f.Connections[2];
        var m = f.Member(2);
        var inv = Inventory(f.Players);
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c)); f.Tick();
        if (paidFood)
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, food, 0)));
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 1, 1, 0, 39, 0)));
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 54, 3, 0, ammunition, 0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        short[] q = new short[59]; q[0] = paidFood ? (short)1 : (short)0; q[54] = 1;
        inv.AdoptFundedQuantities(c, q);
        f.Move(2, true); f.Tick();
        Assert.Equal(paidFood, f.Buffs(2).HasOwnedFood);
        f.Move(2, false); for (int i = 0; i < 55; i++) f.Tick();
        Assert.True(m.ItemPhase.HasValue);
        Assert.Equal(paidFood, m.ItemPhase.Value.DerivedCombat!.Value.WellFed);
        f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(m.MovementFlags | 16),
            0, 64, 1, m.PositionX, m.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        f.Tick(); Assert.True(m.ItemPhase.HasValue);
        var combat = m.ItemPhase.Value.DerivedCombat!.Value;
        Assert.True(VanillaOrdinaryBowLaunch1458.TryResolve(new(39), default, new(ammunition), in combat, out var launch));
        var packet = new TerrariaProjectileUpdateState(new(c.Player.Slot.Value, 901, 1), checked((short)launch.ProjectileType.Value),
            m.PositionX + 5f, m.PositionY + 16f, launch.Speed, 0, 0, 0, 0, 0, (short)launch.Damage, launch.KnockBack, 0);
        f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet));
        var composition = Composition(f.Runtime);
        Assert.Equal(1, composition.Projectiles.PromotedClientProjectileSpawns);
        Assert.True(inv.TryGet(c, 54, out var paidAmmo));
        Assert.Equal((short)2, paidAmmo.Stack);
        Assert.Equal((short)0, inv.CaptureFundedQuantities(c)[54]);
        if (paidFood) Assert.Equal((short)0, inv.CaptureFundedQuantities(c)[0]);
        var shots = RootShots(composition.Projectiles);
        var images = new ProjectileSnapshot[shots.Capacity];
        Assert.Equal(1, shots.CopyActive(images));
        var shot = images[0];
        Assert.True(shots.IsCombatTrusted(shot.Handle));
        if (noItems)
        {
            // Apply after the funded birth: NoItems must not manufacture another use,
            // but it cannot suppress the existing ordinary arrow's source contact.
            f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, new BuffTypeId[] { new(23) })));
            Assert.True(f.Players.TryCaptureProjectileUse(c.Player, out var noItemsOwner));
            Assert.Contains(new BuffTypeId(23), noItemsOwner!.Buffs!);
        }
        var npcs = RootNpcs(composition.Npcs);
        var state = new NpcStateUpdate(3, 3, shot.PositionX, shot.PositionY, 0, 0, 0, default,
            NpcSimulationState.Initial with { Life = 45, LifeMax = 45, LifeRegenCounter = 0,
                Immortal = false, MoneyValue = 0, ExtraMoneyValue = 0, ShimmerTransparency = 0 });
        Assert.True(npcs.TrySpawn(0, in state, out var actor));
        var pass = composition.Npcs.ProjectileNpcCombat;
        long beforeHits = pass.CommittedHits;
        Assert.True(f.Players.TryCaptureProjectileUse(c.Player, out var owner));
        Assert.True(composition.Projectiles.TryTickState(pass));
        pass.Tick();
        Assert.True(npcs.TryGet(actor.Handle, out var after));
        Assert.Equal(1L, pass.CommittedHits - beforeHits);
        Assert.True(after.Simulation.Life < 45);
        return 45 - after.Simulation.Life;
    }

    private static JsonDocument Read(string name)
    {
        using var stream = typeof(FoodConsumerBoundaryChecks).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing original source resource: " + name);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "durations")]
    private static extern ref int[] BuffDurations(PlayerBuffState state);
    public static void Run()
    {
        int clearOrdinaryArrowDamage = 0;
        foreach (short food in new short[] { 0, 967, 357, 4022 })
            foreach (short ammunition in new short[] { 40, 47, 51 })
            {
                int damage = CheckFundedNpcContact(food, ammunition);
                if (food == 0 && ammunition == 40) clearOrdinaryArrowDamage = damage;
            }
        Assert.Equal(clearOrdinaryArrowDamage, CheckFundedNpcContact(0, 40, noItems: true));
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var source = Read("FoodConsumers" + platform + "1458");
            var row = source.RootElement.EnumerateArray().Single(r => r.GetProperty("kind").GetString() == "capped-mixed-consumer" &&
                r.GetProperty("tier").GetInt32() == 207 && r.GetProperty("gear").GetInt32() == 2758 &&
                r.GetProperty("reverse").GetBoolean() && !r.GetProperty("nontrivial").GetBoolean());
            var capped = row.GetProperty("steps").EnumerateArray().Single(r => r.GetProperty("name").GetString() == "CapAttackSpeeds").GetProperty("after");
            Assert.Equal(BitConverter.SingleToInt32Bits(1.32f), BitConverter.SingleToInt32Bits(capped.GetProperty("rangedDamage").GetSingle()));
            Assert.Equal(18, row.GetProperty("weapons").EnumerateArray().Single(w => w.GetProperty("id").GetInt32() == 1).GetProperty("animation").GetInt32());
            Assert.Equal(5, row.GetProperty("weapons").EnumerateArray().Single(w => w.GetProperty("id").GetInt32() == 39).GetProperty("damage").GetInt32());
        }
        foreach (short food in new short[] { 967, 357, 4022 })
        {
            using var f = new FoodConsumerFixture("empty-selected", runtimeTick: true);
            var c = f.Connections[2];
            var m = f.Member(2);
            var inv = Inventory(f.Players);
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            f.Tick();
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, food, 0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            short[] q = new short[59];
            q[0] = 1;
            inv.AdoptFundedQuantities(c, q);
            f.Move(2, true);
            f.Tick();
            Assert.True(f.Buffs(2).HasOwnedFood);
            f.Move(2, false);
            for (int i = 0; i < 55; i++)
                f.Tick();
            Assert.True(m.ItemPhase.HasValue);
            Assert.Equal(0, m.ItemPhase.Value.Selected.Animation);
            // Source-supported flat grounded boundary; movement packet starts from rest.
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 8, (byte)(m.MovementFlags | 16), 0, 64, 0, m.PositionX, m.PositionY, true, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            float x = m.PositionX;
            f.Tick();
            Assert.True(m.ItemPhase.HasValue);
            float factor = food == 967 ? 1.2f : food == 357 ? 1.3f : 1.4f;
            float acceleration = .08f * factor;
            Assert.Equal(BitConverter.SingleToInt32Bits(acceleration), BitConverter.SingleToInt32Bits(m.VelocityX));
            Assert.Equal(BitConverter.SingleToInt32Bits(x + acceleration), BitConverter.SingleToInt32Bits(m.PositionX));
            Console.WriteLine($"PASS movement food={food} vx={m.VelocityX} x={m.PositionX}");
        }
        using (var f = new FoodConsumerFixture("empty-selected", runtimeTick: true))
        {
            var c = f.Connections[2];
            var m = f.Member(2);
            var inv = Inventory(f.Players);
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            f.Tick();
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 4022, 0)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 1, 1, 0, 1, 0)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 2, 1, 0, 39, 0)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 54, 3, 0, 40, 0)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 60, 1, 0, 2758, 0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, new BuffTypeId[] { new(5), new(117) })));
            short[] q = new short[59];
            q[0] = 1;
            q[54] = 1;
            inv.AdoptFundedQuantities(c, q);
            f.Move(2, true);
            f.Tick();
            Assert.True(f.Buffs(2).HasOwnedFood);
            f.Move(2, false);
            for (int i = 0; i < 55; i++)
                f.Tick();
            Assert.True(m.ItemPhase.HasValue);

            Assert.Equal(BitConverter.SingleToInt32Bits(1.32f), BitConverter.SingleToInt32Bits(m.ItemPhase.Value.DerivedCombat!.Value.RangedDamage));
            Assert.True(m.ItemPhase.Value.ToolUse!.LocalClocksKnown);
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(m.MovementFlags | 16), 0, 64, 1, m.PositionX, m.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            f.Tick();
            Assert.True(m.ItemPhase.HasValue);
            Assert.Equal(18, m.ItemPhase.Value.Selected.AnimationMax);
            Assert.True(m.ItemPhase.Value.ToolUse!.LocalClocksKnown);
            Assert.True(m.ItemPhase.Value.ToolUse.UseEpoch > 0);
            Console.WriteLine($"PASS source-capped Pick1 food+gear cadence={m.ItemPhase.Value.Selected.AnimationMax}");
            f.Move(2, false);
            for (int i = 0; i < 25; i++)
                f.Tick();
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(m.MovementFlags | 16), 0, 64, 2, m.PositionX, m.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            f.Tick();
            Assert.True(m.ItemPhase.HasValue);
            var combat = m.ItemPhase.Value.DerivedCombat!.Value;
            Assert.True(VanillaOrdinaryBowLaunch1458.TryResolve(new(39), default, new(40), in combat, out var launch));
            // Independent source GetWeaponDamage39=5; source ammo40 damage5 truncated after the captured1.32 multiplier gives6.
            Assert.Equal(11, launch.Damage);
            var packet = new TerrariaProjectileUpdateState(new(c.Player.Slot.Value, 901, 1), 1, m.PositionX + 5f, m.PositionY + 16f, launch.Speed, 0, 0, 0, 0, 0, 11, 2f, 0);
            f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet));
            Assert.Equal(1, Composition(f.Runtime).Projectiles.PromotedClientProjectileSpawns);
            Assert.True(inv.TryGet(c, 54, out var ammo));
            Assert.Equal((short)2, ammo.Stack);
            Assert.Equal((short)0, inv.CaptureFundedQuantities(c)[54]);
            Console.WriteLine("PASS actual owned Bow39 food+gear damage11 and paid ammo debit");
        }
        using (var f = new FoodConsumerFixture("empty-selected", runtimeTick: true, expert: true))
        {
            var c = f.Connections[2];
            var m = f.Member(2);
            var inv = Inventory(f.Players);
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            f.Tick();
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 4022, 0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            short[] q = new short[59];
            q[0] = 1;
            inv.AdoptFundedQuantities(c, q);
            f.Move(2, true);
            f.Tick();
            f.Move(2, false);
            for (int i = 0; i < 55; i++)
                f.Tick();
            var before = f.Buffs(2);
            var terminal = before.Clone();
            terminal.CaptureSlots(out var t, out _);
            BuffDurations(terminal)[Array.FindIndex(t, b => b.Value == 207)] = 1;
            Assert.True(Profiles(f.Players).TryAdoptBuffState(c, before, terminal));
            f.Players.SetNpcHealthWorldFacts(() => new(true, false));
            m.NpcHealth = new PlayerNpcHealthState1458(50, 0, 1200f, true);
            f.Tick();
            Console.WriteLine($"REGEN phase={m.ItemPhase.HasValue} fed={m.ItemPhase?.DerivedCombat?.WellFed} hp={m.NpcHealth} duration={f.Buffs(2).GetDuration(new(207))}");
            Assert.True(m.ItemPhase.HasValue);
            Assert.True(m.ItemPhase.Value.DerivedCombat!.Value.WellFed);
            Assert.Equal(0, f.Buffs(2).GetDuration(new(207)));
            Console.WriteLine($"REGEN terminal count={m.NpcHealth?.RegenCount} time={m.NpcHealth?.RegenTime}");
            Assert.Equal(5, m.NpcHealth!.Value.RegenCount);
            m.NpcHealth = new PlayerNpcHealthState1458(50, 0, 1200f, true);
            f.Tick();
            Assert.True(m.ItemPhase.HasValue);
            Assert.False(m.ItemPhase.Value.DerivedCombat!.Value.WellFed);
            Console.WriteLine($"REGEN expired count={m.NpcHealth?.RegenCount} time={m.NpcHealth?.RegenTime}");
            Assert.Equal(2, m.NpcHealth!.Value.RegenCount);
        }
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "transferProfiles")]
    private static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateRandom")]
    private static extern ref VanillaUnifiedRandom1458? UpdateRandom(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState owner);
    private sealed class FoodConsumerFixture : IDisposable
    {
        internal readonly PlayerAuthority Players;
        internal ServerRuntimeState Runtime => state!;
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 300));
        internal readonly VanillaUnifiedRandom1458 Random;
        private readonly ServerRuntimeState? state;
        internal readonly FoodConsumerEventSink Events = new();
        internal readonly Dictionary<byte, ConnectionHandle> Connections = [];
        internal readonly Dictionary<byte, short> ItemIds = [];
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly HashSet<byte> moved = [];
        private readonly PlayerSlotPool pool = new(3);
        internal FoodConsumerFixture(string mode, bool runtimeTick = false, bool expert = false)
        {
            for (int x = 0; x < 400; x++)
                Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectileReplication = new RuntimeProjectileReplicationRegistry();
            var projectiles = new RuntimeProjectileStore(1000, projectileReplication);
            if (runtimeTick)
            {
                state = new(playerEvents: Events, worldTiles: Tiles, naturalSpawnRandom: new SystemVanillaNpcRandom(new VanillaUnifiedRandom1458(0)), projectiles: projectiles, projectileReplication: projectileReplication, expertMode: expert,
                    townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
                    {
                        WorldSurface = 80
                    },
                    playerUpdateRandomSeed: new(0),
                    sourcePlayerWorld: new(400, 300, 80, false, false)
                    { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows(), JourneyMode = false });
                ToolPhaseWorldFixture1458.BindFreshInactiveEvent(state, Tiles);
                Players = Composition(state).Players;
                Random = UpdateRandom(Players)!;
            }
            else
            {
                Players = new(Events, Tiles);
                Random = new(0);
            }
            // These prefix-none references have identical clocks on both source platforms.
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false)
            {
                WindowsItemPrefixArithmetic = OperatingSystem.IsWindows(), JourneyMode = false
            });
            Players.SetPlayerUpdateRandom(Random);
            Players.SetRemotePlayerEnvironment(new(expert, false), projectiles);
            Players.SetNpcHealthWorldFacts(() => new(expert, false));
            Players.SetNpcHealthGrapplingFacts(_ => false);
            for (int i = 0; i < 3; i++)
            {
                Assert.True(pool.TryAcquireConnection(out var lease));
                sessions.Add(new(lease!));
            }
            var membership = Membership(Players);
            // Slot255 is not a connection lease. A trusted imported source placeholder remains untouched.
            membership.Commit(new RuntimePlayerMember
            {
                Slot = new(255),
                Revision = 1,
                Connection = new(GameCommandSourceId.FromConnection(99255),
                new(new(255), new(1))),
                ItemPhase = null,
                PhysicsPhase = null
            });
            foreach (byte slot in mode == "reverse-join" ? new byte[] { 2, 0 } : new byte[] { 0, 2 })
            {
                if (slot == 0 && mode is "inactive-first" or "empty-selected")
                    continue;
                var session = sessions[slot];
                session.ObserveWorldRequest();
                session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99000 + slot), session.Handle);
                Connections.Add(slot, connection);
                short id = slot == 2 ? (mode == "empty-selected" ? (short)0 : (short)28) : slot == 0 && mode == "potion-first" ? (short)28 : slot == 0 && mode == "weapon98-first" ? (short)98 : (short)0;
                if (slot == 2 && mode == "potion-first")
                    id = 0;
                ItemIds.Add(slot, id);
                Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
                Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
                Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, id == 0 ? (short)0 : (short)3, 0, id, 0)));
                Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
                Move(slot, false);
                // Explicit source-configured imported lifecycle branches, after genuine membership creation.
                // Packet movement admission is not weakened to inject outside coordinates.
                if (slot == 0 && mode == "outside-first")
                {
                    Member(slot).PositionX = 0;
                    Member(slot).PositionY = 0;
                }
                if (slot == 0 && mode == "dead-first")
                    Member(slot).IsDead = true;
                if (slot == 0 && mode == "ghost-first")
                    Member(slot).MovementFlags |= VanillaPlayerHealthContext1458.GhostMovementFlag;
            }
        }
        internal RuntimePlayerMember Member(byte slot)
        {
            Assert.True(Players.TryGet(slot, out var member));
            return member;
        }
        internal void Apply(RuntimeCommand command)
        {
            if (state is null)
                Assert.True(Players.TryApply(command));
            else
                state.Apply(command);
        }
        internal void Tick()
        {
            Assert.NotNull(state);
            state.Tick();
        }
        internal bool Step()
        {
            if (state is null)
                return Players.TryTickRemotePlayerPhase();
            state.Tick();
            return Member(2).ItemPhase is not null;
        }
        internal void ReconnectFirst()
        {
            Apply(new PlayerDisconnectRuntimeCommand(Connections[0]));
            sessions[0].Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!);
            sessions.Add(session);
            Assert.Equal(new PlayerSlotId(0), session.Slot);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99100), session.Handle);
            Connections[0] = connection;
            Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
            Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, 0)));
            Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
        }
        internal void Move(byte slot, bool control)
        {
            var member = Member(slot);
            bool first = moved.Add(slot);
            if (!first && member.PositionX == 0 && member.PositionY == 0)
            {
                // Trusted source-configured outside import: ordinary movement would reject this geometry.
                member.ControlFlags = control ? (byte)32 : (byte)0;
                return;
            }
            Apply(new PlayerMovementRuntimeCommand(Connections[slot], new(new(slot), control ? (byte)32 : (byte)0,
                (byte)(member.MovementFlags | 16), 0, 64, 0, first ? 1600 + slot * 40 : member.PositionX,
                first ? 1606 : member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        internal PlayerBuffState Buffs(byte slot) => Profiles(Players).CaptureBuffState(Connections[slot])!;
        public void Dispose()
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }
    private sealed class FoodConsumerEventSink : IRuntimePlayerEventSink
    {
        internal int Count;
        internal readonly List<byte> Seen = [];
        internal Action<ConnectionHandle, PlayerEquipmentCommitRequest>? OnEquipment = null;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r)
        {
            Count++;
            Seen.Add(5);
            OnEquipment?.Invoke(c, r);
        }
        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r)
        {
            Count++;
            Seen.Add(16);
        }
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r)
        {
            Count++;
            Seen.Add(42);
        }
        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r)
        {
            Count++;
            Seen.Add(50);
        }
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
