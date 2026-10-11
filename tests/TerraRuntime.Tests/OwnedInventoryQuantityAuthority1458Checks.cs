using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;


namespace TerraRuntime.Tests;

internal static class OwnedInventoryQuantityAuthority1458Checks
{
    internal static void Run()
    {

        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        T Field<T>(PlayerAuthority owner, string name) => (T)typeof(PlayerAuthority).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        foreach (int type in new[] { 109, 110, 189, 500, 2209 })
            foreach (bool normalized in new[] { false, true })
                foreach (short initial in new short[] { 1, 3 })
                {
                    var c = new ConnectionHandle(GameCommandSourceId.FromConnection(9001), new(new(0), new(1)));
                    var players = new PlayerAuthority(null, null);
                    var random = new VanillaUnifiedRandom1458(0);
                    if (normalized) players.BindReceiveEquipmentRandom(random);
                    var inv = Field<RuntimePlayerInventoryStore>(players, "inventory");
                    var members = Field<RuntimePlayerMembership>(players, "membership");
                    var member = new RuntimePlayerMember { Connection = c, Slot = c.Player.Slot, Revision = 1, HasMana = true, Mana = 100, MaxMana = 200 };
                    members.Commit(member); Check(inv.TryAttach(c), "attach");
                    var before = new RuntimePlayerInventoryItem(new(type), initial, default, 0);
                    var after = initial == 1 ? default : before with { Stack = (short)(initial - 1) };
                    Check(inv.TrySet(c, before.ToCommitRequest(c.Player.Slot, 2)), "initial item");
                    Check(inv.TryPrepareRemotePhase([new(c, 2, before, after)], inv.Serial, out var use) && use is not null, "prepare owned debit");
                    Check(use!.TryAdoptUnpublished(), "adopt debit/cap");
                    Check(inv.CaptureOwnedQuantities(c).Single() == new OwnedInventoryQuantity1458(2, new(type), (short)(initial - 1)), "retained cap");
                    member.ItemPhase = null;
                    void Report(short t, short quantity, byte flags = 0) => players.TryApply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 2, quantity, 0, t, flags)));
                    void Refuse()
                    {
                        ulong serial = inv.Serial, revision = member.Revision, input = member.ProjectileUseInputRevision;
                        long rejected = players.RejectedEquipmentUpdates, applied = players.AppliedEquipmentUpdates;
                        var rng = random.Clone(); Check(inv.TryGet(c, 2, out var image), "capture item");
                        Report((short)type, initial);
                        Check(players.RejectedEquipmentUpdates == rejected + 1 && players.AppliedEquipmentUpdates == applied, "old count refused");
                        Check(inv.Serial == serial && member.Revision == revision && member.ProjectileUseInputRevision == input, "no partial owner write");
                        Check(inv.TryGet(c, 2, out var actual) && actual == image, "no quantity restored");
                        Check(random.HasSameState(rng), "no prefix cursor advance");
                    }
                    Refuse(); Report(1, 1); Refuse(); Report(0, 0); Refuse();
                    Report(after.IsEmpty ? (short)0 : (short)type, after.Stack, after.IsEmpty ? (byte)0 : (byte)1);
                    Check(inv.TryGet(c, 2, out var ack) && ack.Stack == after.Stack, "remaining quantity acknowledged");
                    Refuse();
                    if (initial > 1)
                    {
                        var last = ack with { Stack = 1 };
                        Check(inv.TryPrepareRemotePhase([new(c, 2, ack, last)], inv.Serial, out var next) && next is not null, "next consume after old phase retired");
                        Check(next!.TryAdoptUnpublished(), "next debit");
                        Check(inv.CaptureOwnedQuantities(c).Single().Maximum == 1, "next attempt cannot raise cap");
                        Refuse();
                    }
                    var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
                    players.TryApply(new PlayerTransferDetachRuntimeCommand(c, detach));
                    var transfer = detach.Task.GetAwaiter().GetResult();
                    Check(transfer is not null && transfer.OwnedInventoryQuantities.Length == 1, "detach carries cap after phase retirement");
                    Check(inv.CaptureOwnedQuantities(c).Length == 0, "detached occupation cleared");
                    var destination = new PlayerAuthority(null, null);
                    var targetInv = Field<RuntimePlayerInventoryStore>(destination, "inventory");
                    var newConnection = c with { Player = c.Player with { Generation = new(2) } };
                    var invalidItems = (RuntimePlayerInventoryItem[])transfer!.Inventory.Clone(); invalidItems[2] = before;
                    var malformed = transfer with { Inventory = invalidItems };
                    var rejectedAttach = new TaskCompletionSource<bool>(); ulong destinationSerial = targetInv.Serial;
                    destination.TryApply(new PlayerTransferAttachRuntimeCommand(newConnection, malformed, 100, 103, false, false, rejectedAttach));
                    Check(!rejectedAttach.Task.GetAwaiter().GetResult(), "over-cap transfer rejected");
                    Check(targetInv.Serial == destinationSerial && targetInv.CaptureOwnedQuantities(newConnection).Length == 0, "bad transfer no owner writes");
                    var duplicate = transfer with { OwnedInventoryQuantities = [transfer.OwnedInventoryQuantities[0], transfer.OwnedInventoryQuantities[0]] };
                    rejectedAttach = new(); destination.TryApply(new PlayerTransferAttachRuntimeCommand(newConnection, duplicate, 100, 103, false, false, rejectedAttach));
                    Check(!rejectedAttach.Task.GetAwaiter().GetResult() && targetInv.Serial == destinationSerial, "duplicate transfer cap refused atomically");
                    var attach = new TaskCompletionSource<bool>(); destination.TryApply(new PlayerTransferAttachRuntimeCommand(newConnection, transfer, 100, 103, false, false, attach));
                    Check(attach.Task.GetAwaiter().GetResult(), "valid transfer accepted");
                    Check(targetInv.CaptureOwnedQuantities(newConnection).Single() == transfer.OwnedInventoryQuantities.Single(), "new generation retained cap");
                    long rejectedAfter = destination.RejectedEquipmentUpdates;
                    destination.TryApply(new PlayerEquipmentRuntimeCommand(newConnection, new(newConnection.Player.Slot, 2, initial, 0, (short)type, 0)));
                    Check(destination.RejectedEquipmentUpdates == rejectedAfter + 1, "transfer does not reopen old count");

                }
        {
            var inv = new RuntimePlayerInventoryStore();
            var first = new ConnectionHandle(GameCommandSourceId.FromConnection(9011), new(new(0), new(1)));
            var second = new ConnectionHandle(GameCommandSourceId.FromConnection(9012), new(new(1), new(1)));
            Check(inv.TryAttach(first) && inv.TryAttach(second), "two actor census");
            foreach (int type in new[] { 109, 110, 189, 500, 2209 })
            {
                var before = new RuntimePlayerInventoryItem(new(type), 3, default, 0); var after = before with { Stack = 2 };
                Check(inv.TrySet(first, before.ToCommitRequest(first.Player.Slot, 2)), "different owned identity in same physical slot");
                Check(inv.TryPrepareRemotePhase([new(first, 2, before, after)], inv.Serial, out var journal) && journal is not null && journal.TryAdoptUnpublished(), "different type consumed");
            }
            Check(inv.CaptureOwnedQuantities(first).Length == 5, "all five same-slot obligations survive");
            foreach (int type in new[] { 109, 110, 189, 500, 2209 })
                Check(!inv.CanAcceptOwnedQuantityReport(first, new(first.Player.Slot, 2, 3, 0, (short)type, 0)), "older identity cannot restore");
            var item = new RuntimePlayerInventoryItem(new(110), 3, default, 0); var consumed = item with { Stack = 2 };
            Check(inv.TrySet(first, item.ToCommitRequest(first.Player.Slot, 3)) && inv.TrySet(second, item.ToCommitRequest(second.Player.Slot, 3)), "two actor inventory");
            ulong serial = inv.Serial;
            Check(inv.TryPrepareRemotePhase([new(first, 3, item, consumed), new(second, 3, item, consumed)], serial, out var census) && census is not null, "prepare complete census");
            var newer = item with { Stack = 1 }; Check(inv.TrySet(second, newer.ToCommitRequest(second.Player.Slot, 3)), "late actor mutation");
            Check(!census!.TryAdoptUnpublished(), "late mutation refuses whole census");
            Check(inv.TryGet(first, 3, out var untouched) && untouched == item, "first actor not partially adopted");
            Check(inv.TryGet(second, 3, out var retained) && retained == newer, "external newer actor state preserved");
            Check(inv.CaptureOwnedQuantities(first).All(x => x.Slot != 3) && inv.CaptureOwnedQuantities(second).Length == 0, "no partial new caps");
            Check(!inv.TryPrepareRemotePhase([new(first, 3, item, consumed), new(first, 3, item, consumed)], inv.Serial, out _), "duplicate physical mutation refuses");
            var image = new RuntimePlayerInventoryItem[59]; image[3] = item;
            Check(!RuntimePlayerInventoryStore.AreValidOwnedQuantities([new(58, new(110), 2)], image), "mouse-slot cap malformed");
            Check(!RuntimePlayerInventoryStore.AreValidOwnedQuantities([new(3, new(28), 2)], image), "unsupported type cap malformed");
            Check(!RuntimePlayerInventoryStore.AreValidOwnedQuantities([new(3, new(110), -1)], image), "negative cap malformed");
            Check(!RuntimePlayerInventoryStore.AreValidOwnedQuantities([new(3, new(110), 2)], image), "over-cap image malformed");
            Check(RuntimePlayerInventoryStore.AreValidOwnedQuantities([new(3, new(110), 3)], image), "exact cap accepted");
        }

    }
}
