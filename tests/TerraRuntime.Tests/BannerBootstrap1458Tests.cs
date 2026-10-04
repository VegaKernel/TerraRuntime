using System.Buffers;
using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class BannerBootstrap1458Tests
{
    [Fact]
    public void Live_owner_banner_and_bestiary_baseline_precede_packet49_not_packet12()
    {
        var owner = new RuntimeNpcDeathPrelude1458(bestiary: new([new("BlueSlime", 7)], [], []));
        var outbound = new TerrariaConnectionOutboundQueue(new(32, 16384, 4096));
        using var bootstrap = CreateBootstrap(outbound);
        bootstrap.SetDeathPreludeBaseline(owner.CaptureJoinFrames);
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
        var queue = Inner(outbound); while (queue.TryRead(out _)) { }
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(TerrariaMessageId.SpawnTileData, new byte[9])));
        var frames = new List<byte[]>(); while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
        Assert.Equal(new byte[] { 7, 9, 10, 82, 82, 49 }, frames.Select(static frame => frame[2]).ToArray());
        Assert.Equal(owner.CaptureJoinFrames()[0].ToArray(), frames[3]);
        Assert.Equal(owner.CaptureJoinFrames()[1].ToArray(), frames[4]);
    }

    [Fact]
    public void Claim_frame_posts_authenticated_generation_and_rejects_client_counter_writes()
    {
        var outbound = new TerrariaConnectionOutboundQueue(new(32, 16384, 4096));
        using var bootstrap = CreateBootstrap(outbound); bootstrap.OnFrame(Hello());
        var ingress = new Capture(); var source = GameCommandSourceId.FromConnection(9);
        var sink = new BannerClaimFrameSink1458(source, bootstrap, bootstrap, ingress);
        byte[] bytes = TerrariaNpcDeathPreludeCodec1458.EncodeClaimRequest(1, 0);
        var frame = new TerrariaFrame((ushort)bytes.Length, bytes[2], default, new ReadOnlySequence<byte>(bytes.AsMemory(3)));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(frame));
        Assert.Equal(source, ingress.Connection.Source); Assert.Equal(bootstrap.AssignedPlayerHandle, ingress.Connection.Player);
        Assert.Equal(new TerrariaBannerClaim1458(1, 0), ingress.Request);
        bytes = TerrariaNpcDeathPreludeCodec1458.EncodeBannerClaimCount(1, 9999);
        frame = new((ushort)bytes.Length, bytes[2], default, new ReadOnlySequence<byte>(bytes.AsMemory(3)));
        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(frame)); Assert.Equal(1, ingress.Count);
        Assert.Equal(TerrariaFrameRejectionCategory.MalformedProtocol, sink.RejectionCategory);
    }
    private static PlayerBootstrapFrameSink CreateBootstrap(TerrariaConnectionOutboundQueue outbound) => new(
        new PlayerSlotPool(1), outbound, PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 },
            [new byte[] { 3, 0, 10 }], new byte[] { 3, 0, 49 }));
    private static BoundedOutboundQueue Inner(TerrariaConnectionOutboundQueue outbound) =>
        (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
    private static TerrariaFrame Hello() => Frame(TerrariaMessageId.Hello, [11, .. System.Text.Encoding.ASCII.GetBytes("Terraria326")]);
    private static TerrariaFrame Frame(TerrariaMessageId message, byte[] payload) => new((ushort)(3 + payload.Length),
        (byte)message, default, new ReadOnlySequence<byte>(payload));
    private sealed class Capture : IBannerClaimIngress1458
    {
        public ConnectionHandle Connection; public TerrariaBannerClaim1458 Request; public int Count;
        public bool TryPost(ConnectionHandle connection, in TerrariaBannerClaim1458 request)
        { Connection = connection; Request = request; Count++; return true; }
    }
}
