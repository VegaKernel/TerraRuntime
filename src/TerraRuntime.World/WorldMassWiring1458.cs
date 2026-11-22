namespace TerraRuntime.World;

internal readonly record struct WorldMassWiringEdit1458(int X, int Y, byte Action, WorldTile Before, WorldTile After);
internal sealed record WorldMassWiringPlan1458(WorldMassWiringEdit1458[] Edits,
    int WiresConsumed, int ActuatorsConsumed, int WireDrops, int ActuatorDrops, bool Exhausted);

/// <summary>Detached terrain/resource plan for Wiring.MassWireOperationInner/Step, Terraria 1.4.5.8.</summary>
internal static class WorldMassWiring1458
{
    // Bounds hostile packet109 work while admitting the full L path across vanilla's largest world.
    internal const int MaximumCells = 16_384;

    internal static bool TryPlan(WorldTileStore tiles, int startX, int startY, int endX, int endY,
        bool facingRight, byte mode, int wires, int actuators, Func<int, int, WorldTile, bool> canUse,
        out WorldMassWiringPlan1458? plan)
    {
        plan = null;
        if (mode > 63 || wires < 0 || actuators < 0 ||
            Math.Abs((long)startX - endX) + Math.Abs((long)startY - endY) + 1 > MaximumCells) return false;
        var edits = new List<WorldMassWiringEdit1458>();
        int wiresLeft = wires, actuatorsLeft = actuators, wireDrops = 0, actuatorDrops = 0;
        bool cutter = (mode & 32) != 0, exhausted = false;

        bool Visit(int x, int y)
        {
            if (x < 1 || y < 1 || x >= tiles.Dimensions.WidthTiles - 1 || y >= tiles.Dimensions.HeightTiles - 1)
                return true;
            var tile = tiles.Get(x, y);
            if (!canUse(x, y, tile)) return true;
            // MultiToolMode order differs from circuit channels: red, green, blue, yellow, actuator.
            for (int index = 0; index < 5; index++)
            {
                if ((mode & (1 << index)) == 0) continue;
                var channel = index switch { 0 => WorldWireChannel.Red, 1 => WorldWireChannel.Green, 2 => WorldWireChannel.Blue, _ => WorldWireChannel.Yellow };
                var kind = index == 4
                    ? cutter ? WorldWiringMutationKind.KillActuator : WorldWiringMutationKind.PlaceActuator
                    : cutter ? WorldWiringMutationKind.KillWire : WorldWiringMutationKind.PlaceWire;
                var result = VanillaWorldWiringMutationService.Evaluate(new(kind, x, y, channel), in tile);
                if (!result.Applied) continue;
                if (!cutter)
                {
                    if (index == 4 ? actuatorsLeft == 0 : wiresLeft == 0) return false;
                    if (index == 4) actuatorsLeft--; else wiresLeft--;
                }
                else if (index == 4) actuatorDrops++; else wireDrops++;
                byte action = index switch { 0 => 5, 1 => 12, 2 => 10, 3 => 16, _ => 8 };
                if (cutter) action++;
                edits.Add(new(x, y, action, tile, result.After));
                tile = result.After;
            }
            return true;
        }

        int x = startX, y = startY;
        int dx = Math.Sign((long)endX - startX), dy = Math.Sign((long)endY - startY);
        while (facingRight ? y != endY : x != endX)
        {
            if (!Visit(x, y)) { exhausted = true; break; }
            if (facingRight) y += dy; else x += dx;
        }
        if (!exhausted)
        {
            while (facingRight ? x != endX : y != endY)
            {
                if (!Visit(x, y)) { exhausted = true; break; }
                if (facingRight) x += dx; else y += dy;
            }
        }
        if (!exhausted && !Visit(endX, endY)) exhausted = true;
        plan = new(edits.ToArray(), wires - wiresLeft, actuators - actuatorsLeft, wireDrops, actuatorDrops, exhausted);
        return true;
    }
}
