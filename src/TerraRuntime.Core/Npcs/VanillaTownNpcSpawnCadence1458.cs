namespace TerraRuntime.Core;

/// <summary>
/// World-owned Main.UpdateTime_SpawnTownNPCs cadence state for TerrariaServer 1.4.5.8.
/// The source increments checkForSpawns and evaluates after 7200 / WorldGen.GetWorldUpdateRate() updates.
/// </summary>
public sealed class VanillaTownNpcSpawnCadence1458
{
    private int _checkForSpawns;

    public int PendingTicks => _checkForSpawns;

    public bool Advance(int worldUpdateRate)
    {
        if (worldUpdateRate <= 0)
            return false;

        _checkForSpawns++;
        int threshold = 7200 / worldUpdateRate;
        if (_checkForSpawns < threshold)
            return false;

        _checkForSpawns = 0;
        return true;
    }

    public void Reset() => _checkForSpawns = 0;
}
