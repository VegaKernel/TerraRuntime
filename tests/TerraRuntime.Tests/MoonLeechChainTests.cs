using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonLeechChainTests
{
    [Fact]
    public void Coupled_head_tongue_buffs_clots_and_healing_match_400_original_steps()
    {
        using Stream resource = typeof(MoonLeechChainTests).Assembly.GetManifestResourceStream("MoonLeechChain1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal("94c6de51c11f96a7971d20ea220162990dd5f5d9763ba3a84a29bbafe74c0de7",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        var players = new PlayerAuthority(null, null);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(73), session.Handle);
        var spawn = new PlayerSpawnCommitRequest(session.Slot, 100, 100, 0, 0, 0, 0, 0);
        Assert.True(players.TryApply(new PlayerSpawnRuntimeCommand(connection, session, spawn)));
        Assert.True(players.TryCapture(connection.Player, out var player));
        var lookup = new FixedPlayer(player with { PositionX = 1500, PositionY = 800 });
        var npcs = new RuntimeNpcStore(8);
        int[] types = [398, 396, 397, 397], maxima = [50000, 45000, 25000, 25000], deficits = [500, 2000, 0, 0];
        for (byte i = 0; i < 4; i++)
        {
            var state = new NpcStateUpdate(types[i], (short)types[i], 1000, 1000, 0, 0, 0,
                new NpcAiState(0, i == 1 ? 209 : 0, i == 3 ? 1 : 0, 0),
                NpcSimulationState.Initial with
                {
                    Life = maxima[i] - deficits[i], LifeMax = maxima[i],
                    LocalAi = new NpcAiState(0, 0, 0, i == 1 ? 15 : 0)
                });
            Assert.True(npcs.TrySpawn(i, in state, out _));
        }
        var replication = new RuntimeProjectileReplicationRegistry();
        var projectiles = new RuntimeProjectileStore(16, replication);
        var vanilla = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 821, 0, true, false, false, false)]);
        vanilla.SetProjectileAnchors(new RuntimeNpcProjectileAnchors(projectiles, replication.WireIdentities, players));
        var world = new WorldTileStore(new WorldDimensions(400, 400));
        var npcStepper = new VanillaNpcWorldMotionAiStepper(new SelectedNpcs(vanilla), world);
        var npcExecutor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        var projectileStepper = new RuntimeProjectileBehaviorStateStepper(
            new VanillaProjectileWorldStateStepper(world, lookup, npcs: npcs),
            new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>());
        var projectileExecutor = new RuntimeProjectileStateExecutor(projectiles,
            new RuntimeProjectileSimulationCommitSink(new RuntimeProjectileLiveChildSpawnQueue(16),
                new RuntimeCultistLightningArcTrailRegistry(16), players));
        var npcBuffer = new NpcSnapshot[8];
        var projectileBuffer = new ProjectileSnapshot[16];
        foreach (var row in json.RootElement.EnumerateArray())
        {
            npcExecutor.Tick(npcStepper);
            projectileExecutor.Tick(projectileStepper);
            Assert.True(npcs.TryGetActive(0, out var core));
            Assert.True(npcs.TryGetActive(1, out var head));
            Assert.Equal(row.GetProperty("coreLife").GetInt32(), core.Simulation.Life);
            Assert.Equal(row.GetProperty("headLife").GetInt32(), head.Simulation.Life);
            Assert.Equal(row.GetProperty("buff").GetInt32(), players.GetBuffDuration(connection.Player, VanillaBuffIds.MoonLeech));
            int npcCount = npcs.CopyActive(npcBuffer);
            var blobs = npcBuffer.Take(npcCount).Where(n => n.TypeIdentity == VanillaNpcIds.MoonLordLeechBlob).ToArray();
            var expectedBlobs = row.GetProperty("blobs");
            Assert.Equal(expectedBlobs.GetArrayLength(), blobs.Length);
            for (int i = 0; i < blobs.Length; i++)
            {
                var expected = expectedBlobs[i]; var actual = blobs[i];
                Assert.Equal(expected.GetProperty("slot").GetByte(), actual.Handle.Slot);
                Assert.Equal(expected.GetProperty("x").GetSingle(), actual.PositionX);
                Assert.Equal(expected.GetProperty("y").GetSingle(), actual.PositionY);
                Assert.Equal(expected.GetProperty("age").GetSingle(), actual.Ai.Ai2);
                Assert.Equal(expected.GetProperty("key").GetUInt32(), BitConverter.SingleToUInt32Bits(actual.Ai.Ai1));
            }
            int count = projectiles.CopyActive(projectileBuffer);
            var tongues = row.GetProperty("tongues");
            Assert.Equal(tongues.GetArrayLength(), count);
            for (int i = 0; i < count; i++)
            {
                var expected = tongues[i]; var actual = projectileBuffer[i];
                Assert.Equal(expected.GetProperty("slot").GetUInt16(), actual.Handle.Slot);
                Assert.Equal(expected.GetProperty("x").GetSingle(), actual.PositionX);
                Assert.Equal(expected.GetProperty("y").GetSingle(), actual.PositionY);
                Assert.Equal(expected.GetProperty("vx").GetSingle(), actual.VelocityX);
                Assert.Equal(expected.GetProperty("vy").GetSingle(), actual.VelocityY);
                Assert.Equal(expected.GetProperty("ai")[0].GetSingle(), actual.Ai.Ai0);
                Assert.Equal(expected.GetProperty("ai")[1].GetSingle(), actual.Ai.Ai1);
                Assert.True(projectiles.TryGetLifecycle(actual.Handle, out var lifecycle));
                Assert.Equal(expected.GetProperty("local")[0].GetSingle(), lifecycle.LocalAi.Ai0);
                Assert.Equal(expected.GetProperty("local")[1].GetSingle(), lifecycle.LocalAi.Ai1);
                Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), lifecycle.TimeLeft);
            }
        }
    }

    private sealed class FixedPlayer(PlayerStateSnapshot snapshot) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        { player = snapshot; return slot == snapshot.Player.Slot; }
    }

    private sealed class SelectedNpcs(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return (npc.TypeIdentity == VanillaNpcIds.MoonLordHead || npc.TypeIdentity == VanillaNpcIds.MoonLordLeechBlob) &&
                inner.TryStepState(in npc, out next);
        }
    }
}
