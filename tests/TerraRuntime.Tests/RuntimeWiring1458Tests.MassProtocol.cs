using TerraRuntime.Application;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    [Fact]
    public void Literal109_is_bound_to_authenticated_generation_and_retained_before_buffer_release()
    {
        using var scene = new Scene();
        // Independent MessageBuffer109 layout: four signed Int16 endpoints, then one mode byte.
        byte[] bytes = [12, 0, 109, 30, 0, 40, 0, 27, 0, 37, 0, 63];
        var frame = Decode(bytes);
        Assert.Equal(TerrariaFrameSinkResult.Continue, scene.Sink.OnFrame(frame));
        Array.Fill(bytes, (byte)0);
        var command = Assert.IsType<ClientMassWiringRuntimeCommand1458>(Assert.Single(scene.Commands.Commands));
        Assert.Equal(scene.Owner, command.Connection);
        Assert.Equal(new TerrariaMassWiringState1458(30, 40, 27, 37, 63), command.State);
        Assert.Equal(0, scene.State.AppliedClientTileManipulations);
    }

    [Theory]
    [InlineData(0)] [InlineData(8)] [InlineData(10)]
    public void Malformed_mass_payload_is_protocol_rejection_before_command_allocation(int length)
    {
        using var scene = new Scene();
        byte[] bytes = new byte[length + 3]; bytes[0] = (byte)bytes.Length; bytes[2] = (byte)TerrariaMessageId.MassWireOperation;
        var frame = Decode(bytes);
        Assert.Equal(TerrariaFrameSinkResult.Stop, scene.Sink.OnFrame(frame));
        Assert.Empty(scene.Commands.Commands);
    }
}
