using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal interface IVanillaGemBunnyCapturedWorld1458 : IVanillaGemBunnyTerrain1458
{
    bool IsCurrent { get; }
    GemBunnyDanger1458 Danger { get; }
    bool ShelterWeather { get; }
    void FindRestingSpot(int homeX, int homeY, bool dayTime, out int x, out int y);
    bool TryStageStatus(IVanillaNpcRandom random);
    bool TryAdoptStatus(in NpcSnapshot accepted);
    void PublishStatus();
    bool SupportsCollision(in NpcStateUpdate state);
}

internal interface IVanillaGemBunnyEnvironment1458
{
    bool TryCapture(in NpcSnapshot source, VanillaNpcBehaviorContext context,
        out IVanillaGemBunnyCapturedWorld1458 capture);
}

/// <summary>Stages the exact subtype through the existing generation/RNG preview and accepted motion path.</summary>
internal sealed class VanillaGemBunnyAccepted1458
{
    private RuntimeNpcStore? store;
    private IVanillaGemBunnyEnvironment1458? environment;
    private SystemVanillaNpcRandom? random;
    private int worldWidthTiles;
    private double worldSurfaceTiles = double.NaN;
    private Plan? pending;
    private NpcSnapshot completedBefore, completedAfter;
    private bool completedForce, completedInactive;

    private sealed record Plan(NpcSnapshot Before, NpcStateUpdate Ai,
        RuntimeNpcStore.AiSpawnPreview Preview, IVanillaGemBunnyCapturedWorld1458 World,
        VanillaNpcBehaviorContext Context, VanillaNpcTargetCandidate[] Candidates,
        NpcRawPlayerSlotSnapshot1458[] Players, bool Force, bool DayTime, bool Eclipse, bool SlimeRain);

    internal void Configure(RuntimeNpcStore owner, IVanillaGemBunnyEnvironment1458 world, IVanillaNpcRandom source,
        int widthTiles = 0, double surfaceTiles = double.NaN)
    {
        store = owner;
        environment = world;
        random = source as SystemVanillaNpcRandom;
        worldWidthTiles = widthTiles;
        worldSurfaceTiles = surfaceTiles;
    }

    internal bool HasPlan(in NpcSnapshot before) => pending?.Before == before;
    internal bool TryGetConditions(in NpcSnapshot before, out bool dayTime, out bool eclipse)
    {
        dayTime = eclipse = false;
        if (pending is not { } plan || plan.Before != before || !InputsCurrent(plan)) return false;
        dayTime = plan.DayTime; eclipse = plan.Eclipse;
        return true;
    }
    internal void Cancel() => pending = null;

    internal bool TryStep(in NpcSnapshot before, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        pending = null;
        completedInactive = false;
        next = default;
        if (store is null || environment is null || random is null || !VanillaGemBunnyAi1458.IsSupported(before.Type) ||
            before.Simulation.LifeMax != 5 ||
            before.Simulation.CatchableNpcTempImmunityCounter is not (>= 0 and <= 90) ||
            before.Simulation.ReleaseOwner is not (>= 0 and <= 255) || before.Simulation.Friendly is null ||
            !context.HasRawPlayerSlots || before.Simulation.HomeTileX is not { } homeX ||
            before.Simulation.HomeTileY is not { } homeY || before.Simulation.Homeless is not { } homeless ||
            before.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer ||
            before.Simulation.ShimmerTransparency is not 0f ||
            !definition.TryResolveHitbox(before.Simulation, out var body) ||
            !store.TryCreateAiSpawnPreview(in before, random.SourceRandom, out var preview)) return false;

        if (!context.TryCaptureRawPlayer(byte.MaxValue, out var sentinel)) return false;
        var players = new List<NpcRawPlayerSlotSnapshot1458> { sentinel };
        foreach (var candidate in context.Candidates)
        {
            if (!context.TryCaptureRawPlayer(candidate.Slot, out var player) ||
                !Matches(candidate, player.Facts)) return false;
            players.Add(player);
        }
        NpcSnapshot staged = before;
        bool force = false;
        if (before.Target == byte.MaxValue)
        {
            if (!context.TrySelectRawClosestTarget(in before, in definition, out var selection) ||
                !context.TryCaptureRawPlayer((byte)selection.Target, out var player)) return false;
            if (!players.Any(row => row.Facts.Slot == player.Facts.Slot)) players.Add(player);
            int direction = selection.DirectionX;
            if (before.PositionX < player.Facts.PositionX) direction = 1;
            if (before.PositionX > player.Facts.PositionX) direction = -1;
            force = selection.Target != before.Target || direction != before.Simulation.DirectionX;
            if (homeX == -1) homeX = (int)((before.PositionX + body.Width / 2) / 16f);
            staged = before with { Target = selection.Target, Simulation = before.Simulation with {
                DirectionX = direction, SpriteDirection = direction, HomeTileX = homeX } };
        }
        else if (homeX == -1 && homeY == -1 && before.VelocityY == 0f)
        {
            homeX = (int)(before.PositionX + body.Width * .5f) / 16;
            homeY = (int)(before.PositionY + body.Height + 4f) / 16;
        }
        staged = staged with { Simulation = staged.Simulation with {
            HomeTileX = homeX, HomeTileY = homeY, Homeless = homeless } };
        if (!environment.TryCapture(in staged, context, out var world) || !world.IsCurrent ||
            !VanillaNpcGravity.TryApply(in definition, before.PositionY, before.VelocityY,
                before.Simulation.Wet, before.Simulation.LiquidContact,
                worldWidthTiles > 0 ? worldWidthTiles : (int)(context.WorldWidthPixels / 16d),
                double.IsFinite(worldSurfaceTiles) ? worldSurfaceTiles : context.WorldSurfacePixels / 16d,
                out var gravity)) return false;
        if (!world.TryStageStatus(new SystemVanillaNpcRandom(preview!.Random))) return false;
        world.FindRestingSpot(homeX, homeY, context.DayTime, out int restingX, out int restingY);
        var ai = new VanillaGemBunnyAi1458 {
            X = before.PositionX, Y = before.PositionY, Vx = before.VelocityX, Vy = before.VelocityY,
            Width = body.Width, Height = body.Height, HomeX = homeX, HomeY = homeY,
            RestingX = restingX, RestingY = restingY, Homeless = homeless,
            Direction = staged.Simulation.DirectionX, Life = before.Simulation.Life, LifeMax = before.Simulation.LifeMax,
            Wet = before.Simulation.Wet, Friendly = before.Simulation.Friendly.Value,
            Ai = before.Ai, Local = before.Simulation.LocalAi,
            OldX = before.Simulation.OldPositionX, OldY = before.Simulation.OldPositionY,
            OldVx = before.Simulation.OldVelocityX, OldVy = before.Simulation.OldVelocityY,
            Gravity = gravity.Parameters.Gravity, ShelterWeather = world.ShelterWeather };
        if (!ai.TryStep(world, world.Danger, new SystemVanillaNpcRandom(preview!.Random))) return false;
        int immunity = before.Simulation.CatchableNpcTempImmunityCounter.Value;
        if (before.Simulation.ReleaseOwner != 255 || before.Simulation.SpawnedFromStatue) immunity = 0;
        bool friendly = immunity > 0;
        if (immunity > 0) immunity--;
        var update = new NpcStateUpdate(before.Type, before.NetId, ai.X, ai.Y, ai.Vx, ai.Vy, staged.Target,
            ai.Ai, staged.Simulation with { DirectionX = ai.Direction, DirectionY = -1,
                Friendly = friendly, CatchableNpcTempImmunityCounter = immunity,
                LocalAi = ai.Local, DontTakeDamage = false, DefenseOverride = before.Simulation.BaseDefense ?? definition.Defense });
        var plan = new Plan(before, update, preview!, world, context, context.Candidates.ToArray(), players.ToArray(),
            force || ai.Force || friendly != before.Simulation.Friendly, context.DayTime, context.EclipseActive, context.SlimeRainActive);
        if (!RuntimeNpcStore.IsValid(in update) || !InputsCurrent(plan) || !preview!.IsBeforeCurrent()) return false;
        pending = plan;
        next = State(in before) with { VanillaNetworkPhase = true,
            RequiresLowPrioritySync = before.Simulation.Life < before.Simulation.LifeMax &&
                before.Simulation.FriendlyRegenerationCounter == 180 };
        return true;
    }

    internal bool TryGetPlan(in NpcSnapshot before, in NpcSnapshot accepted, out NpcStateUpdate ai)
    {
        ai = default;
        if (pending is not { } plan || plan.Before != before || !InputsCurrent(plan) || !plan.Preview.IsCurrent(in accepted))
            return false;
        ai = plan.Ai;
        return true;
    }

    internal bool TryGetPlanRandom(in NpcSnapshot before, in NpcSnapshot accepted,
        out VanillaUnifiedRandom1458 beforeRandom, out VanillaUnifiedRandom1458 afterRandom)
    {
        beforeRandom = afterRandom = default!;
        if (random is null || !TryGetPlan(in before, in accepted, out _)) return false;
        beforeRandom = random.SourceRandom.Clone();
        afterRandom = pending!.Preview.Random;
        return true;
    }

    internal bool IsAcceptedCurrent(in NpcSnapshot before, in NpcSnapshot accepted) =>
        pending is { } plan && plan.Before == before && InputsCurrent(plan) && plan.Preview.IsCurrent(in accepted);

    internal bool TryGetCapturedWorld(in NpcSnapshot before, in NpcSnapshot accepted,
        out IVanillaGemBunnyCapturedWorld1458 world)
    {
        world = default!;
        if (!IsAcceptedCurrent(in before, in accepted)) return false;
        world = pending!.World;
        return true;
    }

    internal void ForceSync() { if (pending is { } plan) pending = plan with { Force = true }; }

    // The application supplies the result of its shared gravity→friendly-regeneration/contact→collision tail.
    // It must not call this method with AI-only motion when contact/death admission is unavailable.
    internal NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot accepted, in NpcStateUpdate physics)
    {
        if (pending is not { } plan || plan.Before != before || !InputsCurrent(plan) ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(physics.Simulation, out var body)) { pending = null; return default; }
        var presented = FinishPresentation(in physics);
        Span<VanillaNpcRawPlayer1458> players = stackalloc VanillaNpcRawPlayer1458[plan.Players.Length];
        int count = 0;
        foreach (var player in plan.Players) if (player.Facts.Active) players[count++] = player.Facts;
        if (!VanillaOrdinarySlimeCheckActive1458.TryStep(physics.PositionX, physics.PositionY, body.Width, body.Height,
                physics.Simulation.TimeLeft, players[..count], out int timeLeft, out bool inactive))
        { pending = null; return default; }
        var final = presented with { Simulation = presented.Simulation with {
            // UpdateNetworkCode precedes CheckActive. Keep this active network offer's life;
            // the existing executor/store removal writes zero for the later inactive packet.
            TimeLeft = timeLeft } };
        if (!InputsCurrent(plan) || !plan.Preview.TryAdopt(in accepted, in final, out var completed))
        { pending = null; return default; }
        if (!plan.World.TryAdoptStatus(in completed))
            throw new InvalidOperationException("An accepted gem-bunny phase lost its status owner without a callback.");
        pending = null;
        completedBefore = before; completedAfter = completed; completedForce = plan.Force; completedInactive = inactive;
        plan.World.PublishStatus();
        return completed;
    }

    internal static NpcStateUpdate FinishPresentation(in NpcStateUpdate physics)
    {
        int frame = physics.Simulation.FrameIndex ?? 0;
        double frameCounter = physics.Simulation.FrameCounter;
        int sprite = physics.Simulation.SpriteDirection;
        if (physics.VelocityY == 0f)
        {
            if (physics.Simulation.DirectionX != 0) sprite = physics.Simulation.DirectionX;
            if (physics.VelocityX == 0f) { frame = 0; frameCounter = 0d; }
            else
            {
                frameCounter += Math.Abs(physics.VelocityX);
                frameCounter++;
                if (frameCounter > 6d) { frame++; frameCounter = 0d; }
                if (frame >= 7) frame = 0;
            }
        }
        else { frameCounter = 0d; frame = physics.VelocityY < 0f ? 4 : 6; }
        return physics with { Simulation = physics.Simulation with {
            FrameIndex = frame, FrameCounter = frameCounter, SpriteDirection = sprite, JustHit = false } };
    }

    internal bool RequiresForcedUpdate(in NpcSnapshot before, in NpcSnapshot after) =>
        completedBefore == before && completedAfter == after && completedForce;
    internal bool Deactivates(in NpcSnapshot before, in NpcSnapshot after) =>
        completedBefore == before && completedAfter == after && completedInactive;

    private static bool InputsCurrent(Plan plan)
    {
        if (plan.Context.DayTime != plan.DayTime || plan.Context.EclipseActive != plan.Eclipse ||
            plan.Context.SlimeRainActive != plan.SlimeRain || !plan.Context.Candidates.SequenceEqual(plan.Candidates) ||
            !plan.World.IsCurrent) return false;
        foreach (var player in plan.Players) if (!plan.Context.IsRawPlayerCurrent(in player)) return false;
        return plan.World.IsCurrent;
    }

    private static NpcStateUpdate State(in NpcSnapshot source) => new(source.Type, source.NetId,
        source.PositionX, source.PositionY, source.VelocityX, source.VelocityY, source.Target, source.Ai, source.Simulation);

    private static bool Matches(VanillaNpcTargetCandidate candidate, VanillaNpcRawPlayer1458 raw) =>
        candidate.Active == raw.Active && candidate.Dead == raw.Dead && candidate.Ghost == raw.Ghost &&
        candidate.NoAggro == raw.NoAggro && candidate.Aggro == raw.Aggro && candidate.Width == raw.Width &&
        candidate.Height == raw.Height && candidate.CenterX == raw.PositionX + raw.Width * .5f &&
        candidate.CenterY == raw.PositionY + raw.Height * .5f;
}
