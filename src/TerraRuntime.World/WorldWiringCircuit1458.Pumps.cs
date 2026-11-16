namespace TerraRuntime.World;

/// <summary>One color's ordered pump cells, valid during the callback. The existing liquid owner stages and adopts the transfer.</summary>
public delegate bool WorldWiringPumpTransfer1458(ReadOnlySpan<(int X, int Y)> intakes, ReadOnlySpan<(int X, int Y)> outlets);

public sealed partial class WorldWiringCircuit1458
{
    // Wiring.HitWireSingle142/143 admits nineteen cells, not twenty objects.
    internal const int PumpCellCapacity = 19;
    private readonly List<(int X, int Y)> intakePumps = new(PumpCellCapacity);
    private readonly List<(int X, int Y)> outletPumps = new(PumpCellCapacity);

    private void RegisterPump(int left, int top, bool intake)
    {
        var cells = intake ? intakePumps : outletPumps;
        for (int row = 1; row >= 0; row--)
        for (int column = 0; column < 2 && cells.Count < PumpCellCapacity; column++)
            cells.Add((left + column, top + row));
    }
}
