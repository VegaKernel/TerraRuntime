using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

internal static class BankChainMemoAuthority1458Checks
{
    internal static void Run()
    {
        // Configured real owner/capture with two valid terminal private images.
        // These are not claimed as independently captured original UI histories.
        var owner = new RuntimePlayerInventoryStore();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99101), new(new(0), new(1)));
        Xunit.Assert.True(owner.TryAttach(connection));
        var stone = new RuntimePlayerInventoryItem(new(3), 3, new(0), 0);
        var silver = new RuntimePlayerInventoryItem(new(72), 1, new(0), 0);
        Xunit.Assert.True(owner.TrySet(connection, stone.ToCommitRequest(connection.Player.Slot, 10)));
        Xunit.Assert.True(owner.TrySet(connection, silver.ToCommitRequest(connection.Player.Slot, 50)));
        var initialQuantity = new short[59]; initialQuantity[10] = 2;
        var initialCopper = new long[59]; initialCopper[50] = 2;
        owner.AdoptFundedQuantities(connection, initialQuantity, initialCopper);
        Xunit.Assert.True(owner.TryCaptureShopJournal(connection, out var capture));
        Xunit.Assert.NotNull(capture);
        var target = capture.Before.ToArray(); target[10] = default; target[59] = stone;
        var seen = new bool[219]; seen[10] = seen[59] = true;
        foreach (bool varyCopper in new[] { false, true })
        {
            using var work = owner.TryCreateShopBankChainWork(connection, capture, target, seen, 0, 0);
            Xunit.Assert.NotNull(work);
            void AddTerminal(int quantityBudget, int copperBudget)
            {
                var quantity = new short[219]; quantity[59] = (short)quantityBudget;
                var copper = new long[219]; copper[50] = copperBudget;
                foreach (bool step in Visit(work, target.ToArray(), copper, quantity,
                    capture.Caps, Enumerable.Repeat((byte)2, 219).ToArray(), false))
                    Xunit.Assert.True(step);
            }
            AddTerminal(1, 1);
            Xunit.Assert.NotNull(work.Preparation);
            Xunit.Assert.False(work.Ambiguous);
            AddTerminal(varyCopper ? 1 : 2, varyCopper ? 2 : 1);
            Xunit.Assert.True(work.Ambiguous);
            Xunit.Assert.Null(work.Preparation);
            Xunit.Assert.True(capture.IsCurrent);
            Xunit.Assert.Equal(initialQuantity, owner.CaptureFundedQuantities(connection));
            Xunit.Assert.Equal(initialCopper, owner.CaptureFundedCoinValues(connection));
            work.Dispose();
            Xunit.Assert.False(work.MoveNext());
        }
    }

    // Test-only typed runtime accessor; no reflection or shipping getter.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Visit")]
    private static extern IEnumerable<bool> Visit(RuntimePlayerInventoryStore.BankChainWork owner,
        RuntimePlayerInventoryItem[] image, long[] copper, short[] quantity, short[] caps,
        byte[] stage, bool relayed);
}
