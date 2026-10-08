using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ProjectileItemUseAppearance1458Tests
{
    [Fact]
    public void Packet4_during_shot_planning_preserves_new_unlock_and_refuses_stale_player_capture()
    {
        foreach (bool master in new[] { false, true })
        {
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            using var session = new PlayerJoinSession(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9901), session.Handle);
            var players = new PlayerAuthority(null, null, expertMode: true, masterMode: master);
            Assert.True(players.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
                new PlayerSpawnCommitRequest(session.Slot, 100, 100, 0, 0, 0, 0, 0))));
            var appearance = new PlayerAppearanceCommitRequest(connection.Player.Slot, 0, 0, 0f,
                0, "Appearance guard", 0, 0, 0, default, default, default, default, default, default,
                default, VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag, 0, 0);
            Assert.True(players.TryApply(new PlayerAppearanceRuntimeCommand(connection, appearance)));
            SetItem(0, 95, 1);
            SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 4915, 5);
            SetItem((short)(VanillaPlayerItemSlotCatalog.ArmorStart + (master ? 9 : 8)), 156, 1);
            Assert.True(players.TryApply(new PlayerMovementRuntimeCommand(connection,
                new PlayerMovementCommitRequest(connection.Player.Slot, 0, 0, 0, 0, 0, 100f, 100f,
                    false, 0f, 0f, false, 0, false, 0f, 0f, 0f, 0f, false, 0f, 0f))));
            Assert.True(players.TryCaptureCombatSnapshot(connection, out var initialCombat));
            Assert.Equal(1, initialCombat.Defense);

            var random = new VanillaUnifiedRandom1458(1458);
            var beforeRandom = random.Clone();
            var replication = new RuntimeProjectileReplicationRegistry();
            var projectiles = new RuntimeProjectileStore(4, replication);
            PlayerStateSnapshot? changedPlayer = null;
            Action? duringTick = () =>
            {
                // Real packet-4 ingress advances the active member revision before storing appearance.
                Assert.True(players.TryApply(new PlayerAppearanceRuntimeCommand(connection,
                    appearance with { DifficultyFlags = 0 })));
                Assert.True(players.TryCapture(connection.Player, out var snapshot));
                changedPlayer = snapshot;
            };
            var authority = new ProjectileAuthority(projectiles, players, new RuntimeNpcStore(),
                new RuntimePlayerSnapshotLookup(players, null), null, replication,
                () => { var callback = duringTick; duringTick = null; callback?.Invoke(); return 100; },
                projectileRandom: random);
            var packet = new TerrariaProjectileUpdateState(
                new TerrariaProjectileKeyState(connection.Player.Slot.Value, 701, 1),
                14, 120f, 100f, 10.5f, 0f, 0f, 0f, 0f, 0, 22, 5f, 0);
            Assert.True(authority.TryApply(new ClientProjectileUpdateRuntimeCommand(connection, packet)));
            Assert.NotNull(changedPlayer);
            Assert.Equal(0, projectiles.ActiveCount);
            Assert.Equal(0, authority.PromotedClientProjectileSpawns);
            Assert.True(random.HasSameState(beforeRandom));
            Assert.True(players.TryGetInventoryItem(connection, VanillaPlayerItemSlotCatalog.AmmoSlotStart, out var ammo));
            Assert.Equal(5, ammo.Stack);
            Assert.True(players.TryCapture(connection.Player, out var finalPlayer));
            Assert.Equal(changedPlayer, finalPlayer);
            Assert.True(players.TryCaptureCombatSnapshot(connection, out var finalCombat));
            Assert.Equal(master ? 1 : 0, finalCombat.Defense);

            // Refusal does not consume cadence or projectile generation; a fresh capture is accepted.
            Assert.True(authority.TryApply(new ClientProjectileUpdateRuntimeCommand(connection, packet)));
            Assert.Equal(1, authority.PromotedClientProjectileSpawns);
            Assert.True(projectiles.TryGetActive(0, out var shot));
            Assert.True(projectiles.IsCombatTrusted(shot.Handle));
            Assert.Equal(new ProjectileGeneration(1), shot.Handle.Generation);
            Assert.True(random.HasSameState(beforeRandom));
            Assert.True(players.TryGetInventoryItem(connection, VanillaPlayerItemSlotCatalog.AmmoSlotStart, out ammo));
            Assert.Equal(4, ammo.Stack);

            void SetItem(short slot, short type, short stack) => Assert.True(players.TryApply(
                new PlayerEquipmentRuntimeCommand(connection,
                    new PlayerEquipmentCommitRequest(connection.Player.Slot, slot, stack, 0, type, 0))));
        }
    }
}
