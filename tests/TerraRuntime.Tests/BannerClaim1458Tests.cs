using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class BannerClaim1458Tests
{
    [Theory]
    [MemberData(nameof(OriginalClaims))]
    public void Actual_original_claim_amount_zero_partial_and_full_grants_match_owned_state(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        ushort available = row.GetProperty("available").GetUInt16(), requested = row.GetProperty("requested").GetUInt16();
        var replication = new RuntimeNpcReplicationRegistry();
        var source = GameCommandSourceId.FromConnection(1);
        var outbound = new TerrariaConnectionOutboundQueue(new(16, 16384, 4096));
        Assert.True(replication.TryRegister(source, outbound));
        var connection = new ConnectionHandle(source, new(new(1), new(1)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 100, 100, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(connection, spawn);
        ushort[] claims = new ushort[293]; claims[1] = available;
        var owner = new RuntimeNpcDeathPrelude1458(new(new int[293], claims), replication: replication);
        Assert.True(owner.TryClaim(connection, new(1, requested)));
        Assert.Equal(row.GetProperty("remaining").GetUInt16(), owner.CaptureBanners().ClaimableCounts[1]);
        ushort normalized = requested == 0 ? (ushort)1 : requested;
        ushort granted = Math.Min(normalized, available);
        Assert.Equal((granted > 0 ? 2 : 0) + (normalized > granted ? 1 : 0), outbound.QueuedFrames);
        Assert.Empty(owner.CaptureBestiary().Kills);
    }

    [Fact]
    public void Wrong_generation_unregistered_source_and_pre_spawn_cannot_mutate_claims()
    {
        var replication = new RuntimeNpcReplicationRegistry(); var source = GameCommandSourceId.FromConnection(1);
        var outbound = new TerrariaConnectionOutboundQueue(new(16, 16384, 4096));
        Assert.True(replication.TryRegister(source, outbound));
        var connection = new ConnectionHandle(source, new(new(1), new(1)));
        var owner = new RuntimeNpcDeathPrelude1458(new([0, 0], [0, 3]), replication: replication);
        Assert.False(owner.TryClaim(connection, new(1, 1)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 100, 100, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(connection, spawn);
        Assert.False(owner.TryClaim(new(source, new(new(1), new(2))), new(1, 1)));
        Assert.False(owner.TryClaim(new(GameCommandSourceId.FromConnection(2), connection.Player), new(1, 1)));
        Assert.False(owner.TryClaim(connection, new(-1, 1)));
        Assert.False(owner.TryClaim(connection, new(293, 1)));
        Assert.Equal(3, owner.CaptureBanners().ClaimableCounts[1]); Assert.Equal(0, outbound.QueuedFrames);
        Assert.True(owner.TryClaim(connection, new(1, 2))); Assert.Equal(1, owner.CaptureBanners().ClaimableCounts[1]);
        Assert.True(replication.TryUnregister(source));
        Assert.False(owner.TryClaim(connection, new(1, 1))); Assert.Equal(1, owner.CaptureBanners().ClaimableCounts[1]);
    }
    public static IEnumerable<object[]> OriginalClaims() => NpcDeathPrelude1458Tests.Rows("claim-behavior");

    [Theory]
    [MemberData(nameof(OriginalAchievements))]
    public void Source_terminal_Eater_and_last_Twin_achievement_gate_retains_actual_targeted_bytes(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var replication = new RuntimeNpcReplicationRegistry(); var source = GameCommandSourceId.FromConnection(1);
        var outbound = new TerrariaConnectionOutboundQueue(new(16, 16384, 4096)); Assert.True(replication.TryRegister(source, outbound));
        var connection = new ConnectionHandle(source, new(new(0), new(1)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 100, 100, 0, 0, 0, 0, 0); replication.PlayerSpawned(connection, spawn);
        var owner = new RuntimeNpcDeathPrelude1458(replication: replication); var preview = owner.CreatePreview();
        int type = row.GetProperty("type").GetInt32(); var npc = new NpcSnapshot(new(0, new(1)), new(1), type,
            (short)type, 100, 100, 0, 0, 0, default, NpcSimulationState.Initial);
        var context = new RuntimeNpcDeathPreludeContext1458(false, row.GetProperty("terminal").GetBoolean(),
            row.GetProperty("paired").GetBoolean(), false, false, true, false, false, new PlayerHandle[] { connection.Player });
        Assert.True(preview.TryApply(npc, context, new VanillaUnifiedRandom1458(42), out _)); Assert.True(owner.TryPublish(preview, owner.Revision));
        byte[] expected = Convert.FromHexString(row.GetProperty("hex").GetString()!);
        var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(outbound)!;
        if (expected.Length == 0) Assert.False(queue.TryRead(out _));
        else { Assert.True(queue.TryRead(out var frame)); Assert.Equal(expected, frame.Bytes.ToArray()); Assert.False(queue.TryRead(out _)); }
    }
    public static IEnumerable<object[]> OriginalAchievements() => NpcDeathPrelude1458Tests.Rows("achievement-behavior");
}
