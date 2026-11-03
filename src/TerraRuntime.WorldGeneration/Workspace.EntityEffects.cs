using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Runtime;
public sealed partial class Workspace {
 internal IWorldGenerationEntityEffects1458? EntityEffects {get;private set;}
 internal byte GenerationMyPlayer {get;private set;}=byte.MaxValue;
 internal float GenerationNpcDifficulty {get;private set;}=1f;
 internal bool GenerationJourney {get;private set;}
 /// <summary>Composes canonical cold entity owners before this candidate executes its generation plan.</summary>
 public void AttachEntityEffects(IWorldGenerationEntityEffects1458 effects,byte sourceMyPlayer=byte.MaxValue,float sourceNpcDifficulty=1f,bool sourceJourney=false){
  ArgumentNullException.ThrowIfNull(effects);
  if(EntityEffects is not null||generatedTownNpcs.Count!=0)throw new InvalidOperationException("Cold entity owners must be composed before town births.");
  if(!float.IsFinite(sourceNpcDifficulty)||sourceNpcDifficulty is <.5f or >4f)throw new ArgumentOutOfRangeException(nameof(sourceNpcDifficulty));
  EntityEffects=effects;GenerationMyPlayer=sourceMyPlayer;GenerationNpcDifficulty=sourceNpcDifficulty;GenerationJourney=sourceJourney;
 }
 private void UpdateGeneratedTownProjection(int index,WorldTownNpc projection){
  var before=generatedTownNpcs[index];
  if(before.Handle is {} handle){
   if(EntityEffects is null)throw new InvalidOperationException("Missing cold entity owner.");
   EntityEffects.MoveTownNpc(handle,projection.X,projection.Y,projection.Homeless,projection.HomeTileX,projection.HomeTileY);
  }
  generatedTownNpcs[index]=before with{Projection=projection};
 }
 public WorldTileEntity[] CaptureGeneratedTileEntities(){
  if(TileStore.RuntimeTileEntities is {} owner)return owner.CaptureSnapshot();
  if(EntityEffects is not null)throw new InvalidOperationException("The source cold candidate lost its canonical tile-entity owner.");
  return []; // Legacy custom tile-only generation has no composed source effect lifecycle.
 }
}
