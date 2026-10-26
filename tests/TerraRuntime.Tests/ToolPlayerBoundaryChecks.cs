using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;
using ProjectileDefinitions = TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog;

namespace TerraRuntime.Tests.ToolPlayer;

// Portable State.Apply/Tick boundaries. Source numeric rows are independent
// oracles; configured transport-policy cases are explicitly labelled below.
internal static partial class ToolPlayerBoundaryChecks
{
    internal static void Run()
    {
        SourceBeforeHealthChronology();
        NaturalBeforeFirstTick(false);
        NaturalBeforeFirstTick(true);
        ChangedBaselineBeforeHurt(false);
        ChangedBaselineBeforeHurt(true);
        SuccessfulImmuneFollowup();
        UnrelatedAndGeneration();
        RefusedWholePhaseDoesNotRetainHealthForever();
        BaselinePublicationAndProviderCurrentness();
        SelectionAndRelease();
        Lucy141();
    }

    private static JsonDocument Source(string platform)
    {
        using var input = typeof(ToolPlayerBoundaryChecks).Assembly.GetManifestResourceStream("ToolPlayerSource" + platform + "1458")
            ?? throw new InvalidOperationException("Missing embedded independent tool player source.");
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void SourceBeforeHealthChronology()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var doc = Source(platform);
            var altar = doc.RootElement.GetProperty("altar");
            var frames = altar.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).ToArray();
            Assert.Equal(new byte[] { 13, 16, 117, 17 }, frames.Select(x => x[2]).ToArray());
            // Original sends BEFORE-Hurt health100; afterLife is50. Never turn
            // this into a fabricated natural packet16 carrying50.
            Assert.Equal(100, BitConverter.ToInt16(frames[1], 4));
            Assert.Equal(400, BitConverter.ToInt16(frames[1], 6));
            Assert.Equal(50, altar.GetProperty("afterLife").GetInt32());
            Assert.Equal(1, BitConverter.ToInt16(frames[3], 8));
            var selection = doc.RootElement.GetProperty("selection").GetProperty("steps");
            Assert.True(selection[2].GetProperty("selection").GetProperty("buffered").GetBoolean());
            Assert.Equal(0, selection[4].GetProperty("selection").GetProperty("selected").GetInt32());
            Assert.Equal(1, selection[5].GetProperty("selection").GetProperty("selected").GetInt32());
            Assert.True(selection[5].GetProperty("before").GetProperty("releaseUseItem").GetBoolean());
        }
    }

    private static void NaturalBeforeFirstTick(bool held)
    {
        using var f = new Fixture(held ? (short)1262 : (short)7);
        if (held)
        {
            // A genuine released update establishes source ReleaseUseItem. No
            // private phase flags or clocks are assigned for the measured cohort.
            f.Tick();
        }
        var image = f.CaptureInventory();
        f.Events.Clear();
        f.Move(0, true); // packet13 command owner, before any measured Tick.
        if (held) f.Shoot(); // packet27 observed birth, still unpublished.
        f.Health(100); // literal source BEFORE-Hurt16, not configured post-Hurt50.
        f.FailAltar(); // packet17 Data1; no successful mutation authority in this report.
        Assert.Equal(100, f.Life);
        Assert.Equal(0UL, f.Tools.ActionSerial);
        Assert.Equal(0, f.Events.Births);
        if (held) Assert.DoesNotContain("health:100", f.Events.Order);
        f.Events.OnAuthoritativeHealth = () =>
        {
            Assert.Equal(50, f.Life);
            Assert.Equal(1UL, f.Tools.ActionSerial);
            Assert.True(f.Tiles.Get(102, 101).IsActive);
            f.AssertInventory(image);
            if (held) Assert.Equal(1, f.Events.Births);
        };
        for (int i = 0; i < 8 && f.Life == 100; i++) f.Tick();
        Assert.Equal(50, f.Life);
        Assert.Equal(1UL, f.Tools.ActionSerial);
        Assert.Equal(new short[] { 50 }, f.Events.AuthoritativeHealth.ToArray());
        Assert.True(f.Tiles.Get(102, 101).IsActive);
        f.AssertInventory(image);
        if (held)
        {
            Assert.Equal(1, f.Events.Births);
            Assert.True(f.Tools.HeldProjectile is { });
            var handle = f.Tools.HeldProjectile!.Value;
            Assert.True(f.Projectiles.IsCombatTrusted(handle));
            Assert.True(f.Events.Order.IndexOf("birth") < f.Events.Order.IndexOf("authoritative:50"));
        }
    }

    // Source-before baseline changes first. Do not credit the old100->50 Hurt:
    // a real50 baseline must produce its separate canonical50->25 tool Hurt.
    private static void ChangedBaselineBeforeHurt(bool held)
    {
        using var f = new Fixture(held ? (short)1262 : (short)7);
        if (held) f.Tick();
        var image = f.CaptureInventory();
        f.Events.Clear();
        f.Move(0, true);
        if (held) f.Shoot();
        f.Health(50);
        Assert.Equal(50, f.Life); // Baseline is adopted NOW, not held as an AFTER claim.
        if (held)
        {
            Assert.NotNull(f.Tools.Input?.ShootReport);
            Assert.NotNull(f.Tools.Input?.DeferredHealthPublication);
            Assert.DoesNotContain("health:50", f.Events.Order);
        }
        f.FailAltar();
        for (int i = 0; i < 8 && f.Life == 50; i++) f.Tick();
        Assert.Equal(25, f.Life);
        Assert.Equal(1UL, f.Tools.ActionSerial);
        Assert.Equal(new short[] { 25 }, f.Events.AuthoritativeHealth.ToArray());
        if (held)
        {
            Assert.Equal(1, f.Events.Births);
            Assert.DoesNotContain("health:50", f.Events.Order); // stale baseline cannot revive50 after25.
            Assert.True(f.Events.Order.IndexOf("birth") < f.Events.Order.IndexOf("authoritative:25"));
        }
        f.AssertInventory(image);
    }

    private static void SuccessfulImmuneFollowup()
    {
        using var f = new Fixture(7);
        // Real admitted no-knockback equipment keeps the same altar reachable.
        // Defense1 + before98 gives Classic floor(49-.5)=48 Hurt, after50.
        f.Equip(62, 156);
        f.Health(98);
        f.Events.Clear();
        f.Move(0, true);
        f.Health(98); // Configured BEFORE status; never a claimed AFTER outcome.
        f.FailAltar();
        f.Tick();
        Assert.Equal(50, f.Life);
        int ticks = 0;
        while ((f.Tools.Clocks.Selected.ItemTime != 0 || f.Tools.Clocks.ToolTime != 0) && ticks++ < 25) f.Tick();
        Assert.True(f.Tools.Clocks.Selected.Animation > 0);
        ulong before = f.Tools.ActionSerial;
        int health = f.Events.AuthoritativeHealth.Count;
        f.FailAltar(); // during source immunity: no new health16 exists.
        f.Tick();
        Assert.Equal(before + 1, f.Tools.ActionSerial);
        Assert.Equal(50, f.Life);
        Assert.Equal(health, f.Events.AuthoritativeHealth.Count);
    }

    private static void UnrelatedAndGeneration()
    {
        using (var f = new Fixture(7))
        {
            f.Move(0, true);
            f.Health(73); // ordinary BEFORE baseline, unrelated to any verified tool effect.
            Assert.Equal(73, f.Life);
            f.Tick();
            Assert.Equal(73, f.Life);
            Assert.Equal(0UL, f.Tools.ActionSerial);
            Assert.Null(f.Tools.Input);
        }
        using (var f = new Fixture(7))
        {
            f.Move(0, true);
            f.Health(50);
            var old = f.Connection;
            f.Replace();
            Assert.NotEqual(old.Player, f.Connection.Player);
            f.Apply(new PlayerHealthRuntimeCommand(old, new(old.Player.Slot, 50, 400)));
            f.Apply(new ClientTileManipulationRuntimeCommand(old, new(0, 102, 101, 1, 0)));
            f.Tick();
            Assert.Equal(100, f.Life);
            Assert.Equal(0UL, f.Tools.ActionSerial);
            Assert.Null(f.Tools.Input?.DeferredHealthPublication);
        }
    }

    private static void RefusedWholePhaseDoesNotRetainHealthForever()
    {
        using var f = new Fixture(1262);
        f.Tick();
        f.Move(0, true);
        f.Shoot();
        f.Health(73);
        Assert.Equal(73, f.Life); // No phase/8-window can delay established baseline HP.
        Assert.NotNull(f.Tools.Input?.DeferredHealthPublication);
        f.AddUnsupportedSibling();
        f.Tick();
        Assert.Null(f.Member.ItemPhase); // real whole-census refusal.
        Assert.Equal(73, f.Life);
        Assert.Equal(0, f.Events.Births);
        Assert.Single(f.Events.Order, x => x == "health:73");
    }

    private static void BaselinePublicationAndProviderCurrentness()
    {
        using (var f = new Fixture(1262))
        {
            f.Tick(); f.Move(0, true); f.Shoot(); f.Health(50);
            var publications = new List<ToolHealthPublication1458>();
            f.Players.CollectToolBaselineHealthPublications(publications);
            Assert.Single(publications);
            f.Health(73);
            Assert.Equal(73, f.Life);
            Assert.False(publications[0].TryPublish()); // older50 cannot overwrite new73.
            f.Tick();
            Assert.Equal(1, f.Events.Births);
            Assert.Equal(73, f.Life);
            Assert.False(publications[0].TryPublish());
        }
        using (var f = new Fixture(1262))
        {
            f.Tick(); f.Move(0, true); f.Shoot(); f.Health(50);
            var publications = new List<ToolHealthPublication1458>();
            f.Players.CollectToolBaselineHealthPublications(publications);
            Assert.Single(publications);
            f.Replace();
            Assert.False(publications[0].TryPublish()); // exact old connection, not slot identity.
            Assert.Equal(100, f.Life);
        }
        using (var f = new Fixture(1262))
        {
            f.Tick(); f.Move(0, true); f.Shoot(); f.Health(50);
            bool changed = false;
            f.Players.SetNpcHealthWorldFacts(() =>
            {
                if (!changed) { changed = true; f.Equip(0, 7); }
                return new(false, false);
            });
            f.Tick();
            Assert.True(changed);
            Assert.Equal(50, f.Life); // baseline already committed; provider cannot resurrect100.
            Assert.Equal(0, f.Events.Births); // stale held capture cannot follow its replaced physical tool.
            Assert.True(f.Tiles.Get(102, 101).IsActive);
        }
    }

    private static void SelectionAndRelease()
    {
        using var f = new Fixture(7);
        f.Tick(); // genuine released source stage, not a direct PoundRelease write.
        Assert.NotNull(f.Tools.ReleaseWitness);
        Assert.Equal(5, f.Tools.Reach!.Value.TileRangeX);
        Assert.Equal(3, f.Tools.Reach!.Value.TileRangeY);
        f.Move(0, true);
        f.Tick();
        Assert.True(f.Tools.Clocks.Selected.Animation > 0);
        Assert.NotNull(f.Tools.ReleaseWitness);
        Assert.True(f.Players.TryPrepareToolAttempt(f.Connection, out var attempt));
        Assert.True(attempt!.TryQualifyPoundRelease(102, 101, out _));
        Assert.False(attempt.TryQualifyPoundRelease(150, 101, out _));
        f.Equip(1, 1);
        int animation = f.Tools.Clocks.Selected.Animation;
        ulong epoch = f.Tools.UseEpoch;
        f.Move(1, true);
        Assert.Equal(animation, f.Tools.Clocks.Selected.Animation); // raw13 never shortens clocks.
        f.Tick();
        Assert.Equal(animation - 1, f.Tools.Clocks.Selected.Animation);
        Assert.Equal((byte)1, f.Tools.DeferredSelectionSlot);
        Assert.Null(f.Tools.ReleaseWitness);
        for (int i = 0; i < 60 && f.Tools.SourceSlot != 1; i++) f.Tick();
        Assert.Equal((byte)1, f.Tools.SourceSlot);
        Assert.Null(f.Tools.DeferredSelectionSlot);
        Assert.True(f.Tools.UseEpoch > epoch);
        Assert.Equal(new ItemTypeId(1), f.Tools.SourceItem);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);

    private sealed record Image(RuntimePlayerInventoryItem[] Items, short[] Quantity, long[] Copper, short[] Caps);

    private sealed class Fixture : IDisposable
    {
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 300));
        internal readonly EventSink Events = new();
        internal readonly RuntimeProjectileReplicationRegistry Replication = new();
        internal readonly RuntimeTileManipulationReplicationRegistry TileReplication = new();
        internal readonly RuntimeProjectileStore Projectiles;
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        private readonly PlayerSlotPool slots = new(2);
        private readonly List<PlayerJoinSession> sessions = [];
        internal ConnectionHandle Connection;
        private readonly short initialTool;
        private readonly short initialStack;
        internal readonly VanillaUnifiedRandom1458 PlacementWorldRandom;
        internal RuntimePlayerMember Member { get { Assert.True(Players.TryGet(Connection, out var member)); return member; } }
        internal RuntimePlayerToolUse1458 Tools => Member.ItemPhase?.ToolUse ?? throw new InvalidOperationException("Missing represented tool owner.");
        internal int Life => Member.NpcHealth?.Life ?? Member.Life;
        internal Fixture(short item, IWorldItemStateCommitSink? worldItemSink = null,
            short initialStack = 1, int playerSeed = 0, int worldSeed = 0, bool knownLocalContext = true)
        {
            initialTool = item;
            this.initialStack = initialStack;
            for (int x = 0; x < 400; x++) Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            for (int x = 0; x < 3; x++) for (int y = 0; y < 2; y++)
                Tiles.Set(102 + x, 101 + y, new WorldTile { Type = 26, Flags = WorldTileFlags.Active,
                    FrameX = (short)(x * 18), FrameY = (short)(y * 18) });
            if (item is 5095 or 5295)
            {
                for (int x = 102; x <= 104; x++) for (int y = 101; y <= 102; y++) Tiles.Set(x, y, default);
                Tiles.Set(102, 102, new WorldTile { Type = 5, Flags = WorldTileFlags.Active, FrameX = 88, FrameY = 132 });
                Tiles.Set(102, 103, new WorldTile { Type = 2, Flags = WorldTileFlags.Active });
            }
            Projectiles = new(1000, new ProjectileSink(Replication, Events));
            var world = new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false)
            {
                GetGoodWorld = false, InfectedSeed = false, NotTheBeesWorld = false,
                NoTrapsWorld = false, TenthAnniversaryWorld = false, DontStarveWorld = false,
                DrunkWorld = false, JourneyMode = knownLocalContext ? false : null, HallowBackground = 0,
                WindowsItemPrefixArithmetic = OperatingSystem.IsWindows()
            };
            var main = new VanillaUnifiedRandom1458(worldSeed);
            PlacementWorldRandom = main;
            State = new(playerEvents: Events, worldTiles: Tiles,
                worldClock: new RuntimeWorldClock(1000, true, default, 0, 0), projectiles: Projectiles,
                worldItems: worldItemSink is null ? null : new RuntimeWorldItemStore(worldItemSink),
                projectileReplication: Replication, tileManipulationReplication: TileReplication, playerUpdateRandomSeed: new(playerSeed),
                naturalSpawnRandom: new SystemVanillaNpcRandom(main),
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(main),
                townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 },
                sourcePlayerWorld: world);
            var composition = Composition(State);
            Players = composition.Players;
            if (knownLocalContext)
                composition.WorldTileAuthority.BindToolBuildingPermission(RuntimeToolBuildingPermission1458.FromLoadedWorld(Tiles));
            Players.SetRemotePlayerEnvironment(new(false, false), Projectiles);
            Players.SetNpcHealthWorldFacts(() => new(false, false));
            Players.SetNpcHealthGrapplingFacts(_ => false);
            Connection = Join(item);
            Events.Clear();
        }
        private ConnectionHandle Join(short item)
        {
            Assert.True(slots.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!); sessions.Add(session);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var c = new ConnectionHandle(GameCommandSourceId.FromConnection(96000 + sessions.Count), session.Handle);
            Apply(new PlayerHealthRuntimeCommand(c, new(session.Slot, 100, 400)));
            Apply(new PlayerManaRuntimeCommand(c, new(session.Slot, 0, 20)));
            Apply(new PlayerEquipmentRuntimeCommand(c, new(session.Slot, 0, initialStack, 0, item, 0)));
            Apply(new PlayerSpawnRuntimeCommand(c, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            return c;
        }
        internal void Apply(RuntimeCommand command) => State.Apply(command);
        internal void Tick() => State.Tick();
        internal void Equip(byte slot, short item) => Apply(new PlayerEquipmentRuntimeCommand(Connection,
            new(Connection.Player.Slot, slot, 1, 0, item, 0)));
        internal void Move(byte selected, bool use)
        {
            var m = Member;
            Apply(new PlayerMovementRuntimeCommand(Connection, new(Connection.Player.Slot,
                (byte)(64 | (use ? 32 : 0)), 16, 0, 64, selected, m.PositionX, m.PositionY,
                false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        internal void Health(short life) => Apply(new PlayerHealthRuntimeCommand(Connection, new(Connection.Player.Slot, life, 400)));
        internal void FailAltar() => Apply(new ClientTileManipulationRuntimeCommand(Connection, new(0, 102, 101, 1, 0)));
        internal void Shoot()
        {
            Assert.True(VanillaToolCatalog1458.TryResolve(new(initialTool), default, false, OperatingSystem.IsWindows(), out var tool));
            Assert.True(VanillaToolCatalog1458.TryGetLaunchStats(tool, 1f, 1f, false, false, OperatingSystem.IsWindows(), out var launch));
            Assert.True(ProjectileDefinitions.TryGet(launch.Projectile, out var definition));
            var m = Member;
            var packet = new TerrariaProjectileUpdateState(new(Connection.Player.Slot.Value, 921, 1),
                (short)launch.Projectile.Value, m.PositionX + 10 - definition.Width * .5f,
                m.PositionY + 21 - definition.Height * .5f, launch.Speed, 0, 0, 0, 0, 0,
                (short)launch.Damage, launch.KnockBack, 0);
            Apply(new ClientProjectileUpdateRuntimeCommand(Connection, packet));
        }
        internal Image CaptureInventory()
        {
            Assert.True(Inventory(Players).TryCaptureShopJournal(Connection, out var image));
            return new(image!.Before.ToArray(), image.Quantity.ToArray(), image.Copper.ToArray(), image.Caps.ToArray());
        }
        internal void AssertInventory(Image expected)
        {
            var actual = CaptureInventory();
            Assert.Equal(expected.Items, actual.Items); Assert.Equal(expected.Quantity, actual.Quantity);
            Assert.Equal(expected.Copper, actual.Copper); Assert.Equal(expected.Caps, actual.Caps);
        }
        internal void Replace()
        {
            Apply(new PlayerDisconnectRuntimeCommand(Connection));
            sessions[0].Dispose();
            Connection = Join(initialTool);
        }
        internal void AddUnsupportedSibling() => Join(5005);
        public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    }

    private sealed class EventSink : IRuntimePlayerEventSink
    {
        internal readonly List<string> Order = [];
        internal readonly List<short> AuthoritativeHealth = [];
        internal int Births;
        internal Action? OnAuthoritativeHealth;
        internal Action<PlayerEquipmentCommitRequest>? OnPlacementEquipment;
        internal void Clear() { Order.Clear(); AuthoritativeHealth.Clear(); Births = 0; }
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
        { OnPlacementEquipment?.Invoke(request); }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
        public void PlayerHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request) => Order.Add("health:" + request.Life);
        public void PlayerAuthoritativeHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request)
        {
            Order.Add("authoritative:" + request.Life); AuthoritativeHealth.Add(request.Life);
            OnAuthoritativeHealth?.Invoke();
        }
    }
    private sealed class ProjectileSink(RuntimeProjectileReplicationRegistry registry, EventSink events) : IProjectileStateCommitSink
    {
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            registry.ProjectileStateCommitted(kind, in snapshot);
            if (kind == ProjectileStateCommitKind.Spawn) { events.Births++; events.Order.Add("birth"); }
        }
    }
}
