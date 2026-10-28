using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed class VanillaGroundCritterEnvironment1458(
    WorldTileStore tiles, RuntimeNpcStore npcs, RuntimeNpcBuffStatus1458 status,
    Func<NpcSnapshot, RuntimeTownNpcScheduleConditions1458> conditions,
    Func<RuntimeTownPlayerDanger1458[]> playerDanger,
    RuntimeNpcReplicationRegistry? replication = null,
    Func<bool?>? bloodMoonOwner = null,
    IRuntimePlayerPreludeNameLookup1458? playerNames = null) : IVanillaGroundCritterEnvironment1458
{
    public bool TryCapture(in NpcSnapshot source, VanillaNpcBehaviorContext context,
        out IVanillaGroundCritterCapturedWorld1458 capture)
    {
        capture = default!;
        // Unlike gem bunnies, convertible identities transform before AI007 during Blood Moon.
        // Its transformation owner is not admitted here. Missing context cannot mean neutral.
        if (TerraRuntime.Gameplay.Npcs.VanillaNonresidentGroundCritterCatalog1458.IsEvilConvertible(source.Type) &&
            bloodMoonOwner?.Invoke() is not false) return false;
        var peers = new NpcSnapshot[npcs.Capacity];
        int count = npcs.CopyActive(peers);
        var players = playerDanger();
        var weather = conditions(source);
        if (!npcs.TryGet(source.Handle, out var retained) ||
            !status.TryPlanGemBunnyPhase(in retained, out var phase)) return false;
        if (weather.DayTime != context.DayTime || weather.Eclipse != context.EclipseActive ||
            weather.SlimeRain != context.SlimeRainActive || !status.TryGetStinky(source.Handle, out bool stinky)) return false;
        RuntimeTownNpcDanger1458 danger = default;
        if (source.Type is not (616 or 617 or 625) &&
            !RuntimeTownNpcDangerScanner1458.TryScan(tiles, in source, peers.AsSpan(0, count), players,
                status, false, false, out danger)) return false;
        var terrain = new VanillaGroundCritterTerrain1458(tiles);
        // Source danger LOS cannot leave its 200-pixel scan radius. Capture the actual shared
        // LOS read envelope before retaining any random-dependent AI proposal.
        terrain.FenceBody(source.PositionX - 224f, source.PositionY - 224f, 0f, 0f, 480, 480);
        if (!TerraRuntime.Gameplay.Npcs.VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(source.Simulation, out var body) ||
            VanillaWorldCollision.TryGetWetContact(tiles, source.PositionX, source.PositionY, body.Width, body.Height,
                out var liquid) && liquid == WorldLiquidKind.Shimmer) return false;
        TurnCapture? turn = null;
        if (VanillaNonresidentGroundCritterCatalog1458.IsTurningSquirrel(source.Type) &&
            (source.Simulation.AiStyleOverride?.Value ?? definition.AiStyle.Value) == 7 &&
            !TryCaptureTurn(terrain, in source, context, body.Width, body.Height, playerNames, out turn)) return false;
        capture = new Capture(terrain, source, peers[..count], players, weather, danger, stinky,
            status, conditions, playerDanger, tiles, phase, replication,
            terrain.TileType((int)(source.PositionX + body.Width / 2) / 16,
                (int)(source.PositionY + body.Height + 1f) / 16) == 666, bloodMoonOwner, turn);
        return capture.IsCurrent;
    }

    private static bool TryCaptureTurn(VanillaGroundCritterTerrain1458 terrain, in NpcSnapshot source,
        VanillaNpcBehaviorContext context, int width, int height, IRuntimePlayerPreludeNameLookup1458? names,
        out TurnCapture? turn)
    {
        turn = null;
        byte fallback = byte.MaxValue, selected = byte.MaxValue;
        float best = -1f;
        // Player.FindClosest uses integer half dimensions and includes ghosts;
        // an all-dead census falls back to its first active physical slot.
        foreach (var candidate in context.Candidates)
        {
            if (candidate.Slot == byte.MaxValue) continue;
            if (!context.TryCaptureRawPlayer(candidate.Slot, out var raw)) return false;
            if (!raw.Facts.Active) continue;
            if (candidate.Slot < fallback) fallback = candidate.Slot;
            if (raw.Facts.Dead) continue;
            float distance = MathF.Abs(raw.Facts.PositionX + raw.Facts.Width / 2 - (source.PositionX + width / 2)) +
                MathF.Abs(raw.Facts.PositionY + raw.Facts.Height / 2 - (source.PositionY + height / 2));
            if (best < 0f || distance < best || distance == best && candidate.Slot < selected)
            { best = distance; selected = candidate.Slot; }
        }
        if (selected == byte.MaxValue) selected = fallback == byte.MaxValue ? (byte)0 : fallback;
        if (!context.TryCaptureRawPlayer(selected, out var player)) return false;
        float luck = 0f;
        string name = "";
        if (player.LivePlayer is { } live)
        {
            luck = live.Luck;
            if (!float.IsFinite(luck) || names is null || !names.TryGetPreludePlayerName(live.Player, out name))
                return false;
        }
        else if (player.Facts.Active) return false;
        if (!terrain.TryCanHit(source.PositionX, source.PositionY, width, height, player.Facts, out bool canHit))
            return false;
        var facts = new GroundCritterTurnFacts1458(canHit, luck,
            VanillaNonresidentGroundCritterCatalog1458.IsSpecialTurningName(name));
        turn = new(context, names, player, name, facts);
        return turn.IsCurrent;
    }

    private sealed record TurnCapture(VanillaNpcBehaviorContext Context, IRuntimePlayerPreludeNameLookup1458? Names,
        NpcRawPlayerSlotSnapshot1458 Player, string Name, GroundCritterTurnFacts1458 Facts)
    {
        internal bool IsCurrent
        {
            get
            {
                if (!Context.IsRawPlayerCurrent(Player)) return false;
                if (Player.LivePlayer is not { } live) return !Player.Facts.Active;
                return Names is not null && Names.TryGetPreludePlayerName(live.Player, out string current) && current == Name;
            }
        }
    }

    private sealed class Capture(VanillaGroundCritterTerrain1458 terrain, NpcSnapshot source,
        NpcSnapshot[] peers, RuntimeTownPlayerDanger1458[] players, RuntimeTownNpcScheduleConditions1458 weather,
        RuntimeTownNpcDanger1458 danger, bool selfStinky, RuntimeNpcBuffStatus1458 status,
        Func<NpcSnapshot, RuntimeTownNpcScheduleConditions1458> weatherOwner,
        Func<RuntimeTownPlayerDanger1458[]> playerOwner, WorldTileStore tiles, RuntimeNpcBuffPlan1458 phase,
        RuntimeNpcReplicationRegistry? replication, bool repeatedStinky, Func<bool?>? bloodMoonOwner, TurnCapture? turn)
        : IVanillaGroundCritterCapturedWorld1458, IRuntimeGemBunnyStatusPhase1458
    {
        private readonly RuntimeNpcBuffPlan1458 afterAi = repeatedStinky
            ? RuntimeNpcBuffStatus1458.PlanGemBunnyRepeatedStinky(in phase) : phase;
        private RuntimeNpcStinkyVisualOffer1458 visual;
        private RuntimeNpcBuffPlan1458? collisionPhase;
        private byte[]? collisionFrame;
        public bool ShelterWeather => weather.ReturnHomeRequested;
        public GroundCritterDanger1458 Danger => new(danger.WithinRange, danger.ThreatDirection, selfStinky);
        public bool TryGetSquirrelTurnFacts(out GroundCritterTurnFacts1458 facts)
        {
            facts = turn?.Facts ?? default;
            return turn is not null && IsCurrent;
        }
        public bool TryFinishPresentation(in NpcStateUpdate physics, out NpcStateUpdate presented) =>
            VanillaGroundCritterPresentation1458.TryFinish(in physics, out presented) && IsCurrent;
        public bool TryPrepareFighterTerrain(in NpcStateUpdate planned, in VanillaNpcRawPlayer1458 target)
        {
            if (!IsCurrent || !SupportsCollision(in planned) ||
                !VanillaNpcDefinitionCatalog.TryGet(new(planned.Type), new(planned.NetId), out var definition) ||
                !definition.TryResolveHitbox(planned.Simulation, out var body)) return false;
            return terrain.TryCanHit(planned.PositionX, planned.PositionY, body.Width, body.Height, in target, out _) && IsCurrent;
        }
        public bool IsCurrent =>
            (turn?.IsCurrent ?? true) &&
            (!TerraRuntime.Gameplay.Npcs.VanillaNonresidentGroundCritterCatalog1458.IsEvilConvertible(source.Type) ||
                bloodMoonOwner?.Invoke() is false) &&
            terrain.IsCurrent && status.IsCurrentGemBunnyPhase(in phase) && weatherOwner(source) == weather &&
            playerOwner().AsSpan().SequenceEqual(players) && status.TryGetStinky(source.Handle, out bool stinky) &&
            stinky == selfStinky && CurrentDanger() && terrain.IsCurrent;
        private bool CurrentDanger() => source.Type is 616 or 617 or 625 ||
            RuntimeTownNpcDangerScanner1458.TryScan(tiles, in source, peers, players,
                status, false, false, out var current) && current == danger;
        public GroundCritterTile1458 Tile(int x, int y) => terrain.Tile(x, y);
        public bool NavigationObstacle(int left, int right, int top, int bottom) => terrain.NavigationObstacle(left, right, top, bottom);
        public bool Drowning(float x, float y, int width, int height) => terrain.Drowning(x, y, width, height);
        public bool TryGetWaterLine(int centerTileX, int centerTileY, out bool found, out float waterLineHeight) =>
            terrain.TryGetWaterLine(centerTileX, centerTileY, out found, out waterLineHeight);
        public GroundCritterStep1458 StepDown(float x, float y, float vx, float vy, int width, int height) =>
            terrain.StepDown(x, y, vx, vy, width, height);
        public GroundCritterStep1458 StepUp(float x, float y, float vx, float vy, int width, int height, bool matchPlatforms) =>
            terrain.StepUp(x, y, vx, vy, width, height, matchPlatforms);
        public void FindRestingSpot(int homeX, int homeY, bool dayTime, out int x, out int y) =>
            terrain.FindRestingSpot(homeX, homeY, dayTime, peers, out x, out y);
        public bool TryStageStatus(TerraRuntime.Gameplay.Npcs.IVanillaNpcRandom random)
        {
            if (!IsCurrent) return false;
            visual = RuntimeNpcBuffStatus1458.PlanVisualOffers(in phase, new NpcRuntimeTownCombatRandom1458(random), false);
            return IsCurrent;
        }
        public bool SupportsCollision(in NpcStateUpdate state)
        {
            if (!TerraRuntime.Gameplay.Npcs.VanillaNpcDefinitionCatalog.TryGet(new(state.Type), new(state.NetId), out var definition) ||
                !definition.TryResolveHitbox(state.Simulation, out var body)) return false;
            terrain.FenceBody(state.PositionX, state.PositionY, state.VelocityX, state.VelocityY, body.Width, body.Height);
            // UpdateCollision invokes StepConveyorBelt for every lifeMax5/damage0 critter.
            // Its shared edge/line collision producer is not admitted yet; reject before adoption.
            return !terrain.HasConveyorAtBody(state.PositionX, state.PositionY, body.Width, body.Height) &&
                (!VanillaWorldCollision.TryGetWetContact(tiles, state.PositionX, state.PositionY, body.Width, body.Height,
                    out var liquid) || liquid != WorldLiquidKind.Shimmer) && IsCurrent;
        }
        public bool TryAdoptStatus(in NpcSnapshot accepted) => TryAdoptStatus(in accepted, null);
        public bool TryAdoptStatus(in NpcSnapshot accepted, RuntimeNpcBuffAdditionPlan1458? addition) =>
            status.TryAdoptGemBunnyPhase(collisionPhase ?? afterAi, in accepted, addition) && status.ObserveVisualOffer(accepted.Handle, in visual);
        public bool TryStageWetRemoval(in NpcStateUpdate collision, RuntimeNpcBuffAdditionPlan1458? addition = null)
        {
            if (!IsCurrent || collisionPhase.HasValue) return false;
            var original = addition is null ? afterAi.Slots : addition.Slots;
            var removed = RuntimeNpcBuffStatus1458.PlanGemBunnyWetRemoval(in original, phase.Stinky, collision.Simulation.Wet);
            bool changed = false;
            for (int i = 0; i < RuntimeNpcBuffStatus1458.Capacity; i++) changed |= original[i] != removed[i];
            if (changed && !RuntimeNpcBuffStatus1458.TryPrepareGemBunnyBuffFrame(source.Handle.Slot, in removed, out collisionFrame))
                return false;
            collisionPhase = afterAi with { Slots = removed };
            if (addition is not null) addition.Slots = removed;
            return IsCurrent;
        }
        public byte[]? CollisionBuffFrame => collisionFrame;
        public void PublishCollisionStatus()
        {
            if (collisionFrame is not null) replication?.PublishGemBunnyBuffs(collisionFrame);
        }
        public void PublishStatus()
        {
            if (ExpiredBuffFrame is { } frame) replication?.PublishGemBunnyBuffs(frame);
        }
        public void PublishAiStatus()
        {
            if (AiBuffFrame is { } frame) replication?.PublishGemBunnyBuffs(frame);
        }
        public byte[]? AiBuffFrame
        {
            get
            {
                var original = phase.Slots; var added = afterAi.Slots;
                bool changed = false;
                for (int i = 0; i < RuntimeNpcBuffStatus1458.Capacity; i++) changed |= original[i] != added[i];
                return changed && RuntimeNpcBuffStatus1458.TryPrepareGemBunnyBuffFrame(source.Handle.Slot,
                    in added, out var frame) ? frame : null;
            }
        }
        public byte[]? ExpiredBuffFrame
        {
            get
            {
                var slots = phase.Slots;
                return phase.Expired && RuntimeNpcBuffStatus1458.TryPrepareGemBunnyBuffFrame(source.Handle.Slot,
                    in slots, out var frame) ? frame : null;
            }
        }
        public bool TryPlanLavaAddition(in NpcSnapshot accepted, out RuntimeNpcBuffAdditionPlan1458 addition, out byte[]? frame)
        {
            frame = null;
            if (!status.TryPlanGemBunnyLavaAddition(in afterAi, in accepted, out addition)) return false;
            if (!addition.Changed) return true;
            if (!RuntimeNpcBuffStatus1458.TryPrepareGemBunnyBuffFrame(source.Handle.Slot, in addition.Slots, out var encoded)) return false;
            frame = encoded;
            return true;
        }
    }
}
