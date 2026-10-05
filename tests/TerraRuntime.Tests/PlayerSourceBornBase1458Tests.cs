using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PlayerSourceBornBase1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(PlayerSourceBornBase1458Tests).Assembly.GetManifestResourceStream("TownSourceBornBase1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Actual_constructor_health_and_authenticated_pre_post_spawn_packets_preserve_phase_provenance(string json)
    {
        using var doc = JsonDocument.Parse(json); var row = doc.RootElement;
        var tiles = new WorldTileStore(new(100, 80)); var pool = new PlayerSlotPool(1);
        Assert.True(pool.TryAcquireConnection(out var lease)); using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(77201), session.Handle);
        var owner = new PlayerAuthority(null, tiles);
        string order = row.GetProperty("order").GetString()!;
        if (order == "preSpawn") Health();
        owner.TryApply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 40, 30, 0, 0, 0, 0, (byte)row.GetProperty("context").GetInt32())));
        Assert.True(owner.TryGet(connection, out var member));
        if (order == "postSpawn") Health();
        owner.TryApply(new PlayerBuffTypesRuntimeCommand(connection, new(session.Slot,
            Enumerable.Repeat(VanillaBuffIds.Lifeforce, row.GetProperty("slots").GetInt32()).ToArray())));
        string mode = row.GetProperty("mode").GetString()!;
        member.PositionX = mode == "outside" ? 0 : 600; member.PositionY = 439;
        member.IsDead = mode == "dead";
        member.MovementFlags = mode == "ghost" ? VanillaPlayerHealthContext1458.GhostMovementFlag : (byte)0;
        for (int tick = 1; tick <= row.GetProperty("tick").GetInt32(); tick++)
        {
            // The original continuous fixture explicitly restores the observed dead phase each tick;
            // packet16 can temporarily clear dead when its observed life becomes positive.
            member.IsDead = mode == "dead";
            owner.TickHealthContext();
            if (order == "afterFirstPhase" && tick == 1 && row.GetProperty("tick").GetInt32() > 1) Health();
        }
        Assert.Equal(row.GetProperty("baseMaximum").GetInt32(), member.BaseLifeMax);
        Assert.Equal(row.GetProperty("derived").GetInt32(), member.DerivedLifeMax);
        Assert.Equal(100, row.GetProperty("claimedSlotBase").GetInt32());
        Assert.Equal(new PlayerDebuffSnapshot1458(false, false, false), member.Debuffs);
        void Health() => owner.TryApply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 100, 400)));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Transfer_preserves_explicit_known_or_missing_base_and_flags_without_constructor_fallback(bool known)
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(400, 24); f.Players.TickHealthContext();
        var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
        f.Players.TryApply(new PlayerTransferDetachRuntimeCommand(f.Connection, detached));
        var transfer = Assert.IsType<RuntimePlayerTransferState>(await detached.Task);
        if (!known) transfer = transfer with { Player = transfer.Player with { BaseLifeMax = null, Debuffs = null }, BuffTypes = null };
        var destination = new PlayerAuthority(null, f.Tiles); var completion = new TaskCompletionSource<bool>();
        destination.TryApply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 40, 30, true, false, completion));
        Assert.True(await completion.Task); Assert.True(destination.TryGet(f.Connection, out var member));
        Assert.Equal(known ? 400 : null, member.BaseLifeMax);
        destination.TickHealthContext(); Assert.Equal(known ? 400 : null, member.DerivedLifeMax);
        Assert.Equal(known ? new PlayerDebuffSnapshot1458(true, false, false) : null, member.Debuffs);
    }
}
