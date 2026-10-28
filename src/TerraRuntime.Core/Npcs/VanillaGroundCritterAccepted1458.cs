using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal readonly record struct GroundCritterTurnFacts1458(bool CanHit, float Luck, bool IsSpecialName);

internal interface IVanillaGroundCritterCapturedWorld1458 : IVanillaGroundCritterTerrain1458
{
    bool IsCurrent { get; }
    GroundCritterDanger1458 Danger { get; }
    bool ShelterWeather { get; }
    void FindRestingSpot(int homeX, int homeY, bool dayTime, out int x, out int y);
    bool TryStageStatus(IVanillaNpcRandom random);
    bool TryAdoptStatus(in NpcSnapshot accepted);
    void PublishStatus();
    bool SupportsCollision(in NpcStateUpdate state);
    bool TryFinishPresentation(in NpcStateUpdate physics, out NpcStateUpdate presented);
    bool TryGetSquirrelTurnFacts(out GroundCritterTurnFacts1458 facts);
    bool TryPrepareFighterTerrain(in NpcStateUpdate source, in VanillaNpcRawPlayer1458 target) => false;
}

internal interface IVanillaGroundCritterEnvironment1458
{
    bool TryCapture(in NpcSnapshot source, VanillaNpcBehaviorContext context,
        out IVanillaGroundCritterCapturedWorld1458 capture);
}

/// <summary>Stages the exact subtype through the existing generation/RNG preview and accepted motion path.</summary>
internal sealed class VanillaGroundCritterAccepted1458
{
    private RuntimeNpcStore? store;
    private IVanillaGroundCritterEnvironment1458? environment;
    private SystemVanillaNpcRandom? random;
    private int worldWidthTiles;
    private double worldSurfaceTiles = double.NaN;
    private Plan? pending;
    private NpcSnapshot completedBefore, completedAfter;
    private bool completedForce, completedInactive;

    private sealed record Plan(NpcSnapshot Before, NpcStateUpdate Ai,
        RuntimeNpcStore.AiSpawnPreview Preview, IVanillaGroundCritterCapturedWorld1458 World,
        VanillaNpcBehaviorContext Context, VanillaNpcTargetCandidate[] Candidates,
        NpcRawPlayerSlotSnapshot1458[] Players, bool Force, bool DayTime, bool Eclipse, bool SlimeRain);

    internal void Configure(RuntimeNpcStore owner, IVanillaGroundCritterEnvironment1458 world, IVanillaNpcRandom source,
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
        VanillaNpcBehaviorContext context, VanillaGroundFighterNpcBehaviorStrategy fighter,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        pending = null;
        completedInactive = false;
        next = default;
        var original = before;
        int style = before.Simulation.AiStyleOverride?.Value ?? definition.AiStyle.Value;
        bool squirrel = VanillaNonresidentGroundCritterCatalog1458.IsTurningSquirrel(before.Type);
        bool hostile = squirrel && style == 3;
        if (store is null || environment is null || random is null || !VanillaGroundCritterAi1458.IsSupported(before.Type) ||
            !VanillaNonresidentGroundCritterCatalog1458.TryGetFamily(before.Type, out var family) ||
            (style != 7 && !hostile) || before.Simulation.LifeMax != (hostile ? 25 : 5) ||
            style == 7 && (squirrel ? before.Ai.Ai3 is not (0f or 2f) : before.Ai.Ai3 != 0f) ||
            before.Simulation.CatchableNpcTempImmunityCounter is not (>= 0 and <= 90) ||
            before.Simulation.ReleaseOwner is not (>= 0 and <= 255) || before.Simulation.Friendly is null ||
            !context.HasRawPlayerSlots || before.Simulation.HomeTileX is not { } homeX ||
            before.Simulation.HomeTileY is not { } homeY || before.Simulation.Homeless is not { } homeless ||
            before.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer ||
            before.Simulation.ShimmerTransparency is not 0f ||
            !definition.TryResolveHitbox(before.Simulation, out var body) ||
            !store.TryCreateAiSpawnPreview(in before, random.SourceRandom, out var preview)) return false;

        if (!context.TryCaptureRawPlayer(byte.MaxValue, out var sentinel)) return false;
        if (!context.TryCaptureRawPlayer(0, out var zero)) return false;
        var players = new List<NpcRawPlayerSlotSnapshot1458> { sentinel, zero };
        foreach (var candidate in context.Candidates)
        {
            if (!context.TryCaptureRawPlayer(candidate.Slot, out var player) ||
                !Matches(candidate, player.Facts)) return false;
            if (!players.Any(row => row.Facts.Slot == player.Facts.Slot)) players.Add(player);
        }
        ushort oldTarget = before.Target;
        if (oldTarget < byte.MaxValue && !players.Any(row => row.Facts.Slot == oldTarget))
        {
            if (!context.TryCaptureRawPlayer((byte)before.Target, out var currentTarget)) return false;
            players.Add(currentTarget);
        }
        NpcSnapshot staged = before;
        bool force = false;
        if (!hostile && before.Target == byte.MaxValue)
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
        else if (!hostile && homeX == -1 && homeY == -1 && before.VelocityY == 0f)
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
        next = State(in before) with { VanillaNetworkPhase = true,
            RequiresLowPrioritySync = before.Simulation.Life < before.Simulation.LifeMax &&
                before.Simulation.FriendlyRegenerationCounter == 180 };
        var previewRandom = new SystemVanillaNpcRandom(preview.Random);
        if (hostile)
        {
            if (!fighter.TryStepHostileGroundCritterPreview(in staged, in definition, context, inner, previewRandom, out var hostileUpdate))
                return false;
            return Retain(FinishCatchProtection(in hostileUpdate), hostileUpdate.RequiresImmediateSync);
        }
        int nextStyle = style;
        int life = before.Simulation.Life, maximumLife = before.Simulation.LifeMax;
        int defense = before.Simulation.BaseDefense ?? definition.Defense;
        int? damage = before.Simulation.DamageOverride;
        var aiState = before.Ai;
        var local = before.Simulation.LocalAi;
        if (family == VanillaGroundCritterFamily1458.Penguin && local.Ai0 == 0f)
            local = local with { Ai0 = previewRandom.NextInt32(1, 5) };
        world.FindRestingSpot(homeX, homeY, context.DayTime, out int restingX, out int restingY);
        if (squirrel)
        {
            if (!world.TryGetSquirrelTurnFacts(out var turn) || turn.CanHit && !float.IsFinite(turn.Luck)) return false;
            bool luckTurns = turn.CanHit && turn.Luck < 0f &&
                (float)previewRandom.NextDouble() < -turn.Luck && previewRandom.NextInt32(0, 300) == 0;
            if (turn.CanHit && (luckTurns || turn.IsSpecialName))
            { aiState = aiState with { Ai3 = 2f }; force = true; }
            if (aiState.Ai3 != 0f)
            { nextStyle = 3; maximumLife = life = 25; defense += 6; damage = 20; }
        }
        var ai = new VanillaGroundCritterAi1458 {
            Family = family,
            X = before.PositionX, Y = before.PositionY, Vx = before.VelocityX, Vy = before.VelocityY,
            Width = body.Width, Height = body.Height, HomeX = homeX, HomeY = homeY,
            RestingX = restingX, RestingY = restingY, Homeless = homeless,
            Direction = staged.Simulation.DirectionX, Life = life, LifeMax = maximumLife,
            Wet = before.Simulation.Wet, Friendly = before.Simulation.Friendly.Value,
            CollideX = before.Simulation.CollideX,
            WorldWidthTiles = worldWidthTiles > 0 ? worldWidthTiles : (int)(context.WorldWidthPixels / 16d),
            Ai = aiState, Local = local,
            OldX = before.Simulation.OldPositionX, OldY = before.Simulation.OldPositionY,
            OldVx = before.Simulation.OldVelocityX, OldVy = before.Simulation.OldVelocityY,
            Gravity = gravity.Parameters.Gravity, ShelterWeather = world.ShelterWeather };
        if (!ai.TryStep(world, world.Danger, previewRandom)) return false;
        var proposed = new NpcStateUpdate(before.Type, before.NetId, ai.X, ai.Y, ai.Vx, ai.Vy, staged.Target,
            ai.Ai, staged.Simulation with { DirectionX = ai.Direction, DirectionY = -1,
                Life = life, LifeMax = maximumLife, AiStyleOverride = nextStyle != definition.AiStyle.Value ? new(nextStyle) : before.Simulation.AiStyleOverride,
                LocalAi = ai.Local, DontTakeDamage = false, DefenseOverride = defense, DamageOverride = damage });
        return Retain(FinishCatchProtection(in proposed), force || ai.Force);

        NpcStateUpdate FinishCatchProtection(in NpcStateUpdate state)
        {
            if (!VanillaNonresidentGroundCritterCatalog1458.HasCatchProtection(original.Type)) return state;
            int immunity = original.Simulation.CatchableNpcTempImmunityCounter!.Value;
            if (original.Simulation.ReleaseOwner != 255 || original.Simulation.SpawnedFromStatue) immunity = 0;
            bool friendly = immunity > 0;
            if (immunity > 0) immunity--;
            return state with { Simulation = state.Simulation with { Friendly = friendly, CatchableNpcTempImmunityCounter = immunity } };
        }
        bool Retain(NpcStateUpdate update, bool forceUpdate)
        {
            var plan = new Plan(original, update, preview!, world, context, context.Candidates.ToArray(), players.ToArray(),
                forceUpdate || update.Simulation.Friendly != original.Simulation.Friendly,
                context.DayTime, context.EclipseActive, context.SlimeRainActive);
            if (!RuntimeNpcStore.IsValid(in update) || !InputsCurrent(plan) || !preview!.IsBeforeCurrent()) return false;
            pending = plan;
            return true;
        }
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
        out IVanillaGroundCritterCapturedWorld1458 world)
    {
        world = default!;
        if (!IsAcceptedCurrent(in before, in accepted)) return false;
        world = pending!.World;
        return true;
    }

    internal bool TryGetRawTarget(in NpcSnapshot before, in NpcSnapshot accepted, out VanillaNpcRawPlayer1458 target)
    {
        target = default;
        if (!IsAcceptedCurrent(in before, in accepted) || pending is not { } plan) return false;
        foreach (var player in plan.Players)
            if (player.Facts.Slot == plan.Ai.Target) { target = player.Facts; return target.IsValid; }
        return false;
    }

    internal void ForceSync() { if (pending is { } plan) pending = plan with { Force = true }; }

    // The application supplies the result of its shared gravity→friendly-regeneration/contact→collision tail.
    // It must not call this method with AI-only motion when contact/death admission is unavailable.
    internal NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot accepted, in NpcStateUpdate physics)
    {
        if (pending is not { } plan || plan.Before != before || !InputsCurrent(plan) ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(physics.Simulation, out var body)) { pending = null; return default; }
        if (!plan.World.TryFinishPresentation(in physics, out var presented)) { pending = null; return default; }
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
