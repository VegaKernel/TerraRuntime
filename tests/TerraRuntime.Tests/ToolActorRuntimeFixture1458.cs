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
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests.Tools1458.ActorRuntime;

internal sealed class ToolActorRuntimeFixture1458 : IDisposable
{
    internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 300));
    internal readonly RuntimeProjectileReplicationRegistry Replication = new();
    internal readonly RuntimeTileManipulationReplicationRegistry TileReplication = new();
    internal readonly TerrariaConnectionOutboundQueue TileOutbound = new(new OutboundQueueOptions(1024, 262144, 1024));
    internal readonly VanillaUnifiedRandom1458 Random = new(7);
    internal readonly RuntimeWorldItemReplicationRegistry ItemReplication = new();
    internal readonly RuntimeWorldItemStore Items;
    internal readonly RuntimeProjectileStore Shots;
    internal readonly ToolActorCommitSink1458 Sink;
    internal readonly ServerRuntimeState State;
    internal readonly PlayerAuthority Players;
    internal readonly ConnectionHandle Connection;
    private readonly PlayerSlotPool pool = new(3);
    private readonly List<PlayerJoinSession> sessions = [];
    private bool moved;
    internal TerrariaProjectileUpdateState BirthPacket { get; private set; }
    internal ServerRuntimeComposition Composition => Runtime(State);
    internal ProjectileAuthority Authority => Composition.Projectiles;
    internal RuntimePlayerMember Member
    {
        get { Assert.True(Players.TryGet(Connection.Player, out var member)); return member; }
    }

    internal ToolActorRuntimeFixture1458(bool windows, bool sourcePlayerZero = false, bool aliasPlayerRandom = false)
    {
        for (int x = 0; x < 400; x++) Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Items = new(ItemReplication);
        Sink = new(Replication);
        Shots = new(1001, Sink);
        var world = new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false)
        {
            GetGoodWorld = false, InfectedSeed = false, NotTheBeesWorld = false,
            NoTrapsWorld = false, TenthAnniversaryWorld = false, DontStarveWorld = false,
            DrunkWorld = false, JourneyMode = false, HallowBackground = 0, OnlyShimmerOceanWorlds = false
        };
        State = new(worldTiles: Tiles, worldItems: Items, projectiles: Shots,
            worldItemReplication: ItemReplication,
            projectileReplication: Replication, tileManipulationReplication: TileReplication,
            naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random),
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0, freezeWind: true, freezeRain: true),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 },
            playerUpdateRandomSeed: aliasPlayerRandom ? null : new PlayerUpdateRandomSeed1458(0), sourcePlayerWorld: world);
        Players = Composition.Players;
        if (aliasPlayerRandom) Players.SetPlayerUpdateRandom(Random);
        State.BindToolBuildingPermission(RuntimeToolBuildingPermission1458.FromLoadedWorld(Tiles));
        Players.SetRemotePlayerEnvironment(new(false, false), Shots);
        Players.SetNpcHealthWorldFacts(() => new(false, false));
        Players.SetNpcHealthGrapplingFacts(_ => false);
        for (int index = 0; index < 3; index++)
        {
            Assert.True(pool.TryAcquireConnection(out var lease));
            sessions.Add(new(lease!));
        }
        // Dedicated LocalPlayer255 is an explicit constructor-empty placeholder.
        Members(Players).Commit(new RuntimePlayerMember
        {
            Slot = new(255), Revision = 1,
            Connection = new(GameCommandSourceId.FromConnection(99255), new(new(255), new(1))),
            ItemPhase = null, PhysicsPhase = null
        });
        var session = sessions[sourcePlayerZero ? 0 : 2]; session.ObserveWorldRequest(); session.ObserveSectionRequest();
        Connection = new(GameCommandSourceId.FromConnection(99002), session.Handle);
        Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
        Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 200, 200)));
        Apply(new PlayerEquipmentRuntimeCommand(Connection, new(session.Slot, 0, 0, 0, 0, 0)));
        Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
        Move(false);
        if (sourcePlayerZero)
        {
            // Configured official source Player constructor pose. This is a
            // source-component preimage, not an ingress-authorized teleport:
            // ordinary movement reconciliation retains its owned spawn pose.
            Member.PositionX = Member.PositionY = 0f;
            Member.VelocityX = Member.VelocityY = 0f;
        }
        Assert.True(TileReplication.TryRegister(Connection.Source, TileOutbound));
        var playing = new PlayerSpawnCommitRequest(Connection.Player.Slot, 100, 103, 0, 0, 0, 0, 0);
        TileReplication.PlayerSpawned(Connection, in playing);
    }

    internal ProjectileSnapshot Birth(JsonElement launch)
    {
        short item = (short)launch.GetProperty("item").GetInt32();
        Apply(new PlayerInventorySyncCompleteRuntimeCommand(Connection)); State.Tick();
        Apply(new PlayerEquipmentRuntimeCommand(Connection, new(Connection.Player.Slot, 0, 1, 0, item, 0)));
        Apply(new PlayerInventorySyncCompleteRuntimeCommand(Connection)); Move(false); State.Tick(); Move(true);
        var born = launch.GetProperty("projectiles")[0]; var owner = Member;
        var position = born.GetProperty("position"); var velocity = born.GetProperty("velocity");
        var packet = new TerrariaProjectileUpdateState(new(Connection.Player.Slot.Value, 950, 1),
            born.GetProperty("type").GetInt32(), position.GetProperty("x").GetSingle() + owner.PositionX - 400f,
            position.GetProperty("y").GetSingle() + owner.PositionY - 400f,
            velocity.GetProperty("x").GetSingle(), velocity.GetProperty("y").GetSingle(),
            0f, 0f, 0f, 0, (short)born.GetProperty("damage").GetInt32(), born.GetProperty("knockBack").GetSingle(), 0);
        BirthPacket = packet;
        Apply(new ClientProjectileUpdateRuntimeCommand(Connection, packet)); State.Tick();
        Assert.Equal(1, Authority.PromotedClientProjectileSpawns);
        var image = new ProjectileSnapshot[Shots.Capacity]; Assert.Equal(1, Shots.CopyActive(image));
        Assert.True(Shots.IsCombatTrusted(image[0].Handle));
        Assert.True(Shots.TryGetCombatTrustedOwner(image[0].Handle, out var trusted)); Assert.Equal(Connection.Player, trusted);
        Assert.True(Shots.TryGetLifecycle(image[0].Handle, out var lifecycle));
        if (VanillaHeldToolProjectile1458.IsSupported(image[0].Type)) Assert.NotNull(lifecycle.HeldTool);
        else Assert.NotNull(lifecycle.ThrownAxe);
        Assert.Equal(0, Inventory(Players).CaptureFundedQuantities(Connection)[0]);
        return image[0];
    }

    internal void Move(bool use)
    {
        var member = Member; bool first = !moved; moved = true;
        Apply(new PlayerMovementRuntimeCommand(Connection,
            new(Connection.Player.Slot, use ? (byte)96 : (byte)64, (byte)(member.MovementFlags | 16),
                0, 64, 0, first ? 1680 : member.PositionX, first ? 1606 : member.PositionY,
                false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
    }
    internal List<TerrariaFrame> ReadTileFrames()
    {
        var result = new List<TerrariaFrame>();
        while (Queue(TileOutbound).TryRead(out var frame))
        {
            var input = new ReadOnlySequence<byte>(frame.Bytes);
            Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out var decoded));
            Assert.Equal(0, input.Length); result.Add(decoded);
        }
        return result;
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_queue")]
    private static extern ref BoundedOutboundQueue Queue(TerrariaConnectionOutboundQueue owner);

    internal RuntimePlayerMember ReplaceOwner()
    {
        Apply(new PlayerDisconnectRuntimeCommand(Connection)); sessions[2].Dispose();
        Assert.True(pool.TryAcquireConnection(out var lease)); var session = new PlayerJoinSession(lease!); sessions.Add(session);
        Assert.Equal(Connection.Player.Slot, session.Slot); session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var next = new ConnectionHandle(GameCommandSourceId.FromConnection(99302), session.Handle);
        Apply(new PlayerHealthRuntimeCommand(next, new(session.Slot, 400, 400)));
        Apply(new PlayerManaRuntimeCommand(next, new(session.Slot, 200, 200)));
        Apply(new PlayerEquipmentRuntimeCommand(next, new(session.Slot, 0, 1, 0, 9, 0)));
        Apply(new PlayerSpawnRuntimeCommand(next, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
        Apply(new PlayerInventorySyncCompleteRuntimeCommand(next));
        Assert.True(Players.TryGet(next.Player, out var member)); return member;
    }

    internal void Apply(RuntimeCommand command) => State.Apply(command);
    internal RuntimePlayerInventoryStore.ShopJournalCapture InventoryImage()
    {
        Assert.True(Inventory(Players).TryCaptureShopJournal(Connection, out var image)); Assert.NotNull(image); return image;
    }
    public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Runtime(ServerRuntimeState owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Members(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
}

internal sealed class ToolActorCommitSink1458(RuntimeProjectileReplicationRegistry replication) : IProjectileStateCommitSink
{
    internal readonly List<(ProjectileStateCommitKind Kind, ProjectileSnapshot State)> Events = [];
    internal Action<ProjectileStateCommitKind, ProjectileSnapshot>? BeforeReplication;
    public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
    {
        Events.Add((kind, snapshot));
        BeforeReplication?.Invoke(kind, snapshot);
        replication.ProjectileStateCommitted(kind, in snapshot);
    }
}
