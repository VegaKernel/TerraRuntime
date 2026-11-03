using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Twelfth source-backed Terraria 1.4.5.8 world-generation overlay. Terraria registers Micro Biomes as one GenPass;
/// the pass itself orchestrates several generation biomes and two TrackGenerator waves. This implementation preserves
/// that public pass identity and the pinned ordinary-world inner order while keeping each clean-room placer isolated.
/// </summary>
public sealed class SourceBackedMicroBiomes1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId MicroBiomesId = new("terraria:1.4.5.8/MicroBiomes");
    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedUndergroundFinish1458 baseline = new();

    public WorldGeneratorId Id => baseline.Id;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);

        WorldGenerationRequest requestCopy = request;
        VanillaWorldSeedProfile1458 profile = WorldSeedResolver1458.Resolve(in requestCopy);
        if (!profile.IsDefault || !TerrainPass1458.IsCanonicalWorldSize(request.WidthTiles, request.HeightTiles))
        {
            capture.Replay(builder);
            return;
        }

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            builder.Add(
                new WorldGenerationPassDescriptor(
                    MicroBiomesId,
                    WorldGenerationRngMode.VanillaSharedRng,
                    requiredAfter: [SourceBackedUndergroundFinish1458.LarvaId]),
                new MicroBiomesPass1458());
            builder.Add(CloneDescriptor(entry.Descriptor, [MicroBiomesId]), entry.Pass);
        }
    }

    private static WorldGenerationPassDescriptor CloneDescriptor(
        WorldGenerationPassDescriptor source,
        WorldGenerationPassId[] requiredAfter) =>
        new(source.Id, source.RngMode, requiredAfter, source.OptionalAfter.ToArray(), source.OptionalBefore.ToArray());

    private readonly record struct CapturedPass(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CapturePlanBuilder : IWorldGenerationPlanBuilder
    {
        private readonly List<CapturedPass> entries = [];
        public IReadOnlyList<CapturedPass> Entries => entries;
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            entries.Add(new CapturedPass(descriptor, pass));
        public void Replay(IWorldGenerationPlanBuilder builder)
        {
            foreach (CapturedPass entry in entries)
                builder.Add(entry.Descriptor, entry.Pass);
        }
    }
}

/// <summary>Ordinary source Micro Biomes order over the candidate's canonical placement and effect owners.</summary>
internal sealed class MicroBiomesPass1458 : IWorldGenerationPass
{
    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var workspace=context.Workspace as Workspace??throw new InvalidOperationException("Micro Biomes requires Workspace.");
        var random=context.VanillaRandom??throw new InvalidOperationException("Micro Biomes requires the shared source random stream.");
        if(context.Metadata is null||!context.Metadata.TryGetLayers(out var layers))throw new InvalidOperationException("Micro Biomes requires source terrain layers.");
        var terrain=workspace.VanillaTerrainState??throw new InvalidOperationException("Micro Biomes requires retained terrain state.");
        int lava=workspace.VanillaLiquidLines?.LavaLine??throw new InvalidOperationException("Micro Biomes requires the source lava line.");
        // Source Micro Biomes reads both Main.worldSurface and the unrounded, retained GenVars layers.
        Run(workspace,random,layers.WorldSurface,terrain.CurrentWorldSurface,terrain.CurrentRockLayer,lava,
            (layers.WorldSurface+layers.RockLayer)/2+40,context.CancellationToken,
            fraction=>context.ReportProgress(fraction,"Micro Biomes"));
    }

    internal static void Run(Workspace workspace,IWorldGenerationVanillaRandom random,double worldSurface,
        double generationSurface,double generationRockLayer,int lavaLine,double oceanLevel,
        CancellationToken cancellationToken=default,Action<double>? progress=null)
    {
        var profile=workspace.VanillaSeedProfile;
        if(profile.Has(VanillaSpecialWorldSeed1458.Skyblock))return;
        if(profile.Secret!=VanillaSecretWorldSeed1458.None||profile.Special is not (VanillaSpecialWorldSeed1458.None or VanillaSpecialWorldSeed1458.DrunkWorld or VanillaSpecialWorldSeed1458.ForTheWorthy or VanillaSpecialWorldSeed1458.CelebrationMk10 or VanillaSpecialWorldSeed1458.TheConstant or VanillaSpecialWorldSeed1458.NotTheBees or VanillaSpecialWorldSeed1458.Remix or VanillaSpecialWorldSeed1458.NoTraps or (VanillaSpecialWorldSeed1458)255))
            throw new NotSupportedException("Micro Biomes extra and combined seed branches require independent source qualification.");
        bool bees=profile.Has(VanillaSpecialWorldSeed1458.NotTheBees),remix=profile.Has(VanillaSpecialWorldSeed1458.Remix),worthy=profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy),anniversary=profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10);
        if(!workspace.HasVanillaGenerationSolidity)throw new InvalidOperationException("Micro Biomes requires the live source solidity owner.");
        if(worthy)workspace.SetVanillaGenerationSolid(new(56),true);
        workspace.SetVanillaGenerationSolid(new(229),true);
        double widthScale=workspace.WidthTiles/4200d,areaScale=workspace.WidthTiles*(double)workspace.HeightTiles/5040000d;
        var deadMan=new MicroDeadMansChest1458(workspace,random,oceanLevel);
        var chestSlots=workspace.CaptureGeneratedChests();
        var candidates=deadMan.GetPossibleChests().Select(point=>chestSlots.Single(chest=>chest.X==point.X&&chest.Y==point.Y).SlotId).ToList();
        int target=Range(10,20,widthScale),attempts=3000;
        for(int done=0;done<target&&candidates.Count>0;done++)
        {
            cancellationToken.ThrowIfCancellationRequested();if(--attempts<=0)break;
            int index=random.Next(candidates.Count);
            var point=workspace.CaptureGeneratedChests().FirstOrDefault(chest=>chest.SlotId==candidates[index])??
                throw new NullReferenceException("The source selected a chest slot whose canonical occupation was removed.");
            deadMan.Place(point.X,point.Y);candidates.RemoveAt(index);
        }
        progress?.Invoke(.1);
        if(!bees||remix)
        {
        target=Range(3,5,widthScale);int failures=0;
        for(int done=0;done<target;)
        {
            cancellationToken.ThrowIfCancellationRequested();var point=Point((int)worldSurface+20,50,200,50);
            if(ThinIceBiome1458.Place(workspace,random,point.X,point.Y)){done++;failures=0;}
            else if(++failures>1000){done++;failures=0;}
        }
        }
        progress?.Invoke(.2);
        target=Range(1,2,widthScale);double swordChance=.5;
        if(anniversary){target*=2;swordChance/=2;}
        for(int attempt=0;attempt<target;attempt++)
        {
            if(!(anniversary&&attempt==0)&&random.NextDouble()<swordChance)continue;
            int retries=0;
            while(retries++<=workspace.WidthTiles)
            {
                cancellationToken.ThrowIfCancellationRequested();int y=(int)generationSurface+random.Next(50,100);
                int x=random.Next(2)==0?random.Next(50,(int)(workspace.WidthTiles*.3)):random.Next((int)(workspace.WidthTiles*.7),workspace.WidthTiles-50);
                if(MicroEnchantedSword1458.Place(workspace,random,x,y,anniversary:anniversary))break;
            }
        }
        progress?.Invoke(.3);
        if(!bees||remix)
        {
        target=Range(6,11,areaScale);attempts=1000;
        for(int done=0;done<target;)
        {
            cancellationToken.ThrowIfCancellationRequested();if(--attempts<=0)break;
            var point=Point((int)worldSurface,380,200,380);if(MicroCampsite1458.Place(workspace,random,point.X,point.Y))done++;
        }
        }
        progress?.Invoke(.4);
        if(!bees||remix)
        {
        target=Range(14,29,areaScale);attempts=3000;
        if((worthy||profile.Has(VanillaSpecialWorldSeed1458.NoTraps))&&!bees)target=(int)(target*1.5);
        for(int done=0;done<target;)
        {
            cancellationToken.ThrowIfCancellationRequested();if(--attempts<=0)break;
            var point=Point((int)(remix?worldSurface:generationRockLayer),380,remix?(int)generationRockLayer:200,380);if(MicroMiningExplosives1458.Place(workspace,random,point.X,point.Y))done++;
        }
        }
        progress?.Invoke(.5);
        target=Range(6,11,widthScale);attempts=0;
        for(int done=0;done<target&&attempts<20000;attempts++)
        {
            cancellationToken.ThrowIfCancellationRequested();var point=Point((int)worldSurface+50,50,500,50);
            if(MicroMahoganyTree1458.Place(workspace,random,point.X,point.Y))done++;
        }
        progress?.Invoke(.6);progress?.Invoke(.7);
        var tracks=new TrackGenerator1458(workspace,random);
        PlaceTracks(Range(1,2,widthScale),(int)(400*widthScale),(int)(1000*widthScale));progress?.Invoke(.8);
        PlaceTracks(Range(4,7,areaScale),(int)(150*widthScale),(int)(300*widthScale));progress?.Invoke(.9);
        if(!bees)for(int done=0;done<workspace.WidthTiles*.02;done++)
            for(int attempt=0;attempt<10150;attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();int x=random.Next(200,workspace.WidthTiles-200),y=random.Next(lavaLine-100,workspace.HeightTiles-210);
                if(MicroLavaTrap1458.Place(workspace,random,x,y))break;
            }
        progress?.Invoke(1);
        if(worthy)workspace.SetVanillaGenerationSolid(new(56),false);

        int Range(int min,int max,double scale)=>random.Next((int)(min*scale),(int)(max*scale)+1);
        (int X,int Y) Point(int top,int right,int bottom,int left)=>(random.Next(left,workspace.WidthTiles-right),random.Next(top,workspace.HeightTiles-bottom));
        void PlaceTracks(int count,int min,int max)
        {
            int failed=0;
            for(int done=0;done<count;)
            {
                cancellationToken.ThrowIfCancellationRequested();var point=Point((int)worldSurface,10,200,10);
                if(tracks.Place(point.X,point.Y,min,max)){done++;failed=0;}
                else if(++failed>workspace.WidthTiles/2){done++;failed=0;}
            }
        }
    }
}
