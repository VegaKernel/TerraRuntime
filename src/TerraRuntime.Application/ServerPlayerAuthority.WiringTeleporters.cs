using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    internal Action<PlayerStateSnapshot, float, float>? WeightedTeleportContact { get; set; }
    private readonly WiringPlayerTeleport1458[] wiringTeleports = new WiringPlayerTeleport1458[256];
    internal WiringPlayerTeleport1458 CaptureWiringTeleport(PlayerHandle player) =>
        wiringTeleports[player.Slot.Value].Owner == player ? wiringTeleports[player.Slot.Value] : default;

    internal bool TryWiringTeleport(in PlayerStateSnapshot before, int dx, int dy, VanillaUnifiedRandom1458 random,
        RuntimeProjectileStore projectiles, Func<bool> current)
    {
        if (before.IsDead || before.HasMount || before.Revision.Value == ulong.MaxValue || !states.TryGet(before.Player, out var actual) || actual != before) return false;
        float x = before.PositionX + dx, y = before.PositionY + dy;
        var source = random.Clone(); var prepared = source.Clone();
        var history = CaptureWiringTeleport(before.Player);
        VanillaWiringTeleport1458.ConsumePlayerEffectSide(new SystemVanillaNpcRandom(prepared), 20, 42, history.Time);
        if (!current() || !states.TryGet(before.Player, out actual) || actual != before || !random.HasSameState(source)) return false;
        PlayerAuthority.RemoveWiringTeleportHooks(projectiles, before.Player);
        int slot = before.Player.Slot.Value;
        if (liquidOwners[slot] == before.Player && liquidContacts[slot].Shimmer)
            liquidContacts[slot] = liquidContacts[slot] with { Shimmer = false, Wet = false };
        random.CopyStateFrom(prepared);
        WeightedTeleportContact?.Invoke(before, before.PositionX, before.PositionY);
        if (!states.TrySetMotion(before.Player, x, y, before.VelocityX, before.VelocityY, out var committed))
            throw new InvalidOperationException("Prepared teleport motion changed during source contact continuation.");
        WeightedTeleportContact?.Invoke(before, x, y);
        VanillaWiringTeleport1458.ConsumePlayerEffectSide(new SystemVanillaNpcRandom(random), 20, 42, history.Time);
        wiringTeleports[before.Player.Slot.Value] = new(before.Player, 1f, 0, (int)(y / 16f), 4);
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
