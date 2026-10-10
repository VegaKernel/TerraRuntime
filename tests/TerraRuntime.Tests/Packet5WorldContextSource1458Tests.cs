using System.Reflection;
using System.IO.Compression;
using System.Text.Json.Nodes;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.WorldGeneration.Flat;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.Network;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using Xunit;

namespace TerraRuntime.Tests;

// Original Linux GetData5 and Windows CLR4 Item.Prefix references are separate caller scopes.
// WorldRuntime/transfer/currentness checks prove runtime ownership; they do not claim whole Main parity.
public sealed partial class Packet5WorldContextSource1458Tests
{
    private static JsonNode Source()
    {
        using var stream = typeof(Packet5WorldContextSource1458Tests).Assembly.GetManifestResourceStream("Packet5WorldContextSource1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonNode.Parse(gzip)!;
    }

    private static VanillaItemPrefixWorld1458 World(int bits) => new((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0);
    private static void Check(bool value, string detail) => Assert.True(value, detail);

    [Fact]
    public void Original_eight_contexts_adopt_prefix_and_cursor_before_observers()
    {
        var source = Source();
        int rows = 0;
        foreach (bool windows in new[] { false, true })
        {
            foreach (var raw in source[windows ? "windows" : "linux"]!.AsArray())
            {
                var row = raw!;
                int bits = windows ? row["contextBits"]!.GetValue<int>() : row["spec"]!["ContextBits"]!.GetValue<int>();
                int type = windows ? row["weapon"]!.GetValue<int>() : row["spec"]!["Type"]!.GetValue<int>();
                int request = windows ? row["requested"]!.GetValue<int>() : row["spec"]!["Prefix"]!.GetValue<int>();
                int expected = windows ? row["actual"]!.GetValue<int>() : row["afterItem"]!["canonical"]!["prefix"]!.GetValue<int>();
                int next = row[windows ? "next" : "afterNext"]!.GetValue<int>();
                using var f = new ReceiveFixture(0, true);
                f.Equip(0, 219, 0, 1, 0);
                f.Equip(54, 97, 0, 7, 0);
                f.Random = new(0);
                f.Players.BindReceiveEquipmentRandom(f.Random, windows, World(bits));
                ulong input = f.Member.ProjectileUseInputRevision;
                int observations = 0;
                f.Observer = () =>
                {
                    observations++;
                    Assert.True(f.Players.TryGetInventoryItem(f.Connection.Player, 0, out var item));
                    Assert.Equal(expected, item.Prefix.Value);
                    Assert.True(f.Member.ProjectileUseInputRevision > input);
                    Assert.Equal(3, f.Players.AppliedEquipmentUpdates);
                    Assert.True(SameRandom(f.Random, row["afterRng"]!));
                    Assert.Equal(next, f.Random.Clone().Next());
                };
                f.Equip(0, type, request, 1, 1);
                Assert.Equal(1, observations);
                Assert.Equal(3, f.Events);
                if (!windows)
                {
                    foreach (var slotRow in row["afterInventory"]!.AsArray())
                    {
                        var expectedItem = slotRow!["item"]!;
                        Assert.True(f.Players.TryGetInventoryItem(f.Connection.Player, (short)slotRow["slot"]!.GetValue<int>(), out var item));
                        Assert.Equal(expectedItem["type"]!.GetValue<int>(), item.ItemType.Value);
                        Assert.Equal(expectedItem["stack"]!.GetValue<int>(), item.Stack);
                        Assert.Equal(expectedItem["prefix"]!.GetValue<int>(), item.Prefix.Value);
                        Assert.Equal(expectedItem["favorited"]!.GetValue<bool>() ? 1 : 0, item.ItemFlags);
                    }
                }
                rows++;
            }
        }
        Assert.Equal(2800, rows);
    }

    [Fact]
    public void Actual_State_and_WorldRuntime_bind_raw_context_without_inventing_unknown_normal()
    {
        var source = Source();
        var windowsRows = source["windows"]!.AsArray();
        int literalLinuxRelays = 0;
        int typedPlatformReferences = 0;
        foreach (var raw in source["linux"]!.AsArray())
        {
            var row = raw!;
            var spec = row["spec"]!;
            int bits = spec["ContextBits"]!.GetValue<int>();
            int type = spec["Type"]!.GetValue<int>();
            int prefix = spec["Prefix"]!.GetValue<int>();
            var expected = OperatingSystem.IsWindows()
                ? Assert.Single(windowsRows, n => n!["weapon"]!.GetValue<int>() == type && n["contextBits"]!.GetValue<int>() == bits && n["requested"]!.GetValue<int>() == prefix)!
                : row;
            int accepted = OperatingSystem.IsWindows() ? expected["actual"]!.GetValue<int>() : expected["afterItem"]!["canonical"]!["prefix"]!.GetValue<int>();
            using var f = new ContextBootstrapSupport(0, World(bits));
            int observations = 0;
            f.Observer = () =>
            {
                observations++;
                Assert.True(f.Players.TryGetInventoryItem(f.Connection.Player, 0, out var item));
                Assert.Equal(accepted, item.Prefix.Value);
                Assert.True(SameRandom(f.Random, expected["afterRng"]!));
            };
            f.Report(Convert.FromHexString(row["incomingHex"]!.GetValue<string>()));
            Assert.Equal(1, observations);
            Assert.Empty(f.DrainOwner());
            var frames = f.DrainPeer();
            if (accepted == row["afterItem"]!["canonical"]!["prefix"]!.GetValue<int>())
            {
                Assert.Equal(row["outboundFrames"]!.AsArray().Select(n => n!.GetValue<string>()), frames);
                literalLinuxRelays++;
            }
            else
            {
                // Different Windows original Prefix result: no fabricated Windows Receive5 bytes.
                Assert.Single(frames);
                typedPlatformReferences++;
            }
        }
        Assert.Equal(OperatingSystem.IsWindows() ? 1368 : 1400, literalLinuxRelays);
        Assert.Equal(OperatingSystem.IsWindows() ? 32 : 0, typedPlatformReferences);

        for (int bits = 0; bits < 8; bits++)
        {
            using var runtime = CreateRuntime(bits);
            var players = Players(runtime.State);
            var field = typeof(PlayerAuthority).GetField("receivePrefixWorld", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.Equal(World(bits), field.GetValue(players));
            var random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority).GetField("receiveEquipmentRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(players)!;
            var before = random.Clone();
            players.BindReceiveEquipmentRandom(random, OperatingSystem.IsWindows(), World(bits));
            Assert.Throws<InvalidOperationException>(() => players.BindReceiveEquipmentRandom(random, OperatingSystem.IsWindows(), null));
            Assert.Throws<InvalidOperationException>(() => players.BindReceiveEquipmentRandom(new(0), OperatingSystem.IsWindows(), World(bits)));
            Assert.True(random.HasSameState(before));
        }
        using (var f = new ReceiveFixture(0, true))
        {
            f.Players.BindReceiveEquipmentRandom(f.Random);
            Assert.Throws<InvalidOperationException>(() => f.Players.BindReceiveEquipmentRandom(f.Random, false, World(0)));
            var before = f.Random.Clone();
            Assert.False(f.Players.TryPrepareReceivedEquipment(new(f.Connection, new(f.Connection.Player.Slot, 0, 1, 82, 112, 1)), out _));
            f.Equip(0, 112, 82, 1, 1);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection.Player, 0, out var legacy));
            Assert.Equal(82, legacy.Prefix.Value); // Explicit preceding component policy, not source normalization.
            Assert.True(f.Random.HasSameState(before));
            f.Equip(0, 112, 0, 1, 0);
            Assert.True(f.Random.HasSameState(before));
        }
    }

    [Fact]
    public async Task Context_currentness_transfer_destination_and_matching_reports_preserve_custody()
    {
        var contextField = typeof(PlayerAuthority).GetField("receivePrefixWorld", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (bool afterAdoption in new[] { false, true })
        {
            using var f = new ReceiveFixture(0, true);
            f.Players.BindReceiveEquipmentRandom(f.Random, false, World(1));
            var before = f.Random.Clone();
            Assert.True(f.Players.TryPrepareReceivedEquipment(new(f.Connection, new(f.Connection.Player.Slot, 0, 1, 82, 112, 1)), out var token));
            if (afterAdoption) Assert.True(token!.TryAdoptUnpublished());
            var accepted = f.Random.Clone();
            var snapshot = f.Member.CaptureSnapshot();
            contextField.SetValue(f.Players, World(0)); // Managed-only direct unowned mutation witness.
            if (afterAdoption) Assert.False(token!.TryPublish());
            else Assert.False(token!.TryAdoptUnpublished());
            Assert.Equal(0, f.Events);
            Assert.Equal(snapshot, f.Member.CaptureSnapshot());
            Assert.True(f.Random.HasSameState(afterAdoption ? accepted : before));
        }
        using (var source = new ReceiveFixture(0, true))
        using (var destination = new ReceiveFixture(0, false))
        {
            source.Players.BindReceiveEquipmentRandom(source.Random, false, World(1));
            destination.Players.BindReceiveEquipmentRandom(destination.Random, false, World(0));
            source.Equip(0, 112, 82, 1, 1);
            Assert.True(source.Players.TryGetInventoryItem(source.Connection.Player, 0, out var carried));
            var sourceBefore = source.Random.Clone();
            var destinationBefore = destination.Random.Clone();
            var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
            Assert.True(source.Players.TryApply(new PlayerTransferDetachRuntimeCommand(source.Connection, detached)));
            var payload = await detached.Task;
            Assert.NotNull(payload);
            var attached = new TaskCompletionSource<bool>();
            Assert.True(destination.Players.TryApply(new PlayerTransferAttachRuntimeCommand(destination.Connection, payload!, 100, 103, true, false, attached)));
            Assert.True(await attached.Task);
            Assert.True(destination.Players.TryGetInventoryItem(destination.Connection.Player, 0, out var restored));
            Assert.Equal(carried, restored);
            Assert.Equal(World(0), contextField.GetValue(destination.Players));
            Assert.True(source.Random.HasSameState(sourceBefore));
            Assert.True(destination.Random.HasSameState(destinationBefore));
            destination.Observer = null;
            destination.Equip(0, 112, 82, 1, 1);
            var expected = Assert.Single(Source()["linux"]!.AsArray(), n => n!["spec"]!["Type"]!.GetValue<int>() == 112 && n["spec"]!["ContextBits"]!.GetValue<int>() == 0 && n["spec"]!["Prefix"]!.GetValue<int>() == 82)!;
            Assert.True(destination.Players.TryGetInventoryItem(destination.Connection.Player, 0, out var current));
            Assert.Equal(expected["afterItem"]!["canonical"]!["prefix"]!.GetValue<int>(), current.Prefix.Value);
            Assert.True(SameRandom(destination.Random, expected["afterRng"]!));
        }
        using var launch = PendingBulletPlayerPhase1458Tests.Source();
        foreach (bool variantReport in new[] { false, true })
        {
            using var f = new PendingBulletPlayerPhase1458Tests.Fixture(PendingBulletPlayerPhase1458Tests.Row(launch), variantReport ? World(1) : null);
            f.Report(0);
            ulong input = f.Member().ProjectileUseInputRevision;
            if (variantReport)
            {
                var reference = Assert.Single(Source()["linux"]!.AsArray(), n => n!["spec"]!["Type"]!.GetValue<int>() == 112 && n["spec"]!["ContextBits"]!.GetValue<int>() == 1 && n["spec"]!["Prefix"]!.GetValue<int>() == 82)!;
                Assert.True(SameRandom(f.Random, reference["beforeRng"]!));
                f.State.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 1, 1, 82, 112, 1)));
                Assert.True(f.State.TryCapturePlayerInventoryItem(f.Connection.Player, 1, out var normalized));
                Assert.Equal(reference["afterItem"]!["canonical"]!["prefix"]!.GetValue<int>(), normalized.Prefix.Value);
                Assert.True(SameRandom(f.Random, reference["afterRng"]!));
                Assert.Equal(reference["afterNext"]!.GetValue<int>(), f.Random.Clone().Next());
            }
            else
                f.SetItem(0, 534, 1);
            Assert.True(f.Member().ProjectileUseInputRevision > input);
            var after = f.Random.Clone();
            f.Complete();
            Assert.Equal(0, f.Store.ActiveCount);
            Assert.Equal(20, f.Ammo());
            Assert.True(f.Random.HasSameState(after));
        }
    }

    private static PlayerAuthority Players(ServerRuntimeState state)
    {
        object composition = typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(state)!;
        return (PlayerAuthority)composition.GetType().GetProperty("Players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(composition)!;
    }
    private static WorldRuntime CreateRuntime(int bits)
    {
        var source = new SandboxWorldSource.Generated(FlatProvider.GeneratorId, "Context-proof", 0, 32, 24, WorldGenerationOptions.Default);
        var result = new SandboxWorldMaterializer(BuiltInWorldGeneratorSource.Instance, ServerWorldLoadPolicy.CreateLimits()).Materialize(source, CancellationToken.None);
        Assert.True(result.Succeeded, result.Error);
        var metadata = new WorldFileRuntimeMetadata();
        foreach (var property in typeof(WorldFileRuntimeMetadata).GetProperties().Where(p => p.CanRead && p.CanWrite))
            property.SetValue(metadata, property.GetValue(result.World!.RuntimeMetadata));
        typeof(WorldFileRuntimeMetadata).GetProperty(nameof(WorldFileRuntimeMetadata.RemixWorld))!.SetValue(metadata, (bits & 1) != 0);
        typeof(WorldFileRuntimeMetadata).GetProperty(nameof(WorldFileRuntimeMetadata.GetGoodWorld))!.SetValue(metadata, (bits & 2) != 0);
        typeof(WorldFileRuntimeMetadata).GetProperty(nameof(WorldFileRuntimeMetadata.SkyblockWorld))!.SetValue(metadata, (bits & 4) != 0);
        var world = result.World! with { RuntimeMetadata = metadata };
        return new(new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()), source, world, result.Bootstrap!, new InterestManagementControl(), new WorldRuntimeOptions { MaxPlayers = 4 });
    }
}
