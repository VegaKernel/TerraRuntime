using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Buffs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PlayerDerivedHealth1458Tests
{
    public static IEnumerable<object[]> Cases(string kind)
    {
        using var stream = typeof(PlayerDerivedHealth1458Tests).Assembly.GetManifestResourceStream("TownPlayerHealth1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray()) if (row.GetProperty("kind").GetString() == kind) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Cases), "remote")]
    public void Continuous_remote_Update_retains_exact_derived_maximum_duplicates_and_outside_order(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        using var f = new Fixture(row.GetProperty("maximum").GetInt32(), row.GetProperty("buff").GetInt32());
        f.Phase(row.GetProperty("mode").GetString()!);
        for (int tick = 0; tick < row.GetProperty("tick").GetInt32(); tick++) f.Players.TickHealthContext();
        Assert.Equal(row.GetProperty("derived").GetInt32(), f.Member.DerivedLifeMax);
        Assert.Equal(row.GetProperty("buffTime").GetInt32(), f.Players.GetBuffDuration(f.Connection.Player, f.FirstBuff));
        Span<byte> items = stackalloc byte[7];
        Assert.True(RuntimeTownNpcSchedule1458.TryCopySocialItemTopics(row.GetProperty("life").GetInt32(), f.Member.DerivedLifeMax, items, out int count));
        Assert.Equal(row.GetProperty("items").EnumerateArray().Select(x => x.GetByte()), items[..count].ToArray());
    }

    [Theory, MemberData(nameof(Cases), "main")]
    public void Genuine_Main_player_phase_sequence_preserves_dead_field_clears_nonpersistent_buffs_and_recovers(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        using var f = new Fixture(row.GetProperty("maximum").GetInt32(), row.GetProperty("buff").GetInt32());
        string initial = row.GetProperty("initial").GetString()!;
        string[] sequence = [initial, "alive", "dead", "alive", "ghost", "alive", "outside", "outside", "alive"];
        int final = row.GetProperty("index").GetInt32() % sequence.Length;
        for (int index = 0; index <= final; index++) { f.Phase(sequence[index]); f.Players.TickHealthContext(); }
        Assert.Equal(row.GetProperty("derived").GetInt32(), f.Member.DerivedLifeMax);
        Assert.Equal(row.GetProperty("buffTime").GetInt32(), f.Players.GetBuffDuration(f.Connection.Player, f.FirstBuff));
        Assert.Equal(row.GetProperty("phase").GetString(), sequence[final]);
    }

    [Theory, MemberData(nameof(Cases), "persistent")]
    public void Dead_buff_metadata_matches_all_original_identities(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        int type = row.GetProperty("id").GetInt32();
        Assert.Equal(row.GetProperty("value").GetBoolean(), VanillaBuffDefinitionCatalog.PersistsThroughPlayerDeath(new(type)));
        if (type == 0) return;
        using var f = new Fixture(400, type); f.Phase("dead"); f.Players.TickHealthContext();
        Assert.Equal(row.GetProperty("value").GetBoolean() ? 60 : 0, f.Players.GetBuffDuration(f.Connection.Player, new(type)));
    }

    [Theory, MemberData(nameof(Cases), "items")]
    public void Items_topics_use_source_integer_half_threshold_without_extra_random_calls(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        Span<byte> topics = stackalloc byte[7];
        Assert.True(RuntimeTownNpcSchedule1458.TryCopySocialItemTopics(row.GetProperty("life").GetInt32(), row.GetProperty("derived").GetInt32(), topics, out int count));
        Assert.Equal(row.GetProperty("items").EnumerateArray().Select(x => x.GetByte()), topics[..count].ToArray());
    }

    [Theory, MemberData(nameof(Cases), "use")]
    public void Actual_animated_remote_crystal_and_fruit_use_is_owner_only_and_does_not_invalidate_context(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        using var f = new Fixture(row.GetProperty("maximum").GetInt32(), 0);
        Assert.True(f.Players.TryCommitInventoryMutation(f.Connection, new(0, new(new(row.GetProperty("itemId").GetInt32()), 10, new(0), 0))));
        f.Member.ItemAnimation = row.GetProperty("animation").GetInt32();
        f.Member.MiscFlags2 = row.GetProperty("success").GetBoolean() ? (byte)64 : (byte)0;
        f.Players.TickHealthContext(); f.Players.TickItemAnimation();
        Assert.Equal(row.GetProperty("derived").GetInt32(), f.Member.DerivedLifeMax);
        Assert.Equal(row.GetProperty("maximumAfter").GetInt32(), f.Member.MaxLife);
        Assert.Equal(row.GetProperty("animationAfter").GetInt32(), f.Member.ItemAnimation);
        Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var item)); Assert.Equal(row.GetProperty("stack").GetInt32(), item.Stack);
    }

    [Theory, MemberData(nameof(Cases), "spawn")]
    public void Source_Spawn_preserves_previous_derived_context_until_next_player_phase(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        using var f = new Fixture(400, 0); f.Member.IsDead = row.GetProperty("dead").GetBoolean(); f.Member.DerivedLifeMax = row.GetProperty("prior").GetInt32();
        byte context = row.GetProperty("context").GetString() switch { "ReviveFromDeath" => 0, "SpawningIntoWorld" => 1, "RecallFromItem" => 2, "TeamSwap" => 3, _ => throw new InvalidOperationException() };
        Assert.True(f.Players.TryApply(new PlayerRespawnRuntimeCommand(f.Connection, new(f.Session.Slot, 40, 30, 0, 0, 0, 0, context))));
        Assert.Equal(row.GetProperty("derived").GetInt32(), f.Member.DerivedLifeMax);
    }

    [Fact]
    public void Health_and_buff_ingress_do_not_eagerly_derive_and_revision_exhaustion_preserves_all_context()
    {
        using var f = new Fixture(400, 1132); Assert.Equal(100, f.Member.DerivedLifeMax);
        f.Players.TickHealthContext(); Assert.Equal(560, f.Member.DerivedLifeMax);
        f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Session.Slot, 200, 399)));
        Assert.Equal(560, f.Member.DerivedLifeMax);
        f.Member.Revision = ulong.MaxValue; f.Phase("dead"); var before = f.Member.CaptureSnapshot();
        f.Players.TickHealthContext(); Assert.Equal(before, f.Member.CaptureSnapshot()); Assert.Equal(60, f.Players.GetBuffDuration(f.Connection.Player, VanillaBuffIds.Lifeforce));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Transfer_preserves_known_or_unknown_derived_and_buff_provenance(bool known)
    {
        using var f = new Fixture(399, 1132); f.Players.TickHealthContext();
        var detached = new TaskCompletionSource<RuntimePlayerTransferState?>(); f.Players.TryApply(new PlayerTransferDetachRuntimeCommand(f.Connection, detached));
        var transfer = Assert.IsType<RuntimePlayerTransferState>(await detached.Task);
        if (!known) transfer = transfer with { Player = transfer.Player with { DerivedLifeMax = null }, BuffTypes = null };
        var destination = new PlayerAuthority(null, f.Tiles); var completion = new TaskCompletionSource<bool>();
        destination.TryApply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 40, 30, true, false, completion)); Assert.True(await completion.Task);
        Assert.True(destination.TryGet(f.Connection, out var member)); Assert.Equal(known ? 519 : null, member.DerivedLifeMax);
        member.IsDead = true; destination.TickHealthContext(); Assert.Equal(known ? 519 : null, member.DerivedLifeMax);
        member.IsDead = false; destination.TickHealthContext(); Assert.Equal(known ? 399 : null, member.DerivedLifeMax);
        destination.TryApply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(member.Slot, Array.Empty<BuffTypeId>())));
        destination.TickHealthContext(); Assert.Equal(399, member.DerivedLifeMax);
    }

    [Fact]
    public void Replacement_generation_owns_constructor_value_and_stale_health_or_buff_commands_cannot_write_it()
    {
        using var f = new Fixture(400, 1132); f.Players.TickHealthContext(); Assert.Equal(560, f.Member.DerivedLifeMax);
        f.Players.TryApply(new PlayerDisconnectRuntimeCommand(f.Connection)); f.Session.Dispose(); Assert.True(f.Pool.TryAcquireConnection(out var lease));
        using var next = new PlayerJoinSession(lease!); next.ObserveWorldRequest(); next.ObserveSectionRequest(); var connection = new ConnectionHandle(f.Connection.Source, next.Handle);
        f.Players.TryApply(new PlayerSpawnRuntimeCommand(connection, next, new(next.Slot, 40, 30, 0, 0, 0, 0, 0)));
        f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection, new(next.Slot, 10, 500)));
        f.Players.TryApply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(next.Slot, new[] { VanillaBuffIds.Lifeforce })));
        Assert.True(f.Players.TryGet(connection, out var member)); Assert.Equal(100, member.DerivedLifeMax); Assert.False(member.HasHealth);
        Assert.Null(default(PlayerStateSnapshot).DerivedLifeMax);
    }

    [Fact]
    public void Unknown_imported_selected_health_and_arithmetic_overflow_remain_explicit()
    {
        Span<byte> buffer = stackalloc byte[7]; Assert.False(RuntimeTownNpcSchedule1458.TryCopySocialItemTopics(null, 100, buffer, out _));
        Assert.False(RuntimeTownNpcSchedule1458.TryCopySocialItemTopics(10, null, buffer, out _));
        Assert.Null(VanillaPlayerHealthContext1458.Resolve(int.MaxValue, 400, 1, true, false, false));
        Assert.Equal(399, VanillaPlayerHealthContext1458.Resolve(null, 399, null, false, true, false));
        Assert.Null(VanillaPlayerHealthContext1458.Resolve(100, null, 1, false, false, false));
    }

    [Fact]
    public void Death_preserves_slot_holes_and_new_authoritative_buff_uses_the_first_free_slot()
    {
        var buffs = new PlayerBuffState(); buffs.ReplaceNetworkSnapshot([VanillaBuffIds.Lifeforce, new(71), VanillaBuffIds.OnFire, new(79)]);
        Assert.True(buffs.ClearNonPersistentOnDeath()); Assert.Equal(new BuffTypeId[] { new(71), new(79) }, buffs.CaptureTypes());
        Assert.True(buffs.TryApplyMoonLeech(120)); Assert.Equal(new BuffTypeId[] { VanillaBuffIds.MoonLeech, new(71), new(79) }, buffs.CaptureTypes());
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(100, 80));
        internal readonly PlayerSlotPool Pool = new(1);
        internal readonly PlayerJoinSession Session;
        internal readonly ConnectionHandle Connection;
        internal readonly PlayerAuthority Players;
        internal readonly BuffTypeId FirstBuff;
        internal RuntimePlayerMember Member { get { Assert.True(Players.TryGet(Connection, out var member)); return member; } }
        internal Fixture(int maximum, int buff)
        {
            Players = new(null, Tiles); Assert.True(Pool.TryAcquireConnection(out var lease)); Session = new(lease!);
            Session.ObserveWorldRequest(); Session.ObserveSectionRequest(); Connection = new(GameCommandSourceId.FromConnection(62001), Session.Handle);
            Players.TryApply(new PlayerHealthRuntimeCommand(Connection, new(Session.Slot, (short)(maximum / 3), (short)maximum)));
            if (buff != 0)
            { FirstBuff = new(buff == 1132 ? 113 : buff); BuffTypeId[] types = buff == 1132 ? [FirstBuff, FirstBuff] : [FirstBuff]; Players.TryApply(new PlayerBuffTypesRuntimeCommand(Connection, new(Session.Slot, types))); }
            else FirstBuff = VanillaBuffIds.None;
            Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, Session, new(Session.Slot, 40, 30, 0, 0, 0, 0, 0))); Phase("alive");
        }
        internal void Phase(string mode)
        { Member.IsDead = mode == "dead"; Member.MovementFlags = mode == "ghost" ? VanillaPlayerHealthContext1458.GhostMovementFlag : (byte)0; Member.PositionX = mode == "outside" ? 0 : 600; Member.PositionY = 439; }
        public void Dispose() => Session.Dispose();
    }
}
