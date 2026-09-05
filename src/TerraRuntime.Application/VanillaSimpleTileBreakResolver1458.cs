using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal enum VanillaTileDropResolutionStatus : byte
{
    NoDrop = 0,
    Resolved = 1,
    WrongPath = 2
}

internal readonly record struct VanillaSimpleTileBreakOutcome(
    VanillaTileDropResolutionStatus DropStatus,
    WorldItemDropStateUpdate Drop,
    bool FillWithHoney,
    byte NpcSpawnCount,
    NpcAiSpawnIntent FirstNpc,
    NpcAiSpawnIntent SecondNpc)
{
    public bool HasDrop => DropStatus == VanillaTileDropResolutionStatus.Resolved;
}

/// <summary>
/// Resolves TerrariaServer 1.4.5.8 simple-cell break outcomes from one immutable tile definition. Runtime authority
/// supplies only contextual facts (nearest-player equipment and the server-owned RNG stream); raw TileID branching
/// stays confined to the definition catalog.
/// </summary>
internal static class VanillaSimpleTileBreakResolver1458
{
    private const float TileSize = 16f;
    private const float SpawnCenterOffset = 8f;
    private const float ItemVelocityScale = 0.1f;
    private const float BeeVelocityScale = 0.002f;

    public static VanillaSimpleTileBreakOutcome Resolve(
        VanillaTileDefinition definition,
        int tileX,
        int tileY,
        bool closestPlayerHasCordage,
        IWorldItemSpawnRandom random)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(random);

        if (definition.BreakPath != VanillaTileBreakPath.SimpleCell)
            return new VanillaSimpleTileBreakOutcome(VanillaTileDropResolutionStatus.WrongPath, default, false, 0, default, default);

        VanillaTileDropRule rule = definition.DropRule;
        switch (rule.Kind)
        {
            case VanillaTileDropRuleKind.None:
                return new VanillaSimpleTileBreakOutcome(VanillaTileDropResolutionStatus.NoDrop, default, false, 0, default, default);

            case VanillaTileDropRuleKind.Fixed:
                return MaterializeFixed(rule, tileX, tileY, random);

            case VanillaTileDropRuleKind.Contextual:
                return ResolveContextual(definition.ContextualDropKind, tileX, tileY, closestPlayerHasCordage, random);

            default:
                return new VanillaSimpleTileBreakOutcome(VanillaTileDropResolutionStatus.WrongPath, default, false, 0, default, default);
        }
    }

    private static VanillaSimpleTileBreakOutcome ResolveContextual(
        VanillaTileContextualDropKind kind,
        int tileX,
        int tileY,
        bool closestPlayerHasCordage,
        IWorldItemSpawnRandom random)
    {
        switch (kind)
        {
            case VanillaTileContextualDropKind.CordageVine:
                if (random.NextInt32(0, 2) != 0 || !closestPlayerHasCordage)
                    return NoDrop();
                return MaterializeItem(VanillaItemIds.VineRope, 1, tileX, tileY, random);

            case VanillaTileContextualDropKind.MushroomVine:
                if (random.NextInt32(0, 2) != 0)
                    return NoDrop();
                return MaterializeItem(VanillaItemIds.GlowingMushroom, 1, tileX, tileY, random);

            case VanillaTileContextualDropKind.Hive:
                return ResolveHive(tileX, tileY, random);

            default:
                return new VanillaSimpleTileBreakOutcome(VanillaTileDropResolutionStatus.WrongPath, default, false, 0, default, default);
        }
    }

    private static VanillaSimpleTileBreakOutcome ResolveHive(
        int tileX,
        int tileY,
        IWorldItemSpawnRandom random)
    {
        // TerrariaServer 1.4.5.8 WorldGen.KillTile_GetItemDrops: one third of Hive breaks leave a full honey cell
        // and produce no block/NPC drop. Other breaks drop one Hive Block and may spawn one or two Bee/SmallBee NPCs.
        if (random.NextInt32(0, 3) == 0)
            return new VanillaSimpleTileBreakOutcome(VanillaTileDropResolutionStatus.NoDrop, default, true, 0, default, default);

        byte npcCount = 0;
        NpcAiSpawnIntent first = default;
        NpcAiSpawnIntent second = default;
        if (random.NextInt32(0, 2) == 0)
        {
            npcCount = random.NextInt32(0, 3) == 0 ? (byte)2 : (byte)1;
            first = CreateBeeSpawn(tileX, tileY, random);
            if (npcCount == 2)
                second = CreateBeeSpawn(tileX, tileY, random);
        }

        // Item.NewItem happens after KillTile_GetItemDrops in vanilla, so consume NPC RNG before item-spawn velocity RNG.
        VanillaSimpleTileBreakOutcome drop = MaterializeItem(
            VanillaItemIds.HiveBlock,
            1,
            tileX,
            tileY,
            random);
        return drop with
        {
            NpcSpawnCount = npcCount,
            FirstNpc = first,
            SecondNpc = second
        };
    }

    private static NpcAiSpawnIntent CreateBeeSpawn(
        int tileX,
        int tileY,
        IWorldItemSpawnRandom random)
    {
        NpcTypeId type = random.NextInt32(VanillaNpcIds.Bee.Value, VanillaNpcIds.SmallBee.Value + 1) == VanillaNpcIds.Bee.Value
            ? VanillaNpcIds.Bee
            : VanillaNpcIds.SmallBee;
        return new NpcAiSpawnIntent(
            type,
            BottomX: tileX * 16 + 8,
            BottomY: tileY * 16 + 15,
            VelocityX: random.NextInt32(-200, 201) * BeeVelocityScale,
            VelocityY: random.NextInt32(-200, 201) * BeeVelocityScale,
            Target: byte.MaxValue);
    }

    private static VanillaSimpleTileBreakOutcome MaterializeFixed(
        VanillaTileDropRule rule,
        int tileX,
        int tileY,
        IWorldItemSpawnRandom random)
    {
        if (rule.PrimaryItem.Value <= 0 || rule.PrimaryStack == 0)
            return NoDrop();

        // NoPrefix is retained on the source-backed definition for frame/object paths. Simple-cell tile drops in
        // 1.4.5.8 resolve to block/material items here, so the committed runtime item state is prefixless.
        return MaterializeItem(rule.PrimaryItem, rule.PrimaryStack, tileX, tileY, random);
    }

    private static VanillaSimpleTileBreakOutcome MaterializeItem(
        ItemTypeId item,
        ushort stack,
        int tileX,
        int tileY,
        IWorldItemSpawnRandom random)
    {
        float halfSize = 6f;
        if (VanillaDefinitionCatalog.TryGetRuntimeDefaults(item, out VanillaItemRuntimeDefaults defaults) && defaults.IsValid)
            halfSize = Math.Min(defaults.Width, defaults.Height) * 0.5f;

        float centerX = tileX * TileSize + SpawnCenterOffset;
        float centerY = tileY * TileSize + SpawnCenterOffset;
        float velocityX = random.NextInt32(-30, 31) * ItemVelocityScale;
        float velocityY = random.NextInt32(-40, -15) * ItemVelocityScale;

        var drop = new WorldItemDropStateUpdate(
            PositionX: centerX - halfSize,
            PositionY: centerY - halfSize,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Stack: checked((short)stack),
            Prefix: VanillaPrefixIds.NoneValue,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: checked((short)item.Value),
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);
        return new VanillaSimpleTileBreakOutcome(VanillaTileDropResolutionStatus.Resolved, drop, false, 0, default, default);
    }

    private static VanillaSimpleTileBreakOutcome NoDrop() =>
        new(VanillaTileDropResolutionStatus.NoDrop, default, false, 0, default, default);
}
