using Xunit;
using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

internal static partial class CraftStateChecks
{
    internal static void RunCurrencyIngress()
    {
        foreach (int crafts in new[] { 1, 2 })
        foreach (int fundedSlot in new[] { 0, 1 })
        {
            using var fixture = new Fixture("inactive-first", true);
            var connection = fixture.Connections[2];
            for (short slot = 0; slot < 2; slot++)
                fixture.Apply(new PlayerEquipmentRuntimeCommand(connection,
                    new(connection.Player.Slot, (short)(99 + slot), 100, 0, 71, 0)));
            fixture.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
            for (int tick = 0; !fixture.Players.CanApplyShopSource(connection.Source) && tick < 2000; tick++) fixture.Tick();
            Assert.True(fixture.Players.CanApplyShopSource(connection.Source));
            var store = Inventory(fixture.Players);
            fixture.Players.SetCraftingWorldFacts(default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 });
            store.AdoptFundedQuantities(connection, new short[59], new long[59]);
            BankMoney(store)[2 * 160 + fundedSlot] = 1;
            Assert.True(store.TryCaptureShopJournal(connection, out var before));
            Assert.NotNull(before);
            // Source-grounded configured actor/container context. These typed
            // commands do not claim an original UI or natural wire chronology.
            fixture.Tiles.Set(103, 99, new WorldTile { Type = 29, Flags = WorldTileFlags.Active });
            fixture.Apply(new PlayerEquipmentRuntimeCommand(connection,
                new(connection.Player.Slot, 58, (short)crafts, 0, 72, 0)));
            var capturedAtOutput = fixture.Member(2).CursorMove?.Shop?.Capture;
            Assert.NotNull(capturedAtOutput);
            Assert.True(capturedAtOutput.Before[58].IsEmpty);
            Assert.Equal(71, capturedAtOutput.Before[59].ItemType.Value);
            Assert.Equal(100, capturedAtOutput.Before[59].Stack);
            Assert.Equal(100, capturedAtOutput.Before[60].Stack);
            Assert.Equal(1L, capturedAtOutput.Copper.Sum());
            for (short slot = 0; slot < crafts; slot++)
                fixture.Apply(new PlayerEquipmentRuntimeCommand(connection,
                    new(connection.Player.Slot, (short)(99 + slot), 0, 0, 0, 0)));
            fixture.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
            int ticks = 0;
            while (!fixture.Players.CanApplyShopSource(connection.Source) && ticks++ < 2000) fixture.Tick();
            Assert.True(ticks < 2000);
            Assert.True(store.TryCaptureShopJournal(connection, out var after));
            Assert.NotNull(after);
            if (crafts == 2)
            {
                Assert.Equal(72, after.Before[58].ItemType.Value); Assert.Equal(2, after.Before[58].Stack);
                Assert.Equal(1L, after.Copper[58]);
                Assert.True(after.Before[59].IsEmpty); Assert.True(after.Before[60].IsEmpty);
                Assert.Equal(1L, after.Copper.Sum());
            }
            else
            {
                // No ambiguous transformation may create paid output. Ordinary
                // raw-report physical policy is asserted separately from credit.
                Assert.Equal(0L, after.Copper[58]);
            }
            for (int slot = 0; slot < 219; slot++)
            {
                Assert.Equal(0, after.Quantity[slot]);
                if (crafts == 2 && slot is not (58 or 59 or 60))
                    Assert.Equal(before.Before[slot], after.Before[slot]);
            }
            for (int lane = 0; lane < 219 * RuntimePlayerInventoryStore.QuantityLaneCount; lane++) Assert.Equal(before.Caps[lane], after.Caps[lane]);

        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "shopBankFundedCopper")]
    private static extern ref long[] BankMoney(RuntimePlayerInventoryStore owner);
}
