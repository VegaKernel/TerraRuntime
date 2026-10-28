using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Items;
namespace TerraRuntime.Tests;

public sealed partial class OwnedAmmoUseContinuity1458Tests
{
    [Fact]
    public void Original_local_UpdatePlayers_owns_PickAmmo_random_before_next_common()
    {
        foreach(string mode in new[]{"repeat","box"})
        {
            using var source=Read(39,mode,0);var row=source.RootElement;
            using var f=new LocalWeaponClockRuntimeChecks1458.Fixture(false,sourceLaunchCheckpoint:false,sourceStepper:new NoProjectileUpdates());
            var initial=row.GetProperty("before");int slot=0;
            foreach(var item in initial.GetProperty("inventory").EnumerateArray())
                f.Equip((short)slot++,(short)I(item,"type"),(short)I(item,"stack"),(byte)I(item,"prefix"),item.GetProperty("favorite").GetBoolean());
            f.SetMana();
            var types=initial.GetProperty("buffType").EnumerateArray().Select(x=>x.GetUInt16()).Where(x=>x!=0).ToArray();
            var buffs=new byte[4+(types.Length+1)*2];BinaryPrimitives.WriteUInt16LittleEndian(buffs,(ushort)buffs.Length);buffs[2]=50;buffs[3]=f.Player.Slot.Value;
            for(int i=0;i<types.Length;i++)BinaryPrimitives.WriteUInt16LittleEndian(buffs.AsSpan(4+i*2),types[i]);
            // Actual packet50 source type presence; times remain the existing imported-owner policy.
            f.OriginalAmmoFrame(buffs);
            var clock=Clock(initial.GetProperty("clock"));
            var phase=f.Member.ItemPhase!.Value;var local=phase.ToolUse!;
            f.Member.ItemPhase=phase with {Selected=clock.Selected,ManaHeat=initial.GetProperty("clock").GetProperty("manaHeat").GetSingle(),
                Mana=phase.Mana with {Mana=200,Maximum=200},ToolUse=local with {Clocks=clock,SelectionInitialized=false,
                    SourceLocalDefinition=null,SourceDefinition=null,WeaponShoot=null,LocalClocksKnown=true}};
            // Explicit initial original checkpoint, never a mid-episode replay/reset.
            f.PlayerRandom.CopyStateFrom(new VanillaUnifiedRandom1458(0));f.Member.MarkRemotePhaseSnapshot();
            var worldBefore=f.GameplayRandom.Clone();var step=row.GetProperty("steps")[0];
            var publication=row.GetProperty("publications").EnumerateArray().First(x=>I(x,"packet")==27);
            f.Events.OnBirth=()=>
            {
                SameRandom(publication.GetProperty("random"),f.PlayerRandom,mode+" birth callback");
                Assert.True(f.GameplayRandom.HasSameState(worldBefore),"Local Shoot must not consume the independent world/projectile-AI owner.");
            };
            foreach(var frame in step.GetProperty("checkFrames").EnumerateArray())f.OriginalAmmoFrame(Convert.FromHexString(frame.GetString()!));
            foreach(var frame in step.GetProperty("syncFrames").EnumerateArray())f.OriginalAmmoFrame(Convert.FromHexString(frame.GetString()!));
            Assert.Equal(0,f.Projectiles.PromotedClientProjectileSpawns);
            f.State.Tick();Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);Assert.NotNull(f.Member.ItemPhase);
            SameRandom(step.GetProperty("random"),f.PlayerRandom,mode);
            int ammo=step.GetProperty("after").GetProperty("inventory")[54].GetProperty("stack").GetInt32();
            Assert.Equal(ammo,f.Item(54).Stack);
        }
    }

    [Fact]
    public void Original_repeated_seventeen_weapon_payment_acknowledgements_and_tool_switch()
    {
        var failures=new List<string>();
        foreach(string edition in new[]{"Linux","Windows"})
        {
            using var resource=typeof(OwnedAmmoUseContinuity1458Tests).Assembly.GetManifestResourceStream("OwnedAmmoCaller"+edition+"1458")!;
            using var zip=new GZipStream(resource,CompressionMode.Decompress);
            using var reader=new StreamReader(zip);
            int cases=0;
            while(reader.ReadLine() is { } line)
            {
                using var source=JsonDocument.Parse(line);
                var row=source.RootElement;
                try {ReplayOriginal(row,edition);}
                catch(Exception error) {failures.Add(edition+"/"+I(row,"item")+"/"+row.GetProperty("mode")+"/"+I(row,"seed")+": "+error.Message);}
                cases++;
            }
            Assert.Equal(176,cases);
        }
        Assert.True(failures.Count==0,string.Join("\n",failures));
    }

    private static void ReplayOriginal(JsonElement row,string edition)
    {
        int seed=I(row,"seed"),weapon=I(row,"item");string mode=row.GetProperty("mode").GetString()!;
        string label=$"{edition}/{weapon}/{mode}/{seed}";
        using var f=Start(row);
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

    private static LocalWeaponClockRuntimeChecks1458.Fixture Start(JsonElement row,RuntimeItemUseCaller1458 itemUseCaller=RuntimeItemUseCaller1458.LocalOwnerProjection,bool selectInitially=true)
    {
        int seed=I(row,"seed"),weapon=I(row,"item");string mode=row.GetProperty("mode").GetString()!;
        var f=new LocalWeaponClockRuntimeChecks1458.Fixture(false,seed,false,new NoProjectileUpdates(),itemUseCaller,selectInitially);
        var initial=row.GetProperty("before");
        short slot=0;
        foreach(var item in initial.GetProperty("inventory").EnumerateArray())
            f.Equip(slot++,(short)I(item,"type"),(short)I(item,"stack"),(byte)I(item,"prefix"),item.GetProperty("favorite").GetBoolean());
        slot=VanillaPlayerItemSlotCatalog.ArmorStart;
        foreach(var item in initial.GetProperty("armor").EnumerateArray())
            f.Equip(slot++,(short)I(item,"type"),(short)I(item,"stack"),(byte)I(item,"prefix"),item.GetProperty("favorite").GetBoolean());
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));
        f.SetMana();
        var types=initial.GetProperty("buffType").EnumerateArray().Select(x=>x.GetUInt16()).Where(x=>x!=0).ToArray();
        var wire=new byte[4+(types.Length+1)*2];BinaryPrimitives.WriteUInt16LittleEndian(wire,(ushort)wire.Length);wire[2]=50;wire[3]=f.Player.Slot.Value;
        for(int i=0;i<types.Length;i++)BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(4+i*2),types[i]);
        f.OriginalAmmoFrame(wire);
        var clock=Clock(initial.GetProperty("clock"));var phase=f.Member.ItemPhase!.Value;
        f.Member.ItemPhase=phase with {Selected=clock.Selected,ManaHeat=initial.GetProperty("clock").GetProperty("manaHeat").GetSingle(),
            Mana=phase.Mana with {Mana=200,Maximum=200},ToolUse=phase.ToolUse! with {Clocks=clock,SelectionInitialized=false,
                SourceLocalDefinition=null,SourceDefinition=null,WeaponShoot=null,WeaponShootHistory=null,LocalClocksKnown=true}};
        f.PlayerRandom.CopyStateFrom(new VanillaUnifiedRandom1458(seed));f.Member.MarkRemotePhaseSnapshot();
        return f;
    }

    private static void EqualHit(JsonElement expected,ToolHitState1458 actual)
    {
        Assert.Equal(I(expected,"buffer"),actual.BufferLocation);
        for(int i=0;i<ToolHitState1458.SlotCount;i++)
        {
            var e=expected.GetProperty("entries")[i];
            Assert.Equal(new ToolHitEntry1458(e[0].GetInt32(),e[1].GetInt32(),e[2].GetInt32(),e[3].GetInt32(),e[4].GetInt32()),actual.GetEntry(i));
            Assert.Equal(expected.GetProperty("order")[i].GetInt32(),actual.GetOrder(i));
        }
    }
    private static JsonDocument Read(int item,string mode,int seed)
    {
        using var compressed=typeof(OwnedAmmoUseContinuity1458Tests).Assembly.GetManifestResourceStream("OwnedAmmoCallerLinux1458")!;
        using var zip=new GZipStream(compressed,CompressionMode.Decompress);using var reader=new StreamReader(zip);
        string prefix="{\"schema\":4,\"item\":"+item+",\"mode\":\""+mode+"\",\"seed\":"+seed+",";
        while(reader.ReadLine() is { } line)if(line.StartsWith(prefix,StringComparison.Ordinal))return JsonDocument.Parse(line);
        throw new InvalidOperationException("Missing independent source profile.");
    }
    private static int I(JsonElement e,string name)=>e.GetProperty(name).GetInt32();
    private static JsonDocument ReadLastArrow()
    {
        using var resource=typeof(OwnedAmmoUseContinuity1458Tests).Assembly.GetManifestResourceStream("OwnedAmmoLastArrowCallerLinux1458")!;
        using var zip=new GZipStream(resource,CompressionMode.Decompress);using var reader=new StreamReader(zip);
        return JsonDocument.Parse(reader.ReadLine()??throw new InvalidOperationException("Missing actual last-arrow source profile."));
    }
    private static ToolClockState1458 Clock(JsonElement e)=>new(new(I(e,"itemTime"),I(e,"itemTimeMax"),I(e,"itemAnimation"),I(e,"itemAnimationMax"),e.GetProperty("releaseUseItem").GetBoolean(),0,I(e,"revolverCritChanceBonus")),I(e,"toolTime"),e.GetProperty("poundRelease").GetBoolean(),e.GetProperty("channel").GetBoolean()){ReuseDelay=I(e,"reuseDelay")};
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="seedArray")]private static extern ref int[] Cells(VanillaUnifiedRandom1458 r);
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="inext")]private static extern ref uint Cursor(VanillaUnifiedRandom1458 r);
    private static void SameRandom(JsonElement expected,VanillaUnifiedRandom1458 actual,string mode)
    {
        Assert.True(expected.GetProperty("cursor").GetUInt32()==Cursor(actual),$"{mode}: source cursor {expected.GetProperty("cursor")} actual {Cursor(actual)}");
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(e=>e.GetInt32()),Cells(actual));
    }
    private sealed class NoProjectileUpdates:IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext p,out ProjectileSimulationStepResult next){next=default;return false;}
    }
}
