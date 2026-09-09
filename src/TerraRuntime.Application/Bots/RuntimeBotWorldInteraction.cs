using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

using static TerraRuntime.Application.Bots.BotPolicy;

namespace TerraRuntime.Application.Bots;

internal sealed class RuntimeBotWorldInteraction(
    BotState bot, ServerPlayerAuthority serverPlayers, WorldTileStore worldTiles, WorldTileAuthority tileAuthority,
    WorldRuntimeIdentity world, RuntimeBotMining? mining = null)
    : IRuntimeBotWorldInteraction
{
    public RuntimeBotActionResult Mine(in RuntimeBotObservationSnapshot observation) => mining?.Tick(observation)
        ?? RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.UnsupportedAction);
    public void FinishMining(bool failed) => mining?.Finish(failed);

    public RuntimeBotActionResult AssistMining(in RuntimeBotObservationSnapshot observation)
    {
        if (!RuntimeBotObservationScope.IsCurrent(bot, world, observation))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.StaleDecision);
        if (observation.TargetPlayer is not PlayerStateSnapshot target)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.TargetUnavailable);
        if (worldTiles.WorldSurfaceTiles is not double surface)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.UnsupportedAction);
        if (observation.Self.PositionY <= surface * 16d || target.PositionY <= surface * 16d)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.PermissionDenied);
        return TryAssistMining(bot, observation.Self, target, observation.Tick);
    }

    private RuntimeBotActionResult TryAssistMining(BotState bot, in PlayerStateSnapshot self, in PlayerStateSnapshot target, long tick)
    {
        if (tick < bot.UseItemUntilTick || worldTiles.WorldSurfaceTiles is not double surface ||
            self.PositionY <= surface * 16d || target.PositionY <= surface * 16d) return RuntimeBotActionResult.Pending();
        float x = self.PositionX + 10f, y = self.PositionY + 21f;
        float dx = target.PositionX - self.PositionX, dy = target.PositionY - self.PositionY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance < 24f || distance > 160f || BotTraversal.ClearLeg(worldTiles, x, y, x + dx, y + dy)) return RuntimeBotActionResult.Pending();
        for (float step = 16f; step <= Math.Min(64f, distance); step += 8f)
        {
            float cx = x + dx / distance * step, cy = y + dy / distance * step;
            for (int tx = (int)((cx - 10f) / 16f); tx <= (int)((cx + 9f) / 16f); tx++)
            for (int ty = (int)((cy - 21f) / 16f); ty <= (int)((cy + 20f) / 16f); ty++)
            {
                if (tx < 0 || ty < 0 || tx >= worldTiles.Dimensions.WidthTiles || ty >= worldTiles.Dimensions.HeightTiles)
                    return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.PermissionDenied);
                if (!worldTiles.Get(tx, ty).IsActive) continue;
                if (tileAuthority.TryAssistUndergroundMining(serverPlayers, bot.ServerPlayerId, target.Player, tx, ty))
                {
                    bot.UseItemUntilTick = tick + 12;
                    return RuntimeBotActionResult.Success;
                }
                return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.PermissionDenied); // Never mine behind a denied obstruction.
            }
        }
        return RuntimeBotActionResult.Pending();
    }

}
