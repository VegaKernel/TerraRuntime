namespace TerraRuntime.Core.Npcs;

/// <summary>World-owned source noSpawnCycle flag, consumed before the next NPC.SpawnNPC pass.</summary>
public sealed class RuntimeNpcSpawnCycle1458
{
    private bool suppressed;

    internal void SuppressNext() => suppressed = true;

    public bool TryBeginCycle()
    {
        bool permitted = !suppressed;
        suppressed = false;
        return permitted;
    }
}
