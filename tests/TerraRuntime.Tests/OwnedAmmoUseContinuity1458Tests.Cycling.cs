using TerraRuntime.Application;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed partial class OwnedAmmoUseContinuity1458Tests
{
    [Fact]
    public void Original_four_accessories_own_repeated_paid_selection_and_entropy()
    {
        var failures=new List<string>();
        foreach(string edition in new[]{"Linux","Windows"})
        {
            using var resource=typeof(OwnedAmmoUseContinuity1458Tests).Assembly.GetManifestResourceStream("OwnedAmmoCyclingCaller"+edition+"1458")!;
            using var zip=new GZipStream(resource,CompressionMode.Decompress);
            using var reader=new StreamReader(zip);
            int cases=0;
            while(reader.ReadLine() is { } line)
            {
                using var source=JsonDocument.Parse(line);var row=source.RootElement;
                try {ReplayCyclingOriginal(row,edition);}
                catch(Exception error) {failures.Add(edition+"/"+I(row,"item")+"/"+row.GetProperty("mode")+"/"+I(row,"gear")+"/"+I(row,"seed")+": "+error.Message);}
                cases++;
            }
            Assert.Equal(704,cases);
        }
        Assert.True(failures.Count==0,string.Join("\n",failures));
    }

    [Fact]
    public void Owned_four_gear_paid_ammo_boundaries_and_lifecycle_remain_atomic()
    {
        Cycling_effective_equipment_combinations_reach_the_owned_phase_once();
        Cycling_paid_cost_precedes_late_body_and_acknowledgement_without_extra_credit();
        Owned_cycler_state_survives_selection_equipment_death_and_transfer_without_replaying_payment();
        Cycling_phase_rejects_stale_owned_counter_random_funding_and_equipment_context();
        Cycling_counter_cost_and_entropy_are_committed_before_reentrant_or_throwing_birth_observers();
    }

    private static void Cycling_paid_cost_precedes_late_body_and_acknowledgement_without_extra_credit()
    {
        foreach(int gear in new[]{6168,6169,6193,6194})
        foreach(short budget in new short[]{0,1,3})
        {
            using var source=ReadCyclingRow(39,"repeat",gear,0);var row=source.RootElement;
            using var f=Start(row);f.Fund(54,budget);
            var step=row.GetProperty("steps")[0];
            var reports=step.GetProperty("checkFrames").EnumerateArray().Select(x=>Convert.FromHexString(x.GetString()!)).Where(x=>x[2]==27).ToArray();
            Assert.Single(reports);f.Control(0,true);
            foreach(var frame in step.GetProperty("syncFrames").EnumerateArray())f.OriginalAmmoFrame(Convert.FromHexString(frame.GetString()!));
            Assert.Equal(12,f.Item(54).Stack);Assert.Equal(budget,f.Funded(54));
            f.State.Tick();Assert.Equal(11,f.Item(54).Stack);Assert.Equal(Math.Max(0,budget-1),f.Funded(54));
            Assert.Equal(0,f.Projectiles.PromotedClientProjectileSpawns);
            var cycle=f.Member.ItemPhase!.Value.AmmoCycling;var random=f.PlayerRandom.Clone();
            Assert.Equal(new AmmoCyclingState1458(I(step.GetProperty("after").GetProperty("clock"),"ammoCyclingOffset"),I(step.GetProperty("after").GetProperty("clock"),"ammoCyclingCooldown")),cycle);
            f.Events.OnBirth=()=>
            {
                Assert.Equal(11,f.Item(54).Stack);Assert.Equal(Math.Max(0,budget-1),f.Funded(54));
                Assert.Equal(cycle,f.Member.ItemPhase!.Value.AmmoCycling);Assert.True(f.PlayerRandom.HasSameState(random));
            };
            f.OriginalAmmoFrame(reports[0]);Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);
            f.OriginalAmmoFrame(reports[0]);
            foreach(var frame in step.GetProperty("syncFrames").EnumerateArray())f.OriginalAmmoFrame(Convert.FromHexString(frame.GetString()!));
            Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(11,f.Item(54).Stack);
            Assert.Equal(Math.Max(0,budget-1),f.Funded(54));Assert.Equal(cycle,f.Member.ItemPhase!.Value.AmmoCycling);
            Assert.True(f.PlayerRandom.HasSameState(random));
        }
    }

    private static void Owned_cycler_state_survives_selection_equipment_death_and_transfer_without_replaying_payment()
    {
        foreach(int gear in new[]{6168,6169,6193,6194})
        foreach(string boundary in new[]{"unequip","death","transfer"})
        {
            using var source=ReadCyclingRow(39,"repeat",gear,0);var row=source.RootElement;
            using var f=Start(row);f.Fund(54,3);f.Control(0,true);
            // Configured transport delay: no27 or5 is an authority to create
            // this phase. The existing source-local owner reserves the use.
            f.State.Tick();Assert.Equal(11,f.Item(54).Stack);Assert.Equal(2,f.Funded(54));
            var before=f.Member.ItemPhase!.Value.AmmoCycling;Assert.NotNull(before);
            var afterPhaseRandom=f.PlayerRandom.Clone();
            var reports=row.GetProperty("steps")[0].GetProperty("checkFrames").EnumerateArray()
                .Select(x=>Convert.FromHexString(x.GetString()!)).Where(x=>x[2]==27).ToArray();
            Assert.Single(reports);
            if(boundary=="transfer")
            {
                var moved=f.Detach();f.Attach(moved);
                Assert.Equal(before,f.Member.ItemPhase!.Value.AmmoCycling);
                Assert.Null(f.ToolClock.WeaponShootHistory);
                foreach(var report in reports)f.OriginalAmmoFrame(report);
                Assert.Equal(0,f.Projectiles.PromotedClientProjectileSpawns);
                Assert.Equal(11,f.Item(54).Stack);Assert.Equal(2,f.Funded(54));
                Assert.True(f.PlayerRandom.HasSameState(afterPhaseRandom));
                continue;
            }
            if(boundary=="death")
            {
                f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection,new(f.Player.Slot,0,100)));
                Assert.True(f.Member.IsDead);
            }
            else { f.Equip(VanillaPlayerItemSlotCatalog.ArmorStart+3,0,0);f.Control(7,false); }
            f.State.Tick();
            Assert.Equal(VanillaAmmoCycling1458.AdvanceLocalUpdate(before.Value),f.Member.ItemPhase!.Value.AmmoCycling);
            var currentRandom=f.PlayerRandom.Clone();var currentCycle=f.Member.ItemPhase!.Value.AmmoCycling;
            foreach(var report in reports)f.OriginalAmmoFrame(report);
            Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.Equal(11,f.Item(54).Stack);Assert.Equal(2,f.Funded(54));
            Assert.Equal(currentCycle,f.Member.ItemPhase!.Value.AmmoCycling);
            Assert.True(f.PlayerRandom.HasSameState(currentRandom));
        }
    }

    private static void Cycling_phase_rejects_stale_owned_counter_random_funding_and_equipment_context()
    {
        foreach(int gear in new[]{6168,6169,6193,6194})
        foreach(string mutation in new[]{"counter","random","funding","equipment"})
        {
            using var source=ReadCyclingRow(39,"repeat",gear,0);using var f=Start(source.RootElement);
            f.Fund(54,3);f.Control(0,true);var before=f.PlayerRandom.Clone();bool called=false;
            f.Players.SetNpcHealthWorldFacts(()=>
            {
                if(!called)
                {
                    called=true;
                    if(mutation=="counter")f.Member.ItemPhase=f.Member.ItemPhase!.Value with {AmmoCycling=new(7,137)};
                    if(mutation=="random")f.PlayerRandom.Next();
                    if(mutation=="funding")f.Fund(54,2);
                    if(mutation=="equipment")f.Players.SetCombatEquipmentWorldModes(true,true);
                }
                return new(false,false);
            });
            f.State.Tick();Assert.True(called);Assert.Equal(12,f.Item(54).Stack);
            Assert.Equal(mutation=="funding"?2:3,f.Funded(54));Assert.Equal(0,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.True(f.Member.ItemPhase?.ToolUse?.WeaponShootHistory is not {Uses.Length:>0});
            var expected=before.Clone();if(mutation=="random")expected.Next();
            Assert.True(f.PlayerRandom.HasSameState(expected));
            if(mutation=="counter" && f.Member.ItemPhase?.AmmoCycling is { } retained)
                Assert.Equal(new AmmoCyclingState1458(7,137),retained);
        }
    }

    private static void Cycling_counter_cost_and_entropy_are_committed_before_reentrant_or_throwing_birth_observers()
    {
        foreach(int gear in new[]{6168,6169,6193,6194})
        foreach(bool throwing in new[]{false,true})
        {
            using var source=ReadCyclingRow(39,"repeat",gear,0);var row=source.RootElement;
            using var f=Start(row);f.Fund(54,3);f.Control(0,true);f.State.Tick();
            var report=row.GetProperty("steps")[0].GetProperty("checkFrames").EnumerateArray()
                .Select(x=>Convert.FromHexString(x.GetString()!)).Single(x=>x[2]==27);
            var cycle=f.Member.ItemPhase!.Value.AmmoCycling;var random=f.PlayerRandom.Clone();int callbacks=0;
            f.Events.OnBirth=()=>
            {
                callbacks++;Assert.Equal(1,callbacks);Assert.Equal(11,f.Item(54).Stack);Assert.Equal(2,f.Funded(54));
                Assert.Equal(cycle,f.Member.ItemPhase!.Value.AmmoCycling);Assert.True(f.PlayerRandom.HasSameState(random));
                f.OriginalAmmoFrame(report);Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);
                if(throwing)throw new InvalidOperationException("cycling observer");
            };
            if(throwing)Assert.Equal("cycling observer",Assert.Throws<InvalidOperationException>(()=>f.OriginalAmmoFrame(report)).Message);
            else f.OriginalAmmoFrame(report);
            Assert.Equal(1,callbacks);f.Events.OnBirth=null;f.OriginalAmmoFrame(report);
            Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(11,f.Item(54).Stack);Assert.Equal(2,f.Funded(54));
            Assert.Equal(cycle,f.Member.ItemPhase!.Value.AmmoCycling);Assert.True(f.PlayerRandom.HasSameState(random));
        }
    }

    private static void Cycling_effective_equipment_combinations_reach_the_owned_phase_once()
    {
        using var compressed=typeof(OwnedAmmoUseContinuity1458Tests).Assembly.GetManifestResourceStream("AmmoCyclingRowsLinux1458")!;
        using var zip=new GZipStream(compressed,CompressionMode.Decompress);using var rows=JsonDocument.Parse(zip);int combinations=0;
        using var profile=ReadCyclingRow(39,"repeat",6168,0);
        foreach(var row in rows.RootElement.EnumerateArray())
        {
            if(row.GetProperty("kind").GetString()!="equipment")continue;
            using var f=Start(profile.RootElement);f.Equip(VanillaPlayerItemSlotCatalog.ArmorStart+3,0,0);
            short slot=VanillaPlayerItemSlotCatalog.ArmorStart+3;
            foreach(var item in row.GetProperty("ids").EnumerateArray())f.Equip(slot++,item.GetInt16(),1);
            var checkpoint=row.GetProperty("before");
            // Explicit captured source checkpoint, never a control/report seed.
            f.Member.ItemPhase=f.Member.ItemPhase!.Value with {AmmoCycling=new(I(checkpoint,"offset"),I(checkpoint,"cooldown"))};
            f.Member.MarkRemotePhaseSnapshot();f.Fund(54,3);f.Control(7,false);f.State.Tick();
            var expected=row.GetProperty("after");var actual=f.Member.ItemPhase!.Value.DerivedCombat!.Value;
            Assert.Equal(I(expected,"mode"),(int)actual.AmmoCyclingMode);
            Assert.Equal(expected.GetProperty("damage").GetBoolean(),actual.AmmoCyclerDamage);
            Assert.Equal(expected.GetProperty("rangedDamageBits").GetUInt32(),BitConverter.SingleToUInt32Bits(actual.RangedDamage));
            Assert.Equal(new AmmoCyclingState1458(7,1),f.Member.ItemPhase!.Value.AmmoCycling);
            Assert.Equal(12,f.Item(54).Stack);Assert.Equal(3,f.Funded(54));Assert.Equal(0,f.Projectiles.PromotedClientProjectileSpawns);
            combinations++;
        }
        Assert.Equal(45,combinations);
    }

    private static JsonDocument ReadCyclingRow(int item,string mode,int gear,int seed)
    {
        using var resource=typeof(OwnedAmmoUseContinuity1458Tests).Assembly.GetManifestResourceStream("OwnedAmmoCyclingCallerLinux1458")!;
        using var zip=new GZipStream(resource,CompressionMode.Decompress);using var reader=new StreamReader(zip);
        while(reader.ReadLine() is { } line)
        {
            var document=JsonDocument.Parse(line);var row=document.RootElement;
            if(I(row,"item")==item && row.GetProperty("mode").GetString()==mode && I(row,"gear")==gear && I(row,"seed")==seed)return document;
            document.Dispose();
        }
        throw new InvalidOperationException("Missing independent cycling source profile.");
    }

    private static void ReplayCyclingOriginal(JsonElement row,string edition)
    {
        int seed=I(row,"seed"),weapon=I(row,"item");string mode=row.GetProperty("mode").GetString()!;
        string label=$"{edition}/{weapon}/{mode}/{I(row,"gear")}/{seed}";
        using var f=Start(row);
        var initialCycle=row.GetProperty("before").GetProperty("clock");
        Assert.Equal(new AmmoCyclingState1458(I(initialCycle,"ammoCyclingOffset"),I(initialCycle,"ammoCyclingCooldown")),f.Member.ItemPhase!.Value.AmmoCycling);
        int sourceChildren=0;
        foreach(var step in row.GetProperty("steps").EnumerateArray())
        {
            int tick=I(step,"tick");
            SameRandom(step.GetProperty("callerRandomBefore"),f.PlayerRandom,label+" expanded-caller entry/"+tick);
            var check=step.GetProperty("checkFrames").EnumerateArray().Select(x=>Convert.FromHexString(x.GetString()!)).ToArray();
            var sync=step.GetProperty("syncFrames").EnumerateArray().Select(x=>Convert.FromHexString(x.GetString()!)).ToArray();
            var sourceAfter=step.GetProperty("after");
            bool sourceWeapon=step.GetProperty("afterSelection").GetProperty("selected").GetInt32()==0;
            int children=check.Count(x=>x[2]==27);sourceChildren+=children;
            if(sourceWeapon)
                f.Events.OnBirth=()=>SameRandom(step.GetProperty("random"),f.PlayerRandom,label+" birth/"+tick);
            else f.Events.OnBirth=null;
            // ItemCheck/TrySyncingMyPlayer is not Main's control-sync caller.
            // Supply its independently captured input facts through real13;
            // literal Shoot/inventory/mining frames remain unchanged below.
            f.Control(checked((byte)step.GetProperty("afterSelection").GetProperty("selected").GetInt32()),
                step.GetProperty("entryClock").GetProperty("controlUseItem").GetBoolean());
            foreach(var frame in check)f.OriginalAmmoFrame(frame);
            foreach(var frame in sync)f.OriginalAmmoFrame(frame);
            f.State.Tick();
            Assert.True(f.Member.ItemPhase is not null,label+" phase/"+tick);
            SameRandom(step.GetProperty("random"),f.PlayerRandom,label+" phase/"+tick);
            Assert.True(sourceChildren==f.Projectiles.PromotedClientProjectileSpawns,label+" births/"+tick+" expected="+sourceChildren+" actual="+f.Projectiles.PromotedClientProjectileSpawns);
            var cycleExpected=sourceAfter.GetProperty("clock");
            Assert.Equal(new AmmoCyclingState1458(I(cycleExpected,"ammoCyclingOffset"),I(cycleExpected,"ammoCyclingCooldown")),f.Member.ItemPhase!.Value.AmmoCycling);
            var combat=f.Member.ItemPhase!.Value.DerivedCombat;
            Assert.NotNull(combat);
            Assert.Equal(I(cycleExpected,"ammoCyclingMode"),(int)combat.Value.AmmoCyclingMode);
            Assert.Equal(cycleExpected.GetProperty("accAmmoCyclerDamage").GetBoolean(),combat.Value.AmmoCyclerDamage);
            Assert.Equal(BitConverter.SingleToUInt32Bits(cycleExpected.GetProperty("rangedDamage").GetSingle()),BitConverter.SingleToUInt32Bits(combat.Value.RangedDamage));
            int index=0;
            foreach(var item in sourceAfter.GetProperty("inventory").EnumerateArray())
            {
                var actual=f.Item(checked((short)index++));
                Assert.True(actual.ItemType.Value==I(item,"type") && actual.Stack==I(item,"stack") &&
                    actual.Prefix.Value==I(item,"prefix"),label+" inventory/"+tick+"/"+(index-1));
            }
            var expected=Clock(sourceAfter.GetProperty("clock"));var actualClocks=f.Member.ItemPhase!.Value.ToolUse!.Clocks;
            if(expected.PoundRelease!=actualClocks.PoundRelease)
            {
                // Source ItemCheck knows its mouse target. A released pick has
                // no hammer certificate or target report in the runtime. Pin
                // that existing refusal explicitly, without guessing a target.
                Assert.Equal(1,step.GetProperty("afterSelection").GetProperty("selected").GetInt32());
                Assert.True(VanillaToolCatalog1458.TryGet(new(3509),out var pick));
                Assert.Equal(0,pick.Hammer);Assert.True(expected.PoundRelease);
                Assert.False(actualClocks.PoundRelease);
                Assert.True(expected.Selected.ReleaseUseItem);
                expected=expected with {PoundRelease=false};
            }
            Assert.True(expected==actualClocks,label+" represented local clocks/"+tick+" expected="+expected+" actual="+actualClocks);
            if(children!=0 || check.Any(x=>x[2]==17) || tick==239)
            {
                EqualHit(row.GetProperty("hitSnapshots")[I(step,"hit")],f.Member.ItemPhase!.Value.ToolUse!.Hits);
                EqualHit(row.GetProperty("replacementHitSnapshots")[I(step,"replace")],f.Member.ItemPhase!.Value.ToolUse!.ReplacementHits);
            }
        }
        Assert.True(sourceChildren>0,label+" source birth coverage");
        Assert.False(f.Tiles.Get(50,52).IsActive);
    }

}
