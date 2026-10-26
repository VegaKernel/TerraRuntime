using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldMutationPlan
    {
        private readonly List<NpcAuthority.LarvaBossPreparation> larvaBosses = [];
        internal bool TryStageLarvaBoss(int frameX,int frameY,ReadOnlySpan<ToolTreeActor1458> actors)
        {
            if (sealedPlan || !IsCurrentOwned || publicationOrder.Count + 2 > ToolWorldTileOverlay1458.MaximumChangedCells ||
                !owner.npcAuthority.TryPrepareLarvaBoss(Overlay,frameX,frameY,actors,SourceRandom,out var boss) || boss is null) return Fail();
            if (boss.Found)
            {
                if (npcSpawns is null)
                {
                    var live=((SystemWorldItemSpawnRandom)owner.worldItemSpawnRandom).SourceRandom;
                    if (!owner.npcs.TryPrepareWorldSpawns(live,SourceRandom,out npcSpawns) || npcSpawns is null) return Fail();
                }
                if (!npcSpawns.TryStageLarvaBoss(boss.BottomX,boss.BottomY,boss.Target,boss.PlayerCount,out bool allocated,out var birth)) return Fail();
                if (allocated) { boss.BindBirth(in birth);publicationOrder.Add(ToolWorldPublicationKind1458.Npc); }
            }
            larvaBosses.Add(boss);
            publicationOrder.Add(ToolWorldPublicationKind1458.LarvaBoss);
            return IsCurrentOwned;
        }
        private bool AreLarvaBossOwnersCurrent => larvaBosses.All(boss=>boss.IsCurrentOwned);
        private void AdoptLarvaBosses() { foreach(var boss in larvaBosses)boss.AdoptUnpublished(); }
        private bool HasSameLarvaBosses(ToolWorldMutationPlan other)
        {
            if (larvaBosses.Count!=other.larvaBosses.Count)return false;
            for(int index=0;index<larvaBosses.Count;index++)
                if(!larvaBosses[index].HasSameSourceEffects(other.larvaBosses[index]))return false;
            return true;
        }
        internal NpcAuthority.LarvaBossPreparation[] CaptureLarvaPublications()=>larvaBosses.ToArray();
    }
    internal sealed partial class ToolWorldPublication1458
    {
        private NpcAuthority.LarvaBossPreparation[] larvaBosses=[];
        private int nextLarva;
        private bool TryPublishLarvaBoss() => nextLarva<larvaBosses.Length && larvaBosses[nextLarva++].Publish();
    }
}
