using TerraRuntime.Core.Worlds;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using TerraRuntime.Contracts.Gameplay;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests.WorldChestCrafting;

internal static class RemoteCraftSecurityChecks
{
    internal static int Run()
    {
        int checks = 0, cases = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        foreach (string mode in new[] { "partial-local", "q0", "short-cost", "excess-cost", "late-actor", "late-world", "throw", "reentry" })
        {
            using var source = WorldChestCraftSource.Read("RemoteCraftEarlyLinux1458");
            var row = source.RootElement.EnumerateArray().Single(x => x.GetProperty("mode").GetString() == "remote-open-approve");
            WorldChestItem[] Stock(int index) => row.GetProperty("before").GetProperty("worldChests")[index].EnumerateArray().Select(x => new WorldChestItem(x.GetProperty("stack").GetInt32(), mode == "partial-local" && x.GetProperty("type").GetInt32() != 0 ? 23 : x.GetProperty("type").GetInt32(), x.GetProperty("prefix").GetByte())).ToArray();
            using var wrapper = JsonDocument.Parse("{\"name\":\"owner-open\",\"before\":" + row.GetProperty("before").GetProperty("worldChests")[0].GetRawText() + "}");
            using var fixture = new Fixture(wrapper.RootElement, new[] { new WorldChest(0, 20, 20, "", Stock(0)), new WorldChest(1, 24, 20, "", Stock(1)) });
            var imported = new RuntimePlayerInventoryMutation((byte)0, new(new ItemTypeId(9), 3, default, 0));
            Check(fixture.ActorInventory.TryApplyAtomic(fixture.Connection, new[] { imported }), mode + " trusted fixture"); Fixture.Funding(fixture.ActorInventory)[0] = (short)(mode == "q0" ? 0 : mode == "partial-local" ? 1 : 3);
            fixture.Member.HasHealth = true; fixture.Member.Life = 100; fixture.Member.MaxLife = 100;
            void Dispatch(IEnumerable<string> frames)
            {
                foreach (string hex in frames)
                {
                    var frame = Fixture.Decode(Convert.FromHexString(hex)); fixture.Bootstrap.OnFrame(in frame);
                    while (fixture.Commands.Commands.Count > 0) { var command = fixture.Commands.Commands[0]; fixture.Commands.Commands.RemoveAt(0); if (!fixture.Processor.TryApply(command)) fixture.State.Apply(command); }
                }
            }
            string[] CraftFrames() => row.GetProperty("craftFrames").EnumerateArray().Select(x => x.GetString()!).ToArray();
            string[] SyncFrames() => mode == "partial-local" ? new[] { "0C0005003A00030000080000", "03008A" } : row.GetProperty("syncFrames").EnumerateArray().Select(x => x.GetString()!).ToArray();
            var request = CraftFrames(); if (mode == "partial-local") request = ["0D00520C000117000000010100"]; if (mode == "short-cost") request = ["0E00520C00010900000006020001"]; if (mode == "excess-cost") request = ["0E00520C00010900000008020001"];
            Dispatch(request.Concat(mode == "partial-local" ? new[] { "0C0005000000020000090000", "03008A" } : row.GetProperty("preResponseSyncFrames").EnumerateArray().Select(x => x.GetString()!)));
            for (int i = 0; i < 2048 && fixture.Processor.AppliedCraftingRequests == 0; i++) { int work = fixture.Processor.TickCraftingWork(32); fixture.Players.TickShopJournalWork(64 - work); }
            Check(fixture.Processor.AppliedCraftingRequests == 1, mode + " world cost actually committed");
            var costs = fixture.Member.CursorMove!.Shop!.RemoteCosts!;
            bool rejected = mode is "q0" or "short-cost" or "excess-cost" or "late-actor" or "late-world";
            int callbacks = 0; var expectedException = new InvalidOperationException("observer"); bool originalThrow = false;
            fixture.Events.OnEquipment = () =>
            {
                callbacks++;
                var current = fixture.CapturePlayer();
                if (!rejected)
                {
                    Check(current.Items[58].ItemType.Value == (mode == "partial-local" ? 8 : 36) && current.Quantity[58] == (mode == "partial-local" ? 3 : 1) && current.Quantity[0] == 0, mode + " joint first observer paid output");
                    Check(Fixture.CostConsumed(costs) && !costs.IsCurrent && !costs.TryConsumeUnpublished(fixture.Connection), mode + " consumed before observer");
                }
                if (mode == "reentry") Dispatch(SyncFrames());
                if (mode == "throw" && callbacks == 1) throw expectedException;
            };
            Dispatch(SyncFrames());
            if (mode == "late-actor") Check(fixture.ActorInventory.TryApplyAtomic(fixture.Connection, new[] { new RuntimePlayerInventoryMutation((byte)57, new(new ItemTypeId(3), 1, default, 0)) }), mode + " later owner write");
            if (mode == "late-world") { fixture.Players.TickShopJournalWork(5); fixture.Tiles.Set(19, 20, new WorldTile { Type = 1, Flags = WorldTileFlags.Active }); }
            for (int tick = 0; tick < 2048 && fixture.Member.CursorMove?.Shop is not null; tick++)
            {
                try { int work = fixture.Processor.TickCraftingWork(32); fixture.Players.TickShopJournalWork(64 - work); Check(work + fixture.ShopSteps <= 64, mode + " global budget"); }
                catch (InvalidOperationException ex) when (ReferenceEquals(ex, expectedException)) { originalThrow = true; }
            }
            var final = fixture.CapturePlayer();
            if (rejected) { Check(final.Quantity[58] == 0, mode + " no funded output"); Check(!costs.TryConsumeUnpublished(fixture.Connection), mode + " stale or rejected cost unavailable"); }
            else { Check(final.Quantity[58] == (mode == "partial-local" ? 3 : 1) && final.Items[58].ItemType.Value == (mode == "partial-local" ? 8 : 36), mode + " exact paid result once"); Check(callbacks == 2, mode + " full committed notification batch once"); if (mode == "partial-local") Check(final.Items[0].Stack == 2 && final.Quantity[0] == 0, mode + " exact one local funded unit, not cosmetic min"); }
            if (mode == "throw") Check(originalThrow, mode + " original exception after drain");
            if (mode == "q0") Check(final.Items[0].Stack == 3 && final.Quantity[0] == 0, mode + " source q0 cannot pay or lose held physical");
            cases++; Console.WriteLine("PASS " + mode + " callbacks=" + callbacks);
        }
        Console.WriteLine("REMOTE_SECURITY cases=" + cases + " checks=" + checks);

        return checks;
    }
}
