using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Tiles;

public readonly record struct ToolFrameTile1458(TileTypeId Type, short FrameX, short FrameY,
    bool Active, bool Inactive, bool HalfBrick, byte Slope);

/// <summary>Player hammer frame choices; source packet20 admission and world framing belong to the runtime.</summary>
public static class VanillaToolFurniture1458
{
    // Source frame atlas strides used by pounding and pressure-pad framing.
    private const int TrapFrameStep = 18;
    private const int PressurePadFrameStep = 22;
    // Player.ItemCheck_UseMiningTools_TryPoundingTile (1458). The 3x3
    // neighborhood is row-major, with the target at index4. These choices do
    // not call WorldGen.PoundTile, emit its dust or draw from a random stream.
    public static bool TryRotate(in ToolFrameTile1458 target,
        ReadOnlySpan<ToolFrameTile1458> neighborhood, out short frameX, out bool reportSquare)
    {
        frameX = target.FrameX;
        reportSquare = false;
        if (!target.Active || target.Slope > 4) return false;
        if (target.Type.Value == 137)
        {
            int column = target.FrameX / TrapFrameStep;
            int next = (target.FrameY / TrapFrameStep) switch
            {
                0 or 1 or 2 or 5 => column switch
                { 0 => 2, 1 => 3, 2 => 4, 3 => 5, 4 => 1, _ => 0 },
                3 or 4 => column switch { 0 or 1 => 3, 3 => 2, 2 => 4, _ => 0 },
                _ => 0
            };
            frameX = (short)(next * TrapFrameStep);
            reportSquare = true;
            return true;
        }
        if (target.Type.Value == 424)
        {
            frameX = target.FrameX switch { 0 => 18, 18 => 36, _ => 0 };
            reportSquare = true;
            return true;
        }
        if (target.Type.Value != 442 || neighborhood.Length != 9 || neighborhood[4] != target) return false;
        foreach (var tile in neighborhood)
            if (tile.Slope > 4 || !VanillaToolTileCatalog1458.TryGet(tile.Type, out _)) return false;

        if (!TryGetPressurePadAnchors(neighborhood, out bool ceiling, out bool floor,
            out bool leftAnchor, out bool rightAnchor)) return false;

        int nextDirection = (target.FrameX / PressurePadFrameStep) switch
        {
            0 => leftAnchor ? 2 : ceiling ? 1 : rightAnchor ? 3 : -1,
            2 => ceiling ? 1 : rightAnchor ? 3 : floor ? 0 : -1,
            1 => rightAnchor ? 3 : floor ? 0 : leftAnchor ? 2 : -1,
            3 => floor ? 0 : leftAnchor ? 2 : ceiling ? 1 : -1,
            _ => 0
        };
        if (nextDirection >= 0)
        {
            frameX = (short)(nextDirection * PressurePadFrameStep);
            reportSquare = true;
        }
        return true;
    }

    public static bool TryGetPressurePadAnchors(ReadOnlySpan<ToolFrameTile1458> neighborhood,
        out bool ceiling, out bool floor, out bool leftAnchor, out bool rightAnchor)
    {
        ceiling = floor = leftAnchor = rightAnchor = false;
        if (neighborhood.Length != 9) return false;
        foreach (var tile in neighborhood)
            if (tile.Slope > 4 || !VanillaToolTileCatalog1458.TryGet(tile.Type, out _)) return false;
        var up = neighborhood[1];
        var down = neighborhood[7];
        var left = neighborhood[3];
        var right = neighborhood[5];
        var upRoles = Roles(up);
        var downRoles = Roles(down);
        var leftRoles = Roles(left);
        var rightRoles = Roles(right);
        ceiling = IsPresent(up) && up.Slope is not (3 or 4) && upRoles.IsSolid &&
            (!upRoles.NoAttach || upRoles.IsPlatform && up.HalfBrick) &&
            (up.Slope is 0 or 1 or 2 || up.HalfBrick);
        floor = IsPresent(down) && !down.HalfBrick && down.Slope is not (1 or 2) && downRoles.IsSolid &&
            (!downRoles.NoAttach || downRoles.IsPlatform) && down.Slope is 0 or 3 or 4;
        leftAnchor = IsPresent(left) && left.Slope is 0 or 2 or 4 &&
            (leftRoles.IsSolid && !leftRoles.NoAttach && !left.HalfBrick || IsBeam(left.Type) ||
             leftRoles.IsTreeTrunk && IsTree(neighborhood[0]) && IsTree(neighborhood[6]));
        rightAnchor = IsPresent(right) && right.Slope is 0 or 1 or 3 &&
            (rightRoles.IsSolid && !rightRoles.NoAttach && !right.HalfBrick || IsBeam(right.Type) ||
             rightRoles.IsTreeTrunk && IsTree(neighborhood[2]) && IsTree(neighborhood[8]));

        return true;
    }

    public static bool TryFramePressurePad(in ToolFrameTile1458 target,
        ReadOnlySpan<ToolFrameTile1458> neighborhood, out short frameX, out bool kill)
    {
        frameX = target.FrameX; kill = false;
        if (target.Type.Value != 442 || !target.Active || neighborhood.Length != 9 || neighborhood[4] != target ||
            !TryGetPressurePadAnchors(neighborhood, out bool up, out bool down, out bool left, out bool right)) return false;
        bool valid = (target.FrameX / PressurePadFrameStep) switch { 0 => down, 1 => up, 2 => left, 3 => right, _ => false };
        if (valid) return true;
        if (down) frameX = 0;
        else if (up) frameX = 22;
        else if (left) frameX = 44;
        else if (right) frameX = 66;
        else kill = true;
        return true;
    }

    private static bool IsPresent(in ToolFrameTile1458 tile) => tile.Active && !tile.Inactive;

    private static ToolTileDefinition1458 Roles(in ToolFrameTile1458 tile)
    {
        VanillaToolTileCatalog1458.TryGet(tile.Type, out var roles);
        return roles;
    }

    private static bool IsTree(in ToolFrameTile1458 tile) => IsPresent(tile) && Roles(tile).IsTreeTrunk;

    // TileID.Sets.IsBeam (1458); this is independent of the solid/noAttach gate.
    internal static bool IsBeam(TileTypeId type) => type.Value is 124 or 561 or 574 or 575 or 576 or 577 or 578;
}
