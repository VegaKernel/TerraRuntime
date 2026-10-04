using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class WorldItemAuthority
{
    private bool TryPrepareClientCreation(in WorldItemDropStateUpdate wire, out WorldItemDropStateUpdate created,
        out VanillaUnifiedRandom1458? live, out VanillaUnifiedRandom1458? before, out VanillaUnifiedRandom1458? after,
        out VanillaSeasonalItemDropContext1458 calendar)
    {
        created = default; before = after = null; calendar = default;
        live = (SpawnRandom as SystemWorldItemSpawnRandom)?.SourceRandom;
        if (live is null || wire.Stack <= 0 || !float.IsFinite(wire.PositionX) || !float.IsFinite(wire.PositionY) ||
            !float.IsFinite(wire.VelocityX) || !float.IsFinite(wire.VelocityY) || !float.IsFinite(wire.ShimmerTime) ||
            !wire.TryGetItemType(out var requested) || !VanillaDefinitionCatalog.TryGetWorldDrop(requested, out var definition)) return false;
        bool seasonal = requested == VanillaWallOfFleshItemIds.Heart || requested == VanillaBossRecoveryItemIds1458.Star;
        if (seasonal && seasonalContext is null) return false;
        calendar = seasonalContext?.Invoke() ?? default;
        before = live.Clone(); after = live.Clone();
        var random = new ClientCreationRolls(after);
        ItemTypeId type = VanillaSeasonalItemDropFacts1458.Resolve(requested, calendar, random);
        if (!VanillaDefinitionCatalog.TryGetWorldDrop(type, out definition)) return false;
        // NewItem uses prefix0. Positive explicit weapon-prefix application has a separate source validity
        // boundary; it is not inferred from a natural family. Prefix-less admitted materials retain source0.
        if (wire.Prefix != 0 && definition.PrefixFamily != VanillaItemPrefixFamily.None) return false;
        var initial = new WorldItemDropStateUpdate(wire.PositionX, wire.PositionY,
            after.Next(-30, 31) * 0.1f, definition.NoGravity ? after.Next(-30, 31) * 0.1f : after.Next(-40, -15) * 0.1f,
            wire.Stack, 0, WorldItemOwnershipMode.None, checked((short)type.Value), false, 0, 0);
        // MessageBuffer21 overlays the transmitted fields after actual NewItem materialization, including its
        // two velocity draws. The seasonal-created type is retained by the source flag4 branch.
        created = initial with { VelocityX = wire.VelocityX, VelocityY = wire.VelocityY, Prefix = 0,
            Ownership = wire.Ownership, Shimmered = wire.Shimmered, ShimmerTime = wire.ShimmerTime,
            EnemyGrabDelayTime = wire.EnemyGrabDelayTime };
        return true;
    }

    private sealed class ClientCreationRolls(VanillaUnifiedRandom1458 random) : INpcLootRollSource
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);
        public int RollLuck(int chanceDenominator) => throw new InvalidOperationException("Client NewItem does not call RollLuck.");
    }
}
