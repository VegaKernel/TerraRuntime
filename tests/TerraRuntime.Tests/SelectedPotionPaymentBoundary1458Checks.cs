using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

// Configured direct-owner checks; original ItemCheck does not expose private funding.
// The unchanged five ceiling lanes remain separate from generic funded quantities.
internal static class SelectedPotionPaymentBoundary1458Checks
{
    internal static void Run()
    {
        foreach (int type in new[] { 28, 188, 499, 3544, 110, 189, 500, 2209 })
        {
            var owner = new RuntimePlayerInventoryStore();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(77101), new(new(0), new(1)));
            Check(owner.TryAttach(connection), "attach");
            var before = new RuntimePlayerInventoryItem(new(type), 3, new(0), 0);
            Check(owner.TrySet(connection, before.ToCommitRequest(connection.Player.Slot, 0)), "physical source image");
            var funded = new short[59];
            funded[0] = 1;
            owner.AdoptFundedQuantities(connection, funded);
            var caps = owner.CaptureOwnedQuantities(connection);
            Check(owner.TryPrepareSelectedPotionConsumption(connection, 0, out var mutation), "one funded unit admits payment");
            Check(mutation.Before == before && mutation.After == before with { Stack = 2 }, "exact one physical debit");
            Check(owner.TryPrepareRemotePhase([mutation], owner.Serial, out var prepared) && prepared is not null,
                "shared inventory journal prepares payment");
            var accepted = prepared!;
            Check(accepted.TryAdoptUnpublished(), "payment adopts once");
            Check(!accepted.TryAdoptUnpublished(), "payment cannot adopt twice");
            Check(owner.TryGet(connection, 0, out var after) && after == before with { Stack = 2 }, "physical three becomes two");
            Check(owner.CaptureFundedQuantities(connection)[0] == 0, "funded one becomes zero despite physical two");
            Check(!owner.TryPrepareSelectedPotionConsumption(connection, 0, out _), "remaining unfunded stock cannot pay another benefit");
            Check(owner.CaptureFundedCoinValues(connection).All(value => value == 0), "payment never grants copper");
            if (type is 28 or 188 or 499 or 3544)
                Check(caps.AsSpan().SequenceEqual(owner.CaptureOwnedQuantities(connection)), "healing uses quantity funding without adding cap lanes");
            else
                Check(owner.CaptureOwnedQuantities(connection).Any(cap => cap.Slot == 0 && cap.ItemType.Value == type && cap.Maximum == 2),
                    "existing mana ceiling accompanies its debit");

            // A generic physical report cannot replenish paid provenance. Healing
            // physical growth can remain legacy-compatible while funding stays zero.
            bool rawAccepted = owner.TrySet(connection, before.ToCommitRequest(connection.Player.Slot, 0));
            Check(type is 28 or 188 or 499 or 3544 ? rawAccepted : !rawAccepted,
                "preserve existing heal receive versus mana ceiling policy");
            Check(owner.CaptureFundedQuantities(connection)[0] == 0, "raw restoration never renews paid quantity");
            Check(!owner.TryPrepareSelectedPotionConsumption(connection, 0, out _), "raw restoration cannot reopen payment");
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
