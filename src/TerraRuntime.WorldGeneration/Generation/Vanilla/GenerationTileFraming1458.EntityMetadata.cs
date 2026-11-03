using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;
internal sealed partial class GenerationTileFraming1458 {
 internal void RemoveOwnedTileEntity(int x,int y,WorldTileEntityKind expectedKind){
  if(store.RuntimeTileEntities is not {} owner)throw new NotSupportedException("Tile-entity removal requires the canonical candidate metadata owner.");
  if(!owner.TryPrepare(1,out var preparation)||preparation is null||!preparation.TryGetAt(x,y,out var entity))throw new InvalidOperationException("Could not inspect canonical tile entity.");
  // Terraria.TileEntity.Kill checks kind before mutating either dictionary.
  if(entity is null||entity.Kind!=expectedKind)return;
  if(!preparation.TryStageRemoval(x,y,expectedKind)||!preparation.TrySeal())throw new InvalidOperationException("Could not prepare canonical tile-entity removal.");
  Span<WorldSectionId> sections=stackalloc WorldSectionId[1];sections[0]=TerrariaSectionGeometry.FromTile(store.Dimensions,x,y);
  store.BeginMetadataMutation(sections);
  try{if(!preparation.TryAdoptUnpublished())throw new InvalidOperationException("Canonical tile-entity removal changed during adoption.");}
  finally{store.EndMetadataMutation(sections);}
 }
}
