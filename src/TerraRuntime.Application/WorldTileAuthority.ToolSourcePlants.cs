using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.Gameplay.Tiles;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private bool plantDestroying;
        private static bool IsSourcePlant(ushort type) => type is 3 or 24 or 61 or 71 or 73 or 74 or 110 or 113 or 201 or 637 or 703;

        // WorldGen.PlantCheck (1458) has no reset-frame random draw. Its bad
        // substrate conversion runs before any recursive KillTile, and global
        // destroyObject suppresses reentry until that full source call returns.
        private bool FramePlant(int x, int y, in WorldTile tile)
        {
            if (plantDestroying || saplingDestroying || container?.Destroying == true) return true;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                if (!Overlay.TryReadSourceSafely(x + dx, y + dy, out _)) return false;
            if (!Overlay.TryReadSourceSafely(x, y + 1, out var below)) return false;
            int ground = below.IsActive && (below.Flags & WorldTileFlags.Inactive) == 0 &&
                !below.IsHalfBrick && below.SourceSlope == 0 ? below.Type : -1;
            if (tile.Type == 703)
            {
                if (!VanillaToolTileCatalog1458.TryGet(below.TileType, out var facts)) return false;
                int frame = below.FrameX / 18;
                bool platform = facts.IsPlatform && (frame is >= 0 and <= 7 or >= 12 and <= 16 or >= 25 and <= 26);
                bool solid = below.IsActive && !below.IsActuated && !below.IsHalfBrick &&
                    (facts.IsSolid || facts.IsSolidTop) && (below.SourceSlope is not (1 or 2) || platform);
                return solid || BeginPlantKill(x, y);
            }
            bool valid = tile.Type switch
            {
                3 or 73 => ground is 2 or 477 or 78 or 380 or 579,
                24 => ground is 23 or 661,
                61 or 74 => ground is 60 or 226,
                71 => ground == 70,
                110 or 113 => ground is 109 or 492,
                201 => ground is 199 or 662,
                637 => ground == 633,
                _ => false
            };
            if (valid) return true;
            var after = tile;
            bool mushroom = (tile.Type is 3 or 61 or 110 or 24) && tile.FrameX == 144 ||
                tile.Type == 201 && tile.FrameX == 270;
            if ((tile.Type is 3 or 73) && ground is not (2 or 477) && after.FrameX >= 162) after.FrameX = 126;
            if (tile.Type == 74 && ground is not (60 or 226) && after.FrameX >= 162) after.FrameX = 126;
            switch (ground)
            {
                case 23: case 661: after.Type = 24; if (after.FrameX >= 162) after.FrameX = 126; break;
                case 199: case 662: after.Type = 201; break;
                case 2: case 477: after.Type = tile.Type == 113 ? (ushort)73 : (ushort)3; break;
                case 109: case 492: after.Type = tile.Type == 73 ? (ushort)113 : (ushort)110; break;
                case 60: case 226:
                    after.Type = 61; while (after.FrameX > 126) after.FrameX -= 126; break;
                case 70:
                    after.Type = 71; while (after.FrameX > 72) after.FrameX -= 72; break;
            }
            if (after.Type == tile.Type) return BeginPlantKill(x, y);
            if (mushroom) after.FrameX = after.Type == 201 ? (short)270 : (short)144;
            return Stage(WorldTileMutationKind.TransformTile, x, y, in tile, in after);
        }

        private bool BeginPlantKill(int x, int y)
        {
            plantDestroying = true;
            return Push(new(StepKind.PlantKillComplete, x, y)) && Push(new(StepKind.Kill, x, y));
        }
        private bool FinishPlantKill() { plantDestroying = false; return true; }

        private bool TryStagePlantDrops(int x, int y, in WorldTile tile)
        {
            if (server is null || !TryAdvancePlantBait(x, y, in tile)) return false;
            int item = 0, count = 1;
            switch (tile.Type)
            {
                case 3:
                    if (tile.FrameX == 144) item = 5;
                    else if (random.Next(2) == 0)
                    {
                        if (!TryPlantActor(x, y, out var actor) || actor.HasSeedTool is not { } seeds) return false;
                        if (seeds) item = 283;
                    }
                    break;
                case 73:
                    if (random.Next(2) == 0)
                    {
                        if (!TryPlantActor(x, y, out var actor) || actor.HasSeedTool is not { } seeds) return false;
                        if (seeds) item = 283;
                    }
                    break;
                case 110: if (tile.FrameX == 144) item = 5; break;
                case 24: if (tile.FrameX == 144) item = 60; break;
                case 201: if (tile.FrameX == 270) item = 2887; break;
                case 61: case 74:
                    if (tile.Type == 61 && tile.FrameX == 144) { item = 331; count = random.Next(2, 4); }
                    else if (tile.Type == 61 && tile.FrameX == 162) item = 223;
                    else if (tile.Type == 61 && tile.FrameX is >= 108 and <= 126 && random.Next(20) == 0) item = 208;
                    else if (random.Next(100) == 0) item = 195;
                    break;
                case 71: if (random.Next(40) == 0) item = 194; else if (random.Next(2) == 0) item = 183; break;
                case 637: if (random.Next(100) == 0) item = 5214; break;
                case 703:
                    // The source noPrefix223 branch needs its distinct prefix
                    // constructor contract; do not borrow ordinary item policy.
                    switch (tile.FrameX / 18)
                    { case 6: case 7: item = 208; break; case 8: item = 331; break; case 9: return false; default: item = 195; break; }
                    break;
            }
            return item == 0 || server.TryStageSourceItem(new(item), count, x, y);
        }

        private bool TryPlantActor(int x, int y, out ToolTreeActor1458 actor) =>
            VanillaToolTreeActors1458.TrySelect(actors, x * 16, y * 16, 16, 16, out actor);

        // DropBait is SERVER-only and precedes GetItemDrops. A successful bait
        // roll requiring an unrepresented TargetClosest caller is explicitly
        // refused before adoption; it is never silently changed into no birth.
        private bool TryAdvancePlantBait(int x, int y, in WorldTile tile)
        {
            int worm = -1, hopper = -1, hay = -1, grub = -1, magma = -1;
            switch (tile.Type)
            {
                case 3: worm = 400; hopper = 100; break;
                case 73: worm = 200; hopper = 50; if (tile.FrameX < 108) hay = 1000; break;
                case 61: grub = 80; break;
                case 74: grub = 40; break;
                case 637: magma = 100; break;
                default: return true;
            }
            if (!TryPlantActor(x, y, out var actor)) return false;
            if (tile.FrameX >= 108)
            {
                if (actor.FlowerBoots is not { } flower) return false;
                if (flower) { if (worm > 0) worm *= 10000; if (hopper > 0) hopper *= 10000;
                    if (grub > 0) grub *= 10000; if (magma > 0) magma *= 10000; }
            }
            if (worm > 0)
            {
                bool canRoll;
                if (actor.ZoneGraveyard is { } graveyard)
                {
                    if (!server!.TryCountSourceNpcs(graveyard ? 606 : 357, out int worms)) return false;
                    canRoll = worms < 5;
                }
                else
                {
                    // Unknown scene is not false. Retain only the equivalent
                    // no-birth continuation when both guarded source gates agree.
                    if (!server!.TryCountSourceNpcs(357, out int normal) ||
                        !server.TryCountSourceNpcs(606, out int grave) || (normal < 5) != (grave < 5)) return false;
                    canRoll = normal < 5;
                }
                if (canRoll && random.Next(worm) == 0) return false;
            }
            if (hay > 0 && random.Next(hay) == 0 && !server!.TryStageSourceItem(new(5574), 1, x, y)) return false;
            if (hopper > 0)
            {
                if (!server!.TryCountSourceNpcs(377, out int hoppers)) return false;
                if (hoppers < 5 && random.Next(hopper) == 0) return false;
            }
            if (grub > 0)
            {
                if (!server!.TryCountSourceNpcs(485, out int a) || !server.TryCountSourceNpcs(486, out int b) ||
                    !server.TryCountSourceNpcs(487, out int c)) return false;
                if (a + b + c < 8 && random.Next((int)(grub * 1.25)) == 0) return false;
            }
            if (magma > 0)
            {
                if (!server!.TryCountSourceNpcs(654, out int a) || !server.TryCountSourceNpcs(653, out int b)) return false;
                if (a + b < 8 && random.Next(magma) == 0) return false;
            }
            return true;
        }
    }
}
