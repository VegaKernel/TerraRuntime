namespace TerraRuntime.Contracts.Runtime;

/// <summary>Source remote Player zone1..5 and townNPCs, owned by one player generation.</summary>
public readonly record struct PlayerZoneSnapshot1458(byte Zone1, byte Zone2, byte Zone3, byte Zone4, byte Zone5, byte TownNpcCount)
{
    public bool Corrupt => (Zone1 & 2) != 0;
    public bool Hallow => (Zone1 & 4) != 0;
    public bool Jungle => (Zone1 & 16) != 0;
    public bool Snow => (Zone1 & 32) != 0;
    public bool Crimson => (Zone1 & 64) != 0;
    public bool Desert => (Zone2 & 32) != 0;
    public bool Shimmer => (Zone5 & 1) != 0;
}
