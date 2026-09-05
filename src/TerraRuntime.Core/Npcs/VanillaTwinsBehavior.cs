using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Server-authoritative TerrariaServer 1.4.5.8 aiStyle 30/31 slice for The Twins.
/// The source phase threshold, transformation timers, hover/charge cadence, phase-two movement and
/// projectile cadence counters are retained. Cosmetic rotation, dust, gore and sound are omitted.
/// </summary>
internal sealed class VanillaTwinNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private readonly bool _spazmatism;

    public VanillaTwinNpcBehaviorStrategy(bool spazmatism) => _spazmatism = spazmatism;

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        NpcTypeId expected = _spazmatism ? VanillaNpcIds.Spazmatism : VanillaNpcIds.Retinazer;
        NpcAiStyleId expectedStyle = _spazmatism ? VanillaNpcAiStyles.Spazmatism : VanillaNpcAiStyles.Retinazer;
        if (npc.TypeIdentity != expected || definition.AiStyle != expectedStyle)
        { next = default; return false; }

        NpcAiState ai = npc.Ai;
        NpcSimulationState sim = npc.Simulation;
        NpcAiState local = sim.LocalAi;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        ushort targetSlot = npc.Target;
        int lifeMax = sim.LifeMax > 0 ? sim.LifeMax : definition.LifeMax;
        int life = sim.LifeMax > 0 ? sim.Life : lifeMax;

        if (!TryGetTarget(targetSlot, context, out VanillaNpcTargetCandidate target))
            TryRefresh(in npc, in definition, context, ref targetSlot, out target);
        bool hasTarget = TryGetTarget(targetSlot, context, out target);

        if (context.DayTime || !hasTarget)
        {
            vy -= 0.04f;
            int tl = sim.TimeLeft;
            if (tl < 0 || tl > 10) tl = 10;
            sim = sim with { TimeLeft = tl, NoGravity = true, NoTileCollide = true, ReflectsProjectiles = false };
            next = Build(in npc, vx, vy, targetSlot, in ai, in sim);
            return true;
        }

        if (ai.Ai0 == 0f)
        {
            if (_spazmatism)
                StepSpazPhaseOne(in npc, in target, context, life, lifeMax, ref ai, ref local, ref vx, ref vy);
            else
                StepRetPhaseOne(in npc, in target, context, life, lifeMax, ref ai, ref local, ref vx, ref vy);

            if (life < lifeMax * 0.4f)
                ai = new NpcAiState(1f, 0f, 0f, 0f);
        }
        else if (ai.Ai0 is 1f or 2f)
        {
            float spin = ai.Ai2;
            spin += ai.Ai0 == 1f ? 0.005f : -0.005f;
            spin = Math.Clamp(spin, 0f, 0.5f);
            float timer = ai.Ai1 + 1f;
            vx *= 0.98f; vy *= 0.98f;
            if (MathF.Abs(vx) < 0.1f) vx = 0f;
            if (MathF.Abs(vy) < 0.1f) vy = 0f;
            if (timer >= 100f)
            {
                float phase = ai.Ai0 + 1f;
                timer = 0f;
                if (phase == 3f) spin = 0f;
                ai = ai with { Ai0 = phase, Ai1 = timer, Ai2 = spin };
            }
            else ai = ai with { Ai1 = timer, Ai2 = spin };
        }
        else
        {
            if (_spazmatism)
                StepSpazPhaseTwo(in npc, in target, context, life, lifeMax, ref ai, ref local, ref vx, ref vy);
            else
                StepRetPhaseTwo(in npc, in target, context, life, lifeMax, ref ai, ref local, ref vx, ref vy);
        }

        int damage = ai.Ai0 >= 3f ? (int)(definition.Damage * 1.5) : definition.Damage;
        int defense = ai.Ai0 >= 3f ? definition.Defense + (_spazmatism ? 18 : 10) : definition.Defense;
        sim = sim with
        {
            NoGravity = true,
            NoTileCollide = true,
            LocalAi = local,
            DamageOverride = damage,
            DefenseOverride = defense,
            ReflectsProjectiles = false,
            JustHit = false
        };
        next = Build(in npc, vx, vy, targetSlot, in ai, in sim);
        return true;
    }

    private static void StepRetPhaseOne(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, VanillaNpcBehaviorContext context,
        int life, int lifeMax, ref NpcAiState ai, ref NpcAiState local, ref float vx, ref float vy)
    {
        if (ai.Ai1 == 0f)
        {
            float speed = context.ExpertMode ? 8.25f : 7f;
            float accel = context.ExpertMode ? 0.115f : 0.1f;
            if (context.GoodWorld) { speed *= 1.15f; accel *= 1.15f; }
            float cx = npc.PositionX + 50f, cy = npc.PositionY + 55f;
            int side = cx < target.CenterX + 10f ? -1 : 1;
            float dx = target.CenterX + side * 300f - cx;
            float dy = target.CenterY - 300f - cy;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            ApproachVector(dx, dy, speed, accel, ref vx, ref vy);
            float timer = ai.Ai2 + 1f;
            float shot = ai.Ai3;
            if (timer >= 600f) { ai = ai with { Ai1 = 1f, Ai2 = 0f, Ai3 = 0f }; return; }
            if (npc.PositionY + 110f < target.CenterY - 21f && distance < 400f)
            {
                shot += 1f;
                if (context.ExpertMode && life < lifeMax * .9f) shot += .3f;
                if (context.ExpertMode && life < lifeMax * .8f) shot += .3f;
                if (context.ExpertMode && life < lifeMax * .7f) shot += .3f;
                if (context.ExpertMode && life < lifeMax * .6f) shot += .3f;
                if (context.GoodWorld) shot += .5f;
                if (shot >= 60f) shot = 0f;
            }
            ai = ai with { Ai2 = timer, Ai3 = shot };
        }
        else if (ai.Ai1 == 1f)
        {
            float speed = context.ExpertMode ? 15f : 12f;
            if (context.GoodWorld) speed += 2f;
            SetToward(npc.PositionX + 50f, npc.PositionY + 55f, target.CenterX, target.CenterY, speed, ref vx, ref vy);
            ai = ai with { Ai1 = 2f };
        }
        else if (ai.Ai1 == 2f)
        {
            float timer = ai.Ai2 + 1f;
            if (timer >= 25f) { vx *= .96f; vy *= .96f; if (MathF.Abs(vx)<.1f)vx=0f; if(MathF.Abs(vy)<.1f)vy=0f; }
            if (timer >= 70f)
            {
                float cycle = ai.Ai3 + 1f;
                timer = 0f;
                ai = ai with { Ai1 = cycle >= 4f ? 0f : 1f, Ai2 = timer, Ai3 = cycle >= 4f ? 0f : cycle };
            }
            else ai = ai with { Ai2 = timer };
        }
        _ = local;
    }

    private static void StepRetPhaseTwo(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, VanillaNpcBehaviorContext context,
        int life, int lifeMax, ref NpcAiState ai, ref NpcAiState local, ref float vx, ref float vy)
    {
        float cx = npc.PositionX + 50f, cy = npc.PositionY + 55f;
        if (ai.Ai1 == 0f)
        {
            float speed = context.ExpertMode ? 9.5f : 8f;
            float accel = context.ExpertMode ? .175f : .15f;
            if (context.GoodWorld) { speed *= 1.15f; accel *= 1.15f; }
            ApproachVector(target.CenterX - cx, target.CenterY - 300f - cy, speed, accel, ref vx, ref vy);
            float timer = ai.Ai2 + 1f;
            if (timer >= 300f) { timer = 0f; ai = ai with { Ai1 = 1f, Ai3 = 0f }; }
            ai = ai with { Ai2 = timer };
            float shots = local.Ai1 + 1f + (life < lifeMax*.75f?1f:0f)+(life<lifeMax*.5f?1f:0f)+(life<lifeMax*.25f?1f:0f)+(life<lifeMax*.1f?2f:0f);
            if (shots > 180f) shots = 0f;
            local = local with { Ai1 = shots };
        }
        else
        {
            int side = cx < target.CenterX + 10f ? -1 : 1;
            float speed = context.ExpertMode ? 9.5f : 8f;
            float accel = context.ExpertMode ? .25f : .2f;
            if (context.GoodWorld) { speed *= 1.15f; accel *= 1.15f; }
            ApproachVector(target.CenterX + side * 340f - cx, target.CenterY - cy, speed, accel, ref vx, ref vy);
            float shots = local.Ai1 + 1f + (life<lifeMax*.75f?.5f:0f)+(life<lifeMax*.5f?.75f:0f)+(life<lifeMax*.25f?1f:0f)+(life<lifeMax*.1f?1.5f:0f)+(context.ExpertMode?1.5f:0f);
            if (shots > 60f) shots = 0f;
            local = local with { Ai1 = shots };
            float timer = ai.Ai2 + 1f;
            if (timer >= 180f) ai = ai with { Ai1 = 0f, Ai2 = 0f, Ai3 = 0f };
            else ai = ai with { Ai2 = timer };
        }
    }

    private static void StepSpazPhaseOne(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, VanillaNpcBehaviorContext context,
        int life, int lifeMax, ref NpcAiState ai, ref NpcAiState local, ref float vx, ref float vy)
    {
        if (ai.Ai1 == 0f)
        {
            float speed = 12f, accel = .4f;
            if (context.GoodWorld) { speed *= 1.15f; accel *= 1.15f; }
            float cx=npc.PositionX+50f, cy=npc.PositionY+55f; int side=cx<target.CenterX+10f?-1:1;
            ApproachVector(target.CenterX+side*400f-cx,target.CenterY-cy,speed,accel,ref vx,ref vy);
            float timer=ai.Ai2+1f,shot=ai.Ai3+1f+(context.ExpertMode&&life<lifeMax*.8f?.6f:0f)+(context.GoodWorld?.4f:0f);
            if(timer>=600f){ai=ai with{Ai1=1f,Ai2=0f,Ai3=0f};return;}
            if(shot>=60f)shot=0f; ai=ai with{Ai2=timer,Ai3=shot};
        }
        else if(ai.Ai1==1f)
        {
            float speed=13f;
            if(context.ExpertMode){if(life<lifeMax*.9f)speed+=.5f;if(life<lifeMax*.8f)speed+=.5f;if(life<lifeMax*.7f)speed+=.55f;if(life<lifeMax*.6f)speed+=.6f;if(life<lifeMax*.5f)speed+=.65f;}
            if(context.GoodWorld)speed*=1.2f; SetToward(npc.PositionX+50f,npc.PositionY+55f,target.CenterX,target.CenterY,speed,ref vx,ref vy); ai=ai with{Ai1=2f};
        }
        else if(ai.Ai1==2f)
        {
            float timer=ai.Ai2+1f; if(timer>=8f){vx*=.9f;vy*=.9f;if(MathF.Abs(vx)<.1f)vx=0f;if(MathF.Abs(vy)<.1f)vy=0f;}
            if(timer>=42f){float cycle=ai.Ai3+1f;ai=ai with{Ai1=cycle>=10f?0f:1f,Ai2=0f,Ai3=cycle>=10f?0f:cycle};}else ai=ai with{Ai2=timer};
        }
        _=local;
    }

    private static void StepSpazPhaseTwo(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, VanillaNpcBehaviorContext context,
        int life, int lifeMax, ref NpcAiState ai, ref NpcAiState local, ref float vx, ref float vy)
    {
        float cx=npc.PositionX+50f,cy=npc.PositionY+55f;
        if(ai.Ai1==0f)
        {
            int side=cx<target.CenterX+10f?-1:1; float dx=target.CenterX+side*180f-cx,dy=target.CenterY-cy;float dist=MathF.Sqrt(dx*dx+dy*dy);float speed=4f,acc=.1f;
            if(context.ExpertMode){if(dist>300)speed+=.5f;if(dist>400)speed+=.5f;if(dist>500)speed+=.55f;if(dist>600)speed+=.55f;if(dist>700)speed+=.6f;if(dist>800)speed+=.6f;}
            if(context.GoodWorld){speed*=1.15f;acc*=1.15f;} ApproachVector(dx,dy,speed,acc,ref vx,ref vy);
            float timer=ai.Ai2+1f;if(timer>=400f){timer=0f;ai=ai with{Ai1=1f,Ai3=0f};}ai=ai with{Ai2=timer};
            float shots=local.Ai1+1f+(life<lifeMax*.75f?1f:0f)+(life<lifeMax*.5f?1f:0f)+(life<lifeMax*.25f?1f:0f)+(life<lifeMax*.1f?2f:0f);if(shots>8f)shots=0f;local=local with{Ai1=shots};
        }
        else if(ai.Ai1==1f)
        {float speed=context.ExpertMode?16.5f:14f;SetToward(cx,cy,target.CenterX,target.CenterY,speed,ref vx,ref vy);ai=ai with{Ai1=2f};}
        else if(ai.Ai1==2f)
        {float timer=ai.Ai2+(context.ExpertMode?1.5f:1f);if(timer>=50f){vx*=.93f;vy*=.93f;if(MathF.Abs(vx)<.1f)vx=0f;if(MathF.Abs(vy)<.1f)vy=0f;}if(timer>=80f){float cycle=ai.Ai3+1f;ai=ai with{Ai1=cycle>=6f?0f:1f,Ai2=0f,Ai3=cycle>=6f?0f:cycle};}else ai=ai with{Ai2=timer};}
    }

    private static void ApproachVector(float dx,float dy,float speed,float accel,ref float vx,ref float vy)
    {float d=MathF.Max(.001f,MathF.Sqrt(dx*dx+dy*dy));float tx=dx/d*speed,ty=dy/d*speed;Approach(ref vx,tx,accel);Approach(ref vy,ty,accel);if(vx<0&&tx>0)Approach(ref vx,tx,accel);else if(vx>0&&tx<0)Approach(ref vx,tx,accel);if(vy<0&&ty>0)Approach(ref vy,ty,accel);else if(vy>0&&ty<0)Approach(ref vy,ty,accel);}
    private static void Approach(ref float v,float d,float a){if(v<d)v=MathF.Min(v+a,d);else if(v>d)v=MathF.Max(v-a,d);}
    private static void SetToward(float x,float y,float tx,float ty,float speed,ref float vx,ref float vy){float dx=tx-x,dy=ty-y,d=MathF.Max(.001f,MathF.Sqrt(dx*dx+dy*dy));vx=dx/d*speed;vy=dy/d*speed;}
    private static bool TryGetTarget(ushort slot,VanillaNpcBehaviorContext c,out VanillaNpcTargetCandidate t){if(slot<byte.MaxValue&&c.TryFindCandidate((byte)slot,out t)&&t.Active&&!t.Dead&&!t.Ghost)return true;t=default;return false;}
    private static bool TryRefresh(in NpcSnapshot npc,in VanillaNpcDefinition def,VanillaNpcBehaviorContext c,ref ushort slot,out VanillaNpcTargetCandidate t){if(c.TrySelectClosestTarget(in npc,in def,out VanillaBlueSlimeTargetRefresh r)&&r.HasTarget&&r.Target<byte.MaxValue&&c.TryFindCandidate((byte)r.Target,out t)&&t.Active&&!t.Dead&&!t.Ghost){slot=r.Target;return true;}t=default;return false;}
    private static NpcStateUpdate Build(in NpcSnapshot n,float vx,float vy,ushort target,in NpcAiState ai,in NpcSimulationState s)=>new(n.Type,n.NetId,n.PositionX,n.PositionY,vx,vy,target,ai,s);
}
