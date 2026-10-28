using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GroundCritterNonlethalContact1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(GroundCritterNonlethalContact1458Tests).Assembly
            .GetManifestResourceStream("GroundCritterContactLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Actual_npc_phase_adopts_source_surviving_contact_and_gold_choices_before_the_peer_turn(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        using var f = new Fixture(row);
        var before = row.GetProperty("input");
        Same(before.GetProperty("victim").GetProperty("npc"), f.Victim);
        Same(before.GetProperty("peer").GetProperty("npc"), f.Peer);
        GoldCritterHitEffect1458Tests.AssertRandom(before.GetProperty("victim").GetProperty("rng"), f.Random);
        int observed = 0;
        f.Commits.Observe = snapshot => {
            if (snapshot.Handle != f.Victim.Handle || snapshot.Simulation.Life != 4) return;
            observed++;
            Same(row.GetProperty("after").GetProperty("victim").GetProperty("npc"), snapshot);
            GoldCritterHitEffect1458Tests.AssertRandom(row.GetProperty("after").GetProperty("victim").GetProperty("rng"), f.Random);
            // The source caller updates victim0 with peer1 frozen. Observing
            // this real commit before the next physical slot keeps that exact
            // boundary without suppressing or replacing the runtime's AI.
            Assert.True(f.Npcs.TryGet(f.Peer.Handle, out var peer));
            Assert.Equal(f.Peer, peer);
        };
        NonresidentBunnyNpcPhase1458.Tick(f.World);
        Assert.Equal(1, observed);
        Assert.True(f.Npcs.TryGet(f.Victim.Handle, out var final));
        Assert.Equal(4, final.Simulation.Life);
        Assert.Equal(30, final.Simulation.HostileContactImmunity);
    }

    [Theory]
    [InlineData(443)] [InlineData(445)] [InlineData(447)] [InlineData(539)]
    public void A_late_unowned_conveyor_refusal_discards_staged_gold_contact_entropy_and_hp(int type)
    {
        using var document = Source(type);
        using var f = new Fixture(document.RootElement, peer: false);
        NonresidentBunnyNpcPhase1458.Tick(f.World);
        Assert.True(f.Npcs.TryGet(f.Victim.Handle, out var before));
        f.AddPeer(document.RootElement.GetProperty("input").GetProperty("peer").GetProperty("npc"));
        for (int x = 0; x < 200; x++)
            f.Tiles.Set(x, 100, new WorldTile { Type = 421, Flags = WorldTileFlags.Active });
        var checkpoint = f.Random.Clone();
        var targeting = f.Capability<VanillaNpcTargetingAiStepper>("vanillaTargeting");
        var motion = f.Capability<VanillaNpcWorldMotionAiStepper>("pirateGhostMotion");
        Assert.True(targeting.TryStepState(in before, out var proposal));
        Assert.True(f.Npcs.TryUpdateUnpublished(before.Handle, in proposal, out var accepted));
        Assert.True(targeting.GroundCritter.TryGetPlanRandom(in before, in accepted, out _, out var preview));
        var stagedBeforeContact = preview.Clone();
        var completed = motion.CompleteCommittedState(in before, in accepted, new RuntimeNpcAiStateExecutor(f.Npcs));
        Assert.False(completed.Handle.IsAssigned);
        Assert.False(stagedBeforeContact.HasSameState(preview));
        Assert.True(checkpoint.HasSameState(f.Random));
        Assert.True(f.Npcs.TryGet(before.Handle, out var unchanged));
        // The accepted placeholder remains physical; a refusal cannot claim
        // rollback of its revision or publish the preview's damage/entropy.
        Assert.Equal(accepted, unchanged);
        Assert.Equal(5, unchanged.Simulation.Life);
    }

    private static JsonDocument Source(int type)
    {
        foreach (var data in Cases())
        {
            var document = JsonDocument.Parse((string)data[0]);
            if (document.RootElement.GetProperty("type").GetInt32() == type &&
                document.RootElement.GetProperty("seed").GetInt32() == 1458) return document;
            document.Dispose();
        }
        throw new InvalidOperationException("No original contact case.");
    }

    private static float Float(JsonElement value) => BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(), 16));
    private static NpcAiState Ai(JsonElement value)
    {
        float[] cells = value.EnumerateArray().Select(Float).ToArray();
        return new(cells[0], cells[1], cells[2], cells[3]);
    }

    private static NpcStateUpdate State(JsonElement source)
    {
        float[] Vector(string name) => source.GetProperty(name).EnumerateArray().Select(Float).ToArray();
        float[] p = Vector("position"), old = Vector("oldPosition"), v = Vector("velocity"), oldV = Vector("oldVelocity");
        return new(source.GetProperty("type").GetInt32(), source.GetProperty("netId").GetInt16(),
            p[0], p[1], v[0], v[1], source.GetProperty("target").GetUInt16(), Ai(source.GetProperty("ai")),
            NpcSimulationState.Initial with {
                Life = source.GetProperty("life").GetInt32(), LifeMax = source.GetProperty("lifeMax").GetInt32(),
                BaseLifeMax = source.GetProperty("baseLifeMax").GetInt32(), TimeLeft = source.GetProperty("timeLeft").GetInt32(),
                BaseDefense = source.GetProperty("baseDefense").GetInt32(), BaseDamage = source.GetProperty("baseDamage").GetInt32(),
                DefenseOverride = source.GetProperty("defense").GetInt32(), DamageOverride = source.GetProperty("damage").GetInt32(),
                Scale = Float(source.GetProperty("scale")), KnockBackResist = Float(source.GetProperty("knockBack")),
                Rotation = Float(source.GetProperty("rotation")), Alpha = source.GetProperty("alpha").GetInt32(),
                HitboxOverride = new(source.GetProperty("width").GetInt32(), source.GetProperty("height").GetInt32()),
                DirectionX = source.GetProperty("direction").GetInt32(), DirectionY = source.GetProperty("directionY").GetInt32(),
                SpriteDirection = source.GetProperty("spriteDirection").GetInt32(), OldPositionX = old[0], OldPositionY = old[1],
                OldVelocityX = oldV[0], OldVelocityY = oldV[1], LocalAi = Ai(source.GetProperty("localAi")),
                FrameIndex = source.GetProperty("frameIndex").GetInt32(),
                FrameCounter = BitConverter.UInt64BitsToDouble(Convert.ToUInt64(source.GetProperty("frameCounter").GetString(), 16)),
                HomeTileX = source.GetProperty("homeX").GetInt32(), HomeTileY = source.GetProperty("homeY").GetInt32(),
                Homeless = source.GetProperty("homeless").GetBoolean(), Friendly = source.GetProperty("friendly").GetBoolean(),
                TownNpc = source.GetProperty("townNpc").GetBoolean(), CatchableNpcTempImmunityCounter = source.GetProperty("immunity").GetInt32(),
                ReleaseOwner = source.GetProperty("release").GetInt16(), HostileContactImmunity = source.GetProperty("hostileContactImmunity").GetInt32(),
                Wet = source.GetProperty("wet").GetBoolean(), CollideX = source.GetProperty("collideX").GetBoolean(),
                CollideY = source.GetProperty("collideY").GetBoolean(), JustHit = source.GetProperty("justHit").GetBoolean(),
                Breath = source.GetProperty("breath").GetInt32(), FriendlyRegenerationCounter = source.GetProperty("friendlyRegen").GetInt32(),
                LifeRegenCounter = source.GetProperty("lifeRegen").GetInt32(), NoGravity = source.GetProperty("noGravity").GetBoolean(),
                NoTileCollide = source.GetProperty("noTileCollide").GetBoolean(), DontTakeDamage = source.GetProperty("dontTakeDamage").GetBoolean(),
                Immortal = source.GetProperty("immortal").GetBoolean(), Chaseable = source.GetProperty("chaseable").GetBoolean(),
                MoneyValue = Float(source.GetProperty("money")), ShimmerTransparency = Float(source.GetProperty("shimmer")),
                StairFall = source.GetProperty("stairFall").GetBoolean() });
    }

    private static void Same(JsonElement expected, in NpcSnapshot actual)
    {
        var state = State(expected);
        Assert.Equal(state.Type, actual.Type); Assert.Equal(state.NetId, actual.NetId);
        Assert.Equal(state.PositionX, actual.PositionX); Assert.Equal(state.PositionY, actual.PositionY);
        Assert.Equal(state.VelocityX, actual.VelocityX); Assert.Equal(state.VelocityY, actual.VelocityY);
        Assert.Equal(state.Target, actual.Target); Assert.Equal(state.Ai, actual.Ai);
        var s = actual.Simulation; var e = state.Simulation;
        Assert.Equal(e.Life, s.Life); Assert.Equal(e.LifeMax, s.LifeMax); Assert.Equal(e.BaseLifeMax, s.BaseLifeMax);
        Assert.Equal(e.OldPositionX, s.OldPositionX); Assert.Equal(e.OldPositionY, s.OldPositionY);
        Assert.Equal(e.OldVelocityX, s.OldVelocityX); Assert.Equal(e.OldVelocityY, s.OldVelocityY);
        Assert.Equal(e.DirectionX, s.DirectionX); Assert.Equal(e.DirectionY, s.DirectionY); Assert.Equal(e.SpriteDirection, s.SpriteDirection);
        Assert.Equal(e.LocalAi, s.LocalAi); Assert.Equal(e.FrameIndex, s.FrameIndex); Assert.Equal(e.FrameCounter, s.FrameCounter);
        Assert.Equal(e.HomeTileX, s.HomeTileX); Assert.Equal(e.HomeTileY, s.HomeTileY); Assert.Equal(e.Homeless, s.Homeless);
        Assert.Equal(e.HostileContactImmunity, s.HostileContactImmunity); Assert.Equal(e.Friendly, s.Friendly);
        Assert.Equal(e.CatchableNpcTempImmunityCounter, s.CatchableNpcTempImmunityCounter); Assert.Equal(e.ReleaseOwner, s.ReleaseOwner);
        Assert.Equal(e.DefenseOverride, s.DefenseOverride); Assert.Equal(e.DamageOverride, s.DamageOverride);
        Assert.Equal(e.BaseDefense, s.BaseDefense); Assert.Equal(e.BaseDamage, s.BaseDamage);
        Assert.Equal(e.TimeLeft, s.TimeLeft); Assert.Equal(e.Scale, s.Scale); Assert.Equal(e.KnockBackResist, s.KnockBackResist);
        Assert.Equal(e.Rotation, s.Rotation); Assert.Equal(e.Alpha, s.Alpha); Assert.Equal(e.HitboxOverride, s.HitboxOverride);
        Assert.Equal(e.Wet, s.Wet); Assert.Equal(e.CollideX, s.CollideX); Assert.Equal(e.CollideY, s.CollideY);
        Assert.Equal(e.JustHit, s.JustHit); Assert.Equal(e.Breath, s.Breath); Assert.Equal(e.FriendlyRegenerationCounter, s.FriendlyRegenerationCounter);
        Assert.Equal(e.LifeRegenCounter, s.LifeRegenCounter); Assert.Equal(e.NoGravity, s.NoGravity); Assert.Equal(e.NoTileCollide, s.NoTileCollide);
        Assert.Equal(e.DontTakeDamage, s.DontTakeDamage); Assert.Equal(e.Immortal, s.Immortal); Assert.Equal(e.Chaseable, s.Chaseable);
        Assert.Equal(e.MoneyValue, s.MoneyValue); Assert.Equal(e.ShimmerTransparency, s.ShimmerTransparency); Assert.Equal(e.StairFall, s.StairFall);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(200, 200));
        internal readonly Commits Commits = new();
        internal readonly RuntimeNpcStore Npcs;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly ServerRuntimeState World;
        internal readonly NpcSnapshot Victim;
        internal NpcSnapshot Peer;
        private readonly PlayerJoinSession session;

        internal Fixture(JsonElement row, bool peer = true)
        {
            Random = new(row.GetProperty("seed").GetInt32()); Npcs = new(commitSink: Commits);
            Assert.True(Tiles.TryAttachWorldSurface(80d));
            for (int x = 0; x < 200; x++) Tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            World = new(npcs: Npcs, worldTiles: Tiles, worldClock: new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 0),
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random));
            var slots = new PlayerSlotPool(1); Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9130), session.Handle);
            World.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 81, 100, 0, 0, 0, 0, 0)));
            World.Apply(new PlayerAppearanceRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, "Oracle", 0, 0, 0,
                default, default, default, default, default, default, default, 0, 0, 0)));
            World.Apply(new PlayerMovementRuntimeCommand(connection,
                new(session.Slot, 0, 0, 0, 0, 0, 1300f, 1558f, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            var victim = State(row.GetProperty("input").GetProperty("victim").GetProperty("npc"));
            Assert.True(Npcs.TrySpawn(0, in victim, out Victim));
            if (peer) AddPeer(row.GetProperty("input").GetProperty("peer").GetProperty("npc"));
        }

        internal void AddPeer(JsonElement input) { var peer = State(input); Assert.True(Npcs.TrySpawn(1, in peer, out Peer)); }
        internal T Capability<T>(string field) where T : class
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var graph = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(World));
            return Assert.IsType<T>(typeof(NpcAuthority).GetField(field, flags)!.GetValue(graph.Npcs));
        }
        public void Dispose() => session.Dispose();
    }

    private sealed class Commits : INpcStateCommitSink
    {
        internal Action<NpcSnapshot>? Observe;
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
        { if (kind is NpcStateCommitKind.Update or NpcStateCommitKind.ForcedUpdate) Observe?.Invoke(snapshot); }
    }
}
