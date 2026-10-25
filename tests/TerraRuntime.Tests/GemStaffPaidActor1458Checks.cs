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
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests.GemStaffSimulation;

public static class GemStaffPaidActor1458Checks
{
    public static void PaidActorConsumers()
    {
        using var source=Read("GemStaffBehaviorLinux1458");int cases=0;
        foreach(short staff in new short[]{739,740,741,742,743,744,3377})
        {
            var row=source.RootElement.EnumerateArray().Single(r=>r.GetProperty("local").GetBoolean()&&r.GetProperty("mode").GetString()=="ordinary"&&r.GetProperty("id").GetInt32()==staff&&r.GetProperty("robe").GetInt32()==0);
            using var f=ReadyStaff(staff);var c=f.Connections[2];var member=f.Member(2);var composition=Composition(f.Runtime);var authority=composition.Projectiles;
            short manaBefore=member.Mana;var random=new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());random.Next(0,3);ProjectileRandom(authority).CopyStateFrom(random);
            int count=0;foreach(var born in row.GetProperty("phases")[0].GetProperty("born").EnumerateArray()) f.Apply(new ClientProjectileUpdateRuntimeCommand(c,SourcePacket(f,born,(ushort)(960+count++))));
            Assert.Equal(count,authority.PromotedClientProjectileSpawns);Assert.True(member.Mana<manaBefore);short paidMana=member.Mana;
            var shots=ActorProjectiles(authority);var all=new ProjectileSnapshot[shots.Capacity];Assert.Equal(count,shots.CopyActive(all));var shot=all[0];Assert.True(shots.IsCombatTrusted(shot.Handle));
            Assert.True(shots.TryGetLifecycle(shot.Handle,out var beforeLife));Assert.NotNull(beforeLife.GemStaffFeatures);
            var npcs=ActorNpcs(composition.Npcs);var targetState=new NpcStateUpdate(1,1,shot.PositionX+shot.VelocityX,shot.PositionY+shot.VelocityY,0,0,0,default,
                NpcSimulationState.Initial with {Life=5000,LifeMax=5000,LifeRegenCounter=0,Immortal=false,MoneyValue=0,ExtraMoneyValue=0,DefenseOverride=20});
            Assert.True(npcs.TrySpawn(0,in targetState,out var target));var combat=composition.Npcs.ProjectileNpcCombat;long hits=combat.CommittedHits;
            var inventoryBeforeActor = CaptureImage(f, c);
            Assert.True(authority.TryTickState(combat));Assert.Equal(hits+count,combat.CommittedHits);Assert.Equal(paidMana,member.Mana);
            SameImage(inventoryBeforeActor, CaptureImage(f, c));
            Assert.True(npcs.TryGet(target.Handle,out var after));Assert.True(after.Simulation.Life<5000);
            if(shots.TryGet(shot.Handle,out var retained)){Assert.True(shots.TryGetLifecycle(shot.Handle,out var life));Assert.Equal(1L,life.GemStaffUpdateCount);Assert.Equal(beforeLife.GemStaffFeatures,life.GemStaffFeatures);}
            cases++;
        }
        Console.WriteLine("PAID STAFF INGRESS ACTOR CONTACT CASES "+cases);
    }

    private static TerrariaProjectileUpdateState SourcePacket(GemStaffPaidActorFixture f, JsonElement born, ushort identity)
    {
        var c = f.Connections[2];
        var m = f.Member(2);
        var pos = born.GetProperty("position");
        var vel = born.GetProperty("velocity");
        return new(new(c.Player.Slot.Value,
            identity,
            1),
            (short)born.GetProperty("type").GetInt32(),
            pos.GetProperty("x").GetSingle() + m.PositionX - 400f,
            pos.GetProperty("y").GetSingle() + m.PositionY - 400f,
            vel.GetProperty("x").GetSingle(),
            vel.GetProperty("y").GetSingle(),
            BitConverter.Int32BitsToSingle(born.GetProperty("aiBits")[0].GetInt32()),
            born.GetProperty("ai")[1].GetSingle(),
            0,
            0,
            (short)born.GetProperty("damage").GetInt32(),
            born.GetProperty("knockBack").GetSingle(),
            0);
    }

    private static GemStaffPaidActorFixture ReadyStaff(short staff, short robe = 0, bool flower = false)
    {
        var f = new GemStaffPaidActorFixture();
        var c = f.Connections[2];
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        f.Tick();
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 1, 0, staff, 0)));
        if (robe != 0)
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 60, 1, 0, robe, 0)));
        if (flower)
        {
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 1, 3, 0, 110, 0)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 2, 3, 0, 189, 0)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 62, 1, 0, 555, 0)));
            var q = Inventory(f.Players).CaptureFundedQuantities(c);
            q[1] = q[2] = 1;
            Inventory(f.Players).AdoptFundedQuantities(c, q);
        }

        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        f.Apply(new PlayerManaRuntimeCommand(c, new(c.Player.Slot, (short)(flower ? 0 : 200), 200)));
        f.Move(2, false);
        f.Tick();
        f.Move(2, true);
        return f;
    }

    private static JsonDocument Read(string name)
    {
        using var stream = typeof(GemStaffPaidActor1458Checks).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing original source resource: " + name);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static RuntimePlayerInventoryStore.ShopJournalCapture CaptureImage(GemStaffPaidActorFixture f, ConnectionHandle c)
    {
        Assert.True(Inventory(f.Players).TryCaptureShopJournal(c, out var captured));
        Assert.NotNull(captured);
        return captured!;
    }

    private static void SameImage(RuntimePlayerInventoryStore.ShopJournalCapture before, RuntimePlayerInventoryStore.ShopJournalCapture after)
    {
        Assert.Equal(219, before.Before.Length);
        Assert.Equal(before.Before, after.Before);
        Assert.Equal(before.Quantity, after.Quantity);
        Assert.Equal(before.Copper, after.Copper);
        Assert.Equal(before.Caps, after.Caps);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="projectiles")]
    private static extern ref RuntimeProjectileStore ActorProjectiles(ProjectileAuthority authority);

    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="npcs")]
    private static extern ref RuntimeNpcStore ActorNpcs(NpcAuthority authority);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "projectileRandom")]
    private static extern ref VanillaUnifiedRandom1458 ProjectileRandom(ProjectileAuthority owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] Seeds(VanillaUnifiedRandom1458 owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint Cursor(VanillaUnifiedRandom1458 owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "projectiles")]
    private static extern ref RuntimeProjectileStore ProjectileStore(ProjectileAuthority authority);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "tickProvider")]
    private static extern ref Func<long> ProjectileTickProvider(ProjectileAuthority owner);

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

    private sealed class GemStaffPaidActorFixture : IDisposable
    {
        internal readonly PlayerAuthority Players;
        private readonly ServerRuntimeState state;
        internal ServerRuntimeState Runtime => state;

        private readonly WorldTileStore tiles = new(new WorldDimensions(400, 300));
        internal readonly RuntimeProjectileReplicationRegistry Replication = new();
        internal readonly GemStaffPaidActorPlayerSink Events = new();
        internal readonly Dictionary<byte, ConnectionHandle> Connections = [];
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly HashSet<byte> moved = [];
        private readonly PlayerSlotPool pool = new(3);
        internal GemStaffPaidActorFixture()
        {
            for (int x = 0; x < 400; x++)
                tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectiles = new RuntimeProjectileStore(1000, Replication);
            state = new(playerEvents: Events,
                worldTiles: tiles,
                naturalSpawnRandom: new SystemVanillaNpcRandom(new VanillaUnifiedRandom1458(0)),
                projectiles: projectiles,
                projectileReplication: Replication,
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458)with { WorldSurface = 80 },
                playerUpdateRandomSeed: new(0));
            Players = Composition(state).Players;
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false) { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows() });
            Players.SetPlayerUpdateRandom(UpdateRandom(Players)!);
            Players.SetRemotePlayerEnvironment(new(false, false), projectiles);
            Players.SetNpcHealthWorldFacts(() => new(false, false));
            Players.SetNpcHealthGrapplingFacts(_ => false);
            for (int index = 0; index < 3; index++)
            {
                Assert.True(pool.TryAcquireConnection(out var lease));
                sessions.Add(new(lease!));
            }

            // The dedicated local placeholder is source-excluded, not a network actor.
            Membership(Players).Commit(new RuntimePlayerMember { Slot = new(255), Revision = 1, Connection = new(GameCommandSourceId.FromConnection(99255),
                new(new(255),
                new(1))), ItemPhase = null, PhysicsPhase = null });
            var session = sessions[2];
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var c = new ConnectionHandle(GameCommandSourceId.FromConnection(99002), session.Handle);
            Connections.Add(2, c);
            Apply(new PlayerHealthRuntimeCommand(c, new(session.Slot, 50, 400)));
            Apply(new PlayerManaRuntimeCommand(c, new(session.Slot, 10, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(c, new(session.Slot, 0, 0, 0, 0, 0)));
            Apply(new PlayerSpawnRuntimeCommand(c, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            Move(2, false);
        }

        internal RuntimePlayerMember Member(byte slot)
        {
            Assert.True(Players.TryGet(slot, out var member));
            return member;
        }

        internal void Apply(RuntimeCommand command) => state.Apply(command);
        internal void Tick() => state.Tick();
        internal void ReplaceSecond()
        {
            Apply(new PlayerDisconnectRuntimeCommand(Connections[2]));
            sessions[2].Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!);
            sessions.Add(session);
            Assert.Equal(new PlayerSlotId(2), session.Slot);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var c = new ConnectionHandle(GameCommandSourceId.FromConnection(99302), session.Handle);
            Connections[2] = c;
            Apply(new PlayerHealthRuntimeCommand(c, new(session.Slot, 50, 400)));
            Apply(new PlayerManaRuntimeCommand(c, new(session.Slot, 200, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(c, new(session.Slot, 0, 1, 0, 741, 0)));
            Apply(new PlayerSpawnRuntimeCommand(c, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            moved.Remove(2);
            Move(2, false);
            Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        }

        internal void Move(byte slot, bool control)
        {
            var m = Member(slot);
            bool first = moved.Add(slot);
            Apply(new PlayerMovementRuntimeCommand(Connections[slot],
                new(new(slot),
                control ? (byte)32 : (byte)0,
                (byte)(m.MovementFlags | 16),
                0,
                64,
                0,
                first ? 1600 + slot * 40 : m.PositionX,
                first ? 1606 : m.PositionY,
                false,
                0,
                0,
                false,
                0,
                false,
                0,
                0,
                0,
                0,
                false,
                0,
                0)));
        }

        internal PlayerBuffState Buffs(byte slot) => Profiles(Players).CaptureBuffState(Connections[slot])!;
        public void Dispose()
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }

    private sealed class GemStaffPaidActorPlayerSink : IRuntimePlayerEventSink
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
