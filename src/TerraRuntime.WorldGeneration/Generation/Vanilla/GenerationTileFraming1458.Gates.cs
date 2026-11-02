using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    /// <summary>Generation-only CheckDoorClosed/CheckDoorOpen, TerrariaServer 1.4.5.8.</summary>
    private void DoorCheckFraming(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        int top = j - observed.FrameY % 54 / 18;
        int hinge = i;
        int left = i;
        int objectWidth = 1;
        if (type == 11)
        {
            objectWidth = 2;
            switch (observed.FrameX % 72)
            {
                case 18: hinge--; break;
                case 36: hinge++; left = hinge - 1; break;
                case 54: left = hinge - 1; break;
            }
            if (observed.FrameX % 72 is 0 or 18)
                left = hinge;
        }
        if (!Contains(left, top - 1) || !Contains(left + objectWidth - 1, top + 3))
            return;

        bool broken = !SolidTile(hinge, top - 1) || !SolidTile(hinge, top + 3);
        for (int x = left; x < left + objectWidth; x++)
        for (int y = top; y < top + 3; y++)
        {
            WorldTile member = At(x, y);
            // Both source door validators check membership, deliberately not the other members' frames.
            if (!member.IsActive || member.Type != type)
                broken = true;
        }
        if (!broken)
            return;

        destroyingObject = true;
        for (int x = left; x < left + objectWidth; x++)
        for (int y = top; y < top + 3; y++)
            if (At(x, y).Type == type)
                KillTile(x, y);
        destroyingObject = false;
        // ExploitDestroyQueue is produced only by !generatingWorld falling projectiles. This
        // isolated generation owner cannot enqueue them; there is no second queue to drain here.
    }

    /// <summary>CheckTrapDoor and CheckTallGate through their existing generation-time owner.</summary>
    private void GateCheckFraming(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        bool tall = type is 388 or 389;
        int objectWidth = tall ? 1 : 2;
        int objectHeight = tall ? 5 : type == 386 ? 2 : 1;
        int styleX = observed.FrameX / (objectWidth * 18);
        int styleY = observed.FrameY / (tall ? 94 : objectHeight * 18);
        int left = tall ? i : i - observed.FrameX / 18 % objectWidth;
        int top;
        if (tall)
        {
            // Source TileObjectData heights18/16/16/16/18, padding2. The origin scan
            // subtracts the heights alone; validation below uses the padded frame offsets.
            int remaining = observed.FrameY % 94;
            int row = 0;
            while (row < 5 && remaining >= (row is 0 or 4 ? 18 : 16))
            {
                remaining -= row is 0 or 4 ? 18 : 16;
                row++;
            }
            top = j - row;
        }
        else
            top = j - observed.FrameY / 18 % objectHeight;
        if (!Contains(left - 1, top - 1) || !Contains(left + objectWidth, top + objectHeight))
            return;

        bool broken = false;
        for (int x = left; x < left + objectWidth; x++)
        for (int row = 0; row < objectHeight; row++)
        {
            WorldTile member = At(x, top + row);
            int expectedY = tall ? styleY * 94 + (row == 0 ? 0 : 20 + (row - 1) * 18)
                : styleY * objectHeight * 18 + row * 18;
            // CheckTallGate reads frames only, whereas CheckTileFrames also owns active/type.
            if ((!tall && (!member.IsActive || member.Type != type)) ||
                member.FrameX != styleX * objectWidth * 18 + (x - left) * 18 || member.FrameY != expectedY)
                broken = true;
        }
        if (tall)
        {
            if (!GateAnchor(left, top - 1) || !GateAnchor(left, top + objectHeight))
                broken = true;
        }
        else if (type == 387 || styleX is 0 or 1)
        {
            int supportRow = top + (type == 386 && styleX == 0 ? 1 : 0);
            if (!GateAnchor(left - 1, supportRow) ||
                !GateAnchor(left + objectWidth, supportRow))
                broken = true;
        }
        if (!broken)
            return;

        destroyingObject = true;
        for (int x = left; x < left + objectWidth; x++)
        for (int y = top; y < top + objectHeight; y++)
            // CheckTrapDoor intentionally removes a foreign member too; TallGate does not.
            if (!tall || At(x, y).Type == type)
                KillTile(x, y);
        destroyingObject = false;
        for (int x = left - 1; x <= left + objectWidth; x++)
        for (int y = top - 1; y <= top + objectHeight; y++)
            TileFrame(x, y);
    }

    private bool GateAnchor(int x, int y) =>
        SolidTile(x, y) && !GenerationObjectSupport1458.DisallowsAttachment(At(x, y).Type);

    /// <summary>
    /// The locked-door contribution to source CheckTileBreakability:2 returns before dust;
    ///1 keeps a locked door but still reaches its zero-choice dust and SquareTileFrame failure tail.
    /// Other source breakability families retain their existing owners.
    /// </summary>
    private int DoorBreakability(int x, int y)
    {
        if (Contains(x, y + 1) && At(x, y + 1).IsActive && DoorIsLocked(At(x, y + 1)))
            return 2;
        WorldTile target = At(x, y);
        if (!VanillaTileCollisionCatalog.IsSolid(target.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(target.TileType))
            return 0;
        if (Contains(x, y - 1) && At(x, y - 1).IsActive)
        {
            if (DoorIsLocked(At(x, y - 1)))
                return 2;
            if (DoorIsLocked(target))
                return 1;
        }
        return 0;
    }

    private static bool DoorIsLocked(in WorldTile tile) =>
        tile.Type == 10 && tile.FrameY is >= 594 and <= 646 && tile.FrameX < 54;
}
