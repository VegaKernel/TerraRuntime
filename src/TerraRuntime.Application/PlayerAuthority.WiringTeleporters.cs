using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal readonly record struct WiringPlayerTeleport1458(PlayerHandle Owner, float Time, int Style, int FallStart, int EnvironmentImmunity, bool AwaitingLanding = false);

internal sealed partial class PlayerAuthority
{
    private readonly WiringPlayerTeleport1458[] wiringTeleports = new WiringPlayerTeleport1458[256];
    internal WiringPlayerTeleport1458 CaptureWiringTeleport(PlayerHandle player) =>
        wiringTeleports[player.Slot.Value].Owner == player ? wiringTeleports[player.Slot.Value] : default;

    internal bool TryWiringTeleport(in PlayerStateSnapshot before, int dx, int dy, VanillaUnifiedRandom1458 random, Func<bool> current)
    {
        if (before.IsDead || before.HasMount || !TryGet(before.Player, out var member) || member.CaptureSnapshot() != before ||
            npcHealthGrapplingFacts?.Invoke(before.Player) != false) return false;
        float x = before.PositionX + dx, y = before.PositionY + dy;
        var source = random.Clone(); var prepared = source.Clone();
        var history = CaptureWiringTeleport(before.Player);
        events?.PreparePlayerTeleport(member.Connection, x, y);
        VanillaWiringTeleport1458.ConsumePlayerEffects(new SystemVanillaNpcRandom(prepared), 20, 42, history.Time);
        if (!current() || member.CaptureSnapshot() != before || !membership.IsCurrent(member.Connection) ||
            !random.HasSameState(source) || !member.TryAdvanceRevision()) return false;
        member.PositionX = x; member.PositionY = y;
        wiringTeleports[before.Player.Slot.Value] = new(before.Player, 1f, 0, (int)(y / 16f), 4, AwaitingLanding: true);
        random.CopyStateFrom(prepared);
        events?.PlayerTeleported(member.Connection, x, y, 0, false);
        return true;
    }

    internal void TickWiringTeleportPresentation(VanillaUnifiedRandom1458 random)
    {
        for (int slot = 0; slot < 255; slot++)
        {
            var state = wiringTeleports[slot];
            if (!state.Owner.IsAssigned) continue;
            if (!TryGet(state.Owner, out var player)) { wiringTeleports[slot] = default; continue; }
            if (player.IsDead) continue;
            if (state.Time > 0f && state.Style == 0) _ = random.Next(100);
            wiringTeleports[slot] = state with { Time = state.Time > 0f ? state.Time - .005f : state.Time,
                EnvironmentImmunity = Math.Max(0, state.EnvironmentImmunity - 1) };
        }
    }
}
