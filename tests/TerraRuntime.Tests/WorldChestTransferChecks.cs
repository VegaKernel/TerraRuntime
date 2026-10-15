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

internal static class WorldChestTransferChecks
{
    internal static int Run()
    {
        int checks = 0, cases = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
        RuntimePlayerInventoryItem Item(JsonElement value) => new(new ItemTypeId(value.GetProperty("type").GetInt32()), (short)value.GetProperty("stack").GetInt32(), new PrefixId(value.GetProperty("prefix").GetInt32()), value.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0);
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var document = WorldChestCraftSource.Read("WorldChestCursor" + platform + "1458");
            using var compounds = WorldChestCraftSource.Read("WorldChestCompound" + platform + "1458");
            foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray().Concat(compounds.RootElement.GetProperty("rows").EnumerateArray()))
            {
                string sourceName = row.GetProperty("spec").GetProperty("Name").GetString()!;
                foreach (string budget in sourceName == "partial-coin-collect" ? new[] { "full", "fractional" } : sourceName == "partial-material-collect" ? new[] { "full", "unfunded-start" } : sourceName == "withdraw" ? new[] { "full", "throw", "late-world", "late-actor", "runtime-tick", "reentry" } : sourceName is "deposit" or "merge" or "exchange" or "full-same-exchange" or "closed-full-cycle" ? new[] { "full", "one", "zero" } : new[] { "full" })
                {
                    string name = sourceName + "/" + budget;
                    using var wrapper = JsonDocument.Parse("{\"name\":\"owner-open\",\"before\":" + row.GetProperty("before").GetProperty("chest").GetRawText() + "}");
                    using var fixture = new Fixture(wrapper.RootElement);
                    var before = row.GetProperty("before");
                    var items = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
                    items[58] = Item(before.GetProperty("mouse"));
                    var mutations = items.Select((item, index) => new RuntimePlayerInventoryMutation((byte)index, item)).ToArray();
                    Check(fixture.ActorInventory.TryApplyAtomic(fixture.Connection, mutations), name + " trusted fixture items");
                    for (int i = 0; i < 59; i++) { var it = items[i]; if (VanillaCoinFacts.TryGetValue(it.ItemType, out long unit)) Fixture.Copper(fixture.ActorInventory)[i] = unit * it.Stack; else Fixture.Funding(fixture.ActorInventory)[i] = it.Stack; }
                    if (budget is "one" or "zero")
                    {
                        int partialSlot = sourceName == "closed-full-cycle" ? 2 : 58;
                        Fixture.Funding(fixture.ActorInventory)[partialSlot] = (short)(budget == "one" ? 1 : 0);
                    }
                    if (budget == "fractional") Fixture.Copper(fixture.ActorInventory)[58] = 1;
                    if (budget == "unfunded-start") Fixture.Funding(fixture.ActorInventory)[58] = 0;
                    fixture.Member.HasHealth = true; fixture.Member.Life = 100; fixture.Member.MaxLife = 100;
                    fixture.Member.ItemAnimation = before.GetProperty("animation").GetInt32();
                    fixture.Member.ItemPhase = fixture.Member.ItemPhase!.Value with { Selected = fixture.Member.ItemPhase!.Value.Selected with { Animation = fixture.Member.ItemAnimation, ItemTime = before.GetProperty("time").GetInt32() } };
                    var captured = fixture.CapturePlayer();
                    var publicationFailure = new InvalidOperationException("configured observer failure");
                    int callbacks = 0;
                    fixture.Events.OnEquipment = () =>
                    {
                        callbacks++;
                        var current = fixture.CapturePlayer();
                        Check(current.Items[58] == Item(row.GetProperty("afterSync").GetProperty("mouse")), name + " first observer sees adopted actor");
                        Check(fixture.Store.CaptureSnapshot().Single().Items[0].Stack == row.GetProperty("afterSync").GetProperty("chest")[0].GetProperty("stack").GetInt32(), name + " first observer sees adopted chest");
                        var mouse = Item(row.GetProperty("afterSync").GetProperty("mouse"));
                        if (VanillaCoinFacts.TryGetValue(mouse.ItemType, out long coinUnit)) Check(current.Copper[58] == (budget == "fractional" ? 2_000_001 : coinUnit * mouse.Stack), name + " first observer sees private copper");
                        else Check(current.Quantity[58] == (budget == "unfunded-start" ? 2 : mouse.Stack), name + " first observer sees private funding");
                        if (budget == "reentry")
                        {
                            foreach (string hex in row.GetProperty("operationFrames").EnumerateArray().Concat(row.GetProperty("syncFrames").EnumerateArray()).Select(x => x.GetString()!))
                            {
                                var repeated = Fixture.Decode(Convert.FromHexString(hex)); fixture.Bootstrap.OnFrame(in repeated);
                                while (fixture.Commands.Commands.Count > 0) { var command = fixture.Commands.Commands[0]; fixture.Commands.Commands.RemoveAt(0); if (!fixture.Processor.TryApply(command)) fixture.State.Apply(command); }
                            }
                        }
                        if (budget == "throw") throw publicationFailure;
                    };

                    foreach (string hex in row.GetProperty("operationFrames").EnumerateArray().Concat(row.GetProperty("syncFrames").EnumerateArray()).Select(x => x.GetString()!))
                    {
                        var frame = Fixture.Decode(Convert.FromHexString(hex)); fixture.Bootstrap.OnFrame(in frame);
                        while (fixture.Commands.Commands.Count > 0) { var command = fixture.Commands.Commands[0]; fixture.Commands.Commands.RemoveAt(0); if (!fixture.Processor.TryApply(command)) fixture.State.Apply(command); Console.WriteLine("CMD " + command.GetType().Name + " pending=" + (fixture.Member.CursorMove?.Shop is not null) + " sealed=" + fixture.Member.CursorMove?.Shop?.IsSealed); }
                    }
                    if (budget == "late-world") fixture.Tiles.Set(20, 20, new WorldTile { Type = 21, FrameX = 1, Flags = WorldTileFlags.Active });
                    if (budget == "late-actor") { var extra = new RuntimePlayerInventoryMutation((byte)57, new(new ItemTypeId(3), 1, default, 0)); Check(fixture.ActorInventory.TryApplyAtomic(fixture.Connection, new[] { extra }), name + " actual later owner write"); }
                    bool threw = false;
                    for (int tick = 0; tick < 2048 && fixture.Member.CursorMove?.Shop is not null; tick++) { try { if (budget == "runtime-tick") fixture.State.Tick(); else fixture.Players.TickShopJournalWork(); } catch (InvalidOperationException exception) when (ReferenceEquals(exception, publicationFailure)) { threw = true; } Check(fixture.ShopSteps <= 64, name + " shared work bound"); }
                    Console.WriteLine("DIAG " + name + " chestReject=" + fixture.Processor.RejectedItemUpdates + " actorReject=" + fixture.Players.RejectedEquipmentUpdates + " shop=" + (fixture.Member.CursorMove?.Shop is not null) + " steps=" + fixture.ShopSteps); var after = fixture.CapturePlayer(); var expected = row.GetProperty("afterSync");
                    bool rejected = budget is "one" or "zero" or "late-world" or "late-actor";
                    if (budget == "throw") Check(threw && callbacks == 1, name + " original observer exception once");
                    if (budget == "reentry") Check(callbacks == 1 && fixture.Processor.RejectedItemUpdates > 0 && fixture.Players.RejectedEquipmentUpdates > 0, name + " repeated source ingress cannot regrant during publication");
                    var target = rejected ? items.ToArray() : expected.GetProperty("inventory").EnumerateArray().Select(Item).ToArray(); if (budget == "late-actor") target[57] = new(new ItemTypeId(3), 1, default, 0); if (!rejected) target[58] = Item(expected.GetProperty("mouse"));
                    if (rejected) Check(fixture.Players.RejectedEquipmentUpdates > 0, name + " explicit partial-world/ambiguity refusal");
                    for (int i = 0; i < 219; i++) { if (rejected) { Check(after.Quantity[i] == captured.Quantity[i], name + " retainedq" + i); Check(after.Copper[i] == captured.Copper[i], name + " retainedcopper" + i); } else if (i < 59) { long unit; if (VanillaCoinFacts.TryGetValue(target[i].ItemType, out unit)) Check(after.Copper[i] == (budget == "fractional" && i == 58 ? 2_000_001 : unit * target[i].Stack), name + " exactcoin" + i); else Check(after.Quantity[i] == (budget == "unfunded-start" && i == 58 ? 2 : target[i].Stack), name + " exactq" + i); } else Check(after.Items[i] == captured.Items[i] && after.Quantity[i] == captured.Quantity[i] && after.Copper[i] == captured.Copper[i], name + " otherstorage" + i); }
                    Check(after.Caps.AsSpan().SequenceEqual(captured.Caps), name + " historicalcaps unchanged");
                    for (int i = 0; i < 59; i++) Check(after.Items[i] == target[i], platform + "/" + name + " actor" + i + " wanted " + target[i] + " actual " + after.Items[i]);
                    var chest = fixture.Store.CaptureSnapshot().Single().Items; int slot = 0;
                    foreach (var value in (rejected ? before : expected).GetProperty("chest").EnumerateArray()) { Check(chest[slot].ItemType == value.GetProperty("type").GetInt32() && chest[slot].Stack == value.GetProperty("stack").GetInt32() && chest[slot].Prefix == value.GetProperty("prefix").GetInt32(), name + " chest" + slot); slot++; }
                    cases++; Console.WriteLine("PASS " + platform + "/" + name + " rejections=" + fixture.Players.RejectedEquipmentUpdates);
                }
            }
        }
        foreach (string mode in new[] { "coalesced", "denied", "late-actor", "single" })
        {
            using var source = WorldChestCraftSource.Read("WorldChestCursorLinux1458");
            using var wrapper = JsonDocument.Parse("{\"name\":\"owner-open\",\"before\":" + source.RootElement.GetProperty("rows")[0].GetProperty("before").GetProperty("chest").GetRawText() + "}");
            using var fixture = new Fixture(wrapper.RootElement);
            fixture.Member.HasHealth = true; fixture.Member.Life = 100; fixture.Member.MaxLife = 100;
            var before = fixture.CapturePlayer();
            string request = mode == "coalesced" ? "1200520C0002090000000109000000010100" : mode == "denied" ? "0D00520C000109000000060100" : "0D00520C000109000000010100";
            void Submit(string hex) { var frame = Fixture.Decode(Convert.FromHexString(hex)); fixture.Bootstrap.OnFrame(in frame); Check(fixture.Commands.Commands.Count == 1, mode + " actual module12 dispatch"); Check(fixture.Processor.TryApply(fixture.Commands.Commands[0]), mode + " authority dispatcher"); fixture.Commands.Commands.Clear(); }
            Submit(request);
            if (mode == "late-actor") Check(fixture.ActorInventory.TryApplyAtomic(fixture.Connection, new[] { new RuntimePlayerInventoryMutation((byte)1, new(new ItemTypeId(3), 1, default, 0)) }), mode + " actual private-source invalidation");
            void DrainWork() { for (int i = 0; i < 2048 && fixture.Processor.AppliedCraftingRequests + fixture.Processor.RejectedCraftingRequests == 0; i++) { int remote = fixture.Processor.TickCraftingWork(32); fixture.Players.TickShopJournalWork(64 - remote); Check(remote + fixture.ShopSteps <= 64, mode + " composition shared budget"); } }
            DrainWork();
            if (mode == "coalesced")
            {
                Check(fixture.Member.CursorMove?.Shop?.RemoteCosts?.Materials.Length == 2, mode + " source repeated-slot material legs");
                var certificate = fixture.Member.CursorMove!.Shop!.RemoteCosts!;
                Check(certificate.Materials[0].FundedQuantity == 1 && certificate.Materials[0].FundedCopper == 0, mode + " strict-owned concrete funding");
                Check(certificate.Materials[0].Before.Stack == 5 && certificate.Materials[0].After.Stack == 4 && certificate.Materials[1].Before.Stack == 4 && certificate.Materials[1].After.Stack == 3, mode + " exact source intermediate custody");
                Submit("0D00520C000109000000010100");
                for (int i = 0; i < 2048 && fixture.Processor.AppliedCraftingRequests < 2; i++) { int remote = fixture.Processor.TickCraftingWork(32); fixture.Players.TickShopJournalWork(64 - remote); Check(remote + fixture.ShopSteps <= 64, mode + " second request before138 no deadlock"); }
                Check(fixture.Processor.AppliedCraftingRequests == 2 && fixture.Store.CaptureSnapshot().Single().Items[0].Stack == 2, mode + " two requests committed before anylocal5");
                Check(fixture.Member.CursorMove!.Shop!.RemoteCosts!.Materials.Length == 3, mode + " bounded coalesced cost");
            }
            else if (mode is "denied" or "late-actor")
            {
                Check(fixture.Processor.AppliedCraftingRequests == 0 && fixture.Processor.RejectedCraftingRequests == 1, mode + " no committed cost");
                Check(fixture.Store.CaptureSnapshot().Single().Items[0].Stack == 5, mode + " exact world preimage retained");
                Check(fixture.Member.CursorMove?.Shop?.RemoteCosts is null, mode + " no funding certificate");
            }
            else
            {
                Check(fixture.Processor.AppliedCraftingRequests == 1, mode + " source-compatible cost accepted");
                Check(fixture.Store.CaptureSnapshot().Single().Items[0].Stack == 4, mode + " committed trusted world decrement");
            }
            if (mode is "coalesced" or "single")
            {
                var certificate = fixture.Member.CursorMove!.Shop!.RemoteCosts!;
                Check(certificate.IsCurrent, mode + " exact current certificate");
                Check(certificate.TryConsumeUnpublished(fixture.Connection), mode + " one-shot same-journal consumption");
                Check(!certificate.TryConsumeUnpublished(fixture.Connection), mode + " cannot reuse removed funds");
                var current = fixture.CapturePlayer();
                Check(current.Items.AsSpan().SequenceEqual(before.Items) && current.Quantity.AsSpan().SequenceEqual(before.Quantity) && current.Copper.AsSpan().SequenceEqual(before.Copper) && current.Caps.AsSpan().SequenceEqual(before.Caps), mode + " approval never creates actor funds/output");
            }
            cases++;
        }
        Console.WriteLine("WORLD_TRANSFER cases=" + cases + " checks=" + checks);

        return checks;
    }
}
