using System.Buffers;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaChatCodecTests
{
    [Fact]
    public void Argumentless_help_command_is_valid_client_chat_module_traffic()
    {
        var packet = new LoadNetModule
        {
            LoadedModule = new NetTextModule
            {
                PayloadKind = NetTextModulePayloadKind.ClientChatMessage,
                CommandName = "Help",
                ChatMessage = string.Empty
            }
        };
        byte[] bytes = Serialize(packet);
        var frame = new TerrariaFrame(
            checked((ushort)bytes.Length),
            bytes[2],
            new ReadOnlySequence<byte>(bytes),
            new ReadOnlySequence<byte>(bytes.AsMemory(TerrariaPacket.PacketHeaderLength)));

        Assert.Equal(
            TerrariaClientChatDecodeResult.Decoded,
            TerrariaChatCodec.TryDecodeClientMessage(in frame, out TerrariaClientChatMessage message));
        Assert.Equal("Help", message.CommandName);
        Assert.Equal(string.Empty, message.Text);
    }

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

        Assert.True(TerrariaChatCodec.TryDecodeServerFrame(
            serverBytes,
            out TerrariaServerChatMessage serverMessage));
        Assert.Equal((byte)7, serverMessage.AuthorId);
        Assert.Equal("probe-message", serverMessage.Text);
        Assert.Equal(new TerrariaRgbColor(255, 255, 255), serverMessage.Color);
    }

    private static byte[] Serialize(TerrariaPacket packet)
    {
        using var stream = new MemoryStream();
        packet.ToStream(stream);
        return stream.ToArray();
    }
}
