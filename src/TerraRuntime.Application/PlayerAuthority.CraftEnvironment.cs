using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private RuntimeTownCommerceWorldFacts1458? craftingWorldFacts;

    // Composition supplies the same immutable world facts as the existing town
    // scene owner. Packet36 is never a recipe environment certificate.
    internal void SetCraftingWorldFacts(in RuntimeTownCommerceWorldFacts1458 facts) => craftingWorldFacts = facts;

    internal sealed class CraftEnvironmentCapture(PlayerAuthority owner, ConnectionHandle connection,
        RuntimePlayerMember member, ulong inputRevision, RuntimeTownCommerceWorldFacts1458 world,
        byte unlockFlags, byte torchFlags, bool paidTorchFavor, bool[] stations, RecipeEnvironment1458 environment,
        Dictionary<WorldSectionId, long> sections, byte[] nearbyBanks,
        RuntimeProjectileStore.CraftRemovalPreparation? containers)
    {
        internal bool[] Stations => stations;
        internal byte[] NearbyBanks => nearbyBanks;
        internal RecipeEnvironment1458 Environment => environment;
        internal bool IsCurrent => owner.craftingWorldFacts == world && owner.worldTiles is { } tiles &&
            owner.membership.TryGet(connection, out var current) && ReferenceEquals(current, member) &&
            current.ProjectileUseInputRevision == inputRevision &&
            owner.transferProfiles.TryCapture(connection, out var appearance, out _, out _) &&
            (appearance?.ConsumableUnlockFlags ?? 0) == unlockFlags &&
            ((appearance?.TorchAndCartFlags ?? 0) & 4) == torchFlags && member.PaidTorchFavor == paidTorchFavor &&
            sections.All(pair => tiles.GetSectionVersion(pair.Key) == pair.Value) &&
            (containers is null || containers.IsCurrentContainerCensus(craftContainerProjectileTypes));
    }

    private static readonly ProjectileTypeId[] craftContainerProjectileTypes = [new(525), new(960), new(734)];

    internal sealed class CraftEnvironmentResolution
    {
        internal CraftEnvironmentCapture? Capture;
    }

    private static bool NeedsCraftScene(PendingShopJournal journal)
    {
        var possibleResults = new HashSet<int>();
        for (int slot = 0; slot < 219; slot++)
            if (!journal.Reports[slot].IsEmpty &&
                (journal.Capture.Before[slot].ItemType != journal.Reports[slot].ItemType ||
                 journal.Capture.Before[slot].Stack < journal.Reports[slot].Stack))
                possibleResults.Add(journal.Reports[slot].ItemType.Value);
        foreach (int index in possibleResults.SelectMany(RuntimePlayerInventoryStore.CraftRecipesForResult))
            if (VanillaRecipeCatalog1458.TryGet(index, out var recipe) &&
                possibleResults.Contains(recipe.ResultType.Value == 5437 ? 5358 : recipe.ResultType.Value) &&
                (recipe.Flags & (VanillaRecipeFlags1458.NeedSnowBiome | VanillaRecipeFlags1458.NeedGraveyardBiome)) != 0)
                return true;
        return false;
    }

    private IEnumerable<int> PrepareCraftEnvironment(ConnectionHandle connection, PendingShopJournal journal,
        RuntimeProjectileStore.CraftRemovalPreparation? containers, CraftEnvironmentResolution result)
    {
        yield return 0;
        if (worldTiles is not { } tiles || craftingWorldFacts is not { } world ||
            !membership.TryGet(connection, out var member) || !ReferenceEquals(journal.ObservedMember, member) ||
            member.ProjectileUseInputRevision != journal.ObservedInputRevision ||
            !transferProfiles.TryCapture(connection, out var appearance, out _, out _)) yield break;
        byte flags = appearance?.ConsumableUnlockFlags ?? 0;
        if ((flags & 128) != 0) yield break;
        var actor = journal.ObservedActor;
        var body = actor.HasMount ? VanillaPlayerMountHitbox1458.Resolve(actor.MountType) : (Width: 20f, Height: 42f);
        // Player.ResetEffects' ordinary range is5/3. Journey range powers are
        // not inferred from imports; composition must own them before admission.
        if (world.GameMode == 3) yield break;
        if (!VanillaSelectedConsumable1458.TryGetPermanentCraftingRegion(actor.PositionX, actor.PositionY,
            (int)body.Width, (int)body.Height, 5, 3, new(flags), out var region)) yield break;
        int left = Math.Clamp(region.Left, 0, tiles.Dimensions.WidthTiles - 1);
        int right = Math.Clamp(region.Right, 0, tiles.Dimensions.WidthTiles - 1);
        int top = Math.Clamp(region.Top, 0, tiles.Dimensions.HeightTiles - 1);
        int bottom = Math.Clamp(region.Bottom, 0, tiles.Dimensions.HeightTiles - 1);
        var stations = new bool[754];
        byte torchFlags = (byte)((appearance?.TorchAndCartFlags ?? 0) & 4);
        bool paidTorchFavor = member.PaidTorchFavor;
        // A compatible initial appearance import is not funded favor custody.
        var env = new RecipeEnvironment1458(false, false, false, false, false, false,
            paidTorchFavor && torchFlags != 0,
            world.RemixWorld && world.GoodWorld);
        var sections = new Dictionary<WorldSectionId, long>();
        int centerX = Math.Clamp((int)((actor.PositionX + body.Width * .5f) / 16f), 0, tiles.Dimensions.WidthTiles - 1);
        int centerY = Math.Clamp((int)((actor.PositionY + body.Height * .5f) / 16f), 0, tiles.Dimensions.HeightTiles - 1);
        bool sceneNeeded = NeedsCraftScene(journal);
        bool banksNeeded = journal.Capture.Before.Skip(59).Any(static item => !item.IsEmpty);
        int extraX = sceneNeeded ? VanillaTownSceneMetricsScanner1458.ScanWidth / 2 : banksNeeded ? 39 : 0;
        int extraY = sceneNeeded ? VanillaTownSceneMetricsScanner1458.ScanHeight / 2 : banksNeeded ? 39 : 0;
        int scanLeft = Math.Min(left, Math.Max(0, centerX - extraX));
        int scanTop = Math.Min(top, Math.Max(0, centerY - extraY));
        int scanRight = Math.Max(right, Math.Min(tiles.Dimensions.WidthTiles - 1, centerX + extraX));
        int scanBottom = Math.Max(bottom, Math.Min(tiles.Dimensions.HeightTiles - 1, centerY + extraY));
        var firstSection = TerrariaSectionGeometry.FromTile(tiles.Dimensions, scanLeft, scanTop);
        var lastSection = TerrariaSectionGeometry.FromTile(tiles.Dimensions, scanRight, scanBottom);
        // Capture every required section version BEFORE the first cooperative
        // world read. An unvisited section cannot become a newer baseline.
        for (int sx = firstSection.X; sx <= lastSection.X; sx++)
            for (int sy = firstSection.Y; sy <= lastSection.Y; sy++)
            {
                var section = new WorldSectionId(sx, sy);
                sections.Add(section, tiles.GetSectionVersion(section));
            }
        for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                yield return 0;
                var section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, x, y);
                sections.TryAdd(section, tiles.GetSectionVersion(section));
                var tile = tiles.Get(x, y);
                if (!VanillaRecipeCatalog1458.TryAccumulateCraftingTile(tile.Type, tile.IsActive,
                    tile.LiquidAmount, (int)tile.LiquidKind, stations, ref env)) yield break;
            }
        int snow = 0, graves = 0, sunflowers = 0;
        // Reuse the existing source town-scene window and its recipe-relevant
        // counters. Each tile visit is a scheduler step, not one synchronous scan.
        if (sceneNeeded)
        for (int x = Math.Max(0, centerX - VanillaTownSceneMetricsScanner1458.ScanWidth / 2);
            x < Math.Min(tiles.Dimensions.WidthTiles, centerX + (VanillaTownSceneMetricsScanner1458.ScanWidth + 1) / 2); x++)
            for (int y = Math.Max(0, centerY - VanillaTownSceneMetricsScanner1458.ScanHeight / 2);
                y < Math.Min(tiles.Dimensions.HeightTiles, centerY + VanillaTownSceneMetricsScanner1458.ScanHeight / 2); y++)
            {
                yield return 0;
                var section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, x, y);
                sections.TryAdd(section, tiles.GetSectionVersion(section));
                var tile = tiles.Get(x, y);
                if (!tile.IsActive) continue;
                if (tile.Type is 147 or 148 or 161 or 162 or 164 or 163 or 200) snow++;
                if (tile.Type == 85) graves++;
                if (tile.Type == 27) sunflowers++;
            }
        var nearby = new List<byte>(4);
        bool closedVoid = journal.Capture.Before.Take(58).Any(static item => item.ItemType.Value == 5325 && item.Stack > 0);
        // Source scans projectiles before tiles, all slots0..999 irrespective of
        // owner. The token captured relevant membership and geometry at seal;
        // unrelated projectile clocks/motion are not this query's inputs.
        if (banksNeeded)
        {
            if (containers is null) yield break;
            while (containers.MoveNext()) yield return 0;
            if (!containers.IsCurrentContainerCensus(craftContainerProjectileTypes)) yield break;
            float playerCenterX = actor.PositionX + body.Width * .5f;
            float playerCenterY = actor.PositionY + body.Height * .5f;
            for (ushort slot = 0; slot < 1000; slot++)
            {
                yield return 0;
                if (!containers.TryGetCaptured(slot, out var projectile)) continue;
                int bank = projectile.Type.Value switch { 525 or 960 => 0, 734 => 3, _ => -1 };
                if (bank < 0 || (bank == 3 && closedVoid)) continue;
                if (!TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(projectile.Type, out var definition)) yield break;
                int hitboxLeft = (int)projectile.PositionX;
                int hitboxTop = (int)projectile.PositionY;
                int nearestX = (int)(Math.Clamp(playerCenterX, hitboxLeft, hitboxLeft + definition.Width) / 16f);
                int nearestY = (int)(Math.Clamp(playerCenterY, hitboxTop, hitboxTop + definition.Height) / 16f);
                if (nearestX >= centerX - 39 && nearestX < centerX + 40 &&
                    nearestY >= centerY - 39 && nearestY < centerY + 40 && !nearby.Contains((byte)bank))
                    nearby.Add((byte)bank);
            }
        }
        // NearbyChests uses an x-major 79x79 rectangle about player.Center.
        // Source tiles are inspected by type even if inactive. This is container
        // eligibility, not permission for a raw bank quantity report.
        if (banksNeeded)
        for (int x = Math.Max(0, centerX - 39); x < Math.Min(tiles.Dimensions.WidthTiles, centerX + 40); x++)
            for (int y = Math.Max(0, centerY - 39); y < Math.Min(tiles.Dimensions.HeightTiles, centerY + 40); y++)
            {
                yield return 0;
                var section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, x, y);
                sections.TryAdd(section, tiles.GetSectionVersion(section));
                int bank = tiles.Get(x, y).Type switch { 29 => 0, 97 => 1, 463 => 2, 491 => 3, _ => -1 };
                if (bank >= 0 && (bank != 3 || !closedVoid) && !nearby.Contains((byte)bank)) nearby.Add((byte)bank);
            }
        env = env with { Snow = snow >= (world.SkyblockWorld ? 300 : 1500), Graveyard = Math.Max(0, graves - sunflowers / 2) >= 28 };
        var captured = new CraftEnvironmentCapture(this, connection, member, journal.ObservedInputRevision,
            world, flags, torchFlags, paidTorchFavor, stations, env, sections, nearby.ToArray(), containers);
        if (captured.IsCurrent) result.Capture = captured;
    }
}
