using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds.Generation;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;

namespace TerraRuntime.Application;

public enum RuntimeWorldGenerationCandidateStatus : byte
{
    Generated = 0,
    GeneratorNotFound = 1,
    GenerationFailed = 2
}

/// <summary>
/// Result of one selected world-generator execution. A candidate workspace is exposed only after every configured
/// pass has completed successfully; failed or cancelled generation never leaks a partially generated world into the
/// caller's publish/persistence path.
/// </summary>
public readonly record struct RuntimeWorldGenerationCandidateResult(
    RuntimeWorldGenerationCandidateStatus Status,
    Workspace? Candidate,
    WorldGenerationExecutionResult? Execution)
{
    public bool Succeeded => Status == RuntimeWorldGenerationCandidateStatus.Generated && Candidate is not null;
}

/// <summary>
/// Composition boundary between trusted-host generator registration and TerraRuntime's isolated generation engine.
/// The selected provider is captured before execution so later registration retirement cannot switch the generator
/// halfway through a job.
/// </summary>
public sealed class WorldGenerationCandidateRunner
{
    private readonly ITerraRuntimeWorldGeneratorSource generators;

    public WorldGenerationCandidateRunner(ITerraRuntimeWorldGeneratorSource generators)
    {
        ArgumentNullException.ThrowIfNull(generators);
        this.generators = generators;
    }

    public RuntimeWorldGenerationCandidateResult Generate(
        in WorldGenerationRequest request,
        IWorldGenerationProgressSink? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!generators.TryResolveWorldGenerator(request.GeneratorId, out IWorldGenerationProvider? provider) ||
            provider is null)
        {
            return new RuntimeWorldGenerationCandidateResult(
                RuntimeWorldGenerationCandidateStatus.GeneratorNotFound,
                Candidate: null,
                Execution: null);
        }

        Workspace candidate;
        try
        {
            candidate = new Workspace(request.WidthTiles, request.HeightTiles);
            if(provider.Id==Provider1458.GeneratorId){
                var profile=WorldSeedResolver1458.Resolve(in request);
                bool good=profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy)||profile.Has(VanillaSpecialWorldSeed1458.Zenith);
                float difficulty=(request.Options.GameMode==WorldGenerationGameMode.Master?3:request.Options.GameMode==WorldGenerationGameMode.Expert?2:1)+(good?1:0);
                var npcs=new RuntimeNpcStore();var projectiles=new RuntimeProjectileStore();
                var npcContext=new VanillaNpcSpawnContext(difficulty,0,good){WorldDifficulty=difficulty,TenthAnniversaryWorld=profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10)||profile.Has(VanillaSpecialWorldSeed1458.Zenith),RemixWorld=profile.Has(VanillaSpecialWorldSeed1458.Remix)||profile.Has(VanillaSpecialWorldSeed1458.Zenith)};
                npcs.SetVanillaSpawnContextSource(()=>npcContext);
                npcs.SetVanillaSpawnWetCollisionSource((x,y,width,height)=>VanillaWorldCollision.GetLiquidContacts(candidate.TileStore,x,y,width,height).Wet);
                if(!candidate.TileStore.TryAttachTileEntities([],1000))throw new InvalidOperationException("Fresh source candidate tile-entity owner was not empty.");
                candidate.AttachEntityEffects(new WorldGenerationEntityEffects1458(npcs,projectiles,sourceServer:true),sourceMyPlayer:0,sourceNpcDifficulty:difficulty,sourceJourney:request.Options.GameMode==WorldGenerationGameMode.Journey);
            }

        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            var failed = new WorldGenerationExecutionResult(
                WorldGenerationExecutionStatus.InvalidRequest,
                Error: exception);
            return new RuntimeWorldGenerationCandidateResult(
                RuntimeWorldGenerationCandidateStatus.GenerationFailed,
                Candidate: null,
                Execution: failed);
        }

        WorldGenerationExecutionResult execution = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            candidate,
            progress,
            cancellationToken);

        return execution.Succeeded
            ? new RuntimeWorldGenerationCandidateResult(
                RuntimeWorldGenerationCandidateStatus.Generated,
                candidate,
                execution)
            : new RuntimeWorldGenerationCandidateResult(
                RuntimeWorldGenerationCandidateStatus.GenerationFailed,
                Candidate: null,
                execution);
    }
}
