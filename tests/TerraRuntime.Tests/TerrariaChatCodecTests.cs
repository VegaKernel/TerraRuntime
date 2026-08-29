using System.Buffers;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaChatCodecTests
{
    [Fact]
    public void Client_chat_decodes_and_server_chat_round_trips()
    {
        var clientPacket = new LoadNetModule
        {
            LoadedModule = new NetTextModule
            {
                PayloadKind = NetTextModulePayloadKind.ClientChatMessage,
                CommandName = "Say",
                ChatMessage = "probe-message"
            }
        };
        byte[] clientBytes = Serialize(clientPacket);
        var payload = new ReadOnlySequence<byte>(clientBytes.AsMemory(TerrariaPacket.PacketHeaderLength));
        var frame = new TerrariaFrame(
            checked((ushort)clientBytes.Length),
            clientBytes[2],
            new ReadOnlySequence<byte>(clientBytes),
            payload);

        TerrariaClientChatDecodeResult result = TerrariaChatCodec.TryDecodeClientMessage(
            in frame,
            out TerrariaClientChatMessage message);

        Assert.Equal(TerrariaClientChatDecodeResult.Decoded, result);
        Assert.Equal("Say", message.CommandName);
        Assert.Equal("probe-message", message.Text);

        byte[] serverBytes = TerrariaChatCodec.EncodeServerMessage(
            7,
            message.Text,
            new TerrariaRgbColor(255, 255, 255));
        Assert.Equal((byte)TerrariaMessageId.LoadNetModule, serverBytes[2]);
        Assert.True(TerrariaPacket.TryDeserializePayload(
            serverBytes[2],
            serverBytes.AsMemory(TerrariaPacket.PacketHeaderLength),
            out TerrariaPacket decoded));

        LoadNetModule load = Assert.IsType<LoadNetModule>(decoded);
        NetTextModule module = Assert.IsType<NetTextModule>(load.LoadedModule);
        Assert.Equal(NetTextModulePayloadKind.ServerChatMessage, module.PayloadKind);
        Assert.Equal((byte)7, module.AuthorId);
        Assert.Equal("probe-message", module.ServerText.Text);
    }

    private static byte[] Serialize(TerrariaPacket packet)
    {
        using var stream = new MemoryStream();
        packet.ToStream(stream);
        return stream.ToArray();
    }
}
