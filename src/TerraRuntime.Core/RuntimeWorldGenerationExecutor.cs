using System.Globalization;
using System.Text;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

public enum WorldGenerationExecutionStatus : byte
{
    Completed = 0,
    InvalidRequest = 1,
    ProviderMismatch = 2,
    EmptyPlan = 3,
    InvalidPlan = 4,
    MissingRequiredDependency = 5,
    DependencyCycle = 6,
    UnsupportedRngMode = 7,
    Cancelled = 8,
    ProviderFailed = 9,
    PassFailed = 10
}

public readonly record struct WorldGenerationExecutionResult(
    WorldGenerationExecutionStatus Status,
    WorldGenerationPassId PassId = default,
    WorldGenerationPassId DependencyId = default,
    Exception? Error = null)
{
    public bool Succeeded => Status == WorldGenerationExecutionStatus.Completed;
}

/// <summary>
/// Builds and executes one world-generation plan against an isolated caller-owned workspace. Nothing in this type
/// publishes the candidate as a live world; callers may commit it only after this method returns Completed and any
/// higher-level world metadata/persistence validation has succeeded.
/// </summary>
public static class RuntimeWorldGenerationExecutor
{
    public const int MaxProgressReportsPerPass = 1_024;

    public static WorldGenerationExecutionResult Execute(
        IWorldGenerationProvider provider,
        in WorldGenerationRequest request,
        IWorldGenerationWorkspace workspace,
        IWorldGenerationProgressSink? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(workspace);

        try
        {
            request.Validate();
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return new WorldGenerationExecutionResult(
                WorldGenerationExecutionStatus.InvalidRequest,
                Error: exception);
        }

        WorldGeneratorId providerId;
        try
        {
            providerId = provider.Id;
        }
        catch (Exception exception)
        {
            return new WorldGenerationExecutionResult(
                WorldGenerationExecutionStatus.ProviderFailed,
                Error: exception);
        }

        if (!providerId.IsAssigned || providerId != request.GeneratorId ||
            workspace.WidthTiles != request.WidthTiles || workspace.HeightTiles != request.HeightTiles)
        {
            return new WorldGenerationExecutionResult(WorldGenerationExecutionStatus.ProviderMismatch);
        }

        var passRegistry = new RuntimeWorldGenerationPassRegistry<IWorldGenerationPass>();
        var builder = new PlanBuilder(passRegistry);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            provider.BuildPlan(in request, builder);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new WorldGenerationExecutionResult(WorldGenerationExecutionStatus.Cancelled);
        }
        catch (InvalidWorldGenerationPlanException exception)
        {
            return new WorldGenerationExecutionResult(
                WorldGenerationExecutionStatus.InvalidPlan,
                exception.PassId,
                Error: exception);
        }
        catch (Exception exception)
        {
            return new WorldGenerationExecutionResult(
                WorldGenerationExecutionStatus.ProviderFailed,
                Error: exception);
        }

        if (builder.Count == 0)
            return new WorldGenerationExecutionResult(WorldGenerationExecutionStatus.EmptyPlan);

        WorldGenerationPlanCommitResult planCommit = passRegistry.CommitPending();
        switch (planCommit.Status)
        {
            case WorldGenerationPlanCommitStatus.Published:
            case WorldGenerationPlanCommitStatus.NoChanges:
                break;
            case WorldGenerationPlanCommitStatus.MissingRequiredDependency:
                return new WorldGenerationExecutionResult(
                    WorldGenerationExecutionStatus.MissingRequiredDependency,
                    planCommit.PassId,
                    planCommit.DependencyId);
            case WorldGenerationPlanCommitStatus.DependencyCycle:
                return new WorldGenerationExecutionResult(
                    WorldGenerationExecutionStatus.DependencyCycle,
                    planCommit.PassId);
            default:
                return new WorldGenerationExecutionResult(WorldGenerationExecutionStatus.InvalidPlan);
        }

        RuntimeWorldGenerationPlan<IWorldGenerationPass> plan = passRegistry.Plan;
        ReadOnlySpan<RuntimeWorldGenerationPlanEntry<IWorldGenerationPass>> entries = plan.Entries.Span;
        int? vanillaSeed = null;
        VanillaWorldGenerationRandomAdapter? sharedVanillaRandom = null;
        for (int passIndex = 0; passIndex < entries.Length; passIndex++)
        {
            RuntimeWorldGenerationPlanEntry<IWorldGenerationPass> entry = entries[passIndex];
            WorldGenerationPassDescriptor descriptor = entry.Descriptor;

            if (descriptor.RngMode == WorldGenerationRngMode.CustomProviderRng)
            {
                return new WorldGenerationExecutionResult(
                    WorldGenerationExecutionStatus.UnsupportedRngMode,
                    descriptor.Id);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var random = new WorldGenerationRandomAdapter(
                    WorldGenerationPassRandom.Create(request.Seed, descriptor.Id));
                IWorldGenerationVanillaRandom? vanillaRandom = null;
                if (descriptor.RngMode == WorldGenerationRngMode.VanillaSharedRng)
                {
                    vanillaSeed ??= VanillaSeedText1458.Resolve(in request);
                    sharedVanillaRandom ??= new VanillaWorldGenerationRandomAdapter(
                        new VanillaUnifiedRandom1458(vanillaSeed.Value));
                    vanillaRandom = sharedVanillaRandom;
                }

                var context = new PassContext(
                    request,
                    workspace,
                    random,
                    vanillaRandom,
                    progress,
                    descriptor.Id,
                    passIndex,
                    entries.Length,
                    cancellationToken);
                entry.Pass.Execute(context);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new WorldGenerationExecutionResult(
                    WorldGenerationExecutionStatus.Cancelled,
                    descriptor.Id);
            }
            catch (Exception exception)
            {
                return new WorldGenerationExecutionResult(
                    WorldGenerationExecutionStatus.PassFailed,
                    descriptor.Id,
                    Error: exception);
            }
        }

        return new WorldGenerationExecutionResult(WorldGenerationExecutionStatus.Completed);
    }

    private sealed class PlanBuilder : IWorldGenerationPlanBuilder
    {
        private readonly RuntimeWorldGenerationPassRegistry<IWorldGenerationPass> registry;

        public PlanBuilder(RuntimeWorldGenerationPassRegistry<IWorldGenerationPass> registry) =>
            this.registry = registry;

        public int Count { get; private set; }

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(pass);

            WorldGenerationPassRegistrationResult result = registry.TryRegister(descriptor, pass, out _);
            if (result != WorldGenerationPassRegistrationResult.Registered)
                throw new InvalidWorldGenerationPlanException(descriptor.Id, result);

            Count++;
        }
    }

    private sealed class PassContext : IWorldGenerationContext
    {
        private readonly IWorldGenerationProgressSink? progress;
        private readonly WorldGenerationPassId passId;
        private readonly int passIndex;
        private readonly int passCount;
        private int reports;

        public PassContext(
            WorldGenerationRequest request,
            IWorldGenerationWorkspace workspace,
            IWorldGenerationRandom random,
            IWorldGenerationVanillaRandom? vanillaRandom,
            IWorldGenerationProgressSink? progress,
            WorldGenerationPassId passId,
            int passIndex,
            int passCount,
            CancellationToken cancellationToken)
        {
            Request = request;
            Workspace = workspace;
            Metadata = workspace as IWorldGenerationMetadataWorkspace;
            Random = random;
            VanillaRandom = vanillaRandom;
            this.progress = progress;
            this.passId = passId;
            this.passIndex = passIndex;
            this.passCount = passCount;
            CancellationToken = cancellationToken;
        }

        public WorldGenerationRequest Request { get; }
        public IWorldGenerationWorkspace Workspace { get; }
        public IWorldGenerationMetadataWorkspace? Metadata { get; }
        public IWorldGenerationRandom Random { get; }
        public IWorldGenerationVanillaRandom? VanillaRandom { get; }
        public CancellationToken CancellationToken { get; }

        public void ReportProgress(double fraction, string? message = null)
        {
            if (!double.IsFinite(fraction) || fraction < 0d || fraction > 1d)
                throw new ArgumentOutOfRangeException(nameof(fraction), "World-generation progress must be finite and within [0, 1].");

            CancellationToken.ThrowIfCancellationRequested();
            if (progress is null || reports >= MaxProgressReportsPerPass)
                return;

            reports++;
            var update = new WorldGenerationProgress(passId, passIndex, passCount, fraction, message);
            progress.Report(in update);
        }
    }

    private sealed class WorldGenerationRandomAdapter : IWorldGenerationRandom
    {
        private WorldGenerationPassRandom random;

        public WorldGenerationRandomAdapter(WorldGenerationPassRandom random) => this.random = random;

        public ulong NextUInt64() => random.NextUInt64();
        public uint NextUInt32() => random.NextUInt32();
        public int NextInt32(int exclusiveMax) => random.NextInt32(exclusiveMax);
    }

    private sealed class VanillaWorldGenerationRandomAdapter : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random;

        public VanillaWorldGenerationRandomAdapter(VanillaUnifiedRandom1458 random) => this.random = random;

        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }

    private static class VanillaSeedText1458
    {
        private const uint Polynomial = 0xEDB88320u;

        public static int Resolve(in WorldGenerationRequest request)
        {
            string text = request.SeedText ?? request.Seed.ToString(CultureInfo.InvariantCulture);
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric))
                return numeric;

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            uint crc = uint.MaxValue;
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1u) != 0 ? Polynomial : 0u);
            }

            return unchecked((int)(crc ^ uint.MaxValue));
        }
    }

    private sealed class InvalidWorldGenerationPlanException : InvalidOperationException
    {
        public InvalidWorldGenerationPlanException(
            WorldGenerationPassId passId,
            WorldGenerationPassRegistrationResult result)
            : base($"World-generation pass '{passId}' could not be registered: {result}.")
        {
            PassId = passId;
        }

        public WorldGenerationPassId PassId { get; }
    }
}
