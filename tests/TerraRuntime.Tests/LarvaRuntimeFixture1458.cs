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

namespace TerraRuntime.Tests.Larva1458;

internal sealed class LarvaRuntimeFixture1458 : IDisposable
{
    internal readonly WorldTileStore Tiles = new(new WorldDimensions(600, 300));
    internal readonly RuntimeProjectileReplicationRegistry Replication = new();
    internal readonly RuntimeTileManipulationReplicationRegistry TileReplication = new();
    internal readonly TerrariaConnectionOutboundQueue TileOutbound = new(new OutboundQueueOptions(1024, 262144, 1024));
    internal readonly VanillaUnifiedRandom1458 Random = new(7);
    internal readonly RuntimeWorldItemReplicationRegistry ItemReplication = new();
    internal readonly RuntimeWorldItemStore Items;
    internal readonly RuntimeNpcReplicationRegistry NpcReplication = new();
    internal readonly RuntimeNpcStore Npcs;
    internal readonly TerrariaConnectionOutboundQueue NpcOutbound = new(new OutboundQueueOptions(1024,262144,1024));
    internal readonly LarvaNpcSink1458 NpcSink;
    internal readonly RuntimeProjectileStore Shots;
    internal readonly LarvaProjectileSink1458 Sink;
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

    internal LarvaRuntimeFixture1458(bool windows, bool sourcePlayerZero = false, bool aliasPlayerRandom = false, int mode = 0, bool good = false, bool tenth = false)
    {
        for (int x = 0; x < 400; x++) Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Items = new(ItemReplication);
        Sink = new(Replication);
        Shots = new(1001, Sink);
        var world = new RuntimePlayerUpdateWorld1458(600, 300, 80, false, false)
        {
            GetGoodWorld = good, InfectedSeed = false, NotTheBeesWorld = false,
            NoTrapsWorld = false, TenthAnniversaryWorld = tenth, DontStarveWorld = false,
            DrunkWorld = false, JourneyMode = false, HallowBackground = 0, OnlyShimmerOceanWorlds = false
        };
        NpcSink = new(NpcReplication);
        Npcs = new RuntimeNpcStore(commitSink: NpcSink);
        State = new(npcs: Npcs, npcReplication: NpcReplication, expertMode: mode >= 1, masterMode: mode >= 2, worldTiles: Tiles, worldItems: Items, projectiles: Shots,
            worldItemReplication: ItemReplication,
            projectileReplication: Replication, tileManipulationReplication: TileReplication,
            naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random),
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0, freezeWind: true, freezeRain: true, getGoodWorld: good),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80, GameMode = (byte)mode, GoodWorld = good, TenthAnniversaryWorld = tenth },
            playerUpdateRandomSeed: aliasPlayerRandom ? null : new PlayerUpdateRandomSeed1458(0), sourcePlayerWorld: world);
        Players = Composition.Players;
        if (aliasPlayerRandom) Players.SetPlayerUpdateRandom(Random);
        State.BindToolBuildingPermission(RuntimeToolBuildingPermission1458.FromLoadedWorld(Tiles));
        Players.SetRemotePlayerEnvironment(new(mode>=1 || good, false), Shots);
        Players.SetNpcHealthWorldFacts(() => new(mode>=1 || good, false));
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
        Assert.True(NpcReplication.TryRegister(Connection.Source,NpcOutbound));
        NpcReplication.PlayerSpawned(Connection,in playing);
    }

    internal void Move(bool use)
    {
        var member = Member; bool first = !moved; moved = true;
        Apply(new PlayerMovementRuntimeCommand(Connection,
            new(Connection.Player.Slot, use ? (byte)96 : (byte)64, (byte)(member.MovementFlags | 16),
                0, 64, 0, first ? 1680 : member.PositionX, first ? 1606 : member.PositionY,
                false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
    }
    internal void Apply(RuntimeCommand command) => State.Apply(command);
    public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Runtime(ServerRuntimeState owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Members(PlayerAuthority owner);

}

internal sealed class LarvaProjectileSink1458(RuntimeProjectileReplicationRegistry replication) : IProjectileStateCommitSink
{
    internal readonly List<(ProjectileStateCommitKind Kind, ProjectileSnapshot State)> Events = [];
    internal Action<ProjectileStateCommitKind, ProjectileSnapshot>? BeforeReplication = null;
    public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
    {
        Events.Add((kind, snapshot));
        BeforeReplication?.Invoke(kind, snapshot);
        replication.ProjectileStateCommitted(kind, in snapshot);
    }
}

internal sealed class LarvaNpcSink1458(RuntimeNpcReplicationRegistry relay) : INpcStateCommitSink, INpcBirthRetentionSink
{
 internal Action<NpcStateCommitKind,NpcSnapshot>? Before = null;
 public void NpcStateCommitted(NpcStateCommitKind kind,in NpcSnapshot npc){Before?.Invoke(kind,npc);relay.NpcStateCommitted(kind,in npc);}
 public void NpcBirthRetained(in NpcSnapshot npc){Before?.Invoke(NpcStateCommitKind.Spawn,npc);relay.NpcBirthRetained(in npc);}
}
