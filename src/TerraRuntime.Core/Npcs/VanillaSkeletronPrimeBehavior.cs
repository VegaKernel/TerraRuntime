using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>TerrariaServer 1.4.5.8 aiStyle 32 gameplay state for Skeletron Prime.</summary>
internal sealed class VanillaSkeletronPrimeNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.SkeletronPrime || npc.TypeIdentity != VanillaNpcIds.SkeletronPrime)
        { next = default; return false; }

        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        ushort targetSlot = npc.Target;
        if (ai.Ai0 == 0f) { TryRefresh(in npc, in definition, context, ref targetSlot, out _); ai = ai with { Ai0 = 1f }; }
        bool hasTarget = TryGetTarget(targetSlot, context, out VanillaNpcTargetCandidate target);
        if (!hasTarget || MathF.Abs(npc.PositionX - (target.CenterX - 10f)) > 6000f || MathF.Abs(npc.PositionY - (target.CenterY - 21f)) > 6000f)
        {
            TryRefresh(in npc, in definition, context, ref targetSlot, out target);
            hasTarget = TryGetTarget(targetSlot, context, out target);
            if (!hasTarget || MathF.Abs(npc.PositionX - (target.CenterX - 10f)) > 6000f || MathF.Abs(npc.PositionY - (target.CenterY - 21f)) > 6000f)
                ai = ai with { Ai1 = 3f };
        }
        if (context.DayTime && ai.Ai1 is not 2f and not 3f) ai = ai with { Ai1 = 2f };

        int defense = definition.Defense;
        int damage = definition.Damage;
        int timeLeft = simulation.TimeLeft;
        switch ((int)ai.Ai1)
        {
            case 0:
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 600f) { ai = ai with { Ai2 = 0f, Ai1 = 1f }; TryRefresh(in npc, in definition, context, ref targetSlot, out target); hasTarget = TryGetTarget(targetSlot, context, out target); }
                if (hasTarget) Hover(in npc, in target, context.ExpertMode, ref vx, ref vy);
                break;
            case 1:
                defense *= 2; damage *= 2;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 400f) ai = ai with { Ai2 = 0f, Ai1 = 0f };
                if (hasTarget) Charge(in npc, in target, context.ExpertMode, ref vx, ref vy);
                break;
            case 2:
                defense = 9999; damage = 9999;
                if (hasTarget) Rage(in npc, in target, ref vx, ref vy);
                break;
            case 3:
                if (timeLeft < 0 || timeLeft > 500) timeLeft = 500;
                vy += 0.1f; if (vy < 0f) vy *= 0.95f; vx *= 0.95f;
                break;
        }
        simulation = simulation with { NoGravity = true, NoTileCollide = true, DefenseOverride = defense, DamageOverride = damage, ReflectsProjectiles = false, TimeLeft = timeLeft, JustHit = false };
        next = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, targetSlot, ai, simulation);
        return true;
    }

    private static void Hover(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, bool expert, ref float vx, ref float vy)
    {
        float va = expert ? 0.03f : 0.1f, vm = expert ? 4f : 2f, ha = expert ? 0.07f : 0.1f, hm = expert ? 9.5f : 8f;
        float targetTop = target.CenterY - 21f;
        if (npc.PositionY > targetTop - 200f) { if (vy > 0f) vy *= 0.98f; vy -= va; if (vy > vm) vy = vm; }
        else if (npc.PositionY < targetTop - 500f) { if (vy < 0f) vy *= 0.98f; vy += va; if (vy < -vm) vy = -vm; }
        float cx = npc.PositionX + 40f;
        if (cx > target.CenterX + 100f) { if (vx > 0f) vx *= 0.98f; vx -= ha; if (vx > hm) vx = hm; }
        if (cx < target.CenterX - 100f) { if (vx < 0f) vx *= 0.98f; vx += ha; if (vx < -hm) vx = -hm; }
    }

    private static void Charge(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, bool expert, ref float vx, ref float vy)
    {
        float dx = target.CenterX - (npc.PositionX + 40f), dy = target.CenterY - (npc.PositionY + 51f);
        float d = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy)); float speed = expert ? 6f : 2f;
        if (expert) { if (d > 150f) speed *= 1.05f; for (float t = 200f; t <= 600f; t += 50f) if (d > t) speed *= 1.1f; }
        vx = dx / d * speed; vy = dy / d * speed;
    }

    private static void Rage(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, ref float vx, ref float vy)
    { float dx = target.CenterX-(npc.PositionX+40f), dy=target.CenterY-(npc.PositionY+51f); float d=MathF.Max(0.001f,MathF.Sqrt(dx*dx+dy*dy)); float s=Math.Clamp(10f+d/100f,8f,32f); vx=dx/d*s; vy=dy/d*s; }

    internal static bool TryGetTarget(ushort slot, VanillaNpcBehaviorContext context, out VanillaNpcTargetCandidate target)
    { if (slot<byte.MaxValue && context.TryFindCandidate((byte)slot,out target)&&target.Active&&!target.Dead&&!target.Ghost) return true; target=default; return false; }
    internal static bool TryRefresh(in NpcSnapshot npc,in VanillaNpcDefinition def,VanillaNpcBehaviorContext context,ref ushort slot,out VanillaNpcTargetCandidate target)
    { if(context.TrySelectClosestTarget(in npc,in def,out VanillaBlueSlimeTargetRefresh r)&&r.HasTarget&&r.Target<byte.MaxValue&&context.TryFindCandidate((byte)r.Target,out target)&&target.Active&&!target.Dead&&!target.Ghost){slot=r.Target;return true;} target=default;return false; }
}

/// <summary>Linked aiStyle 33..36 Prime limbs with generation-safe parent ownership and attack-cycle movement.</summary>
internal sealed class VanillaSkeletronPrimeLimbNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc,in VanillaNpcDefinition definition,VanillaNpcBehaviorContext context,INpcAiStateStepper inner,out NpcStateUpdate next)
    {
        _=inner;
        bool supported=npc.TypeIdentity==VanillaNpcIds.PrimeSaw||npc.TypeIdentity==VanillaNpcIds.PrimeVice||npc.TypeIdentity==VanillaNpcIds.PrimeCannon||npc.TypeIdentity==VanillaNpcIds.PrimeLaser;
        if(!supported){next=default;return false;}
        NpcAiState ai=npc.Ai; NpcSimulationState sim=npc.Simulation; float vx=npc.VelocityX,vy=npc.VelocityY; ushort targetSlot=npc.Target;
        if(ai.Ai1<0f||ai.Ai1>byte.MaxValue||!context.TryFindNpcPeer((byte)ai.Ai1,out NpcSnapshot parent)||parent.TypeIdentity!=VanillaNpcIds.SkeletronPrime)
        { ai=ai with{Ai2=ai.Ai2+10f}; if(ai.Ai2>50f) sim=sim with{Life=0,TimeLeft=0}; next=Build(in npc,vx,vy,targetSlot,in ai,in sim);return true; }
        if(parent.Ai.Ai1==3f && (sim.TimeLeft<0||sim.TimeLeft>10)) sim=sim with{TimeLeft=10};
        VanillaSkeletronPrimeNpcBehaviorStrategy.TryGetTarget(targetSlot,context,out VanillaNpcTargetCandidate target);
        if(!target.Active||target.Dead||target.Ghost) VanillaSkeletronPrimeNpcBehaviorStrategy.TryRefresh(in npc,in definition,context,ref targetSlot,out target);
        if(npc.TypeIdentity==VanillaNpcIds.PrimeCannon) StepRanged(in npc,in parent,in target,ref ai,ref sim,ref vx,ref vy,cannon:true);
        else if(npc.TypeIdentity==VanillaNpcIds.PrimeLaser) StepRanged(in npc,in parent,in target,ref ai,ref sim,ref vx,ref vy,cannon:false);
        else StepMelee(in npc,in parent,in target,ref ai,ref vx,ref vy,vice:npc.TypeIdentity==VanillaNpcIds.PrimeVice);
        sim=sim with{NoGravity=true,NoTileCollide=true,JustHit=false}; next=Build(in npc,vx,vy,targetSlot,in ai,in sim); return true;
    }

    private static void StepMelee(in NpcSnapshot npc,in NpcSnapshot parent,in VanillaNpcTargetCandidate target,ref NpcAiState ai,ref float vx,ref float vy,bool vice)
    {
        int state=(int)ai.Ai2;
        if(state==99){Hover(in npc,in parent,ai.Ai0,230f,200f,ref vx,ref vy); if(Distance(npc.PositionX,npc.PositionY,parent.PositionX,parent.PositionY)<400f) ai=ai with{Ai2=0f}; return;}
        if(Distance(npc.PositionX,npc.PositionY,parent.PositionX,parent.PositionY)>800f){ai=ai with{Ai2=99f};return;}
        if(state is 0 or 3)
        {
            if(parent.Ai.Ai1!=0f){ ai=ai with{Ai3=ai.Ai3+1f}; Hover(in npc,in parent,ai.Ai0,-100f,200f,ref vx,ref vy); if(ai.Ai3>=600f) ai=ai with{Ai2=0f,Ai3=0f}; }
            else { float timer=ai.Ai3+1f; float threshold=vice?600f:300f; if(timer>=threshold){state++;timer=0f;} ai=ai with{Ai2=state,Ai3=timer}; Hover(in npc,in parent,ai.Ai0,230f,200f,ref vx,ref vy); }
        }
        else if(state==1)
        {
            if(vice){ if(vy>0f)vy*=0.9f; vy-=0.1f; if(vy<-8f)vy=-8f; }
            else { vx*=0.95f; vy-=0.1f; if(vy<-8f)vy=-8f; }
            if(target.Active&&!target.Dead&&!target.Ghost && npc.PositionY<parent.PositionY-200f){ai=ai with{Ai2=2f};SetToward(in npc,in target,vice?20f:22f,ref vx,ref vy);}
        }
        else if(state==2)
        { if(!target.Active||target.Dead||target.Ghost||npc.PositionY>target.CenterY||vy<0f) ai=ai with{Ai2=3f,Ai3=ai.Ai3+1f}; }
        else if(state==4)
        { vx += 0.1f * -ai.Ai0; vx=Math.Clamp(vx,-8f,8f); if(target.Active&&!target.Dead&&!target.Ghost&&MathF.Abs((npc.PositionX+26f)-(parent.PositionX+40f))>500f){ai=ai with{Ai2=5f};SetToward(in npc,in target,17f,ref vx,ref vy);} }
        else if(state==5 && target.Active && ((vx>0f&&npc.PositionX+26f>target.CenterX)||(vx<0f&&npc.PositionX+26f<target.CenterX))) ai=ai with{Ai2=0f};
    }

    private static void StepRanged(in NpcSnapshot npc,in NpcSnapshot parent,in VanillaNpcTargetCandidate target,ref NpcAiState ai,ref NpcSimulationState sim,ref float vx,ref float vy,bool cannon)
    {
        NpcAiState local=sim.LocalAi;
        if((int)ai.Ai2==0)
        {
            if(parent.Ai.Ai1!=0f){local=local with{Ai0=local.Ai0+(cannon?2f:3f)};Hover(in npc,in parent,ai.Ai0,-100f,200f,ref vx,ref vy);}
            else {float timer=ai.Ai3+1f; float threshold=cannon?1100f:800f; if(timer>=threshold){timer=0f;ai=ai with{Ai2=1f};local=local with{Ai0=0f};} ai=ai with{Ai3=timer};Hover(in npc,in parent,ai.Ai0,cannon?230f:-100f,200f,ref vx,ref vy);}
        }
        else
        {
            float timer=ai.Ai3+1f; float duration=cannon?300f:200f; if(timer>=duration){timer=0f;ai=ai with{Ai2=0f};local=local with{Ai0=0f};} ai=ai with{Ai3=timer};
            if(target.Active&&!target.Dead&&!target.Ghost){float tx=target.CenterX,ty=target.CenterY-(cannon?350f:300f);ApproachPosition(in npc,tx,ty,cannon?6f:7f,0.05f,ref vx,ref vy);}
        }
        local=local with{Ai0=local.Ai0+1f}; sim=sim with{LocalAi=local};
    }

    private static void Hover(in NpcSnapshot npc,in NpcSnapshot parent,float side,float oy,float ox,ref float vx,ref float vy)
    {ApproachPosition(in npc,parent.PositionX+40f+ox*side,parent.PositionY+oy,8f,0.07f,ref vx,ref vy);}
    private static void ApproachPosition(in NpcSnapshot npc,float tx,float ty,float max,float a,ref float vx,ref float vy)
    {float dx=tx-(npc.PositionX+26f),dy=ty-(npc.PositionY+26f);float d=MathF.Max(.001f,MathF.Sqrt(dx*dx+dy*dy));float dvx=dx/d*max,dvy=dy/d*max;Approach(ref vx,dvx,a);Approach(ref vy,dvy,a);}
    private static void Approach(ref float v,float d,float a){if(v<d)v=MathF.Min(v+a,d);else if(v>d)v=MathF.Max(v-a,d);}
    private static void SetToward(in NpcSnapshot npc,in VanillaNpcTargetCandidate t,float speed,ref float vx,ref float vy){float dx=t.CenterX-(npc.PositionX+26f),dy=t.CenterY-(npc.PositionY+26f);float d=MathF.Max(.001f,MathF.Sqrt(dx*dx+dy*dy));vx=dx/d*speed;vy=dy/d*speed;}
    private static float Distance(float x,float y,float xx,float yy){float dx=xx-x,dy=yy-y;return MathF.Sqrt(dx*dx+dy*dy);}
    private static NpcStateUpdate Build(in NpcSnapshot n,float vx,float vy,ushort target,in NpcAiState ai,in NpcSimulationState s)=>new(n.Type,n.NetId,n.PositionX,n.PositionY,vx,vy,target,ai,s);
}
