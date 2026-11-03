using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private int FindLogicGateBelow(int x, int y)
    {
        if (x < 1 || y < 1 || x >= width - 1 || y >= height - 1)
            return -1;
        for (int row = y; row < height; row++)
        {
            WorldTile tile = At(x, row);
            if (!tile.IsActive)
                break;
            if (tile.Type == 420)
                return row;
            if (tile.Type != 419)
                break;
        }
        return -1;
    }

    private void EnsureUnwiredLogicGate(int x, int y)
    {
        int gateY = FindLogicGateBelow(x, y);
        if (gateY >= 0 && At(x, gateY).HasAnyWire)
            throw new NotSupportedException("Connected logic gates require the retained wiring owner.");
    }

    // KillTile419 calls PokeLogicGate after deactivation, before clearing the identity.
    // An unwired gate can refresh its frame without entering the live wire propagation queues.
    private void RefreshUnwiredLogicGate(int x, int y)
    {
        int gateY = FindLogicGateBelow(x, y);
        if (gateY < 0)
            return;
        ref WorldTile gate = ref At(x, gateY);
        int style = gate.FrameY / 18;
        if (style is < 0 or > 5)
            return;
        int count = 0;
        int on = 0;
        bool faulty = false;
        for (int row = gateY - 1; row > 0; row--)
        {
            WorldTile lamp = At(x, row);
            if (!lamp.IsActive || lamp.Type != 419)
                break;
            if (lamp.FrameX == 36)
            {
                faulty = true;
                break;
            }
            count++;
            if (lamp.FrameX == 18)
                on++;
        }
        bool output = style switch
        {
            0 => count == on,
            1 => on > 0,
            2 => count != on,
            3 => on == 0,
            4 => on == 1,
            5 => on != 1,
            _ => false
        };
        bool staleFault = !faulty && gate.FrameX == 36;
        bool triggeredFault = faulty && At(x, y).FrameX == 36;
        if (output == (gate.FrameX == 18) && !staleFault && !triggeredFault)
            return;
        gate.FrameX = faulty ? (short)36 : output ? (short)18 : (short)0;
        SquareTileFrame(x, gateY);
        // Source samples this even with an empty count. Only the wire pulse is omitted here.
        if (triggeredFault)
            _ = random.Next(count) < on;
    }
}
