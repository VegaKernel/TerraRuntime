namespace TerraRuntime.Application;

internal readonly record struct ToolTreeWorldFacts1458(int WorldWidth,
    bool GoodWorld, bool DrunkWorld, bool RemixWorld, bool NotTheBeesWorld,
    bool TenthAnniversaryWorld, bool DontStarveWorld,
    bool DayTime, bool Raining, bool Halloween, bool TooWindyForButterflies)
{
    internal int? HallowBackground { get; init; }
    internal bool? OnlyShimmerOceanWorlds { get; init; }
    // WorldGen.IsPalmOasisTree, Terraria1458; beachDistance is380.
    internal bool IsPalmOasis(int x) => x >= 380 && x <= WorldWidth - 380;
}

internal sealed partial class WorldTileAuthority
{
    internal bool TryCaptureToolTreeWorld(RuntimePlayerToolActorCapture1458 actor,
        out ToolTreeWorldCapture1458? capture)
        => TryCaptureToolTreeWorld(actor, actor.World, out capture);

    // World projectiles capture the same environment without borrowing a human
    // actor's item, phase, inventory or entitlement.
    internal bool TryCaptureToolTreeWorld(out ToolTreeWorldCapture1458? capture)
    {
        capture = null;
        return players.TryCaptureToolSourceWorld(out var world) &&
            TryCaptureToolTreeWorld(null, world, out capture);
    }

    private bool TryCaptureToolTreeWorld(RuntimePlayerToolActorCapture1458? actor,
        RuntimePlayerUpdateWorld1458 world, out ToolTreeWorldCapture1458? capture)
    {
        capture = null;
        if (actor is { IsCurrent: false } || tiles is null || toolClock is null ||
            !players.TryCaptureToolSourceWorld(out var currentWorld) || currentWorld != world ||
            world.MaxTilesX != tiles.Dimensions.WidthTiles || world.MaxTilesY != tiles.Dimensions.HeightTiles ||
            world.GetGoodWorld is not { } good || world.DrunkWorld is not { } drunk ||
            world.NotTheBeesWorld is not { } bees || world.TenthAnniversaryWorld is not { } tenth ||
            world.DontStarveWorld is not { } constant ||
            !npcAuthority.TryCaptureToolSeason(tiles, out var season) || season.TenthAnniversaryWorld != tenth ||
            !float.IsFinite(toolClock.WindSpeedTarget)) return false;
        var facts = new ToolTreeWorldFacts1458(world.MaxTilesX, good, drunk, world.RemixWorld,
            bees, tenth, constant, toolClock.DayTime, toolClock.Raining, season.Halloween,
            MathF.Abs(toolClock.WindSpeedTarget) >= .4f)
        { HallowBackground = world.HallowBackground, OnlyShimmerOceanWorlds = world.OnlyShimmerOceanWorlds };
        capture = new(this, actor, world, toolClock, facts,
            hardModeBaseline || progression.IsCompleted(TerraRuntime.World.VanillaWorldProgressionId.Hardmode));
        return capture.IsCurrent;
    }

    internal sealed class ToolTreeWorldCapture1458(WorldTileAuthority owner,
        RuntimePlayerToolActorCapture1458? actor, RuntimePlayerUpdateWorld1458 world,
        RuntimeWorldClock clock, ToolTreeWorldFacts1458 facts, bool hardMode)
    {
        internal ToolTreeWorldFacts1458 Facts => facts;
        internal RuntimePlayerUpdateWorld1458 World => world;
        internal bool HardMode => hardMode;
        internal bool IsCurrent => (actor is null || actor.IsCurrent) &&
            owner.players.TryCaptureToolSourceWorld(out var currentWorld) && currentWorld == world &&
            (owner.hardModeBaseline || owner.progression.IsCompleted(TerraRuntime.World.VanillaWorldProgressionId.Hardmode)) == hardMode &&
            ReferenceEquals(owner.toolClock, clock) &&
            clock.DayTime == facts.DayTime && clock.Raining == facts.Raining &&
            float.IsFinite(clock.WindSpeedTarget) &&
            (MathF.Abs(clock.WindSpeedTarget) >= .4f) == facts.TooWindyForButterflies &&
            owner.tiles is { } tiles && owner.npcAuthority.TryCaptureToolSeason(tiles, out var season) &&
            season.Halloween == facts.Halloween && season.TenthAnniversaryWorld == facts.TenthAnniversaryWorld;
    }
}

internal sealed partial class PlayerAuthority
{
    internal bool TryCaptureToolSourceWorld(out RuntimePlayerUpdateWorld1458 world)
    {
        world = playerUpdateWorld.GetValueOrDefault();
        return playerUpdateWorld is { IsValid: true };
    }
}
