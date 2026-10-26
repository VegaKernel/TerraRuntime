using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;
internal static class LocalWeaponClockSourceChecks1458
{
    internal static void Run()
    {
        foreach(string platform in new[]{"Linux","Windows"})foreach(string repeat in new[]{"first","repeat"})
        {
            using var stream=typeof(LocalWeaponClockSourceChecks1458).Assembly.GetManifestResourceStream("LocalItemUseStyle"+platform+repeat)!;
            using var zip=new GZipStream(stream,CompressionMode.Decompress);using var doc=JsonDocument.Parse(zip);int id=0;
            foreach(var row in doc.RootElement.EnumerateArray())
            {
                bool canonical=row.GetProperty("type").GetInt32()==id;
                Assert.Equal(canonical,VanillaLocalItemUse1458.TryGetSourceUseStyle(new((short)id),out byte style));
                Assert.Equal(canonical?row.GetProperty("useStyle").GetInt32():0,style);
                Assert.True(VanillaLocalItemUse1458.TryResolveSourceMetadata(new((short)id),out var resolved,out byte resolvedStyle));
                Assert.Equal(row.GetProperty("type").GetInt32(),resolved.Value);
                Assert.Equal(row.GetProperty("useStyle").GetInt32(),resolvedStyle);id++;
            }
            Assert.Equal(6196,id);Assert.False(VanillaLocalItemUse1458.TryGetSourceUseStyle(new(65535),out _));
        }
        CheckOriginalMana();
        var mismatches=new List<string>();
        void Check(bool condition,string message) { if(!condition && mismatches.Count<100) mismatches.Add(message); }
        foreach(string platform in new[]{"Linux","Windows"})foreach(string repeat in new[]{"first","repeat"})
        {
            using var stream=typeof(LocalWeaponClockSourceChecks1458).Assembly.GetManifestResourceStream("LocalWeaponClock"+platform+repeat)!;
            using var zip=new GZipStream(stream,CompressionMode.Decompress);using var doc=JsonDocument.Parse(zip);
            foreach(var row in doc.RootElement.EnumerateArray())
            {
                Assert.True(VanillaLocalWeaponClockCatalog1458.TryGet(new((short)I(row,"item")),out var definition));
                var previousCapture=row.GetProperty("before");var previousRandom=row.GetProperty("beforeRandom");
                foreach(var step in row.GetProperty("steps").EnumerateArray())
                {
                    var entry=step.GetProperty("entryClock");var selection=step.GetProperty("afterSelection");
                    var after=step.GetProperty("after");var expected=after.GetProperty("clock");
                    // Selection.Update itself is separately source-captured; target-bearing
                    // tool effects stay with the existing501/source-action matrix.
                    if(I(selection,"selected")==0 && I(entry,"selectedItem")==0)
                    {
                        var before=new ToolClockState1458(Read(entry),I(entry,"toolTime"),B(entry,"poundRelease"),B(entry,"channel")){ReuseDelay=I(entry,"reuseDelay")};
                        var metadata=row.GetProperty("definition");bool ammo=I(metadata,"useAmmo")==0 || previousCapture.GetProperty("inventory").EnumerateArray().Any(q=>I(q,"type")==I(metadata,"useAmmo") && I(q,"stack")>0);
                        bool attempt=B(entry,"controlUseItem") && before.Selected.ReleaseUseItem && before.Selected.Animation==0 && !B(selection,"buffered");
                        bool slow=I(metadata,"mana")>0 && (attempt?I(entry,"statMana")<I(metadata,"mana"):B(entry,"slowMagicUse"));
                        var facts=new ToolFrameFacts1458(true,B(entry,"controlUseItem"),B(entry,"lastItemUseAttemptSuccess"),B(entry,"noItems"),B(entry,"CCed"),B(selection,"buffered"),false,slow?2.5f:1f){SourceUseAllowed=ammo,WindowsArithmetic=platform=="Windows"};
                        int roll=Next3(previousRandom);
                        Assert.True(VanillaLocalItemUse1458.TryAdvanceFrame(definition,before,facts,roll,out var frame));
                        var state=frame.State;
                        if(frame.ShootBoundary && ammo)Assert.True(VanillaLocalItemUse1458.TryApplyShootTime(definition,state,slow?2.5f:1f,platform=="Windows",out state));
                        Assert.True(VanillaLocalItemUse1458.TryEnterOwnerStage(definition,state,frame.LocalToolStage,out state));
                        string context=$"{platform}/{repeat}/item{I(row,"item")}/{row.GetProperty("mode").GetString()}/seed{I(row,"seed")}/tick{I(step,"tick")}";
                        Check(state.Selected==Read(expected),context+" selected expected="+Read(expected)+" actual="+state.Selected);
                        Check(state.ToolTime==I(expected,"toolTime"),context+" toolTime expected="+I(expected,"toolTime")+" actual="+state.ToolTime);
                        Check(B(expected,"channel")==state.Channel,context+" channel");Check(I(expected,"reuseDelay")==state.ReuseDelay,context+" reuseDelay");
                        Check(B(expected,"lastItemUseAttemptSuccess")==frame.LastUseSuccess,context+" lastSuccess");
                    }
                    previousCapture=after;previousRandom=step.GetProperty("random");
                }
            }
        }
        Assert.True(mismatches.Count==0,string.Join("\n",mismatches));
    }
    private static PlayerSelectedConsumableState1458 Read(JsonElement c)=>new(I(c,"itemTime"),I(c,"itemTimeMax"),I(c,"itemAnimation"),I(c,"itemAnimationMax"),B(c,"releaseUseItem"),0,I(c,"revolverCritChanceBonus"));
    private static int I(JsonElement e,string n)=>e.GetProperty(n).GetInt32();
    private static bool B(JsonElement e,string n)=>e.GetProperty(n).GetBoolean();
    private static int Next3(JsonElement random)
    {
        int cursor=I(random,"cursor")+1;if(cursor>55)cursor=1;int second=cursor+21;if(second>55)second-=55;
        var cells=random.GetProperty("state");int sample=cells[cursor].GetInt32()-cells[second].GetInt32();if(sample==int.MaxValue)sample--;if(sample<0)sample+=int.MaxValue;
        return (int)(sample*4.656612875245797E-10*3);
    }
    private static void CheckOriginalMana()
    {
        foreach(string edition in new[]{"Linux","Windows"})foreach(string repeat in new[]{"first","repeat"})
        {
            using var source=typeof(LocalWeaponClockSourceChecks1458).Assembly.GetManifestResourceStream("LocalWeaponMana"+edition+repeat)!;
            using var zip=new GZipStream(source,CompressionMode.Decompress);using var doc=JsonDocument.Parse(zip);
            Assert.Equal(8,doc.RootElement.GetArrayLength());
            foreach(var row in doc.RootElement.EnumerateArray())
            {
                var random=new TerraRuntime.Core.VanillaUnifiedRandom1458(I(row,"seed"));
                SameRandom(random,row.GetProperty("beforeRandom"));
                Assert.True(VanillaLocalWeaponClockCatalog1458.TryGet(new((short)I(row,"item")),out var definition));
                foreach(var step in row.GetProperty("steps").EnumerateArray())
                {
                    var before=step.GetProperty("beforeRegen");var expected=step.GetProperty("afterRegen");
                    var mana=new PlayerManaRegenerationState1458(I(before,"statMana"),I(before,"statManaMax2"),before.GetProperty("manaRegenDelay").GetSingle(),I(before,"manaRegenCount"),0);
                    var facts=new PlayerManaRegenerationFacts1458(0,0,false,false,false,0,0,0);
                    Assert.True(VanillaRemoteManaRegeneration1458.TryStep(mana,in facts,out var next,out _));
                    Assert.Equal(I(expected,"statMana"),next.Mana);Assert.Equal(I(expected,"manaRegenCount"),next.Count);
                    Assert.Equal(BitConverter.SingleToInt32Bits(expected.GetProperty("manaRegenDelay").GetSingle()),BitConverter.SingleToInt32Bits(next.Delay));
                    if(I(before,"itemAnimation")==0 && I(before,"reuseDelay")==0 && mana.Mana<next.Maximum && next.Mana>=next.Maximum)
                        for(int particle=0;particle<5;particle++)random.Next(20,26);
                    var entry=step.GetProperty("entryClock");var prior=new ToolClockState1458(Read(entry),I(entry,"toolTime"),B(entry,"poundRelease"),B(entry,"channel")){ReuseDelay=I(entry,"reuseDelay")};
                    bool attempt=B(entry,"controlUseItem") && prior.Selected.ReleaseUseItem && prior.Selected.Animation==0;
                    bool slow=attempt?next.Mana<14:B(entry,"slowMagicUse");
                    var clockFacts=new ToolFrameFacts1458(true,B(entry,"controlUseItem"),B(entry,"lastItemUseAttemptSuccess"),B(entry,"noItems"),B(entry,"CCed"),false,false,slow?2.5f:1f){WindowsArithmetic=edition=="Windows"};
                    Assert.True(VanillaLocalItemUse1458.TryAdvanceFrame(definition,prior,clockFacts,random.Next(3),out var frame));
                    var state=frame.State;
                    if(frame.BeganActualUse)next=next with {Mana=Math.Max(0,next.Mana-14)};
                    if(frame.BeganActualUse || prior.Selected.Animation>0)next=next with {Delay=4f};
                    if(frame.ShootBoundary)Assert.True(VanillaLocalItemUse1458.TryApplyShootTime(definition,state,slow?2.5f:1f,edition=="Windows",out state));
                    Assert.True(VanillaLocalItemUse1458.TryEnterOwnerStage(definition,state,frame.LocalToolStage,out state));
                    var after=step.GetProperty("after").GetProperty("clock");
                    Assert.Equal(Read(after),state.Selected);Assert.Equal(I(after,"toolTime"),state.ToolTime);
                    Assert.Equal(I(after,"statMana"),next.Mana);Assert.Equal(I(after,"manaRegenCount"),next.Count);
                    Assert.Equal(BitConverter.SingleToInt32Bits(after.GetProperty("manaRegenDelay").GetSingle()),BitConverter.SingleToInt32Bits(next.Delay));
                    SameRandom(random,step.GetProperty("random"));Assert.Equal(I(step,"next"),random.Clone().Next());
                }
            }
        }
    }
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field,Name="seedArray")]
    private static extern ref int[] RandomCells(TerraRuntime.Core.VanillaUnifiedRandom1458 random);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field,Name="inext")]
    private static extern ref uint RandomCursor(TerraRuntime.Core.VanillaUnifiedRandom1458 random);
    private static void SameRandom(TerraRuntime.Core.VanillaUnifiedRandom1458 random,JsonElement expected)
    {
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(static x=>x.GetInt32()).ToArray(),RandomCells(random));
        Assert.Equal((uint)I(expected,"cursor"),RandomCursor(random));
    }

}
