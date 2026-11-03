using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.WorldGeneration.Runtime;

public sealed partial class Workspace
{
    private VanillaStructureMap1458? vanillaStructureMap;
    internal VanillaStructureMap1458 VanillaStructureMap => vanillaStructureMap??=new(TileStore);
    internal void InitializeVanillaStructureMap() => vanillaStructureMap=new(TileStore);
}
