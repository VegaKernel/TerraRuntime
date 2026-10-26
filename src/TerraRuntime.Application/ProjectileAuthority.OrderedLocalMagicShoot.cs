using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    internal void DrainOrderedLocalMagicBirths()
    {
        Exception? first=null;
        // Freeze every accepted phase afterimage before the first outward
        // observer. Later births cannot refresh stale profile/input/inventory.
        foreach(var prepared in players.PrepareOrderedLocalMagicShoots())
        {
            if(!players.TryTakeOrderedLocalMagicShoot(prepared,out var connection,out var packet,out var pose))
            {RejectedClientProjectileProvenance++;RejectedClientUpdates++;continue;}
            try
            {
                var key=packet.Key;
                if(replication is null ||
                    replication.WireIdentities.TryResolve(in key,out _) ||
                    TryResolveStrictClientProjectileSpawn(connection,in packet,out var use,pose)!=ClientProjectileProvenanceResolveResult.Accepted)
                {RejectedClientProjectileProvenance++;RejectedClientUpdates++;continue;}
                if(use.VolleyStates is not null)
                {
                    // Existing bounded volley owner validates the actual two
                    // source keys; this receipt invents neither keys nor births.
                    if(!TryBeginClientBulletVolley(connection,in packet,in use))
                    {RejectedSpawns++;RejectedClientProjectileProvenance++;RejectedClientUpdates++;continue;}
                    if(prepared.Receipt.PendingSecondReport is { } second && !TryContinuePendingBulletVolley(connection,in second))
                    {RejectedClientProjectileProvenance++;RejectedClientUpdates++;}
                }
                else
                {
                    using var scope=replication.ClientCommitContext.Enter(connection.Source,in key);
                    if(!TryCommitAuthoritativeProjectileUse(in use,null))
                    {RejectedSpawns++;RejectedClientProjectileProvenance++;RejectedClientUpdates++;}
                }
            }
            catch(Exception error){first??=error;}
        }
        if(first is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
    }
}
