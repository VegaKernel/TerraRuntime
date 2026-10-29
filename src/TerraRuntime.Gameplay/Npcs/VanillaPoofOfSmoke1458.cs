namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Caller-side source random offers retained by Utils.PoofOfSmoke on a dedicated1458 server.</summary>
public static class VanillaPoofOfSmoke1458
{
    public static void ConsumeDedicatedSourceOffers(IVanillaNpcRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        int goreCount = random.NextInt32(3, 7);
        for (int index = 0; index < goreCount; index++)
        {
            _ = random.NextDouble();
            _ = random.NextInt32(11, 14);
        }
        for (int index = 0; index < 10; index++)
        {
            _ = random.NextDouble();
            _ = random.NextDouble();
        }
    }
}
