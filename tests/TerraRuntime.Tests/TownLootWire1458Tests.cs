using System.Buffers;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed partial class TownLootSource1458Tests
{
    public static IEnumerable<object[]> Wires() => Rows("town-wire");
    [Theory, MemberData(nameof(Wires))]
    public void Actual_original_TCP_item21_then_owner22_matches_live_production_writer_and_owned_inventory(string raw)
    {
        using var document = JsonDocument.Parse(raw); var row = document.RootElement;
        using var f = new Fixture(row, false, wire: true);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrikeEnvironment(f.Victim.Handle, 9999, 10, 1));
        Check(row, f);
        var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Outbound));
        foreach (var expected in row.GetProperty("itemFrames").EnumerateArray())
        {
            Assert.True(queue.TryRead(out var frame));
            Assert.Equal(Convert.FromHexString(expected.GetString()!), frame.Bytes.ToArray());
        }
        Assert.Equal(0, f.Outbound!.QueuedFrames);
        Assert.Equal(0, f.Registry!.RejectedFrames); Assert.Equal(0, f.Registry.UnsupportedCommits);
    }
}
