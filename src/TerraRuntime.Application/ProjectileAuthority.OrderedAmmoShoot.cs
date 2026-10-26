using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    internal void DrainOrderedAmmoBirths()
    {
        Exception? first=null;
        foreach(var paid in players.CaptureReadyPaidAmmoShoots())
        {
            try
            {
                if(!TryCommitPaidAmmoBirth(paid))
                {players.TryAdoptPaidAmmoBirth(paid,true);RejectedClientProjectileProvenance++;RejectedClientUpdates++;}
            }
            catch(Exception error){first??=error;}
        }
        if(first is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
    }

    private bool TryCommitPaidAmmoBirth(RuntimePlayerPaidAmmoShoot1458 paid)
    {
        if(replication is null || !players.IsPaidAmmoShootCurrent(paid) || paid.Reports.Length!=paid.ShotCount ||
            !VanillaProjectileWeaponCombatCatalog.TryGetWeapon(paid.Weapon.ItemType,out var weapon) ||
            !VanillaProjectileWeaponCombatCatalog.TryGetAmmo(weapon.AmmoFamily,paid.AmmoBefore.ItemType,out var ammo))return false;
        var weaponItem=paid.Weapon;var ammoItem=paid.AmmoBefore;var combat=paid.Combat;
        var first=paid.Reports[0];
        var cursor=paid.RandomBefore.Clone();
        AuthoritativeClientProjectileSpawn use;
        ClientProjectileProvenanceResolveResult result;
        if(VanillaOrdinaryBowLaunch1458.Supports(paid.Weapon.ItemType))
            result=TryResolveOrdinaryBowSourceCandidates(paid.Connection,in first,in weaponItem,in weapon,in ammo,
                in ammoItem,in combat,paid.AmmoSlot,paid.AmmoBox,paid.AmmoPotion,
                paid.LaunchPose.PositionX+PlayerAuthority.VanillaBasePlayerWidth*.5f,
                paid.LaunchPose.PositionY+PlayerAuthority.VanillaBasePlayerHeight*.5f,
                paid.SourceTick,cursor,out use,true);
        else
        {
            float dx=first.PositionX-(paid.LaunchPose.PositionX+PlayerAuthority.VanillaBasePlayerWidth*.5f);
            float dy=first.PositionY-(paid.LaunchPose.PositionY+PlayerAuthority.VanillaBasePlayerHeight*.5f);
            result=TryResolveBulletSourceCandidates(paid.Connection,in first,in weaponItem,in weapon,in ammo,
                in ammoItem,in combat,paid.AmmoSlot,paid.AmmoBox,paid.AmmoPotion,
                dx,dy,weapon.ImpossibleSpawnCenterDistancePixels,paid.SourceTick,cursor,out use,true);
        }
        if(result!=ClientProjectileProvenanceResolveResult.Accepted || !cursor.HasSameState(paid.RandomAfter) ||
            use.InventoryMutation is not { } cost || cost.Slot!=paid.AmmoSlot || cost.Item!=paid.AmmoAfter)return false;
        var states=use.VolleyStates??[use.State];
        if(states.Length!=paid.ShotCount)return false;
        Span<RuntimeProjectileStore.SpawnRequest> requests=stackalloc RuntimeProjectileStore.SpawnRequest[states.Length];
        Span<TerrariaProjectileKeyState> keys=stackalloc TerrariaProjectileKeyState[states.Length];
        for(int i=0;i<states.Length;i++)
        {
            var report=paid.Reports[i];
            if(!MatchesBulletReport(in report,in states[i]) || report.Key.Spawner!=paid.Connection.Player.Slot.Value ||
                replication.WireIdentities.TryResolve(report.Key,out _))return false;
            for(int previous=0;previous<i;previous++)if(keys[previous]==report.Key)return false;
            keys[i]=report.Key;requests[i]=new(states[i]);
        }
        if(!projectiles.TryPrepareVanillaSpawnBatch(requests,out var prepared) || prepared is null)return false;
        using(prepared)
        {
            if(!replication.TryPrepareSpawnBirthJournal(prepared.Births,keys,paid.Connection.Source,out var journal) ||
                journal is null || !journal.IsCurrentOwned || !prepared.IsCurrentOwned ||
                !players.IsPaidAmmoShootCurrent(paid) || !prepared.TryCommitUnpublished())return false;
            // The source phase already owns exactly one ammo cost and the full
            // Shoot RNG tail. Body validation consumed a detached clone only.
            if(!players.TryAdoptPaidAmmoBirth(paid))
                throw new InvalidOperationException("Validated paid ammo receipt changed during callback-free adoption.");
            foreach(var birth in prepared.FinalBirths)
                if(!projectiles.TryMarkCombatTrusted(birth.Handle,paid.Connection.Player))
                    throw new InvalidOperationException("Validated paid ammo birth lost its exact generation.");
            if(!journal.TryAdoptBaselines(projectiles))
                throw new InvalidOperationException("Validated paid ammo birth lost its publication ownership.");
            AppliedSpawns+=states.Length;PromotedClientProjectileSpawns+=states.Length;
            if(journal.TryPublish(projectiles))
            {
                using var observers=journal.EnterObserverPublication();
                prepared.TryPublishBirthJournal();
            }
            return true;
        }
    }
}
