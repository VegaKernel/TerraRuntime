using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;
namespace TerraRuntime.Application;
internal sealed partial class PlayerAuthority
{
    private void AdvanceOwnedFoodAfterFailedPhase()
    {
        // The world tick still elapsed when an unrelated actor/provider invalidated the
        // shared simulation. Spend only retained food time, without callbacks/providers.
        foreach (var member in membership.Members.ToArray())
        {
            if (member.Slot.Value == byte.MaxValue ||
                transferProfiles.CaptureBuffState(member.Connection) is not { HasOwnedFoodSlots: true } before) continue;
            var after = before.Clone();
            if (member.IsDead) after.ClearOwnedFood();
            else
            {
                bool outside = worldTiles is null || VanillaPlayerHealthContext1458.IsRemoteOutOfRange(
                    member.PositionX, member.PositionY, 20, 42,
                    worldTiles.Dimensions.WidthTiles, worldTiles.Dimensions.HeightTiles);
                after.AdvanceOwnedFoodClock(currentCombatTick, !outside);
            }
            if (membership.TryGet(member.Connection, out var current) && ReferenceEquals(current, member))
                transferProfiles.TryAdoptBuffState(member.Connection, before, after);
        }
    }
}
