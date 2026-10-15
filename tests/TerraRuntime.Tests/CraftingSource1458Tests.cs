using System.Text.Json;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Core;
using Xunit;
namespace TerraRuntime.Tests;
public sealed class CraftingSource1458Tests
{
    [Fact]
    public void Source_crafting_catalog_costs_grants_and_rng_keep_complete_physical_images()
    {
void Check(bool b,string m) => Xunit.Assert.True(b,m);
WorldItemPickupSlot1458 Tuple(JsonElement x)=>x.GetProperty("type").GetInt32()==0?default:new(x.GetProperty("type").GetInt32(),x.GetProperty("stack").GetInt32(),x.GetProperty("prefix").GetInt32(),x.GetProperty("favorite").GetBoolean());
WorldItemPickupSlot1458[] Snapshot(JsonElement x)=>x.GetProperty("inventory").EnumerateArray().Select(Tuple).Concat(x.GetProperty("banks").EnumerateArray().SelectMany(b=>b.EnumerateArray().Select(Tuple))).ToArray();
foreach(string family in new[]{"crafting-local-original-source-v1","crafting-local-caller-original-source-v1","crafting-coin-grant-original-source-v1","crafting-alchemy-all-skip-original-source-v1"})foreach(string platform in new[]{"Linux","Windows"})foreach(string run in new[]{"first","repeat"})
{
 using var input=Read(family,platform,run);using var gzip=new GZipStream(input,CompressionMode.Decompress);using var data=JsonDocument.Parse(gzip);
 foreach(var row in data.RootElement.EnumerateArray())
 {
  string mode=row.GetProperty("mode").GetString()!;var before=row.GetProperty("before");var after=Snapshot(before);var expected=Snapshot(row.GetProperty("afterSync"));int recipe=row.GetProperty("recipeIndex").GetInt32();int quantity=row.GetProperty("quantity").GetInt32();
  var bankOrder=mode=="bench-openbank"?new byte[]{0}:mode=="bench-void-open"?new byte[]{3}:Array.Empty<byte>();
  var stations=before.GetProperty("adjTile").EnumerateArray().Select(x=>x.GetBoolean()).ToArray();var environment=new RecipeEnvironment1458(false,false,false,before.GetProperty("alchemyTable").GetBoolean(),false,false,false,false);
  bool locked=row.TryGetProperty("locked",out var lockValue)&&lockValue.GetBoolean();bool sourceAllowed=row.GetProperty("enough").GetBoolean()&&row.GetProperty("environment").GetBoolean()&&!locked&&(!row.TryGetProperty("cursorAllowed",out var cursorValue)||cursorValue.GetBoolean());
  var rng=new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
  for(int unit=0;unit<quantity;unit++)
  {
   bool planned=VanillaWorldItemOwner1458.TryPlanCraftWithRandom(recipe,after,bankOrder,stations,in environment,row.GetProperty("quick").GetBoolean(),locked,rng.Next,platform=="Windows",new(false,false,false),out var plan);
   Check(planned==sourceAllowed,mode+" admission");if(!planned)break;
   Check(plan is not null,"nonnull");after=plan!.After;
   foreach(var leg in plan.Debits){Check(leg.SourceSlot!=58,"cursor not ingredient");Check(leg.Amount>0,"positive debit");Check(leg.Before.Stack-leg.After.Stack==leg.Amount,"exact amount");}
   Check(plan.NominalReservations.Sum(x=>x.Amount)>=plan.Debits.Sum(x=>x.Amount),"nominal bounds");
  }
  Check(after.Length==expected.Length,"length");for(int i=0;i<219;i++)Check(after[i]==expected[i],mode+" physical slot"+i);
  var random=row.GetProperty("afterRandom");Check(RngAccess.Cursor(rng)==random.GetProperty("cursor").GetUInt32(),mode+" random cursor");Check(RngAccess.State(rng).SequenceEqual(random.GetProperty("state").EnumerateArray().Select(x=>x.GetInt32())),mode+" full56");Check(rng.Clone().Next()==row.GetProperty("afterNext").GetInt32(),mode+" next");
 }
}

foreach(string platform in new[]{"Linux","Windows"})foreach(string run in new[]{"first","repeat"})
{
 using var input=Read("crafting-prefix-capability-original-source-v1",platform,run);using var gzip=new GZipStream(input,CompressionMode.Decompress);using var data=JsonDocument.Parse(gzip);
 foreach(var row in data.RootElement.EnumerateArray())
 {
  int type=row.GetProperty("actualType").GetInt32();bool has=VanillaItemPrefixTable1458.TryGet(new(type),out var record);Check(has==row.GetProperty("canHavePrefixes").GetBoolean(),"all6196 prefix capability");
  if(has){Check(VanillaItemPrefixTable1458.GetFamily(record.Family).ToArray().Select(x=>(int)x).SequenceEqual(row.GetProperty("family").EnumerateArray().Select(x=>x.GetInt32())),"all886 prefix family");Check(record.Damage==row.GetProperty("damage").GetInt32(),"prefix damage");Check(record.UseAnimation==row.GetProperty("useAnimation").GetInt32(),"prefix animation");Check(record.Mana==row.GetProperty("mana").GetInt32(),"prefix mana");}
 }
}
// Explicit API policy checks are configured components, not original client RNG ownership.
int variantRecipe=-1;for(int i=0;i<VanillaRecipeCatalog1458.Count;i++){VanillaRecipeCatalog1458.TryGet(i,out var r);if(r.ResultType.Value==544){variantRecipe=i;break;}}
Check(variantRecipe>=0,"variant recipe exists");int draws=0;
bool unknown=VanillaWorldItemOwner1458.TryPlanCraftWithRandom(variantRecipe,new WorldItemPickupSlot1458[219],[],new bool[754],default,false,false,bound=>{draws++;return 0;},false,null,out var unknownPlan,out var unknownStatus);
Check(!unknown&&unknownPlan is null&&unknownStatus==VanillaItemPrefixNormalizationStatus1458.Unsupported,"unknown world refused");Check(draws==0,"unknown world before draw");

int budgetRecipe=-1;int rejectedIndex=-1;int familyLength=0;
for(int i=0;i<VanillaRecipeCatalog1458.Count && budgetRecipe<0;i++)
{
 VanillaRecipeCatalog1458.TryGet(i,out var r);
 if(r.ResultStack!=1 || !VanillaItemPrefixTable1458.TryGet(r.ResultType,out var record))continue;
 var family=VanillaItemPrefixTable1458.GetFamily(record.Family);if(family.Length is 3 or 4)continue;
 for(int j=0;j<family.Length;j++)if(!VanillaItemPrefixTable1458.Accepts(in record,family[j],false)){budgetRecipe=i;rejectedIndex=j;familyLength=family.Length;break;}
}
Check(budgetRecipe>=0,"rejected prefix source record exists");var stock=new WorldItemPickupSlot1458[219];
for(int i=0;i<VanillaRecipeCatalog1458.GetIngredientCount(budgetRecipe);i++){VanillaRecipeCatalog1458.TryGetIngredient(budgetRecipe,i,out var item);stock[i]=new(item.OriginalType.Value,item.Amount,0,false);}
VanillaRecipeCatalog1458.TryGet(budgetRecipe,out var budgetDefinition);var readyStations=new bool[754];if(budgetDefinition.RequiredTile>=0)readyStations[budgetDefinition.RequiredTile]=true;
var fullEnvironment=new RecipeEnvironment1458(true,true,true,true,true,true,true,true);int boundedCalls=0;
bool bounded=VanillaWorldItemOwner1458.TryPlanCraftWithRandom(budgetRecipe,stock,[],readyStations,in fullEnvironment,false,false,bound=>{boundedCalls++;return bound==4?1:bound==familyLength?rejectedIndex:0;},false,new(false,false,false),out var exhaustedPlan,out var exhaustedStatus);
Check(!bounded && exhaustedPlan is null && exhaustedStatus==VanillaItemPrefixNormalizationStatus1458.BudgetExhausted,"bounded prefix exhaustion");Check(boundedCalls>=VanillaItemPrefixNormalization1458.MaximumAttempts && boundedCalls<=VanillaItemPrefixNormalization1458.MaximumAttempts*3,"bounded source attempts");
bool invalid=VanillaWorldItemOwner1458.TryPlanCraftWithRandom(budgetRecipe,stock,[],readyStations,in fullEnvironment,false,false,bound=>bound,false,new(false,false,false),out var invalidPlan,out var invalidStatus);
Check(!invalid && invalidPlan is null && invalidStatus==VanillaItemPrefixNormalizationStatus1458.InvalidDraw,"invalid random callback refused");
// Nominal ingredient availability remains required even when the physical witness saves every unit.
var empty=new WorldItemPickupSlot1458[219];var potionStations=new bool[754];potionStations[13]=true;
bool fabricated=VanillaWorldItemOwner1458.TryPlanCraftWitness(176,empty,[],potionStations,in fullEnvironment,false,false,new[]{1,2,2},0,false,new(false,false,false),out _);Check(!fabricated,"nominal allskip availability");


    }
    private static Stream Read(string family,string platform,string run)
    {
        string prefix=family switch
        {
            "crafting-local-original-source-v1"=>"CraftingLocal",
            "crafting-local-caller-original-source-v1"=>"CraftingCaller",
            "crafting-coin-grant-original-source-v1"=>"CraftingCoin",
            "crafting-alchemy-all-skip-original-source-v1"=>"CraftingAllSkip",
            "crafting-prefix-capability-original-source-v1"=>"CraftingPrefixCapability",
            _=>throw new InvalidOperationException("Unknown original fixture family.")
        };
        string name=prefix+platform+(run=="first"?"First":"Repeat")+"1458";
        return typeof(CraftingSource1458Tests).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing original crafting numeric fixture "+name);
    }
    private static class RngAccess
{
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="seedArray")]internal static extern ref int[] State(VanillaUnifiedRandom1458 random);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="inext")]internal static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
}

}
