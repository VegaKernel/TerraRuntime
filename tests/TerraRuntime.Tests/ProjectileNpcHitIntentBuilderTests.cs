using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class ProjectileNpcHitIntentBuilderTests
{
    [Fact]
    public void Player_owned_projectile_resolves_current_generation_into_npc_hit_intent()
    {
        PlayerStateSnapshot owner = CreatePlayer(slot: 4, generation: 9);
        var players = new FixedSlotLookup(owner);
        ProjectileSnapshot projectile = CreateProjectile(spawner: 4, damage: 25, knockBack: 2.5f);
        NpcHandle target = Npc(slot: 7, generation: 3);

        Assert.True(ProjectileNpcHitIntentBuilder.TryCreateNpcHit(
            in projectile,
            target,
            hitDirection: -1,
            players,
            out ProjectileNpcHitIntent intent));

        Assert.True(intent.IsValid);
        Assert.Equal(target, intent.Target);
        Assert.Equal(DamageSourceKind.PlayerProjectile, intent.Source.Kind);
        Assert.Equal(owner.Player, intent.Source.Player);
        Assert.Equal(projectile.Handle, intent.Source.Projectile);
        Assert.Equal(25, intent.BaseDamage);
        Assert.Equal(2.5f, intent.KnockBack);
        Assert.Equal(-1, intent.HitDirection);
        Assert.True(intent.TryCreateDamageRequest(out NpcDamageRequest damage));
        Assert.Equal(target, damage.Target);
        Assert.Equal(intent.Source, damage.Source);
        Assert.Equal(intent.BaseDamage, damage.BaseDamage);
        Assert.Equal(intent.KnockBack, damage.KnockBack);
        Assert.Equal(intent.HitDirection, damage.HitDirection);
    }

    [Fact]
    public void Reused_or_missing_player_slot_cannot_acquire_projectile_provenance()
    {
        PlayerStateSnapshot wrongSlot = CreatePlayer(slot: 5, generation: 12);
        var players = new FixedSlotLookup(wrongSlot, acceptedSlot: new PlayerSlotId(4));
        ProjectileSnapshot projectile = CreateProjectile(spawner: 4, damage: 25, knockBack: 1f);

        Assert.False(ProjectileNpcHitIntentBuilder.TryCreateNpcHit(
            in projectile,
            Npc(slot: 1, generation: 1),
            hitDirection: 1,
            players,
            out _));
    }

    [Theory]
    [InlineData(VanillaProjectileOwnership.ServerOwner, 25, 1f)]
    [InlineData(4, 0, 1f)]
    [InlineData(4, 25, -1f)]
    public void Unsupported_or_invalid_projectile_combat_state_fails_closed(
        byte spawner,
        short damage,
        float knockBack)
    {
        var players = new FixedSlotLookup(CreatePlayer(slot: 4, generation: 1));
        ProjectileSnapshot projectile = CreateProjectile(spawner, damage, knockBack);

        Assert.False(ProjectileNpcHitIntentBuilder.TryCreateNpcHit(
            in projectile,
            Npc(slot: 1, generation: 1),
            hitDirection: 1,
            players,
            out _));
    }

    [Fact]
    public void Projectile_hit_direction_must_be_source_resolved_and_bounded()
    {
        var players = new FixedSlotLookup(CreatePlayer(slot: 4, generation: 1));
        ProjectileSnapshot projectile = CreateProjectile(spawner: 4, damage: 25, knockBack: 1f);

        Assert.False(ProjectileNpcHitIntentBuilder.TryCreateNpcHit(
            in projectile,
            Npc(slot: 1, generation: 1),
            hitDirection: 2,
            players,
            out _));
    }

    private static ProjectileSnapshot CreateProjectile(byte spawner, short damage, float knockBack) =>
        new(
            Handle: new ProjectileHandle(11, new ProjectileGeneration(2)),
            Revision: new ProjectileRevision(3),
            Type: VanillaProjectileIds.WoodenArrowFriendly,
            Spawner: spawner,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 3f,
            VelocityY: 4f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: damage,
            KnockBack: knockBack,
            OriginalDamage: damage);

    private static PlayerStateSnapshot CreatePlayer(byte slot, ulong generation) =>
        new(
            new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(generation)),
            new PlayerStateRevision(1),
            Team: 0,
            ControlFlags: 0,
            MovementFlags: 0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 0,
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            MountType: 0,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            CameraTargetX: 0f,
            CameraTargetY: 0f);

    private static NpcHandle Npc(byte slot, ulong generation) =>
        new(slot, new NpcGeneration(generation));

    private sealed class FixedSlotLookup(
        PlayerStateSnapshot player,
        PlayerSlotId? acceptedSlot = null) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            if (slot == (acceptedSlot ?? player.Player.Slot))
            {
                snapshot = player;
                return true;
            }

            snapshot = default;
            return false;
        }
    }
}
