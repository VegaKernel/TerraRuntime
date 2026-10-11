using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public static partial class VanillaShopWallet1458
{
    // Detached reusable buffers only. Runtime inventory and value custody stay
    // with the existing Application owner; these views authorize no mutation.
    internal sealed class PrefixWork
    {
        private readonly WorldItemPickupSlot1458[] originalInventory;
        private readonly WorldItemPickupSlot1458[] originalBanks;
        private readonly WorldItemPickupSlot1458[][] images;
        private readonly WorldItemPickupSlot1458[][] rollback;
        private readonly WorldItemPickupSlot1458[][] sold;
        private readonly WorldItemPickupSlot1458[] flattenedBanks = new WorldItemPickupSlot1458[BankCount * BankSlots];
        private readonly List<(int Wallet, int Slot)> coins = new(PaymentSlots + BankCount * BankSlots);
        private readonly List<int>[] empty = CreateEmptyLists();

        private PrefixWork(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
            ReadOnlySpan<WorldItemPickupSlot1458> banks, WorldItemPickupSlot1458[][] images)
        {
            originalInventory = inventory.ToArray();
            originalBanks = banks.ToArray();
            this.images = images;
            rollback = images.Select(image => new WorldItemPickupSlot1458[image.Length]).ToArray();
            sold = images.Select(image => new WorldItemPickupSlot1458[image.Length]).ToArray();
            CopyBanks();
        }

        internal static bool TryCreate(ReadOnlySpan<WorldItemPickupSlot1458> inventory,
            ReadOnlySpan<WorldItemPickupSlot1458> banks, out PrefixWork? work)
        {
            work = null;
            if (!TryCapture(inventory, banks, out var images)) return false;
            work = new(inventory, banks, images);
            return true;
        }

        internal ReadOnlySpan<WorldItemPickupSlot1458> Inventory => images[0];
        internal ReadOnlySpan<WorldItemPickupSlot1458> Banks => flattenedBanks;
        internal ReadOnlySpan<WorldItemPickupSlot1458> SoldInventory => sold[0];

        internal bool TryPayNext(long price)
        {
            for (int wallet = 0; wallet < images.Length; wallet++)
                images[wallet].CopyTo(rollback[wallet], 0);
            if (!TryPay(images, price, coins, empty))
            {
                for (int wallet = 0; wallet < images.Length; wallet++)
                    rollback[wallet].CopyTo(images[wallet], 0);
                return false;
            }
            CopyBanks();
            return true;
        }

        internal void SetCursor(in WorldItemPickupSlot1458 cursor)
            => images[0][VanillaPlayerItemSlotCatalog.InventoryMouseItem] = cursor;

        internal bool TrySellCurrent(long selling, long buying, int sourceValue,
            int count, int memoAmount, out long grant)
        {
            grant = 0;
            for (int wallet = 0; wallet < images.Length; wallet++)
                images[wallet].CopyTo(sold[wallet], 0);
            if (selling > 0 && buying >= 0)
            {
                long perItem = Math.Max(1, selling / 5);
                try
                {
                    grant = checked(perItem * count + (buying - perItem) * Math.Min(memoAmount, count));
                }
                catch (OverflowException)
                {
                    return false;
                }
                if (grant < 0 || !TryDepositSale(sold[0], grant))
                {
                    // Source zero-value sale clears the cursor if the ordinary
                    // payout cannot complete; discard its partial scratch writes.
                    if (sourceValue != 0) return false;
                    images[0].CopyTo(sold[0], 0);
                    grant = 0;
                }
            }
            else if (sourceValue != 0)
            {
                return false;
            }
            sold[0][VanillaPlayerItemSlotCatalog.InventoryMouseItem] = default;
            return true;
        }

        internal ShopWalletPlan1458 DetachBought(long charged)
        {
            var plan = MakePlan(originalInventory, originalBanks, images, charged);
            return plan with { Inventory = plan.Inventory.ToArray() };
        }

        internal ShopWalletPlan1458 DetachSold(long grant)
        {
            var plan = MakePlan(images[0], flattenedBanks, sold, grant);
            return plan with { Inventory = plan.Inventory.ToArray() };
        }

        private void CopyBanks()
        {
            for (int bank = 0; bank < BankCount; bank++)
                images[bank + 1].CopyTo(flattenedBanks, bank * BankSlots);
        }
    }
}
