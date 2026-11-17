using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    private readonly WiringPlayerTeleport1458[] wiringTeleports = new WiringPlayerTeleport1458[256];
    internal WiringPlayerTeleport1458 CaptureWiringTeleport(PlayerHandle player) =>
        wiringTeleports[player.Slot.Value].Owner == player ? wiringTeleports[player.Slot.Value] : default;

    internal bool TryWiringTeleport(in PlayerStateSnapshot before, int dx, int dy, VanillaUnifiedRandom1458 random, Func<bool> current)
    {
        if (before.IsDead || before.HasMount || !states.TryGet(before.Player, out var actual) || actual != before) return false;
        float x = before.PositionX + dx, y = before.PositionY + dy;
        var source = random.Clone(); var prepared = source.Clone();
        VanillaWiringTeleport1458.ConsumePlayerEffects(new SystemVanillaNpcRandom(prepared), 20, 42, CaptureWiringTeleport(before.Player).Time);
        if (!current() || !states.TryGet(before.Player, out actual) || actual != before || !random.HasSameState(source) ||
            !states.TrySetMotion(before.Player, x, y, before.VelocityX, before.VelocityY, out var committed)) return false;
        wiringTeleports[before.Player.Slot.Value] = new(before.Player, 1f, 0, (int)(y / 16f), 4);
        random.CopyStateFrom(prepared);
        events?.ServerPlayerTeleported(in committed);
        return true;
    }

    internal void TickWiringTeleportPresentation(VanillaUnifiedRandom1458 random)
    {
        for (int slot = 0; slot < 255; slot++)
        {
            var state = wiringTeleports[slot];
            if (!state.Owner.IsAssigned) continue;
            if (!states.TryGet(state.Owner, out var player)) { wiringTeleports[slot] = default; continue; }
            if (player.IsDead) continue;
            if (state.Time > 0f) _ = random.Next(100);
            wiringTeleports[slot] = state with { Time = state.Time > 0f ? state.Time - .005f : state.Time,
                EnvironmentImmunity = Math.Max(0, state.EnvironmentImmunity - 1) };
        }
    }
}
