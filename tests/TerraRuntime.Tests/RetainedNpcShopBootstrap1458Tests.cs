using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

using Xunit;
namespace TerraRuntime.Tests;

public sealed class RetainedNpcShopBootstrap1458Tests
{
    [Fact]
    public void Literal_138_delimiters_preserve_authenticated_order_join_and_malformed_boundaries()
    {
        int checks = 0;
void Check(bool value, string label) { checks++; Assert.True(value, label); }
TerrariaFrame Frame(byte id, byte[] payload) => new((ushort)(3 + payload.Length), id,
    ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(payload));
foreach (bool rejectQueue in new[] { false, true })
{
    var spawn = new SpawnIngress();
    var commands = new Commands { Reject = rejectQueue };
    var equipment = new RuntimePlayerEquipmentIngress(commands);
    using var sink = new PlayerBootstrapFrameSink(new PlayerSlotPool(1),
        new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 8192, 1024)),
        PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, [], new byte[] { 3, 0, 49 }),
        GameCommandSourceId.FromConnection(42), spawn, appearanceIngress: null, equipmentIngress: equipment,
        movementIngress: null);
    Check(sink.OnFrame(Frame(1, new byte[] { 11, 84, 101, 114, 114, 97, 114, 105, 97, 51, 50, 54 })) ==
        TerrariaFrameSinkResult.Continue, "hello");
    Check(sink.OnFrame(Frame(138, [])) == TerrariaFrameSinkResult.Continue && commands.Posted.Count == 0,
        "prejoin138 has no authority");
    Check(sink.OnFrame(Frame(6, [])) == TerrariaFrameSinkResult.Continue, "world");
    Check(sink.OnFrame(Frame(8, new byte[9])) == TerrariaFrameSinkResult.Continue, "sections");
    var spawnPayload = new byte[15];
    BinaryPrimitives.WriteInt16LittleEndian(spawnPayload.AsSpan(1), 100);
    BinaryPrimitives.WriteInt16LittleEndian(spawnPayload.AsSpan(3), 200);
    Check(sink.OnFrame(Frame(12, spawnPayload)) == TerrariaFrameSinkResult.Continue, "spawn queued");
    Check(spawn.Session!.TryCommitSpawn(spawn.Request.ClaimedSlot) == PlayerSpawnCommitResult.Committed,
        "authoritative spawn");
    Check(sink.JoinState == PlayerJoinState.Playing, "playing");
    var itemPayload = new byte[9];
    itemPayload[0] = 251; // This claimed player is deliberately forged; queue must use assigned connection.
    BinaryPrimitives.WriteInt16LittleEndian(itemPayload.AsSpan(1), 58);
    BinaryPrimitives.WriteInt16LittleEndian(itemPayload.AsSpan(3), 1);
    BinaryPrimitives.WriteInt16LittleEndian(itemPayload.AsSpan(6), 24);
    Check(sink.OnFrame(Frame(5, itemPayload)) == TerrariaFrameSinkResult.Continue, "packet5");
    Check(sink.OnFrame(Frame(138, [])) == TerrariaFrameSinkResult.Continue, "real empty delimiter");
    Check(commands.Posted.Count == 2, "both attempts in order");
    Check(commands.Posted[0].Command is PlayerEquipmentRuntimeCommand item &&
        item.Connection.Player.Slot.Value == 0 && item.Request.PlayerSlot.Value == 0,
        "packet5 authenticated slot");
    Check(commands.Posted[1].Command is PlayerInventorySyncCompleteRuntimeCommand complete &&
        complete.Connection == ((PlayerEquipmentRuntimeCommand)commands.Posted[0].Command).Connection &&
        commands.Posted[1].Source == complete.Connection.Source, "138 owned same connection ordering");
    sink.BeginWorldTransferLanding(spawn.Request);
    Check(sink.OnFrame(Frame(138, [])) == TerrariaFrameSinkResult.Continue && commands.Posted.Count == 2,
        "landing delimiter ignored");
    Check(sink.OnFrame(Frame(5, itemPayload)) == TerrariaFrameSinkResult.Continue && commands.Posted.Count == 2,
        "landing inventory ignored");
    Check(sink.OnFrame(Frame(138, new byte[] { 0 })) == TerrariaFrameSinkResult.Stop,
        "nonempty delimiter malformed");
    Check(sink.StopReason == PlayerBootstrapStopReason.MalformedInventorySync && commands.Posted.Count == 2,
        "malformed delimiter never posts");
}
var unattachedCommands = new Commands();
Check(!new RuntimePlayerEquipmentIngress(unattachedCommands).TryPostInventorySyncComplete(default) &&
    unattachedCommands.Posted.Count == 0, "unassigned ingress refuses");
Assert.Equal(33, checks);

    }

    private sealed class Commands : IGameCommandIngress<RuntimeCommand>
{
    public bool Reject { get; init; }
    public List<(GameCommandSourceId Source, RuntimeCommand Command)> Posted { get; } = [];
    public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { Posted.Add((source, command)); return !Reject; }
}
private sealed class SpawnIngress : IPlayerSpawnCommitIngress
{
    public PlayerJoinSession? Session { get; private set; }
    public PlayerSpawnCommitRequest Request { get; private set; }
    public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request)
    { Session = session; Request = request; return true; }
}

}
