using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
namespace TerraRuntime.WorldGeneration.Vanilla;
internal sealed partial class FinalPass1458 {
 private void ApplyFinalAnglerQuest(IWorldGenerationContext context,IWorldGenerationVanillaRandom random){
  if(context.Workspace is not Workspace workspace)throw new InvalidOperationException("Angler generation requires Workspace.");
  var world=new AnglerQuestWorld1458(false,context.Request.Options.Evil==WorldGenerationEvil.Crimson,false,state.Layers.WorldSurface>50d);
  if(!VanillaAnglerQuest1458.TryChoose(new AnglerRandom(random),world,out int quest))throw new InvalidOperationException("Source Angler selector exceeded its retained rejection bound.");
  workspace.VanillaAnglerQuest=new WorldAnglerQuestData1458(quest,[]);
 }
 private sealed class AnglerRandom(IWorldGenerationVanillaRandom random):Random(0){public override int Next(int maxValue)=>random.Next(maxValue);}
}
