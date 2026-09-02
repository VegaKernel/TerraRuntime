using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

internal interface IVanillaMysticFrogTeleportRandom1458
{
    int Next(int minInclusive, int maxExclusive);
}

internal sealed class SystemVanillaMysticFrogTeleportRandom1458 : IVanillaMysticFrogTeleportRandom1458
{
    public int Next(int minInclusive, int maxExclusive) => Random.Shared.Next(minInclusive, maxExclusive);
}

/// <summary>
/// TerrariaServer 1.4.5.8 NPC.TryTeleportingCaughtMysticFrog authoritative gameplay path. Visual teleport/smoke
/// packets are presentation concerns; the live NPC generation, position and fallback despawn are committed here.
/// </summary>
internal sealed class RuntimeMysticFrogCatchService1458
{
    private const int FrogType = 687;
    private const int FrogWidth = 18;
    private const int FrogHeight = 20;
    private const int SearchRange = 15;
    private const int TelefragDistanceTiles = 8;
    private const int MaximumAttempts = 100;
    private const float PlayerWidth = 20f;
    private const float PlayerHeight = 42f;
    private const float PlayerVelocityLookahead = 20f;

    private readonly RuntimeNpcStore npcs;
    private readonly WorldTileStore tiles;
    private readonly IRuntimePlayerSlotSnapshotLookup players;
    private readonly IVanillaMysticFrogTeleportRandom1458 random;

    public RuntimeMysticFrogCatchService1458(
        RuntimeNpcStore npcs,
        WorldTileStore tiles,
        IRuntimePlayerSlotSnapshotLookup players,
        IVanillaMysticFrogTeleportRandom1458? random = null)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.random = random ?? new SystemVanillaMysticFrogTeleportRandom1458();
    }

    public bool TryApply(NpcHandle handle, out bool teleported)
    {
        teleported = false;
        if (!npcs.TryGet(handle, out NpcSnapshot frog) || frog.Type != FrogType)
            return false;

        int targetTileX = (int)(frog.PositionX + FrogWidth / 2f) / 16;
        int targetTileY = (int)(frog.PositionY + FrogHeight / 2f) / 16;
        if (TryFindTeleportSpot(targetTileX, targetTileY, out int tileX, out int tileY))
        {
            var update = new NpcStateUpdate(
                Type: frog.Type,
                NetId: frog.NetId,
                PositionX: tileX * 16f - FrogWidth / 2f,
                PositionY: tileY * 16f - FrogHeight,
                VelocityX: frog.VelocityX,
                VelocityY: frog.VelocityY,
                Target: frog.Target,
                Ai: frog.Ai,
                Simulation: frog.Simulation);
            teleported = npcs.TryUpdate(frog.Handle, in update, out _);
            return teleported;
        }

        return npcs.TryDespawn(frog.Handle);
    }

    private bool TryFindTeleportSpot(int targetTileX, int targetTileY, out int chosenX, out int chosenY)
    {
        chosenX = 0;
        chosenY = 0;
        WorldDimensions dimensions = tiles.Dimensions;

        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            int x = random.Next(targetTileX - SearchRange, targetTileX + SearchRange + 1);
            int startY = random.Next(targetTileY - SearchRange, targetTileY + SearchRange + 1);
            for (int y = startY; y < targetTileY + SearchRange; y++)
            {
                if (x < 1 || x >= dimensions.WidthTiles - 1 || y < 4 || y >= dimensions.HeightTiles)
                    continue;
                if (y >= targetTileY - 1 && y <= targetTileY + 1 && x >= targetTileX - 1 && x <= targetTileX + 1)
                    continue;

                WorldTile ground = tiles.Get(x, y);
                if (!IsNActiveSolid(in ground))
                    continue;
                WorldTile above = tiles.Get(x, y - 1);
                if (above.LiquidAmount > 0 && above.LiquidKind == WorldLiquidKind.Lava)
                    continue;
                if (HasSolidTiles(x - 1, x + 1, y - 4, y - 1) || WouldTelefragPlayer(x, y))
                    continue;

                chosenX = x;
                chosenY = y;
                return true;
            }
        }

        return false;
    }

    private bool WouldTelefragPlayer(int tileX, int tileY)
    {
        float left = tileX * 16f - TelefragDistanceTiles * 16f;
        float top = tileY * 16f - TelefragDistanceTiles * 16f;
        float right = tileX * 16f + 16f + TelefragDistanceTiles * 16f;
        float bottom = tileY * 16f + 16f + TelefragDistanceTiles * 16f;

        for (int slot = 0; slot <= byte.MaxValue; slot++)
        {
            if (!players.TryGetPlayer(new PlayerSlotId((byte)slot), out PlayerStateSnapshot player) ||
                !player.Player.IsAssigned || player.IsDead)
            {
                continue;
            }

            float pLeft = MathF.Min(player.PositionX, player.PositionX + player.VelocityX * PlayerVelocityLookahead);
            float pTop = MathF.Min(player.PositionY, player.PositionY + player.VelocityY * PlayerVelocityLookahead);
            float pRight = MathF.Max(player.PositionX + PlayerWidth, player.PositionX + player.VelocityX * PlayerVelocityLookahead + PlayerWidth);
            float pBottom = MathF.Max(player.PositionY + PlayerHeight, player.PositionY + player.VelocityY * PlayerVelocityLookahead + PlayerHeight);
            if (pLeft < right && pRight > left && pTop < bottom && pBottom > top)
                return true;
        }

        return false;
    }

    private bool HasSolidTiles(int startX, int endX, int startY, int endY)
    {
        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (IsNActiveSolid(in tile))
                    return true;
            }
        }
        return false;
    }

    private static bool IsNActiveSolid(in WorldTile tile) =>
        tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType);
}

/// <summary>
/// Source-backed Projectile.Damage_TryUsingPowders type-10 slice for TerrariaServer 1.4.5.8. One powder hitbox
/// can transform every intersecting Demon Tax Collector and Mystic Frog, matching the source NPC scan order.
/// </summary>
internal sealed class RuntimePurificationPowderNpcInteraction1458
{
    private const int PurificationPowderType = 10;
    private const int NormalPowderSize = 64;
    private const int InfectedSeedPowderSize = 106;
    private const int MysticFrogWidth = 18;
    private const int MysticFrogHeight = 20;
    private const int MysticFrogLifeMax = 5;

    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeProjectileStore projectiles;
    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly RuntimeTownNpcRescueService1458 townRescue;
    private readonly RuntimeWorldProgressionMutations progression;
    private readonly bool infectedSeed;
    private readonly ProjectileSnapshot[] projectileBuffer = new ProjectileSnapshot[RuntimeProjectileStore.MaximumProtocolAddressableCapacity];
    private readonly NpcSnapshot[] npcBuffer = new NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];

    public RuntimePurificationPowderNpcInteraction1458(
        RuntimeNpcStore npcs,
        RuntimeProjectileStore projectiles,
        RuntimeTownNpcStateStore townNpcs,
        RuntimeTownNpcRescueService1458 townRescue,
        RuntimeWorldProgressionMutations progression,
        bool infectedSeed)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.townNpcs = townNpcs ?? throw new ArgumentNullException(nameof(townNpcs));
        this.townRescue = townRescue ?? throw new ArgumentNullException(nameof(townRescue));
        this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
        this.infectedSeed = infectedSeed;
    }

    public int Tick()
    {
        int transformed = 0;
        int projectileCount = projectiles.CopyActive(projectileBuffer);
        for (int p = 0; p < projectileCount; p++)
        {
            ProjectileSnapshot powder = projectileBuffer[p];
            if (powder.Type.Value != PurificationPowderType)
                continue;

            int size = infectedSeed ? InfectedSeedPowderSize : NormalPowderSize;
            int npcCount = npcs.CopyActive(npcBuffer);
            for (int n = 0; n < npcCount; n++)
            {
                NpcSnapshot npc = npcBuffer[n];
                if (npc.TypeIdentity == VanillaNpcIds.DemonTaxCollector)
                {
                    if (Intersects(powder.PositionX, powder.PositionY, size, size, npc.PositionX, npc.PositionY, 18, 40) &&
                        townRescue.TryRescuePurificationPowder(npc.Handle, out _))
                    {
                        transformed++;
                    }
                }
                else if (VanillaNpcCatchCatalog1458.IsMysticFrog(npc.TypeIdentity) &&
                         Intersects(powder.PositionX, powder.PositionY, size, size, npc.PositionX, npc.PositionY, MysticFrogWidth, MysticFrogHeight) &&
                         TryTransformMysticFrog(in npc))
                {
                    transformed++;
                }
            }
        }
        return transformed;
    }

    private bool TryTransformMysticFrog(in NpcSnapshot source)
    {
        NpcTypeId targetType = VanillaNpcIds.TownSlimeYellow;
        if (!VanillaTownNpcFacts1458.TryGetDefinition(targetType, out VanillaNpcDefinition target))
            return false;

        int oldLife = source.Simulation.Life > 0 ? source.Simulation.Life : MysticFrogLifeMax;
        int life = Math.Max(1, oldLife * target.LifeMax / MysticFrogLifeMax);
        var simulation = NpcSimulationState.Initial with
        {
            Life = life,
            LifeMax = target.LifeMax,
            TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
            DirectionX = source.Simulation.DirectionX,
            DirectionY = source.Simulation.DirectionY,
            SpriteDirection = source.Simulation.SpriteDirection
        };
        var update = new NpcStateUpdate(
            Type: targetType.Value,
            NetId: checked((short)targetType.Value),
            PositionX: source.PositionX,
            PositionY: source.PositionY + MysticFrogHeight - target.Height,
            VelocityX: source.VelocityX,
            VelocityY: source.VelocityY,
            Target: source.Target,
            Ai: default,
            Simulation: simulation);
        if (!npcs.TryUpdate(source.Handle, in update, out NpcSnapshot transformed))
            return false;

        if (townNpcs.CanAdoptRescuedResident(source.Handle.Slot, targetType) &&
            !townNpcs.TryAdoptRescuedResident(source.Handle.Slot, targetType, in transformed))
        {
            throw new InvalidOperationException("Preflighted Mystic Frog transformation could not be adopted into the town roster.");
        }

        progression.MarkSlimeYellowSpawnUnlocked();
        return true;
    }

    private static bool Intersects(float ax, float ay, int aw, int ah, float bx, float by, int bw, int bh)
    {
        int aLeft = (int)ax;
        int aTop = (int)ay;
        int bLeft = (int)bx;
        int bTop = (int)by;
        return aLeft < bLeft + bw && aLeft + aw > bLeft && aTop < bTop + bh && aTop + ah > bTop;
    }
}
