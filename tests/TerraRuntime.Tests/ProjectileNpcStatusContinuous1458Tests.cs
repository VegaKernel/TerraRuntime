using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ProjectileNpcStatusContinuous1458Tests
{
    private const string SourceSha = "4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034";

    [Fact]
    public void Continuous_owned_NPC_phase_matches_actual_outer_updates_until_expiry_or_death()
    {
        using var document = Read("ProjectileNpcStatusContinuous1458");
        int profiles = 0, updates = 0;
        foreach (var row in document.RootElement.GetProperty("profiles").EnumerateArray())
        {
            using var f = new Fixture(row);
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                string context = $"{row.GetProperty("profile").GetString()} tick {step.GetProperty("tick").GetInt32()}";
                f.Runtime.Npcs.TickSimulation();
                var expected = step.GetProperty("after");
                bool active = f.Npcs.TryGet(f.Actor.Handle, out var actual);
                Assert.True(active == expected.GetProperty("active").GetBoolean(), context + " active");
                if (active)
                {
                    Assert.Equal(expected.GetProperty("life").GetInt32(), actual.Simulation.Life);
                    Assert.Equal(expected.GetProperty("counter").GetInt32(), actual.Simulation.LifeRegenCounter);
                    AssertMotion(expected, actual);
                    Assert.True(f.Status.TryGetDebuffs(actual.Handle, out var flags), context + " flags");
                    Assert.Equal(expected.GetProperty("onFire").GetBoolean(), flags.OnFire);
                    Assert.Equal(expected.GetProperty("poisoned").GetBoolean(), flags.Poisoned);
                    Assert.True(f.Status.TryGetStinky(actual.Handle, out bool stinky), context + " stinky");
                    Assert.Equal(expected.GetProperty("stinky").GetBoolean(), stinky);
                    AssertBuffs(f.Status, actual.Handle, expected);
                }
                // These independently recorded packets expose expiry before the genuine 9999 strike.
                // Motion packet23 cadence is a separate owner; no synthetic final frame is appended here.
                Assert.Equal(step.GetProperty("frames").EnumerateArray().Select(x => x.GetString()!)
                    .Where(IsStatusOrStrike), f.Drain().Where(x => x[2] is 54 or 28).Select(Convert.ToHexString));
                AssertDrops(step.GetProperty("drops"), f.Items);
                Assert.Equal(expected.GetProperty("next").GetInt32(), f.Random.Clone().Next());
                updates++;
            }
            profiles++;
        }
        Assert.Equal(11, profiles);
        Assert.Equal(837, updates);
    }

    [Fact]
    public void Canonical_Zombie_ambient_offer_requires_owned_zero_shimmer_history()
    {
        using var document = Read("ProjectileNpcStatusContinuous1458");
        var row = document.RootElement.GetProperty("profiles")[0];
        foreach (float? shimmer in new float?[] { 0f, 1f, null })
        {
            using var f = new Fixture(row, applyInitialBuff: false);
            Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var before));
            Assert.True(f.Npcs.TryUpdate(before.Handle, new(before.Type, before.NetId,
                before.PositionX, before.PositionY, before.VelocityX, before.VelocityY, before.Target,
                before.Ai, before.Simulation with { ShimmerTransparency = shimmer }), out _));
            Assert.True(f.Npcs.TryGet(before.Handle, out before));
            Assert.Equal(shimmer, before.Simulation.ShimmerTransparency);
            int next = f.Random.Clone().Next();
            f.Runtime.Npcs.TickSimulation();
            Assert.True(f.Npcs.TryGet(before.Handle, out var after));
            if (shimmer == 0f)
            {
                Assert.Equal(800.07f, after.PositionX);
                Assert.Equal(1755192844, f.Random.Clone().Next()); // Actual source NPC-only seed0 cursor.
            }
            else
            {
                Assert.Equal(before, after);
                Assert.Equal(next, f.Random.Clone().Next());
            }
        }
        foreach (int type in new[] { 20, 24 })
        foreach (var unsupported in new (float? Transparency, NpcLiquidContactKind Contact)[]
            { (1f, NpcLiquidContactKind.None), (null, NpcLiquidContactKind.None), (0f, NpcLiquidContactKind.Shimmer) })
        {
            using var f = new Fixture(row, applyInitialBuff: false);
            Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var before));
            Assert.True(f.Npcs.TryUpdate(before.Handle, new(before.Type, before.NetId,
                before.PositionX, before.PositionY, before.VelocityX, before.VelocityY, before.Target,
                before.Ai, before.Simulation with { ShimmerTransparency = unsupported.Transparency,
                    LiquidContact = unsupported.Contact, Wet = unsupported.Contact == NpcLiquidContactKind.Shimmer,
                    Life = 1, LifeRegenCounter = -119 }), out before));
            Assert.True(f.Status.TryApply(before.Handle, new(type), 1));
            Assert.True(f.Status.TryGetDebuffs(before.Handle, out var flags));
            var table = CopyBuffs(f.Status, before.Handle);
            f.Drain();
            int next = f.Random.Clone().Next();
            f.Runtime.Npcs.TickSimulation();
            Assert.True(f.Npcs.TryGet(before.Handle, out var after));
            Assert.Equal(before, after);
            Assert.Equal(next, f.Random.Clone().Next());
            Assert.True(f.Status.TryGetDebuffs(before.Handle, out var afterFlags));
            Assert.Equal(flags, afterFlags);
            Assert.Equal(table, CopyBuffs(f.Status, before.Handle));
            Assert.Empty(f.Drain());
            Assert.Equal(0, f.Items.ActiveCount);
        }
    }

    private static TerrariaNpcBuffEntryState[] CopyBuffs(RuntimeNpcBuffStatus1458 status, NpcHandle actor)
    {
        Span<TerrariaNpcBuffEntryState> table = stackalloc TerrariaNpcBuffEntryState[20];
        Assert.True(status.TryCopyWireBuffs(actor, table, out int count));
        return table[..count].ToArray();
    }

    [Fact]
    public void Accepted_server_trusted_hit_is_aged_by_next_world_tick_and_uses_owned_DOT_death()
    {
        using var document = Read("ProjectileNpcStatusContinuous1458");
        var row = document.RootElement.GetProperty("profiles").EnumerateArray()
            .Single(x => x.GetProperty("profile").GetString() == "zombie-fire-expired-lethal");
        using var projectileSource = Read("ProjectileNpcStatusCoupled1458");
        var sourceHit = projectileSource.RootElement.GetProperty("profiles").EnumerateArray()
            .Single(x => x.GetProperty("projectileType").GetInt32() == 2 && x.GetProperty("seed").GetInt32() == 3 &&
                x.GetProperty("life").GetInt32() == 9);
        using var f = new Fixture(row, applyInitialBuff: false, life: 9, seed: 3);
        Assert.True(f.Shots.TrySpawn(0, new(new(2), f.Player.Slot.Value,
            800, 440, 3, 0, default, 0, 10, 0, 10), out var shot));
        Assert.True(f.Shots.TryMarkCombatTrusted(shot.Handle, f.Player));

        // This is the runtime's explicit server-trusted PvE route. The independent whole Projectile.Update
        // fixture also retains myPlayer255/owner0's no-hit case; it is not a dedicated-local-hit claim.
        f.State.Tick();
        Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var hit));
        Assert.Equal(sourceHit.GetProperty("hit").GetProperty("life").GetInt32(), hit.Simulation.Life);
        Assert.True(f.Status.TryGetDebuffs(hit.Handle, out var beforeFlags));
        Assert.False(beforeFlags.OnFire); // StatusNPC changes the table; BuffSetFlags is next NPC phase.
        Span<TerrariaNpcBuffEntryState> buffs = stackalloc TerrariaNpcBuffEntryState[20];
        Assert.True(f.Status.TryCopyWireBuffs(hit.Handle, buffs, out int count));
        Assert.Equal(new TerrariaNpcBuffEntryState(24, 180), Assert.Single(buffs[..count].ToArray()));
        Assert.False(f.Shots.TryGet(shot.Handle, out _));
        var frames = f.Drain();
        Assert.True(Array.FindIndex(frames, x => x[2] == 54) < Array.FindIndex(frames, x => x[2] == 28));
        Assert.Contains(frames, x => x[2] == 54);
        Assert.Contains(frames, x => x[2] == 28);
        Assert.Equal(sourceHit.GetProperty("hitFrames").EnumerateArray().Select(x => x.GetString()!).Where(IsStatusOrStrike),
            frames.Where(x => x[2] is 54 or 28).Select(Convert.ToHexString));
        Assert.Equal(sourceHit.GetProperty("hit").GetProperty("next").GetInt32(), f.Random.Clone().Next());

        // Fire drains one life at the captured -119 boundary; low remaining health completes the same
        // retained death owner. Additional ticks cover varied damage while preventing a second arrow hit.
        for (int tick = 0; tick < 60 && f.Npcs.TryGet(f.Actor.Handle, out _); tick++) f.State.Tick();
        Assert.False(f.Npcs.TryGet(f.Actor.Handle, out _));
        Assert.Contains(f.Drain(), x => x[2] == 28 && Convert.ToHexString(x) ==
            row.GetProperty("steps")[0].GetProperty("frames").EnumerateArray()
                .Single(x => Convert.FromHexString(x.GetString()!)[2] == 28).GetString());
    }

    private static bool IsStatusOrStrike(string hex) => Convert.FromHexString(hex)[2] is 54 or 28;

    private static void AssertMotion(JsonElement expected, in NpcSnapshot actual)
    {
        Assert.Equal(expected.GetProperty("x").GetSingle(), actual.PositionX);
        Assert.Equal(expected.GetProperty("y").GetSingle(), actual.PositionY);
        Assert.Equal(expected.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(expected.GetProperty("vy").GetSingle(), actual.VelocityY);
        var ai = expected.GetProperty("ai");
        Assert.Equal(new NpcAiState(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle()), actual.Ai);
        var local = expected.GetProperty("localAI");
        Assert.Equal(new NpcAiState(local[0].GetSingle(), local[1].GetSingle(), local[2].GetSingle(), local[3].GetSingle()), actual.Simulation.LocalAi);
        Assert.Equal(expected.GetProperty("direction").GetInt32(), actual.Simulation.DirectionX);
        Assert.Equal(expected.GetProperty("spriteDirection").GetInt32(), actual.Simulation.SpriteDirection);
        Assert.Equal(expected.GetProperty("target").GetInt32(), actual.Target);
        Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), actual.Simulation.TimeLeft);
        Assert.Equal(expected.GetProperty("collideX").GetBoolean(), actual.Simulation.CollideX);
        Assert.Equal(expected.GetProperty("collideY").GetBoolean(), actual.Simulation.CollideY);
        Assert.Equal(expected.GetProperty("justHit").GetBoolean(), actual.Simulation.JustHit);
    }

    private static void AssertBuffs(RuntimeNpcBuffStatus1458 status, NpcHandle actor, JsonElement expected)
    {
        Span<TerrariaNpcBuffEntryState> slots = stackalloc TerrariaNpcBuffEntryState[20];
        Assert.True(status.TryCopyWireBuffs(actor, slots, out int count));
        var types = expected.GetProperty("buffTypes");
        var times = expected.GetProperty("buffTimes");
        Assert.Equal(types.EnumerateArray().Count(x => x.GetInt32() != 0), count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(types[i].GetInt32(), slots[i].BuffType);
            Assert.Equal(times[i].GetInt32(), slots[i].Duration);
        }
    }

    private static void AssertDrops(JsonElement expected, RuntimeWorldItemStore items)
    {
        var snapshots = new WorldItemSnapshot[items.Capacity];
        int count = items.CopyActive(snapshots);
        Assert.Equal(expected.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            var row = expected[i];
            var actual = snapshots[i];
            Assert.Equal(row.GetProperty("slot").GetInt32(), actual.Handle.Slot);
            Assert.Equal(row.GetProperty("type").GetInt32(), actual.ItemNetId);
            Assert.Equal(row.GetProperty("stack").GetInt32(), actual.Stack);
            Assert.Equal(row.GetProperty("prefix").GetInt32(), actual.Prefix);
            Assert.Equal(row.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(row.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(row.GetProperty("vx").GetSingle(), actual.VelocityX);
            Assert.Equal(row.GetProperty("vy").GetSingle(), actual.VelocityY);
        }
    }

    private static JsonDocument Read(string resource)
    {
        using var stream = typeof(ProjectileNpcStatusContinuous1458Tests).Assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        var result = JsonDocument.Parse(gzip);
        Assert.Equal(SourceSha, result.RootElement.GetProperty("sourceSha256").GetString());
        return result;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private readonly PlayerJoinSession session;
        private readonly TerrariaConnectionOutboundQueue outbound = new(new OutboundQueueOptions(128, 131072, 1024));
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly RuntimeProjectileStore Shots = new();
        internal readonly ServerRuntimeState State;
        internal readonly ServerRuntimeComposition Runtime;
        internal readonly NpcSnapshot Actor;
        internal PlayerHandle Player => session.Handle;
        internal RuntimeNpcBuffStatus1458 Status => Runtime.Npcs.NpcBuffStatus;

        internal Fixture(JsonElement row, bool applyInitialBuff = true, int? life = null, int? seed = null)
        {
            Random = new(seed ?? row.GetProperty("seed").GetInt32());
            var registry = new RuntimeNpcReplicationRegistry();
            var tiles = new WorldTileStore(new WorldDimensions(100, 80));
            Assert.True(tiles.TryAttachWorldSurface(40));
            for (int x = 0; x < 100; x++) tiles.Set(x, 30, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            // Source reference starts at NPC.UpdateNPC. Capacity8 disables the earlier natural-spawn
            // subsystem by its real reserve guard, rather than compensating its independent RNG offer.
            Npcs = new(capacity: 8, commitSink: registry);
            State = new(npcs: Npcs, worldItems: Items, projectiles: Shots, npcReplication: registry, worldTiles: tiles,
                worldClock: new RuntimeWorldClock(1000, false, default, 0, 0),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 40, RockLayer = 35 },
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random));
            Runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!;
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var source = GameCommandSourceId.FromConnection(8371458);
            var connection = new ConnectionHandle(source, session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 400, 400)));
            State.Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 200, 200)));
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 51, 27, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerMovementRuntimeCommand(connection,
                new(session.Slot, 0, VanillaPlayerMovementNormalizer.MovementVelocityPresentFlag, 0, 0, 0,
                    820, 438, true, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            Runtime.Players.TickHealthContext();
            var initial = row.GetProperty("initial");
            int type = row.GetProperty("type").GetInt32();
            Assert.True(Npcs.TrySpawn(0, new(type, (short)type, initial.GetProperty("x").GetSingle(), initial.GetProperty("y").GetSingle(),
                0, 0, 0, default, NpcSimulationState.Initial with {
                    Life = life ?? initial.GetProperty("life").GetInt32(), LifeMax = initial.GetProperty("lifeMax").GetInt32(),
                    DirectionX = 0, SpriteDirection = -1, LifeRegenCounter = initial.GetProperty("counter").GetInt32(),
                    Immortal = false, MoneyValue = initial.GetProperty("value").GetSingle(), ExtraMoneyValue = 0
                }), out Actor), "source actor spawn");
            if (applyInitialBuff)
                Assert.True(Status.TryApply(Actor.Handle, new(row.GetProperty("buff").GetInt32()), row.GetProperty("duration").GetInt32()),
                    row.GetProperty("profile").GetString() + " source status admission");
            Assert.True(registry.TryRegister(source, outbound));
            registry.PlayerSpawned(connection, new(session.Slot, 51, 27, 0, 0, 0, 0, 0));
            _ = Drain();
        }

        internal byte[][] Drain()
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
            var frames = new List<byte[]>();
            while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
            return frames.ToArray();
        }

        public void Dispose() => session.Dispose();
    }
}
