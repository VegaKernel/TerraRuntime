using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed partial class OwnedAmmoUseContinuity1458Tests
{
    [Fact]
    public void Detached_paid_delivery_shell_preserves_the_old_null_phase_passive_fallback()
    {
        using var source=Read(39,"repeat",0);var row=source.RootElement;
        using var shell=Start(row);using var oldNull=Start(row);
        var reports=new List<byte[]>();
        foreach(var f in new[]{shell,oldNull})
        {
            f.Fund(54,12);var acks=new List<byte[]>();var ownReports=new List<byte[]>();
            ApplyDelayedInputs(f,row.GetProperty("steps")[0],ownReports,acks);f.State.Tick();
            reports=ownReports;bool once=false;
            f.Players.SetNpcHealthWorldFacts(()=>{if(!once){once=true;f.Fund(54,10);}return new(false,false);});
            f.State.Tick();Assert.True(once);Assert.NotNull(f.Member.ItemPhase);
            Assert.Null(f.Member.ItemPhase!.Value.DerivedCombat);Assert.Null(f.Member.ItemPhase.Value.PermanentSpeeds);
            Assert.Null(f.Member.ItemPhase.Value.MagicPayment);Assert.Null(f.Member.PhysicsPhase);
            Assert.False(f.Players.HasOwnedPickupContext);
        }
        // The comparison is the previous retirement behavior after the same
        // accepted payment and failed census; no live stream is reseeded.
        oldNull.Member.ItemPhase=null;
        for(int tick=0;tick<4;tick++)
        {
            shell.State.Tick();oldNull.State.Tick();
            Assert.Equal(oldNull.Member.CaptureSnapshot(),shell.Member.CaptureSnapshot());
            bool oldCombat=oldNull.Players.TryCaptureCombatSnapshot(oldNull.Connection,out var expected);
            Assert.Equal(oldCombat,shell.Players.TryCaptureCombatSnapshot(shell.Connection,out var actual));
            Assert.Equal(expected,actual);Assert.Equal(oldNull.Item(54),shell.Item(54));
            Assert.Equal(oldNull.Funded(54),shell.Funded(54));
            Assert.True(shell.PlayerRandom.HasSameState(oldNull.PlayerRandom));
            Assert.True(shell.GameplayRandom.HasSameState(oldNull.GameplayRandom));
        }
        foreach(var report in reports)shell.OriginalAmmoFrame(report);
        Assert.Equal(1,shell.Projectiles.PromotedClientProjectileSpawns);
        Assert.Null(shell.Member.RemotePhaseSnapshot);Assert.False(shell.ToolClock.LocalClocksKnown);
    }

    [Fact]
    public void Paid_immutable_receipts_survive_later_inventory_writes_and_observer_inventory_writes()
    {
        foreach(bool callbackChange in new[]{false,true})
        {
            using var source=Read(39,"repeat",0);var row=source.RootElement;using var f=Start(row);f.Fund(54,12);
            var reports=new List<byte[]>();var acks=new List<byte[]>();
            foreach(var step in row.GetProperty("steps").EnumerateArray().Take(33))
            {ApplyDelayedInputs(f,step,reports,acks);f.State.Tick();}
            Assert.Equal(2,reports.Count);Assert.Equal(10,f.Item(54).Stack);
            if(callbackChange)f.Events.OnBirth=()=>f.Equip(10,2,1);
            else {f.Equip(10,2,1);f.State.Tick();}
            var random=f.PlayerRandom.Clone();
            foreach(var report in reports)f.OriginalAmmoFrame(report);
            Assert.Equal(2,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.Equal(10,f.Item(54).Stack);Assert.Equal(10,f.Funded(54));
            Assert.True(f.PlayerRandom.HasSameState(random));
            f.Events.OnBirth=null;
        }
    }

    [Fact]
    public void Paid_volley_publishes_each_immutable_sibling_after_first_observer_throws()
    {
        using var source=Read(534,"repeat",0);var row=source.RootElement;using var f=Start(row);f.Fund(54,12);
        var reports=new List<byte[]>();var acks=new List<byte[]>();
        ApplyDelayedInputs(f,row.GetProperty("steps")[0],reports,acks);f.State.Tick();
        int entered=0;var random=f.PlayerRandom.Clone();
        f.Events.OnBirth=()=>{if(entered++==0)throw new InvalidOperationException("first paid sibling");};
        foreach(var report in reports[..^1])f.OriginalAmmoFrame(report);
        Assert.Equal("first paid sibling",Assert.Throws<InvalidOperationException>(()=>f.OriginalAmmoFrame(reports[^1])).Message);
        Assert.Equal(reports.Count,entered);Assert.Equal(reports.Count,f.Projectiles.PromotedClientProjectileSpawns);
        Assert.True(f.ToolClock.WeaponShootHistory!.Uses[0].Born);Assert.Equal(11,f.Item(54).Stack);
        Assert.True(f.PlayerRandom.HasSameState(random));f.Events.OnBirth=null;
        foreach(var report in reports)f.OriginalAmmoFrame(report);
        Assert.Equal(reports.Count,f.Projectiles.PromotedClientProjectileSpawns);
    }

    [Fact]
    public void Paid_predeath_shoot_remains_deliverable_without_new_dead_player_authority()
    {
        foreach(int weapon in new[]{39,98})
        foreach(bool deadPhase in new[]{false,true})
        {
            using var source=Read(weapon,"repeat",0);var row=source.RootElement;using var f=Start(row);f.Fund(54,12);
            var reports=new List<byte[]>();var acks=new List<byte[]>();
            ApplyDelayedInputs(f,row.GetProperty("steps")[0],reports,acks);f.State.Tick();
            Assert.Single(f.ToolClock.WeaponShootHistory!.Uses);Assert.Equal(11,f.Item(54).Stack);
            f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection,new(f.Player.Slot,0,100)));
            Assert.True(f.Member.IsDead);
            if(deadPhase)f.State.Tick();
            var random=f.PlayerRandom.Clone();
            foreach(var report in reports)f.OriginalAmmoFrame(report);
            Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.Equal(11,f.Item(54).Stack);Assert.Equal(11,f.Funded(54));
            Assert.True(f.PlayerRandom.HasSameState(random));
            foreach(var report in reports)f.OriginalAmmoFrame(report);
            Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);
            var fresh=Decode(reports[0]);fresh=fresh with {Key=fresh.Key with {Generation=(ushort)(fresh.Key.Generation+1)}};
            f.AmmoReport(in fresh);f.State.Tick();
            Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(11,f.Item(54).Stack);
        }
    }

    [Fact]
    public void Cold_original_27_then_first_held_13_waits_for_one_owned_payment()
    {
        foreach(int weapon in new[]{39,98})
        foreach(bool acknowledgeBeforePhase in new[]{false,true})
        {
            using var source=Read(weapon,"repeat",0);var row=source.RootElement;
            using var f=Start(row,selectInitially:false);f.Fund(54,12);
            // Owned constructor checkpoint at the captured source spawn pose;
            // no normalized movement or caller provenance has been observed.
            f.Member.PositionX=736f;f.Member.PositionY=822f;
            f.Member.MovementFlags=16;f.Member.MiscFlags2=64;f.Member.MarkRemotePhaseSnapshot();
            Assert.Equal(RuntimeItemUseCaller1458.Unknown,f.Member.ItemUseCaller);
            Assert.Equal(long.MinValue,f.ToolClock.LastFrameUpdate);
            var before=f.PlayerRandom.Clone();var step=row.GetProperty("steps")[0];
            foreach(var value in step.GetProperty("checkFrames").EnumerateArray())
            {
                var wire=Convert.FromHexString(value.GetString()!);
                if(wire[2]==27)f.OriginalAmmoFrame(wire);
            }
            Assert.Equal(0,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.Equal(12,f.Item(54).Stack);Assert.Equal(12,f.Funded(54));
            Assert.True(f.PlayerRandom.HasSameState(before));
            Assert.NotEmpty(f.ToolClock.WeaponShootHistory!.PendingReports);
            f.Control(0,true);
            if(acknowledgeBeforePhase)foreach(var value in step.GetProperty("syncFrames").EnumerateArray())f.OriginalAmmoFrame(Convert.FromHexString(value.GetString()!));
            f.State.Tick();
            if(!acknowledgeBeforePhase)foreach(var value in step.GetProperty("syncFrames").EnumerateArray())f.OriginalAmmoFrame(Convert.FromHexString(value.GetString()!));
            SameRandom(step.GetProperty("random"),f.PlayerRandom,"cold first source use");
            Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.Equal(11,f.Item(54).Stack);Assert.Equal(11,f.Funded(54));
            Assert.True(f.ToolClock.LocalClocksKnown);Assert.True(f.ToolClock.WeaponShootHistory!.Uses[0].Born);
        }
    }

    [Fact]
    public void Initial_census_refreshes_unpaid_inputs_and_provider_failure_keeps_only_paid_delivery()
    {
        foreach(bool providerMutation in new[]{false,true})
        {
            using var source=Read(39,"repeat",0);var row=source.RootElement;using var f=Start(row);f.Fund(54,12);
            var reports=new List<byte[]>();var acks=new List<byte[]>();
            ApplyDelayedInputs(f,row.GetProperty("steps")[0],reports,acks);f.State.Tick();
            Assert.Single(f.ToolClock.WeaponShootHistory!.Uses);Assert.Equal(11,f.Item(54).Stack);Assert.Equal(11,f.Funded(54));
            ulong serial=f.ToolClock.WeaponShootHistory.NextSerial;
            var before=f.PlayerRandom.Clone();var clocks=f.ToolClock.Clocks;
            if(providerMutation)
            {
                bool once=false;
                f.Players.SetNpcHealthWorldFacts(()=>
                {if(!once){once=true;f.Fund(54,10);}return new(false,false);});
                f.State.Tick();Assert.True(once);Assert.False(f.ToolClock.LocalClocksKnown);
                Assert.Null(f.Member.PhysicsPhase);Assert.Null(f.ToolClock.Input);Assert.Null(f.ToolClock.WeaponShoot);
                Assert.True(f.PlayerRandom.HasSameState(before));Assert.Equal(11,f.Item(54).Stack);Assert.Equal(10,f.Funded(54));
            }
            else
            {
                f.Equip(10,2,1);f.Control(0,false);f.State.Tick();
                Assert.True(f.ToolClock.LocalClocksKnown);Assert.Equal(clocks.Selected.ItemTime-1,f.ToolClock.Clocks.Selected.ItemTime);
                Assert.Equal(serial,f.ToolClock.WeaponShootHistory!.NextSerial);
                Assert.False(f.ToolClock.WeaponShootHistory.Uses[0].Retired);
                var expected=before.Clone();expected.Next(3);Assert.True(f.PlayerRandom.HasSameState(expected));
            }
            foreach(var report in reports)f.OriginalAmmoFrame(report);
            Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(11,f.Item(54).Stack);
            Assert.Equal(providerMutation?10:11,f.Funded(54));
            if(providerMutation)
            {
                Assert.Null(f.Member.RemotePhaseSnapshot);var random=f.PlayerRandom.Clone();
                f.Control(0,true);var fresh=Decode(reports[0]);
                fresh=fresh with {Key=fresh.Key with {Generation=(ushort)(fresh.Key.Generation+1)}};
                f.AmmoReport(in fresh);f.State.Tick();
                Assert.False(f.ToolClock.LocalClocksKnown);Assert.Null(f.Member.RemotePhaseSnapshot);
                Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(11,f.Item(54).Stack);
                Assert.True(f.PlayerRandom.HasSameState(random));
            }
        }
    }

    [Fact]
    public void Caller_provenance_cannot_replace_known_clocks_or_an_owned_source_start()
    {
        foreach(var role in new[]{RuntimeItemUseCaller1458.Unknown,RuntimeItemUseCaller1458.DedicatedRemote})
        {
            using var source=Read(98,"repeat",0);using var f=Start(source.RootElement,role);f.Fund(54,12);
            f.Control(0,true);var before=f.PlayerRandom.Clone();var expected=before.Clone();expected.Next(3);
            Assert.Equal(12,f.Item(54).Stack);Assert.Null(f.ToolClock.WeaponShootHistory);
            f.State.Tick();Assert.Equal(12,f.Item(54).Stack);Assert.Equal(12,f.Funded(54));
            Assert.Null(f.ToolClock.WeaponShootHistory);Assert.True(f.PlayerRandom.HasSameState(expected));
        }
        foreach(bool known in new[]{false,true})
        {
            using var source=Read(98,"repeat",0);using var f=Start(source.RootElement,RuntimeItemUseCaller1458.Unknown);
            f.Fund(54,12);
            if(!known)
            {
                var phase=f.Member.ItemPhase!.Value;
                f.Member.ItemPhase=phase with {ToolUse=phase.ToolUse! with {LocalClocksKnown=false}};
                f.Member.MarkRemotePhaseSnapshot();
            }
            var movement=new PlayerMovementCommitRequest(f.Player.Slot,32,16,0,64,0,736f,822f,
                false,0f,0f,false,0,false,0f,0f,0f,0f,false,0f,0f);
            var before=f.PlayerRandom.Clone();
            f.State.Apply(new PlayerMovementRuntimeCommand(f.Connection,movement)
                {ItemUseCaller=RuntimeItemUseCaller1458.LocalOwnerProjection});
            Assert.True(f.PlayerRandom.HasSameState(before));Assert.Equal(12,f.Item(54).Stack);
            f.State.Tick();
            if(known)
            {
                SameRandom(source.RootElement.GetProperty("steps")[0].GetProperty("random"),f.PlayerRandom,"explicit trusted local input");
                Assert.Single(f.ToolClock.WeaponShootHistory!.Uses);
            }
            else
            {
                var expected=before.Clone();expected.Next(3);
                Assert.True(f.PlayerRandom.HasSameState(expected));Assert.Null(f.ToolClock.WeaponShootHistory);
                Assert.Equal(12,f.Item(54).Stack);Assert.Equal(12,f.Funded(54));
            }
        }
    }

    [Fact]
    public void Source_conserved_second_use_keeps_the_last_funded_arrow_until_third_payment()
    {
        using var source=Read(39,"box",7);var row=source.RootElement;
        var sourceShots=row.GetProperty("steps").EnumerateArray().Where(step=>step.GetProperty("checkFrames")
            .EnumerateArray().Any(w=>Convert.FromHexString(w.GetString()!)[2]==27)).ToArray();
        Assert.Equal(new[]{11,11,10},sourceShots.Select(s=>I(s.GetProperty("after").GetProperty("inventory")[54],"stack")));
        using var f=Start(row);f.Equip(54,40,2);f.Fund(54,2);
        var reports=new List<byte[]>();var inventory=new List<byte[]>();
        foreach(var step in row.GetProperty("steps").EnumerateArray().Take(65))
        {
            ApplyDelayedInputs(f,step,reports,inventory);f.State.Tick();
            // The original full-stock conservation predicate never reads stack
            // beyond positive availability. Starting2 substitutes12 minus10;
            // both remain positive at all three captured Shoot preimages.
            int expected=I(step.GetProperty("after").GetProperty("inventory")[54],"stack")-10;
            Assert.Equal(expected,f.Item(54).Stack);Assert.Equal(expected,f.Funded(54));
            SameRandom(step.GetProperty("random"),f.PlayerRandom,"source-qualified conserved-last stock projection");
            if(I(step,"tick")==32)
            {
                Assert.Equal(1,f.Item(54).Stack);Assert.Equal(2,f.ToolClock.WeaponShootHistory!.Uses.Length);
                var conserved=f.ToolClock.WeaponShootHistory.Uses[1];
                Assert.Equal(conserved.AmmoBefore,conserved.AmmoAfter);Assert.Equal(1,conserved.FundedBefore);
            }
        }
        Assert.Equal(3,reports.Count);Assert.True(f.Item(54).IsEmpty);Assert.Equal(0,f.Funded(54));
        var random=f.PlayerRandom.Clone();
        foreach(var original in inventory)
        {
            var wire=original.ToArray();
            if(wire[2]==5)
            {
                short stack=checked((short)(BinaryPrimitives.ReadInt16LittleEndian(wire.AsSpan(6))-10));
                BinaryPrimitives.WriteInt16LittleEndian(wire.AsSpan(6),stack);
                if(stack==0){wire[8]=0;BinaryPrimitives.WriteInt16LittleEndian(wire.AsSpan(9),0);wire[11]=0;}
            }
            f.OriginalAmmoFrame(wire);
        }
        foreach(var report in reports)f.OriginalAmmoFrame(report);
        Assert.Equal(3,f.Projectiles.PromotedClientProjectileSpawns);
        Assert.True(f.Item(54).IsEmpty);Assert.Equal(0,f.Funded(54));Assert.True(f.PlayerRandom.HasSameState(random));
    }

    [Fact]
    public void Paid_burst_waits_for_all_keys_without_repeating_pick_ammo_or_random()
    {
        foreach(int weapon in new[]{534,4703})
        foreach(bool inventoryFirst in new[]{false,true})
        {
            using var source=Read(weapon,"repeat",0);var row=source.RootElement;using var f=Start(row);f.Fund(54,12);
            var step=row.GetProperty("steps")[0];
            var reports=step.GetProperty("checkFrames").EnumerateArray().Select(v=>Convert.FromHexString(v.GetString()!)).Where(w=>w[2]==27).ToArray();
            Assert.InRange(reports.Length,4,8);
            f.Control(0,true);
            if(inventoryFirst)foreach(var value in step.GetProperty("syncFrames").EnumerateArray())f.OriginalAmmoFrame(Convert.FromHexString(value.GetString()!));
            foreach(var report in reports[..^1])f.OriginalAmmoFrame(report);
            if(!inventoryFirst)foreach(var value in step.GetProperty("syncFrames").EnumerateArray())f.OriginalAmmoFrame(Convert.FromHexString(value.GetString()!));
            Assert.Equal(12,f.Item(54).Stack);Assert.Equal(12,f.Funded(54));
            f.State.Tick();SameRandom(step.GetProperty("random"),f.PlayerRandom,"source burst phase");
            int remaining=step.GetProperty("after").GetProperty("inventory")[54].GetProperty("stack").GetInt32();
            Assert.Equal(remaining,f.Item(54).Stack);Assert.Equal(remaining,f.Funded(54));
            Assert.Equal(0,f.Projectiles.PromotedClientProjectileSpawns);
            f.OriginalAmmoFrame(reports[0]);
            var changed=Decode(reports[0]);changed=changed with {Damage=checked((short)(changed.Damage+1))};
            f.AmmoReport(in changed);
            f.State.Tick();f.State.Tick();var after=f.PlayerRandom.Clone();
            f.Events.OnBirth=()=>
            {
                Assert.Equal(reports.Length,f.Projectiles.PromotedClientProjectileSpawns);
                Assert.Equal(remaining,f.Item(54).Stack);Assert.Equal(remaining,f.Funded(54));
                Assert.True(f.PlayerRandom.HasSameState(after));
            };
            f.OriginalAmmoFrame(reports[^1]);
            Assert.Equal(reports.Length,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.True(f.PlayerRandom.HasSameState(after));
        }
    }

    [Fact]
    public void Earlier_remote_common_precedes_local_pick_ammo_independent_of_input_fifo()
    {
        foreach(bool packetFirst in new[]{false,true})
        foreach(bool remoteInputFirst in new[]{false,true})
        {
            using var source=Read(98,"repeat",0);var row=source.RootElement;
            using var f=new LocalWeaponClockRuntimeChecks1458.Fixture(false,0,false,new NoProjectileUpdates());
            var remote=f.AddLocalAfterExistingRemote();f.Equip(0,98,1);f.Equip(54,97,12);f.Select(0);
            f.Control(0,false);f.Fund(54,12);f.State.Tick();
            Assert.True(f.Players.TryGet(remote,out var remoteMember));
            int remoteCrit=remoteMember.ItemPhase!.Value.Selected.RevolverCritBonus;
            int localCrit=f.ToolClock.Clocks.Selected.RevolverCritBonus;
            var before=f.PlayerRandom.Clone();var expected=before.Clone();
            int remoteRoll=expected.Next(3),localRoll=expected.Next(3);
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(new(98),out var weapon));
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetAmmo(VanillaProjectileAmmoFamily.Bullet,new(97),out var ammo));
            var combat=f.Member.ItemPhase!.Value.DerivedCombat!.Value;
            bool conserved=VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation(in weapon,in ammo,in combat,expected.Next,false,false);
            var shots=new VanillaBulletLaunchShot1458[VanillaBulletWeaponLaunch1458.MaximumShotCount];
            // This detached preimage uses the independently source-qualified
            // launch kernel; every nonrandom body field remains literal below.
            Assert.True(VanillaBulletWeaponLaunch1458.TryPlanFromAim(new(98),374f,-1f,1,11f,VanillaProjectileIds.Bullet,
                expected.Next,expected.NextDouble,shots,out int count));Assert.Equal(1,count);
            byte[] literal=row.GetProperty("steps")[0].GetProperty("checkFrames").EnumerateArray()
                .Select(v=>Convert.FromHexString(v.GetString()!)).First(w=>w[2]==27);
            var packet=Decode(literal);packet=packet with {Key=packet.Key with {Spawner=f.Player.Slot.Value},
                VelocityX=shots[0].VelocityX,VelocityY=shots[0].VelocityY};
            if(remoteInputFirst)f.ReleaseEarlier(remote);
            f.Control(0,true);
            if(!remoteInputFirst)f.ReleaseEarlier(remote);
            f.Events.OnBirth=()=>
            {Assert.True(f.PlayerRandom.HasSameState(expected));Assert.Equal(conserved?12:11,f.Item(54).Stack);};
            if(packetFirst){f.AmmoReport(in packet);Assert.Single(f.ToolClock.WeaponShootHistory!.PendingReports);}
            Assert.Equal(12,f.Item(54).Stack);Assert.True(f.PlayerRandom.HasSameState(before));
            f.State.Tick();
            if(!packetFirst)f.AmmoReport(in packet);
            Assert.True(f.Projectiles.PromotedClientProjectileSpawns==1,
                $"cohort packetFirst={packetFirst} remoteFirst={remoteInputFirst} role={f.Member.ItemUseCaller} known={f.ToolClock.LocalClocksKnown} paid={f.ToolClock.WeaponShootHistory?.Uses.Length} reports={f.ToolClock.WeaponShootHistory?.Uses.FirstOrDefault()?.Reports.Length} retired={f.ToolClock.WeaponShootHistory?.Uses.FirstOrDefault()?.Retired}");
            Assert.True(f.PlayerRandom.HasSameState(expected));
            Assert.Equal(remoteCrit-(remoteRoll==0?2:0),remoteMember.ItemPhase!.Value.Selected.RevolverCritBonus);
            Assert.Equal(localCrit-(localRoll==0?2:0),f.ToolClock.Clocks.Selected.RevolverCritBonus);
        }
    }

    [Fact]
    public void Delayed_paid_cohorts_keep_costs_entropy_keys_and_historical_acknowledgements()
    {
        foreach(int weapon in new[]{39,98})
        foreach(int seed in new[]{0,7})
        foreach(string mode in new[]{"repeat","last-stack","box","priority","full-inventory"})
        foreach(bool funded in new[]{false,true})
        {
            using var source=Read(weapon,mode,seed);var row=source.RootElement;
            using var f=Start(row);
            if(funded)f.Fund(54,f.Item(54).Stack);
            var reports=new List<byte[]>();var acknowledgements=new List<byte[]>();
            foreach(var step in row.GetProperty("steps").EnumerateArray().Take(65))
            {
                ApplyDelayedInputs(f,step,reports,acknowledgements);f.State.Tick();
                SameRandom(step.GetProperty("random"),f.PlayerRandom,mode+" delayed source phase");
            }
            Assert.True(reports.Count>0);Assert.Equal(0,f.Projectiles.PromotedClientProjectileSpawns);
            var history=f.ToolClock.WeaponShootHistory!;
            Assert.Equal(reports.Count,history.Uses.Sum(u=>u.ShotCount));
            var afterAmmo=f.Item(54);short afterFunding=f.Funded(54);
            var random=f.PlayerRandom.Clone();
            long rejectedAcknowledgements=f.State.RejectedPlayerEquipmentUpdates;
            foreach(var wire in acknowledgements)
            {
                f.OriginalAmmoFrame(wire);
                Assert.Equal(afterAmmo,f.Item(54));Assert.Equal(afterFunding,f.Funded(54));
                Assert.Equal(rejectedAcknowledgements,f.State.RejectedPlayerEquipmentUpdates);
            }
            Assert.Equal(afterAmmo,f.Item(54));Assert.Equal(afterFunding,f.Funded(54));
            Assert.True(f.PlayerRandom.HasSameState(random));
            int count=0;
            f.Events.OnBirth=()=>
            {
                Assert.Equal(afterAmmo,f.Item(54));Assert.Equal(afterFunding,f.Funded(54));
                Assert.True(f.PlayerRandom.HasSameState(random));
                count++;
                Assert.Equal(f.Projectiles.PromotedClientProjectileSpawns,f.ToolClock.WeaponShootHistory!.Uses.Where(u=>u.Born).Sum(u=>u.ShotCount));
            };
            foreach(var report in reports)f.OriginalAmmoFrame(report);
            Assert.Equal(reports.Count,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(reports.Count,count);
            var store=ProjectileStore(f.Projectiles);var active=new ProjectileSnapshot[1000];
            int activeCount=store.CopyActive(active);Assert.Equal(reports.Count,activeCount);
            foreach(var actor in active.Take(activeCount))Assert.True(store.TryDespawnAt(actor.Handle,actor.PositionX,actor.PositionY,out _));
            foreach(var report in reports)f.OriginalAmmoFrame(report);
            Assert.Equal(reports.Count,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.Equal(0,store.ActiveCount);Assert.Equal(afterAmmo,f.Item(54));
            Assert.Equal(afterFunding,f.Funded(54));Assert.True(f.PlayerRandom.HasSameState(random));
            Assert.All(f.ToolClock.WeaponShootHistory!.Uses,u=>Assert.True(u.Born));
            if(mode=="last-stack")
            {
                Assert.True(afterAmmo.IsEmpty);Assert.Equal(I(row,"sourceUses"),reports.Count);Assert.Equal(0,afterFunding);
                if(history.Uses.Any(u=>u.AmmoBefore==u.AmmoAfter))
                {Assert.True(reports.Count>1);Assert.All(history.Uses,u=>Assert.Equal(1,u.AmmoBefore.Stack));}
            }
        }
    }

    [Fact]
    public void Paid_cohort_callbacks_and_old_generation_never_replay_resources()
    {
        foreach(string mode in new[]{"throw","reenter","inventory","funding","transfer","generation"})
        {
            using var source=Read(39,"repeat",0);var row=source.RootElement;using var f=Start(row);f.Fund(54,12);
            var reports=new List<byte[]>();var inventory=new List<byte[]>();
            foreach(var step in row.GetProperty("steps").EnumerateArray().Take(65))
            {ApplyDelayedInputs(f,step,reports,inventory);f.State.Tick();}
            Assert.Equal(3,reports.Count);Assert.Equal(9,f.Item(54).Stack);Assert.Equal(9,f.Funded(54));
            var random=f.PlayerRandom.Clone();int entered=0;
            if(mode=="transfer")
            {
                var clocks=f.ToolClock.Clocks;var transferred=f.Detach();f.Attach(transferred);
                Assert.Equal(clocks,f.ToolClock.Clocks);Assert.Null(f.ToolClock.WeaponShootHistory);
                foreach(var report in reports)f.OriginalAmmoFrame(report);
                Assert.Equal(0,f.Projectiles.PromotedClientProjectileSpawns);
                Assert.Equal(9,f.Item(54).Stack);Assert.Equal(9,f.Funded(54));continue;
            }
            if(mode=="generation")
            {
                var old=f.Connection;using var replacement=f.Reconnect();
                foreach(var report in reports)f.State.Apply(new ClientProjectileUpdateRuntimeCommand(old,Decode(report)));
                Assert.Equal(0,f.Projectiles.PromotedClientProjectileSpawns);continue;
            }
            f.Events.OnBirth=()=>
            {
                Assert.True(f.PlayerRandom.HasSameState(random));Assert.Equal(9,f.Item(54).Stack);
                Assert.Equal(9,f.Funded(54));
                if(entered++!=0)return;
                Assert.True(f.ToolClock.WeaponShootHistory!.Uses[0].Born);
                if(mode=="throw")throw new InvalidOperationException("paid ammo observer");
                if(mode=="reenter")
                {f.OriginalAmmoFrame(reports[0]);f.OriginalAmmoFrame(reports[1]);}
                if(mode=="inventory")f.Equip(10,2,1);
                if(mode=="funding")f.Fund(54,8);
            };
            if(mode=="throw")
                Assert.Equal("paid ammo observer",Assert.Throws<InvalidOperationException>(()=>f.OriginalAmmoFrame(reports[0])).Message);
            else f.OriginalAmmoFrame(reports[0]);
            f.Events.OnBirth=null;
            foreach(var report in reports)f.OriginalAmmoFrame(report);
            Assert.Equal(3,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.True(f.PlayerRandom.HasSameState(random));Assert.Equal(9,f.Item(54).Stack);
            Assert.Equal(mode=="funding"?8:9,f.Funded(54));
        }
    }

    private static void ApplyDelayedInputs(LocalWeaponClockRuntimeChecks1458.Fixture f,JsonElement step,
        List<byte[]> reports,List<byte[]> inventory)
    {
        f.Control(checked((byte)I(step.GetProperty("afterSelection"),"selected")),
            step.GetProperty("entryClock").GetProperty("controlUseItem").GetBoolean());
        foreach(string group in new[]{"checkFrames","syncFrames"})
            foreach(var value in step.GetProperty(group).EnumerateArray())
            {
                var wire=Convert.FromHexString(value.GetString()!);
                if(wire[2]==27)reports.Add(wire);
                else if(wire[2] is 5 or 138)inventory.Add(wire);
                else f.OriginalAmmoFrame(wire);
            }
    }

    private static TerrariaProjectileUpdateState Decode(byte[] wire)
    {
        var data=new System.Buffers.ReadOnlySequence<byte>(wire);
        Assert.Equal(TerrariaFrameReadResult.Frame,TerrariaFrameDecoder.TryRead(ref data,out var frame));
        Assert.Equal(TerrariaProjectileDecodeResult.Decoded,TerrariaProjectileDecoder.TryDecodeUpdate(frame,out var packet));
        return packet;
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="projectiles")]
    private static extern ref RuntimeProjectileStore ProjectileStore(ProjectileAuthority authority);
}

