using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed class VanillaGemBunnyEnvironment1458(
    WorldTileStore tiles, RuntimeNpcStore npcs, RuntimeNpcBuffStatus1458 status,
    Func<NpcSnapshot, RuntimeTownNpcScheduleConditions1458> conditions,
    Func<RuntimeTownPlayerDanger1458[]> playerDanger,
    RuntimeNpcReplicationRegistry? replication = null) : IVanillaGemBunnyEnvironment1458
{
    public bool TryCapture(in NpcSnapshot source, VanillaNpcBehaviorContext context,
        out IVanillaGemBunnyCapturedWorld1458 capture)
    {
        capture = default!;
        var peers = new NpcSnapshot[npcs.Capacity];
        int count = npcs.CopyActive(peers);
        var players = playerDanger();
        var weather = conditions(source);
        if (!npcs.TryGet(source.Handle, out var retained) ||
            !status.TryPlanGemBunnyPhase(in retained, out var phase)) return false;
        if (weather.DayTime != context.DayTime || weather.Eclipse != context.EclipseActive ||
            weather.SlimeRain != context.SlimeRainActive || !status.TryGetStinky(source.Handle, out bool stinky) ||
            !RuntimeTownNpcDangerScanner1458.TryScan(tiles, in source, peers.AsSpan(0, count), players,
                status, false, false, out var danger)) return false;
        var terrain = new VanillaGemBunnyTerrain1458(tiles);
        // Source danger LOS cannot leave its 200-pixel scan radius. Capture the actual shared
        // LOS read envelope before retaining any random-dependent AI proposal.
        terrain.FenceBody(source.PositionX - 224f, source.PositionY - 224f, 0f, 0f, 480, 480);
        if (!TerraRuntime.Gameplay.Npcs.VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(source.Simulation, out var body) ||
            VanillaWorldCollision.TryGetWetContact(tiles, source.PositionX, source.PositionY, body.Width, body.Height,
                out var liquid) && liquid == WorldLiquidKind.Shimmer) return false;
        capture = new Capture(terrain, source, peers[..count], players, weather, danger, stinky,
            status, conditions, playerDanger, tiles, phase, replication,
            terrain.TileType((int)(source.PositionX + body.Width / 2) / 16,
                (int)(source.PositionY + body.Height + 1f) / 16) == 666);
        return capture.IsCurrent;
    }

    private sealed class Capture(VanillaGemBunnyTerrain1458 terrain, NpcSnapshot source,
        NpcSnapshot[] peers, RuntimeTownPlayerDanger1458[] players, RuntimeTownNpcScheduleConditions1458 weather,
        RuntimeTownNpcDanger1458 danger, bool selfStinky, RuntimeNpcBuffStatus1458 status,
        Func<NpcSnapshot, RuntimeTownNpcScheduleConditions1458> weatherOwner,
        Func<RuntimeTownPlayerDanger1458[]> playerOwner, WorldTileStore tiles, RuntimeNpcBuffPlan1458 phase,
        RuntimeNpcReplicationRegistry? replication, bool repeatedStinky)
        : IVanillaGemBunnyCapturedWorld1458, IRuntimeGemBunnyStatusPhase1458
    {
        private readonly RuntimeNpcBuffPlan1458 afterAi = repeatedStinky
            ? RuntimeNpcBuffStatus1458.PlanGemBunnyRepeatedStinky(in phase) : phase;
        private RuntimeNpcStinkyVisualOffer1458 visual;
        private RuntimeNpcBuffPlan1458? collisionPhase;
        private byte[]? collisionFrame;
        public bool ShelterWeather => weather.ReturnHomeRequested;
        public GemBunnyDanger1458 Danger => new(danger.WithinRange, danger.ThreatDirection, selfStinky);
        public bool IsCurrent => terrain.IsCurrent && status.IsCurrentGemBunnyPhase(in phase) && weatherOwner(source) == weather &&
            playerOwner().AsSpan().SequenceEqual(players) && status.TryGetStinky(source.Handle, out bool stinky) &&
            stinky == selfStinky && RuntimeTownNpcDangerScanner1458.TryScan(tiles, in source, peers, players,
                status, false, false, out var current) && current == danger && terrain.IsCurrent;
        public GemBunnyTile1458 Tile(int x, int y) => terrain.Tile(x, y);
        public bool NavigationObstacle(int left, int right, int top, int bottom) => terrain.NavigationObstacle(left, right, top, bottom);
        public bool Drowning(float x, float y, int width, int height) => terrain.Drowning(x, y, width, height);
        public GemBunnyStep1458 StepDown(float x, float y, float vx, float vy, int width, int height) =>
            terrain.StepDown(x, y, vx, vy, width, height);
        public GemBunnyStep1458 StepUp(float x, float y, float vx, float vy, int width, int height, bool matchPlatforms) =>
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
