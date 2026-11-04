using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;
internal sealed partial class GenerationTileFraming1458
{
    private bool SpecialSeedWorthy => workspace?.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy) == true;
    private bool SpecialSeedRemix => workspace?.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.Remix) == true;
    private bool SpecialSeedDrunk => workspace?.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.DrunkWorld) == true;
    internal bool TreeHasBareBranches(int groundY) => SpecialSeedRemix &&
        workspace is not null && workspace.TryGetLayers(out var layers) && groundY < layers.WorldSurface;


    private void BreakSpecialSeedSiltNeighbours(int x, int y)
    {
        if (!SpecialSeedWorthy || SourceClient || At(x,y).Type != 57)
            return;
        ReadOnlySpan<int> dx = [-1,1,0,0,-1,1,-1,1];
        ReadOnlySpan<int> dy = [0,0,-1,1,-1,-1,1,1];
        for (int index=0;index<8;index++)
        {
            int nx=x+dx[index],ny=y+dy[index];
            WorldTile neighbour=At(nx,ny);
            // Source evaluates the draw for every active neighbour before testing its identity.
            if (!neighbour.IsActive || random.Next(2)!=0 || neighbour.Type!=57 || SolidTile(nx,ny+1))
                continue;
            if (workspace?.EntityEffects is null)
                throw new NotSupportedException("Worthy silt destruction requires the canonical projectile owner.");
            KillTile(nx,ny);
            // Projectile40 has a10x10 source body; NewProjectile's arguments specify its center.
            workspace.EntityEffects.SpawnProjectile(new(40),workspace.GenerationMyPlayer,
                nx*16+3,ny*16+3,0,.41f,15,0,default,sourceNetUpdate:true);
        }
    }
}
