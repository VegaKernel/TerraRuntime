using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal sealed partial class VanillaNpcPhaseAccepted1458
{
    private static bool TryStepGroundCritter(in NpcSnapshot source, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, IVanillaNpcPhaseWorld1458 world,
        IVanillaNpcRandom random, out NpcStateUpdate next)
    {
        next = default;
        if (!VanillaNpcTransformation1458.TryGetGroundCritterFamily(source.TypeIdentity, out var family) ||
            source.Simulation.HomeTileX is not { } homeX || source.Simulation.HomeTileY is not { } homeY ||
            source.Simulation.Homeless is not { } homeless || source.Simulation.Friendly is not { } friendly ||
            !definition.TryResolveHitbox(source.Simulation, out var body)) return false;
        var staged = source;
        bool force = false;
        if (source.Target == byte.MaxValue)
        {
            if (!context.TrySelectRawClosestTarget(in source, in definition, out var selection) ||
                !context.TryCaptureRawPlayer((byte)selection.Target, out var player)) return false;
            int direction = selection.DirectionX;
            if (source.PositionX < player.Facts.PositionX) direction = 1;
            if (source.PositionX > player.Facts.PositionX) direction = -1;
            force = selection.Target != source.Target || direction != source.Simulation.DirectionX;
            if (homeX == -1) homeX = (int)((source.PositionX + body.Width / 2) / 16f);
            staged = source with { Target = selection.Target, Simulation = source.Simulation with
                { DirectionX = direction, SpriteDirection = direction } };
        }
        else if (homeX == -1 && homeY == -1 && source.VelocityY == 0f)
        {
            homeX = (int)(source.PositionX + body.Width * .5f) / 16;
            homeY = (int)(source.PositionY + body.Height + 4f) / 16;
        }
        int widthTiles = (int)(context.WorldWidthPixels / 16d);
        if (!VanillaNpcGravity.TryApply(in definition, source.PositionY, source.VelocityY,
                source.Simulation.Wet, source.Simulation.LiquidContact, widthTiles,
                context.WorldSurfacePixels / 16d, out var gravity)) return false;
        world.FindRestingSpot(homeX, homeY, context.DayTime, out int restingX, out int restingY);
        var ai = new VanillaGroundCritterAi1458
        {
            Family = family, PreservePassiveAi3History = true, X = source.PositionX, Y = source.PositionY,
            Vx = source.VelocityX, Vy = source.VelocityY, Width = body.Width, Height = body.Height,
            HomeX = homeX, HomeY = homeY, RestingX = restingX, RestingY = restingY,
            Homeless = homeless, Direction = staged.Simulation.DirectionX,
            Life = source.Simulation.Life, LifeMax = source.Simulation.LifeMax,
            Wet = source.Simulation.Wet, Friendly = friendly, CollideX = source.Simulation.CollideX,
            WorldWidthTiles = widthTiles, Ai = source.Ai, Local = source.Simulation.LocalAi,
            OldX = source.Simulation.OldPositionX, OldY = source.Simulation.OldPositionY,
            OldVx = source.Simulation.OldVelocityX, OldVy = source.Simulation.OldVelocityY,
            Gravity = gravity.Parameters.Gravity, ShelterWeather = world.ShelterWeather
        };
        if (!ai.TryStep(world, world.Danger, random)) return false;
        next = new(source.Type, source.NetId, ai.X, ai.Y, ai.Vx, ai.Vy, staged.Target, ai.Ai,
            staged.Simulation with { DirectionX = ai.Direction, DirectionY = -1,
                HomeTileX = homeX, HomeTileY = homeY, Homeless = homeless,
                LocalAi = ai.Local, DontTakeDamage = false })
            { VanillaNetworkPhase = true, RequiresImmediateSync = force || ai.Force };
        return true;
    }
}
