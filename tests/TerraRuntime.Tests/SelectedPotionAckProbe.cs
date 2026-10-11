using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Network;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

// Configured funded owner + genuine source synchronization images. Geometry and
// actor identity are translated to the admitted fixture; no outer Main/UI claim.
public static class SelectedPotionAckProbe
{
    public static void Run(string platform)
    {
        using var source=ReadSource(platform,"Main");
        int cases=0;
        foreach(var row in source.RootElement.EnumerateArray())
        {
            string mode=row.GetProperty("mode").GetString()!;
            if(!row.GetProperty("local").GetBoolean() || mode is not ("ordinary" or "last-stack" or "full-vitals" or "success-false")) continue;
            using var f=new Fixture("empty-first",runtimeTick:true);
            var c=f.Connections[2]; var m=f.Member(2); var inventory=Inventory(f.Players);
            Assert.True(f.Players.HasOwnedPickupContext,"actual owned pickup context");
            int type=row.GetProperty("itemId").GetInt32();
            var before=row.GetProperty("before");
            int stack=before.GetProperty("inventory")[0].GetProperty("stack").GetInt32();
            f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,(short)stack,0,(short)type,0)));
            f.Apply(new PlayerHealthRuntimeCommand(c,new(c.Player.Slot,(short)before.GetProperty("statLife").GetInt32(),400)));
            f.Apply(new PlayerManaRuntimeCommand(c,new(c.Player.Slot,(short)before.GetProperty("statMana").GetInt32(),200)));
            short[] funded=new short[59]; funded[0]=1;inventory.AdoptFundedQuantities(c,funded);
            f.Move(2,false);f.Tick();
            // ItemCheck-only original inputs exclude UpdateManaRegen. The
            // configured owner holds regen inactive for this isolated ACK test.
            m.Mana=(short)before.GetProperty("statMana").GetInt32();
            m.ItemPhase=m.ItemPhase!.Value with {Mana=m.ItemPhase.Value.Mana with {Mana=m.Mana,Delay=1000,Count=0}};
            if(mode=="success-false") Replay(f,row,preUse:true);
            Replay(f,row); // All literal 5/vitals/50/13/138 BEFORE one owned tick.
            f.Tick();
            Assert.True(m.ItemPhase!.Value.SelectedPotionUse is {Completed:true},$"full source batch pays {type}/{mode}");
            Assert.True(inventory.TryGet(c,0,out var after));
            Assert.Equal(stack-1,(int)after.Stack);
            Assert.Equal((short)0,inventory.CaptureFundedQuantities(c)[0]);
            bool healing=type is 28 or 188 or 499 or 3544;
            Assert.True(healing ? row.GetProperty("beforeSync").GetProperty("statLife").GetInt32()==m.NpcHealth!.Value.Life
                : row.GetProperty("beforeSync").GetProperty("statMana").GetInt32()==m.Mana,
                $"paid source vitality {type}/{mode}: life={m.NpcHealth!.Value.Life},mana={m.Mana}");
            f.Buffs(2).CaptureSlots(out var buffTypes,out var buffTimes);
            int life=m.NpcHealth.Value.Life, mana=m.Mana;
            Replay(f,row);
            Assert.Equal(life,m.NpcHealth.Value.Life);
            Assert.Equal(mana,m.Mana);
            Assert.Equal((short)0,inventory.CaptureFundedQuantities(c)[0]);
            f.Buffs(2).CaptureSlots(out var duplicateTypes,out var duplicateTimes);
            Assert.True(buffTypes.SequenceEqual(duplicateTypes) && buffTimes.SequenceEqual(duplicateTimes),"duplicate50 preserves every indexed duration");
            if(healing)
            {
                Assert.Equal(PlayerDamageCommitResult.Committed,f.Players.TryCommitAuthoritativeNpcContactDamage(
                    100,new(0,new(1)),c.Player,5,0,VanillaPlayerImmunityChannel1458.General,out _));
            }
            else Assert.True(f.Players.TryConsumeMana(c,5),"later owned mana payment");
            int newerLife=m.NpcHealth.Value.Life, newerMana=m.Mana;
            Replay(f,row);
            Assert.Equal(newerLife,m.NpcHealth.Value.Life);
            Assert.Equal(newerMana,m.Mana);
            Assert.Equal((short)0,inventory.CaptureFundedQuantities(c)[0]);
            f.Apply(new PlayerBuffTypesRuntimeCommand(c,new(c.Player.Slot,Array.Empty<BuffTypeId>())));
            f.Buffs(2).CaptureSlots(out var removedTypes,out var removedTimes);
            Replay(f,row);
            f.Buffs(2).CaptureSlots(out var staleTypes,out var staleTimes);
            Assert.True(removedTypes.SequenceEqual(staleTypes) && removedTimes.SequenceEqual(staleTimes),
                "exact stale50 never recreates a removed indexed buff, even for legacy60");
            // Physical reports may keep legacy-compatible visible quantities,
            // but cannot erase the paid effect epoch and replay its exact ACK.
            f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,(short)stack,0,(short)type,0)));
            Replay(f,row);
            f.Buffs(2).CaptureSlots(out var restoredTypes,out var restoredTimes);
            Assert.True(newerLife==m.NpcHealth.Value.Life&&newerMana==m.Mana&&
                removedTypes.SequenceEqual(restoredTypes)&&removedTimes.SequenceEqual(restoredTimes),
                $"raw5 cannot retire paid ACK ownership {type}/{mode}");
            Assert.Equal((short)0,inventory.CaptureFundedQuantities(c)[0]);
            var other=f.Member(0);
            other.ItemPhase=other.ItemPhase!.Value with {Mana=other.ItemPhase.Value.Mana with {Delay=100,Count=0}};
            f.Tick();
            Assert.True(other.ItemPhase!.Value.Mana.Delay<100,"late50 cannot stop another actor's clock");
            cases++;
        }
        Console.WriteLine($"ACK actual source batches {platform}: {cases} configured runtime cases passed.");
        VerifyUnfundedActorAndCursorNeighbor();
        VerifyTransfer();
        foreach(var row in source.RootElement.EnumerateArray())
            if(row.GetProperty("local").GetBoolean()&&row.GetProperty("mode").GetString()=="ordinary"&&row.GetProperty("itemId").GetInt32() is 28 or 110)
                VerifyPublicationCallbacks(row);
        foreach(var row in source.RootElement.EnumerateArray())
            if(row.GetProperty("local").GetBoolean()&&row.GetProperty("mode").GetString()=="ordinary"&&row.GetProperty("itemId").GetInt32() is 28 or 110)
                VerifyPendingAuthoritativeChange(row);
        VerifyThrottledVitals(platform);
        VerifySecondPaidManaUse();
    }

    private static void VerifySecondPaidManaUse()
    {
        using var f=new Fixture("empty-first",runtimeTick:true);
        var c=f.Connections[2];var inventory=Inventory(f.Players);var m=f.Member(2);
        f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,3,0,110,0)));
        short[] q=new short[59];q[0]=3;inventory.AdoptFundedQuantities(c,q);f.Tick();
        m.Mana=10;m.ItemPhase=m.ItemPhase!.Value with{Mana=m.ItemPhase.Value.Mana with{Mana=10,Delay=1000,Count=0}};
        for(short remaining=2;remaining>=1;remaining--)
        {
            int expectedMana=remaining==2?60:110;
            // Configured second operation reuses the source-proven one-debit,
            // +50 mana and94 type shape; no original two-use UI capture claim.
            f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,remaining,0,110,0)));
            f.Apply(new PlayerManaRuntimeCommand(c,new(c.Player.Slot,(short)expectedMana,200)));
            f.Apply(new PlayerBuffTypesRuntimeCommand(c,new(c.Player.Slot,new[]{new BuffTypeId(94)})));
            f.Move(2,true);f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));f.Tick();
            Assert.True(inventory.TryGet(c,0,out var after)&&after.Stack==remaining,
                $"report-first remaining={remaining}, actualStack={after.Stack}, funded={inventory.CaptureFundedQuantities(c)[0]},mana={m.Mana}");
            Assert.Equal(remaining,inventory.CaptureFundedQuantities(c)[0]);
            Assert.Equal(expectedMana,(int)m.Mana);
            Assert.True(m.ItemPhase!.Value.SelectedPotionUse is {Completed:true});
            if(remaining==2)
            {
                f.Move(2,false);
                for(int tick=0;tick<20;tick++)f.Tick();
                Assert.True(m.ItemPhase.Value.Selected.Animation==0&&m.ItemPhase.Value.Selected.ItemTime==0&&m.ItemPhase.Value.Selected.ReleaseUseItem,
                    "real owned release and clock progression admit another paid attempt");
            }
        }
    }

    private static JsonDocument ReadSource(string platform,string kind)
    {
        string logicalName=(kind=="Main"?"SelectedPotionUse":"SelectedPotionThrottle")+platform+"First1458";
        using var stream=typeof(SelectedPotionAckProbe).Assembly.GetManifestResourceStream(logicalName)!;
        Assert.NotNull(stream);
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void VerifyThrottledVitals(string platform)
    {
        using var original=ReadSource(platform,"Throttle");
        int rows=0;
        foreach(var row in original.RootElement.EnumerateArray())
        {
            using var f=new Fixture("empty-first",runtimeTick:true);
            var c=f.Connections[2];var m=f.Member(2);var inventory=Inventory(f.Players);
            int type=row.GetProperty("itemId").GetInt32();int stack=row.GetProperty("before").GetProperty("inventory")[0].GetProperty("stack").GetInt32();
            f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,(short)stack,0,(short)type,0)));
            short[] q=new short[59];q[0]=1;inventory.AdoptFundedQuantities(c,q);f.Tick();
            m.Mana=10;m.ItemPhase=m.ItemPhase!.Value with{Mana=m.ItemPhase.Value.Mana with{Mana=10,Delay=1000,Count=0}};
            var calls=row.GetProperty("syncCalls");Assert.Equal(61,calls.GetArrayLength());
            var first=calls[0].GetProperty("frames").EnumerateArray().Select(value=>value.GetString()!).ToArray();
            Assert.DoesNotContain(first,hex=>Convert.FromHexString(hex)[2] is 16 or 42);
            using var initial=JsonDocument.Parse(JsonSerializer.Serialize(new{syncFrames=first}));
            Replay(f,initial.RootElement);f.Tick();
            Assert.True(m.ItemPhase!.Value.SelectedPotionUse is {Completed:true},"no vital report is needed to own payment");
            if(type is 28 or 188 or 499 or 3544)
                Assert.Equal(PlayerDamageCommitResult.Committed,f.Players.TryCommitAuthoritativeNpcContactDamage(
                    100,new(0,new(1)),c.Player,5,0,VanillaPlayerImmunityChannel1458.General,out _));
            else Assert.True(f.Players.TryConsumeMana(c,5));
            int life=m.NpcHealth!.Value.Life,mana=m.Mana;
            for(int index=1;index<61;index++)
            {
                var frames=calls[index].GetProperty("frames").EnumerateArray().Select(value=>value.GetString()!).ToArray();
                if(index<60)Assert.DoesNotContain(frames,hex=>Convert.FromHexString(hex)[2] is 16 or 42);
                else Assert.Contains(frames,hex=>Convert.FromHexString(hex)[2] is 16 or 42);
                using var batch=JsonDocument.Parse(JsonSerializer.Serialize(new{syncFrames=frames}));
                Replay(f,batch.RootElement,expectDelimiter:false);
                Assert.Equal(life,m.NpcHealth.Value.Life);Assert.Equal(mana,(int)m.Mana);
                Assert.Equal((short)0,inventory.CaptureFundedQuantities(c)[0]);
            }
            rows++;
        }
        Console.WriteLine($"Throttled actual source synchronization {platform}: {rows} cases passed.");
    }

    private static void VerifyPendingAuthoritativeChange(JsonElement row)
    {
        using var f=new Fixture("empty-first",runtimeTick:true);
        var c=f.Connections[2];var inventory=Inventory(f.Players);int type=row.GetProperty("itemId").GetInt32();
        f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,3,0,(short)type,0)));
        short[] q=new short[59];q[0]=1;inventory.AdoptFundedQuantities(c,q);f.Tick();
        var m=f.Member(2);m.Mana=10;
        m.ItemPhase=m.ItemPhase!.Value with{Mana=m.ItemPhase.Value.Mana with{Mana=10,Delay=1000,Count=0}};
        f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,2,0,(short)type,0)));
        Assert.True(m.ItemPhase.Value.SelectedPotionUse is {Completed:false},"actual first5 retains anticipated client outcome");
        if(type==28)Assert.Equal(PlayerDamageCommitResult.Committed,f.Players.TryCommitAuthoritativeNpcContactDamage(
            100,new(0,new(1)),c.Player,5,0,VanillaPlayerImmunityChannel1458.General,out _));
        else Assert.True(f.Players.TryConsumeMana(c,5));
        int lifeBefore=m.NpcHealth!.Value.Life,manaBefore=m.Mana;
        Replay(f,row,skipInventory:true);
        Assert.Equal(lifeBefore,m.NpcHealth.Value.Life);
        Assert.Equal(manaBefore,(int)m.Mana);
        f.Tick();
        int paidLife=m.NpcHealth.Value.Life,paidMana=m.Mana;
        Assert.Equal(type==28?95:50,paidLife);
        Assert.Equal(type==110?55:10,paidMana);
        Replay(f,row);
        Assert.True(paidLife==m.NpcHealth.Value.Life&&paidMana==m.Mana,"anticipated client ACK remains explained after actual paid outcome diverges");
        Assert.Equal((short)0,inventory.CaptureFundedQuantities(c)[0]);
    }

    private static void VerifyPublicationCallbacks(JsonElement row)
    {
        foreach(bool reentry in new[]{false,true})
        {
            using var f=new Fixture("empty-first",runtimeTick:true);
            var c=f.Connections[2];var inventory=Inventory(f.Players);int type=row.GetProperty("itemId").GetInt32();
            f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,3,0,(short)type,0)));
            short[] q=new short[59];q[0]=1;inventory.AdoptFundedQuantities(c,q);f.Tick();
            var m=f.Member(2);m.Mana=10;
            m.ItemPhase=m.ItemPhase!.Value with{Mana=m.ItemPhase.Value.Mana with{Mana=10,Delay=1000,Count=0}};
            var failure=new InvalidOperationException("configured first item observer");int calls=0;
            f.Events.OnEquipment=(connection,request)=>
            {
                if(connection!=c||request.SlotId!=0)return;
                calls++;
                Assert.Equal((short)0,inventory.CaptureFundedQuantities(c)[0]);
                Assert.True(inventory.TryGet(c,0,out var physical)&&physical.Stack==2);
                Assert.True(m.ItemPhase!.Value.SelectedPotionUse is {Completed:true});
                Assert.Equal(type==28?100:50,m.NpcHealth!.Value.Life);
                Assert.Equal(type==110?60:10,(int)m.Mana);
                Assert.Equal(type==28?3600:300,f.Buffs(2).GetDuration(new(type==28?21:94)));
                if(reentry)Replay(f,row);
                else throw failure;
            };
            f.Move(2,true);int seenStart=f.Events.Seen.Count;
            Exception? observed=null;
            try{f.Tick();}catch(Exception error){observed=error;}
            if(reentry)Assert.Null(observed);
            else
            {
                Assert.True(ReferenceEquals(failure,observed),"first observer exception retained");
                var published=f.Events.Seen.Skip(seenStart).ToArray();
                Assert.True(published.Contains(type==28?(byte)16:(byte)42)&&published.Contains((byte)50),
                    "throw drains remaining current vitality and buffs");
            }
            Assert.Equal(1,calls);
            Assert.Equal((short)0,inventory.CaptureFundedQuantities(c)[0]);
            Assert.True(inventory.TryGet(c,0,out var paid)&&paid.Stack==2);
        }
    }

    private static void VerifyTransfer()
    {
        foreach(int type in new[]{28,110})
        foreach(bool completed in new[]{false,true})
        {
            using var f=new Fixture("empty-first",runtimeTick:true);
            var c=f.Connections[2];var inventory=Inventory(f.Players);
            f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,3,0,(short)type,0)));
            short[] q=new short[59];q[0]=1;inventory.AdoptFundedQuantities(c,q);f.Tick();
            if(completed){f.Move(2,true);f.Tick();}
            else f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,2,0,(short)type,0)));
            Assert.True(f.Member(2).ItemPhase!.Value.SelectedPotionUse is not null,"transfer has a pending or accepted receipt");
            var detach=new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.Apply(new PlayerTransferDetachRuntimeCommand(c,detach));
            Assert.True(detach.Task.IsCompletedSuccessfully);
            var transfer=detach.Task.Result!;Assert.NotNull(transfer);
            var replacement=new ConnectionHandle(GameCommandSourceId.FromConnection(99122),new(c.Player.Slot,new(c.Player.Generation.Value+1)));
            var attach=new TaskCompletionSource<bool>();
            f.Apply(new PlayerTransferAttachRuntimeCommand(replacement,transfer,100,103,true,false,attach));
            Assert.True(attach.Task.IsCompletedSuccessfully&&attach.Task.Result);
            f.Connections[2]=replacement;
            Assert.Null(f.Member(2).ItemPhase!.Value.SelectedPotionUse);
            Assert.True(transfer.FundedInventoryQuantities!.AsSpan().SequenceEqual(inventory.CaptureFundedQuantities(replacement)));
            Assert.True(transfer.OwnedInventoryQuantities!.AsSpan().SequenceEqual(inventory.CaptureOwnedQuantities(replacement)));
            var actual=new RuntimePlayerInventoryItem[59];Assert.True(inventory.TryCopyInventory(replacement,actual));
            Assert.True(transfer.Inventory.AsSpan().SequenceEqual(actual),"accepted physical debit survives transfer without new payment");
            var beforeOldAck=f.Member(2).CaptureSnapshot();
            f.Players.TryApply(new PlayerHealthRuntimeCommand(c,new(c.Player.Slot,100,400)));
            f.Players.TryApply(new PlayerManaRuntimeCommand(c,new(c.Player.Slot,60,200)));
            Assert.True(beforeOldAck==f.Member(2).CaptureSnapshot(),"old occupation cannot ACK into replacement");
        }
    }

    private static void VerifyUnfundedActorAndCursorNeighbor()
    {
        foreach(bool control in new[]{false,true})
        {
            using var f=new Fixture("empty-first",runtimeTick:true);
            var owner=Inventory(f.Players);var first=f.Connections[2];var other=f.Connections[0];
            f.Apply(new PlayerEquipmentRuntimeCommand(other,new(other.Player.Slot,0,3,0,110,0)));
            short[] paid=new short[59];paid[0]=1;owner.AdoptFundedQuantities(other,paid);
            f.Tick();
            var unowned=f.Member(2);int beforeLife=unowned.NpcHealth!.Value.Life;
            unowned.ItemPhase=unowned.ItemPhase!.Value with {Selected=unowned.ItemPhase.Value.Selected with
                {Animation=5,AnimationMax=17,ItemTime=0,ItemTimeMax=17}};
            unowned.ItemAnimation=5;
            f.Move(2,control);f.Move(0,true);f.Tick();
            Assert.Equal(beforeLife,unowned.NpcHealth.Value.Life);
            Assert.True(owner.TryGet(first,0,out var unpaid)&&unpaid.Stack==3);
            Assert.True(owner.TryGet(other,0,out var after)&&after.Stack==2);
            Assert.Equal((short)0,owner.CaptureFundedQuantities(other)[0]);
            Assert.True(f.Member(0).ItemPhase!.Value.SelectedPotionUse is {Completed:true},"other actor pays while q0 clocks progress");
        }
        using(var f=new Fixture("empty-first",runtimeTick:true))
        {
            var c=f.Connections[2];var owner=Inventory(f.Players);short[] q=new short[59];q[0]=1;
            owner.AdoptFundedQuantities(c,q);f.Tick();int life=f.Member(2).NpcHealth!.Value.Life;
            // The first debit is also a genuine context0 RightClick alternative.
            // Exact cursor credit completes that owner without potion benefit.
            f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,2,0,28,0)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,58,1,0,28,0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));f.Tick();
            Assert.Equal(life,f.Member(2).NpcHealth!.Value.Life);
            Assert.True(owner.TryGet(c,0,out var slot)&&slot.Stack==2);
            Assert.True(owner.TryGet(c,58,out var cursor)&&cursor.Stack==1);
            Assert.Equal(1,owner.CaptureFundedQuantities(c).Sum(value=>(int)value));
            Assert.True(f.Member(2).ItemPhase!.Value.SelectedPotionUse is not {Completed:true},"cursor completion never pays potion");
        }
    }

    private static void Replay(Fixture f,JsonElement row,bool preUse=false,bool skipInventory=false,bool expectDelimiter=true)
    {
        var c=f.Connections[2];var member=f.Member(2);
        int delimiters=0;
        foreach(var hex in row.GetProperty(preUse?"checkFrames":"syncFrames").EnumerateArray())
        {
            byte[] bytes=Convert.FromHexString(hex.GetString()!);
            if(skipInventory&&bytes[2]==5)continue;
            var frame=new TerrariaFrame((ushort)bytes.Length,bytes[2],new ReadOnlySequence<byte>(bytes),new ReadOnlySequence<byte>(bytes.AsMemory(3)));
            switch(bytes[2])
            {
                case 5:
                    Assert.Equal(TerrariaPlayerEquipmentDecodeResult.Decoded,TerrariaPlayerEquipmentCodec.TryDecode(in frame,out var item));
                    f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,item.SlotId,item.Stack,item.Prefix,item.ItemNetId,item.ItemFlags)));break;
                case 16:
                    Assert.Equal(TerrariaPlayerHealthDecodeResult.Decoded,TerrariaPlayerVitalsCodec.TryDecodeHealth(in frame,out var health));
                    f.Apply(new PlayerHealthRuntimeCommand(c,new(c.Player.Slot,health.Life,health.MaxLife)));break;
                case 42:
                    Assert.Equal(TerrariaPlayerManaDecodeResult.Decoded,TerrariaPlayerVitalsCodec.TryDecodeMana(in frame,out var mana));
                    f.Apply(new PlayerManaRuntimeCommand(c,new(c.Player.Slot,mana.Mana,mana.MaxMana)));break;
                case 50:
                    Assert.Equal(TerrariaPlayerBuffDecodeResult.Decoded,TerrariaPlayerBuffCodec1458.TryDecode(in frame,out _,out var types));
                    f.Apply(new PlayerBuffTypesRuntimeCommand(c,new(c.Player.Slot,types)));break;
                case 13:
                    Assert.Equal(TerrariaPlayerMovementDecodeResult.Decoded,TerrariaPlayerMovementDecoder.TryDecode(in frame,out var move));
                    f.Apply(new PlayerMovementRuntimeCommand(c,new(c.Player.Slot,move.ControlFlags,move.MovementFlags,move.MiscFlags1,move.MiscFlags2,move.SelectedItem,
                        member.PositionX,member.PositionY,move.HasVelocity,move.VelocityX,move.VelocityY,move.HasMount,move.MountType,
                        move.HasPotionOfReturnPositions,move.PotionOfReturnOriginalPositionX,move.PotionOfReturnOriginalPositionY,
                        move.PotionOfReturnHomePositionX,move.PotionOfReturnHomePositionY,move.HasCameraTarget,move.CameraTargetX,move.CameraTargetY)));break;
                case 138:f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));delimiters++;break;
                case 35 or 43:
                    Assert.True(preUse && bytes.Length==6,"original cosmetic effect, no vitals grant command");
                    break; // Runtime presentation relay remains a separately declared gap.
                default:throw new InvalidOperationException("Unaccounted source packet "+bytes[2]);
            }
        }
        Assert.Equal(preUse||!expectDelimiter?0:1,delimiters);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="transferProfiles")]
    private static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="playerUpdateRandom")]
    private static extern ref VanillaUnifiedRandom1458? UpdateRandom(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState owner);
    private sealed class Fixture : IDisposable
    {
        internal readonly PlayerAuthority Players;
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 300));
        internal readonly VanillaUnifiedRandom1458 Random;
        private readonly ServerRuntimeState? state;
        internal readonly EventSink Events = new();
        internal readonly Dictionary<byte, ConnectionHandle> Connections = [];
        internal readonly Dictionary<byte, short> ItemIds = [];
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly HashSet<byte> moved = [];
        private readonly PlayerSlotPool pool = new(3);
        internal Fixture(string mode, bool runtimeTick = false)
        {
            for (int x = 0; x < 400; x++) Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectiles = new RuntimeProjectileStore();
            if (runtimeTick)
            {
                state = new(playerEvents: Events, worldTiles: Tiles, projectiles: projectiles,
                    townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 },
                    playerUpdateRandomSeed: new(0));
                Players = Composition(state).Players;
                Random = UpdateRandom(Players)!;
            }
            else { Players = new(Events, Tiles); Random = new(0); }
            // These prefix-none references have identical clocks on both source platforms.
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false)
            { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows() });
            Players.SetPlayerUpdateRandom(Random);
            Players.SetRemotePlayerEnvironment(new(false, false), projectiles);
            Players.SetNpcHealthWorldFacts(() => new(false, false));
            Players.SetNpcHealthGrapplingFacts(_ => false);
            for (int i = 0; i < 3; i++)
            {
                Assert.True(pool.TryAcquireConnection(out var lease));
                sessions.Add(new(lease!));
            }
            var membership = Membership(Players);
            // Slot255 is not a connection lease. A trusted imported source placeholder remains untouched.
            membership.Commit(new RuntimePlayerMember { Slot = new(255), Revision = 1, Connection = new(GameCommandSourceId.FromConnection(99255),
                new(new(255), new(1))), ItemPhase = null, PhysicsPhase = null });
            foreach (byte slot in mode == "reverse-join" ? new byte[] { 2, 0 } : new byte[] { 0, 2 })
            {
                if (slot == 0 && mode == "inactive-first") continue;
                var session = sessions[slot]; session.ObserveWorldRequest(); session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99000 + slot), session.Handle);
                Connections.Add(slot, connection);
                short id = slot == 2 ? (short)28 : slot == 0 && mode == "potion-first" ? (short)28 : slot == 0 && mode == "weapon98-first" ? (short)98 : (short)0;
                if (slot == 2 && mode == "potion-first") id = 0;
                ItemIds.Add(slot, id);
                Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
                Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
                Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, id == 0 ? (short)0 : (short)3, 0, id, 0)));
                Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
                Move(slot, false);
                // Explicit source-configured imported lifecycle branches, after genuine membership creation.
                // Packet movement admission is not weakened to inject outside coordinates.
                if (slot == 0 && mode == "outside-first") { Member(slot).PositionX = 0; Member(slot).PositionY = 0; }
                if (slot == 0 && mode == "dead-first") Member(slot).IsDead = true;
                if (slot == 0 && mode == "ghost-first") Member(slot).MovementFlags |= VanillaPlayerHealthContext1458.GhostMovementFlag;
            }
        }
        internal RuntimePlayerMember Member(byte slot) { Assert.True(Players.TryGet(slot, out var member)); return member; }
        internal void Apply(RuntimeCommand command)
        {
            if (state is null) Assert.True(Players.TryApply(command));
            else state.Apply(command);
        }
        internal void Tick() { Assert.NotNull(state); state.Tick(); }
        internal bool Step()
        {
            if (state is null) return Players.TryTickRemotePlayerPhase();
            state.Tick();
            return Member(2).ItemPhase is not null;
        }
        internal void ReconnectFirst()
        {
            Apply(new PlayerDisconnectRuntimeCommand(Connections[0]));
            sessions[0].Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!); sessions.Add(session);
            Assert.Equal(new PlayerSlotId(0), session.Slot);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99100), session.Handle);
            Connections[0] = connection;
            Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
            Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, 0)));
            Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
        }
        internal void Move(byte slot, bool control)
        {
            var member = Member(slot);
            bool first = moved.Add(slot);
            if (!first && member.PositionX == 0 && member.PositionY == 0)
            {
                // Trusted source-configured outside import: ordinary movement would reject this geometry.
                member.ControlFlags = control ? (byte)32 : (byte)0;
                return;
            }
            Apply(new PlayerMovementRuntimeCommand(Connections[slot], new(new(slot), control ? (byte)32 : (byte)0,
                (byte)(member.MovementFlags | 16), 0, 64, 0, first ? 1600 + slot * 40 : member.PositionX,
                first ? 1606 : member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        internal PlayerBuffState Buffs(byte slot) => Profiles(Players).CaptureBuffState(Connections[slot])!;
        public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    }
    private sealed class EventSink : IRuntimePlayerEventSink
    {
        internal int Count;
        internal readonly List<byte> Seen=[];
        internal Action<ConnectionHandle,PlayerEquipmentCommitRequest>? OnEquipment;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) {Count++;Seen.Add(5);OnEquipment?.Invoke(c,r);}
        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r) {Count++;Seen.Add(16);}
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r) {Count++;Seen.Add(42);}
        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r) {Count++;Seen.Add(50);}
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
