using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using System.Text.Json.Nodes;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class Packet5RetainedIdentitySource1458Tests
{
    [Fact]
    public void Original_server_receive_identity_precedes_prefix_and_is_adopted_before_relay()
    {
        // Independent original SetDefaults numeric projection. The fixture hashes pin its provenance;
        // expectations are read from captured cells, never generated from the production mapping.
        using var identityStream = typeof(Packet5RetainedIdentitySource1458Tests).Assembly
            .GetManifestResourceStream("ItemRetainedIdentity1458")!;
        using var identityBytes = new MemoryStream();
        identityStream.CopyTo(identityBytes);
        Assert.Equal("8ffd8517deff43f8e2dcc24d3a9489336b83c5ea1c1ce5f488c61ae1715ef0a2",
            Convert.ToHexString(SHA256.HashData(identityBytes.ToArray())).ToLowerInvariant());
        identityBytes.Position = 0;
        using var identityGzip = new GZipStream(identityBytes, CompressionMode.Decompress);
        var identity = JsonNode.Parse(identityGzip)!;
        Assert.Equal("8e55981c357eee5193f29039f6af0d2eafd7da99bc80a743d07f28e8e1658dfe",
            identity["sourceManifestSha256"]!.GetValue<string>());
        Assert.Equal("6ef18249545db9b07de2ae85d55b102c6797d6896c8547c17f8e9d0da856ea49",
            identity["sourceCaptureSha256"]!.GetValue<string>());
        Assert.Equal("4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034",
            identity["originalAssemblySha256"]!.GetValue<string>());
        var cells = identity["cells"]!.AsArray();
        Assert.Equal(6196, cells.Count);
        Assert.Equal(Enumerable.Range(0, 6196), cells.Select(c => c!["requested"]!.GetValue<int>()));
        foreach (var cell in cells)
        {
            var requested = new ItemTypeId(cell!["requested"]!.GetValue<int>());
            Assert.True(VanillaItemRetainedIdentity1458.TryResolve(requested, out var resolvedIdentity));
            Assert.Equal(cell["retained"]!.GetValue<int>(), resolvedIdentity.Value);
            Assert.True(VanillaItemRetainedIdentity1458.TryResolve(resolvedIdentity, out var again));
            Assert.Equal(resolvedIdentity, again);
        }
        foreach (int outside in new[] { 6196, 65536, 65559, int.MaxValue })
        {
            Assert.False(VanillaItemRetainedIdentity1458.TryResolve(new(outside), out var outsideResult));
            Assert.Equal(default, outsideResult);
        }

        using var stream = typeof(Packet5RetainedIdentitySource1458Tests).Assembly
            .GetManifestResourceStream("Packet5RetainedIdentity1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        var rows = JsonNode.Parse(gzip)!.AsArray();
        int server = 0, priorPolicyReferences = 0, clientReferences = 0;
        foreach (var node in rows)
        {
            var row = node!;
            var spec = row["spec"]!;
            if (spec["NetMode"]!.GetValue<int>() != 2)
            {
                // Original remote-client receive has no server encoder. Air can retain raw stack/favorite.
                clientReferences++;
                continue;
            }
            var expected = row["afterItem"]!["canonical"]!;
            var request = new PlayerEquipmentCommitRequest(new(0), 0,
                checked((short)spec["Stack"]!.GetValue<int>()), checked((byte)spec["Prefix"]!.GetValue<int>()),
                checked((short)spec["Type"]!.GetValue<int>()),
                (byte)(spec["Favorite"]!.GetValue<bool>() ? 1 : 0));
            var normalized = PlayerEquipmentPacket5Normalizer.Normalize(in request);
            if ((request.Stack < 0 && expected["type"]!.GetValue<int>() != 0) ||
                (request.Stack > 0 && normalized.Stack == 0 && expected["type"]!.GetValue<int>() != 0))
            {
                // Preserve the existing ingress policy. Original negative stacks can retain identity while
                // SendData5 clamps only the wire stack; those rows are reference-only, not runtime parity.
                Assert.Equal(0, normalized.Stack);
                Assert.Equal(0, normalized.ItemNetId);
                priorPolicyReferences++;
                continue;
            }
            using var f = new Packet5BootstrapSupport(spec["Seed"]!.GetValue<int>());
            Assert.True(f.Players.TryGet(f.Connection, out var member));
            ulong input = member.ProjectileUseInputRevision;
            f.Observer = () =>
            {
                Assert.True(f.Players.TryGetInventoryItem(f.Connection.Player, 0, out var item));
                Assert.Equal(expected["type"]!.GetValue<int>(), item.ItemType.Value);
                Assert.Equal(expected["stack"]!.GetValue<int>(), item.Stack);
                Assert.Equal(expected["prefix"]!.GetValue<int>(), item.Prefix.Value);
                Assert.Equal(expected["favorited"]!.GetValue<bool>() ? 1 : 0, item.ItemFlags);
                Assert.True(member.ProjectileUseInputRevision > input);
                Assert.True(SameRandom(f.Random, row["afterRng"]!));
                Assert.Equal(row["afterNext"]!.GetValue<int>(), f.Random.Clone().Next());
            };
            // Real bootstrap ignores claimed31 and binds authenticated slot0. The registry publishes to peers.
            f.Report(Convert.FromHexString(row["incomingHex"]!.GetValue<string>()));
            Assert.Equal(1, f.Events);
            Assert.Equal(row["outboundFrames"]!.AsArray().Select(x => x!.GetValue<string>()), f.DrainPeer());
            Assert.Empty(f.DrainOwner());
            Assert.False(f.State.TryCapturePlayerSnapshot(new(new(31), new(1)), out _));
            server++;
        }
        Assert.Equal(152, server);
        Assert.Equal(12, priorPolicyReferences);
        Assert.Equal(164, clientReferences);

        using (var f = new Packet5BootstrapSupport(1458))
        {
            var command = new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 59, 5, 82, 1803, 1));
            Assert.True(f.Players.TryPrepareReceivedEquipment(command, out var prepared));
            Assert.True(prepared!.TryAdoptUnpublished());
            var profiles = (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority)
                .GetField("transferProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Players)!;
            Assert.True(profiles.TryCapture(f.Connection, out _, out var equipment, out _));
            Assert.Contains(equipment, i => i.SlotId == 59 && i.ItemNetId == 1533 && i.Stack == 5 && i.Prefix == 0 && i.ItemFlags == 1);
            var newer = new PlayerEquipmentCommitRequest(f.Connection.Player.Slot, 59, 1, 0, 92, 0);
            Assert.True(profiles.TrySetEquipment(f.Connection, in newer));
            var checkpoint = f.Random.Clone();
            Assert.False(prepared.TryPublish());
            Assert.Equal(0, f.Events);
            Assert.True(f.Random.HasSameState(checkpoint));
        }
        foreach (bool changeRandom in new[] { false, true })
        {
            using var f = new Packet5BootstrapSupport(1458);
            var command = new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 5, 82, 226, 1));
            Assert.True(f.Players.TryPrepareReceivedEquipment(command, out var prepared));
            if (changeRandom) f.Random.Next();
            else f.Players.TryApply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 1, 7, 0, 97, 0)));
            var checkpoint = f.Random.Clone();
            int events = f.Events;
            Assert.False(prepared!.TryAdoptUnpublished());
            Assert.Equal(events, f.Events);
            Assert.True(f.Random.HasSameState(checkpoint));
        }
        // Standalone unbound component policy deliberately does not claim original server receive normalization.
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(711), session.Handle);
        var component = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
        Assert.True(component.TryApply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 5, 82, 226, 1))));
        var inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority)
            .GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(component)!;
        Assert.True(inventory.TryGet(connection, 0, out var retained));
        Assert.Equal(226, retained.ItemType.Value);
        Assert.Equal(82, retained.Prefix.Value);
    }

    private static bool SameRandom(VanillaUnifiedRandom1458 random, JsonNode expected)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        uint cursor = (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!;
        var state = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!;
        return cursor == expected["cursor"]!.GetValue<uint>() && state.SequenceEqual(
            expected["state"]!.AsArray().Select(x => x!.GetValue<int>()));
    }
}
