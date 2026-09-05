using TerraRuntime.World;
namespace TerraRuntime.Application;
internal sealed class RuntimeTallGateOccupancyProbe : IVanillaTallGateOccupancyProbe
{
    private readonly Func<int, int, bool> isFree;
    public RuntimeTallGateOccupancyProbe(Func<int, int, bool> isFree)
    {
        this.isFree = isFree ?? throw new ArgumentNullException(nameof(isFree));
    }
    public bool IsActorFree(int tileX, int tileY) => isFree(tileX, tileY);
}
