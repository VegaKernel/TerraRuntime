using Xunit;
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

public static class PermanentEightBoundaryChecks
{
    public static void Run()
    {
        foreach (string platform in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
        {
            using var input = Read(platform);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var data = JsonDocument.Parse(gzip);
            foreach (var row in data.RootElement.EnumerateArray())
            {
                int identity = row.GetProperty("itemId").GetInt32();
                string mode = row.GetProperty("mode").GetString()!;
                if (!row.GetProperty("local").GetBoolean() || !(identity == 3335 ? mode is "expert" or "expert-last-stack" : mode is "ordinary" or "last-stack" or "success-false")) continue;
                short initialStack = (short)row.GetProperty("before").GetProperty("inventory")[0].GetProperty("stack").GetInt32();
                foreach (short budget in new short[] { 0, 1 })
                {
                    using var f = new Fixture("inactive-first", true);
                    f.Players.SetCombatEquipmentWorldModes(row.GetProperty("expert").GetBoolean(), false);
                    var c = f.Connections[2]; var member = f.Member(2); var store = Inventory(f.Players);
                    var appearance = new PlayerAppearanceCommitRequest(c.Player.Slot, 0, 0, 0, 0, "configured", 0, 0, 0, default, default, default, default, default, default, default, 0, 0, 0);
                    f.Apply(new PlayerAppearanceRuntimeCommand(c, appearance));
                    // Prior source input arrived while the selected slot was still Air.
                    // Its later packet4 use batch contains no new initial13.
                    var configuredTool = member.ItemPhase!.Value.ToolUse!;
                    member.ItemPhase = member.ItemPhase.Value with { ToolUse = configuredTool with
                    {
                        Clocks = configuredTool.Clocks with { Selected = configuredTool.Clocks.Selected with
                            { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } }
                    } };
                    member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } };
                    f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(member.MovementFlags | 16), 0, 64, 0, member.PositionX, member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
                    f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, initialStack, 0, (short)identity, 0)));
                    f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                    for (int i = 0; i < 10000 && !f.Players.CanApplyShopSource(c.Source); i++) f.Players.TickShopJournalWork();
                    var quantities = new short[59]; quantities[0] = budget; store.AdoptFundedQuantities(c, quantities);
                    var beforeRandom = (int[])RandomState(f.Random).Clone(); uint beforeCursor = RandomCursor(f.Random);
                    var copper = store.CaptureFundedCoinValues(c); var caps = store.CaptureOwnedQuantities(c);
                    var bankPhysical = new RuntimePlayerInventoryItem[160]; var bankCopper = new long[160];
                    Assert.True(store.TryCaptureShopBanks(c, bankPhysical, bankCopper));
                    Assert.True(store.TryCaptureShopBankOwnership(c, out var bankQ, out var bankCaps));
                    member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } };
                    Replay(f, row.GetProperty("checkFrames"));
                    Replay(f, row.GetProperty("syncFrames"));
                    Assert.True(Profiles(f.Players).TryCapture(c, out var pendingAppearance, out _, out _));
                    Assert.Equal((byte)0, pendingAppearance!.Value.ConsumableUnlockFlags);
                    Assert.Equal((byte)0, (byte)(pendingAppearance.Value.DifficultyFlags & 4));
                    Assert.Equal(beforeRandom, RandomState(f.Random)); Assert.Equal(beforeCursor, RandomCursor(f.Random));
                    f.Tick();
                    Assert.True(member.ItemPhase.HasValue, "The represented paid or denied phase must remain admitted.");
                    if (budget > 0)
                    {
                        var source = row.GetProperty("phases")[0].GetProperty("state");
                        var clocks = member.ItemPhase!.Value.Selected;
                        Assert.Equal(source.GetProperty("itemTime").GetInt32(), clocks.ItemTime);
                        Assert.Equal(source.GetProperty("itemAnimation").GetInt32(), clocks.Animation);
                        Assert.Equal(source.GetProperty("itemTimeMax").GetInt32(), clocks.ItemTimeMax);
                    }
                    Assert.True(Profiles(f.Players).TryCapture(c, out var actualAppearance, out _, out _));
                    byte expectedBit = identity switch { 5337 => 1, 5338 => 2, 5339 => 4, 5340 => 8, 5341 => 16, 5342 => 32, 5326 => 64, 3335 => 128, _ => 0 };
                    Assert.Equal(budget > 0 ? (byte)(expectedBit & 127) : (byte)0, actualAppearance!.Value.ConsumableUnlockFlags);
                    Assert.Equal(budget > 0 && identity == 3335 ? (byte)4 : (byte)0, (byte)(actualAppearance.Value.DifficultyFlags & 4));
                    Assert.Equal((short)0, store.CaptureFundedQuantities(c)[0]);
                    for (short index = 0; index < 59; index++)
                    {
                        Assert.True(store.TryGet(c, index, out var item));
                        short remaining = (short)(initialStack - (budget > 0 ? 1 : 0));
                        Assert.Equal(index == 0 && remaining > 0 ? identity : 0, item.ItemType.Value);
                        Assert.Equal(index == 0 ? remaining : (short)0, item.Stack);
                        Assert.Equal(0, item.Prefix.Value); Assert.Equal((byte)0, item.ItemFlags);
                    }
                    Assert.Equal(copper, store.CaptureFundedCoinValues(c)); Assert.Equal(caps, store.CaptureOwnedQuantities(c));
                    var afterBank = new RuntimePlayerInventoryItem[160]; var afterCopper = new long[160];
                    Assert.True(store.TryCaptureShopBanks(c, afterBank, afterCopper));
                    Assert.Equal(bankPhysical, afterBank); Assert.Equal(bankCopper, afterCopper);
                    Assert.True(store.TryCaptureShopBankOwnership(c, out var afterQ, out var afterCaps));
                    Assert.Equal(bankQ, afterQ); Assert.Equal(bankCaps, afterCaps);
                    Assert.Equal(expectedBit, member.OwnedPermanentUpgradeMask);
                    // The paid effect is adopted after this phase's entry projection.
                    Assert.Equal((byte)0, member.ItemPhase!.Value.PermanentPhaseFlags!.Value);
                    Assert.Equal(0f, member.Luck);
                    f.Move(2, false);
                    f.Tick();
                    var next = member.ItemPhase!.Value;
                    Assert.Equal(budget > 0 ? expectedBit : (byte)0, next.PermanentPhaseFlags!.Value);
                    Assert.NotNull(next.PermanentBoosters);
                    Assert.NotNull(next.PermanentSpeeds);
                    var booster = next.PermanentBoosters!.Value;
                    Assert.Equal(budget > 0 && identity == 5338 ? 4 : 0, booster.Defense);
                    Assert.Equal(budget > 0 && identity == 5341 ? 3 : 0, booster.FishingSkill);
                    Assert.Equal(budget > 0 && identity == 5342 ? .95f : 1f, booster.PickSpeed);
                    Assert.Equal(budget > 0 && identity == 5342 ? 1.05f : 1f, booster.TileSpeed);
                    Assert.Equal(budget > 0 && identity == 5342 ? 1.05f : 1f, booster.WallSpeed);
                    Assert.Equal(budget > 0 && identity == 5340 ? .03f : 0f, member.Luck);
                    var stableBoosters = next.PermanentBoosters;
                    var stableSpeeds = next.PermanentSpeeds;
                    f.Tick();
                    Assert.Equal(stableBoosters, member.ItemPhase!.Value.PermanentBoosters);
                    Assert.Equal(stableSpeeds, member.ItemPhase.Value.PermanentSpeeds);
                    Assert.Equal((short)0, store.CaptureFundedQuantities(c)[0]);
                    if (budget == 0)
                    {
                        // A later trusted unit cannot convert the old denied active-use report into a fresh paid use.
                        var lateQuantity=store.CaptureFundedQuantities(c);lateQuantity[0]=1;store.AdoptFundedQuantities(c,lateQuantity);
                        Replay(f,row.GetProperty("checkFrames"));f.Tick();
                        Assert.True(Profiles(f.Players).TryCapture(c,out var stillDenied,out _,out _));
                        Assert.Equal((byte)0,stillDenied!.Value.ConsumableUnlockFlags);Assert.Equal((byte)0,(byte)(stillDenied.Value.DifficultyFlags&4));
                        Assert.Equal((short)1,store.CaptureFundedQuantities(c)[0]);
                    }
                    if (identity == 5342 && budget > 0 && mode == "ordinary")
                    {
                        using var placementInput=typeof(PermanentEightBoundaryChecks).Assembly.GetManifestResourceStream("AmbrosiaConsumers"+platform+"1458") ?? throw new InvalidOperationException("Missing Ambrosia source fixture.");
                        using var placementGzip=new GZipStream(placementInput,CompressionMode.Decompress);using var placementData=JsonDocument.Parse(placementGzip);
                        foreach(var placement in placementData.RootElement.EnumerateArray().Where(r=>r.GetProperty("consume").GetBoolean() && r.GetProperty("control").GetBoolean() && r.GetProperty("subsequentItem").GetInt32()!=4711))
                        {
                            short nextType=(short)placement.GetProperty("subsequentItem").GetInt32();
                            f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,1,3,0,nextType,0)));f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                            while(!f.Players.CanApplyShopSource(c.Source))f.Players.TickShopJournalWork();
                            // Finish the previous use through real owned phases before the subsequent source-scoped consumer.
                            f.Move(2,false);for(int idle=0;idle<55;idle++)f.Tick();
                            Assert.Equal(0,member.ItemPhase!.Value.Selected.Animation);Assert.Equal(0,member.ItemAnimation);Assert.Equal(0,member.ItemPhase.Value.Selected.ItemTime);
                            f.Apply(new PlayerMovementRuntimeCommand(c,new(c.Player.Slot,32,(byte)(member.MovementFlags|16),0,64,1,member.PositionX,member.PositionY,false,0,0,false,0,false,0,0,0,0,false,0,0)));
                            f.Tick();
                            Assert.True(member.ItemPhase.HasValue);
                            var expectedPlacement=placement.GetProperty("remotePhases")[0].GetProperty("state");
                            Assert.Equal(expectedPlacement.GetProperty("itemAnimation").GetInt32(),member.ItemPhase!.Value.Selected.Animation);
                            Assert.Equal(expectedPlacement.GetProperty("itemAnimationMax").GetInt32(),member.ItemPhase.Value.Selected.AnimationMax);
                            Assert.Equal((short)0,store.CaptureFundedQuantities(c)[0]);
                        }
                    }


                }
            }
        }
    }


    public static void SequentialFamilyBoundary()
    {
        using var input = Read("LinuxFirst");
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var data = JsonDocument.Parse(gzip);
        var a = data.RootElement.EnumerateArray().Single(r => r.GetProperty("itemId").GetInt32() == 5337 && r.GetProperty("local").GetBoolean() && r.GetProperty("mode").GetString() == "ordinary");
        var b = data.RootElement.EnumerateArray().Single(r => r.GetProperty("itemId").GetInt32() == 5338 && r.GetProperty("local").GetBoolean() && r.GetProperty("mode").GetString() == "ordinary");
        using var f = new Fixture("inactive-first", true);
        var c=f.Connections[2];var member=f.Member(2);var store=Inventory(f.Players);
        var appearance = new PlayerAppearanceCommitRequest(c.Player.Slot, 0, 0, 0, 0, "configured", 0, 0, 0, default, default, default, default, default, default, default, 0, 0, 0);
        f.Apply(new PlayerAppearanceRuntimeCommand(c,appearance));
        f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,3,0,5337,0)));f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        while(!f.Players.CanApplyShopSource(c.Source)) f.Players.TickShopJournalWork();
        var q=new short[59];q[0]=1;store.AdoptFundedQuantities(c,q);
        member.ItemPhase=member.ItemPhase!.Value with {Selected=member.ItemPhase.Value.Selected with{ReleaseUseItem=true,Animation=0,ItemTime=0}};
        f.Move(2,true);Replay(f,a.GetProperty("checkFrames"));Replay(f,a.GetProperty("syncFrames"));f.Tick();
        Assert.True(Profiles(f.Players).TryCapture(c,out var first, out _,out _));Assert.Equal((byte)1,first!.Value.ConsumableUnlockFlags);Assert.Equal((short)0,store.CaptureFundedQuantities(c)[0]);
        f.Move(2,false);for(int i=0;i<55;i++)f.Tick();
        Assert.Equal(0,member.ItemPhase!.Value.Selected.Animation);Assert.Equal(0,member.ItemPhase.Value.Selected.ItemTime);
        // Genuine real ingress prior13 before next selected raw5. No private CaptureIntent call.
        f.Move(2,true);f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,3,0,5338,0)));f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        while(!f.Players.CanApplyShopSource(c.Source))f.Players.TickShopJournalWork();q[0]=1;store.AdoptFundedQuantities(c,q);
        // Source B bit is OR'ed with already-paid A in a configured sequential appearance;
        // this is not labelled as newly captured natural sequential packet4.
        foreach(var hex in b.GetProperty("checkFrames").EnumerateArray()){
            var bytes=Convert.FromHexString(hex.GetString()!);if(bytes[2]==4)bytes[^1]|=1;Replay(f,new[]{Convert.ToHexString(bytes)});
        }
        Replay(f,b.GetProperty("syncFrames"));f.Tick();
        Assert.True(Profiles(f.Players).TryCapture(c,out var second,out _,out _));
        Assert.Equal((byte)3,second!.Value.ConsumableUnlockFlags);
        Assert.Equal((short)0,store.CaptureFundedQuantities(c)[0]);Assert.True(store.TryGet(c,0,out var physical));Assert.Equal((short)2,physical.Stack);
    }

    public static void LifecycleBoundaries()
    {
        using var input=Read("LinuxFirst");
        using var gzip=new GZipStream(input,CompressionMode.Decompress);using var data=JsonDocument.Parse(gzip);
        var row=data.RootElement.EnumerateArray().Single(r=>r.GetProperty("itemId").GetInt32()==5337 && r.GetProperty("local").GetBoolean() && r.GetProperty("mode").GetString()=="ordinary");
        // Explicit callback, late-provider and stale-input variants of the same genuine source batch.
        for(int mode=0;mode<6;mode++)
        {
            using var f=new Fixture("inactive-first",true);var c=f.Connections[2];var member=f.Member(2);var store=Inventory(f.Players);
            f.Apply(new PlayerAppearanceRuntimeCommand(c,new(c.Player.Slot,0,0,0,0,"configured",0,0,0,default,default,default,default,default,default,default,0,0,0)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,3,0,5337,0)));f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            while(!f.Players.CanApplyShopSource(c.Source))f.Players.TickShopJournalWork();var q=new short[59];q[0]=1;store.AdoptFundedQuantities(c,q);
            member.ItemPhase=member.ItemPhase!.Value with {Selected=member.ItemPhase.Value.Selected with {ReleaseUseItem=true,Animation=0,ItemTime=0}};
            f.Move(2,true);Replay(f,row.GetProperty("checkFrames"));Replay(f,row.GetProperty("syncFrames"));
            var random=(int[])RandomState(f.Random).Clone();uint cursor=RandomCursor(f.Random);int callbacks=0;bool provider=false;
            if(mode<=2)f.Events.OnEquipment=(connection,report)=>
            {
                if(connection!=c||report.SlotId!=0||report.Stack!=2)return;
                callbacks++;
                Assert.True(Profiles(f.Players).TryCapture(c,out var paid,out _,out _));Assert.Equal((byte)1,paid!.Value.ConsumableUnlockFlags);
                Assert.Equal((short)0,store.CaptureFundedQuantities(c)[0]);Assert.True(store.TryGet(c,0,out var physical));Assert.Equal((short)2,physical.Stack);
                if(mode==0)throw new CallbackFailure();
                if(mode==1)Replay(f,row.GetProperty("syncFrames"));
                if(mode==2)f.Apply(new PlayerDisconnectRuntimeCommand(c));
            };
            if(mode==3)f.Move(2,false);
            if(mode==4)f.Players.SetNpcHealthGrapplingFacts(_=>{if(!provider){provider=true;member.OwnedPermanentUpgradeMask^=2;}return false;});
            if(mode==5)f.Players.SetNpcHealthGrapplingFacts(_=>{if(!provider){provider=true;f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,1,1,0,9,0)));}return false;});
            bool threw=false;try{f.Tick();}catch(CallbackFailure){threw=true;}
            f.Events.OnEquipment=null;
            Assert.Equal(mode==0,threw);
            if(mode<=2)Assert.Equal(1,callbacks);
            if(mode==2){Assert.False(f.Players.TryGet(c.Player.Slot.Value,out _));continue;}
            Assert.True(Profiles(f.Players).TryCapture(c,out var actual,out _,out _));
            bool accepted=mode<=1;
            Assert.Equal(accepted?(byte)1:(byte)0,actual!.Value.ConsumableUnlockFlags);
            Assert.Equal(accepted?(short)0:(short)1,store.CaptureFundedQuantities(c)[0]);
            if(mode is 4 or 5){Assert.Equal(random,RandomState(f.Random));Assert.Equal(cursor,RandomCursor(f.Random));}
            if(mode==4)Assert.True(provider);
            if(accepted)
            {
                // Retire the selected receipt; ownership survives real transfer detach/attach.
                f.Move(2,false);f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,3,0,2,0)));f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                while(!f.Players.CanApplyShopSource(c.Source))f.Players.TickShopJournalWork();
                var detached=new TaskCompletionSource<RuntimePlayerTransferState?>();f.Apply(new PlayerTransferDetachRuntimeCommand(c,detached));
                Assert.True(detached.Task.IsCompletedSuccessfully);Assert.NotNull(detached.Task.Result);
                var attached=new TaskCompletionSource<bool>();f.Apply(new PlayerTransferAttachRuntimeCommand(c,detached.Task.Result!,100,103,true,false,attached));
                Assert.True(attached.Task.IsCompletedSuccessfully);Assert.True(attached.Task.Result);
                var returned=f.Member(2);Assert.Equal((byte)1,returned.OwnedPermanentUpgradeMask);Assert.Null(returned.ItemPhase?.SelectedPotionUse);
                f.Apply(new PlayerAppearanceRuntimeCommand(c,actual.Value with {ConsumableUnlockFlags=0}));
                Assert.True(Profiles(f.Players).TryCapture(c,out var protectedAppearance,out _,out _));Assert.Equal((byte)1,protectedAppearance!.Value.ConsumableUnlockFlags);
                Assert.Equal((short)0,store.CaptureFundedQuantities(c)[0]);
            }
        }
    }

    public static void ImportedGalaxyBoundary()
    {
        using var f = new Fixture("inactive-first", true);
        var c=f.Connections[2];var member=f.Member(2);
        var appearance = new PlayerAppearanceCommitRequest(c.Player.Slot,0,0,0,0,"configured",0,0,0,default,default,default,default,default,default,default,0,0,8);
        f.Apply(new PlayerAppearanceRuntimeCommand(c,appearance));
        Assert.Equal(.03f,member.Luck);
        Assert.Null(member.ItemPhase!.Value.PermanentPhaseFlags);
        f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,0,3,0,5340,0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        while(!f.Players.CanApplyShopSource(c.Source)) f.Players.TickShopJournalWork();
        member.ItemPhase=member.ItemPhase!.Value with {Selected=member.ItemPhase.Value.Selected with {ReleaseUseItem=true,Animation=0,ItemTime=0}};
        f.Move(2,true);
        Assert.Equal((byte)8,member.OwnedPermanentUpgradeMask);
        Assert.Equal(.03f,member.Luck);
        // An actual accepted appearance command triggers the existing luck recalculation.
        f.Apply(new PlayerAppearanceRuntimeCommand(c,appearance));
        Assert.Equal(.03f,member.Luck);
        // Unknown high unlock bit is a typed internal boundary, not a natural wire claim.
        using var heart=new Fixture("inactive-first",true);var hc=heart.Connections[2];var hm=heart.Member(2);heart.Players.SetCombatEquipmentWorldModes(true,false);
        heart.Apply(new PlayerAppearanceRuntimeCommand(hc,appearance with {PlayerSlot=hc.Player.Slot,ConsumableUnlockFlags=0}));
        heart.Apply(new PlayerEquipmentRuntimeCommand(hc,new(hc.Player.Slot,0,3,0,3335,0)));heart.Apply(new PlayerInventorySyncCompleteRuntimeCommand(hc));
        while(!heart.Players.CanApplyShopSource(hc.Source))heart.Players.TickShopJournalWork();
        hm.ItemPhase=hm.ItemPhase!.Value with {Selected=hm.ItemPhase.Value.Selected with {ReleaseUseItem=true,Animation=0,ItemTime=0}};heart.Move(2,true);
        Assert.Equal((byte)128,hm.OwnedPermanentUpgradeMask);
        heart.Apply(new PlayerAppearanceRuntimeCommand(hc,appearance with {PlayerSlot=hc.Player.Slot,ConsumableUnlockFlags=128,DifficultyFlags=4}));
        Assert.True(Profiles(heart.Players).TryCapture(hc,out var deniedHeart,out _,out _));Assert.Equal((byte)0,(byte)(deniedHeart!.Value.DifficultyFlags&4));
    }

    private static Stream Read(string suffix) => typeof(PermanentEightBoundaryChecks).Assembly.GetManifestResourceStream("PermanentUnlock" + suffix + "1458") ?? throw new InvalidOperationException("Missing original permanent source fixture.");

    private static void Replay(Fixture f, JsonElement frames)
        => Replay(f, frames.EnumerateArray().Select(hex => hex.GetString()!));

    private static void Replay(Fixture f, IEnumerable<string> frames)
    {
        var c = f.Connections[2]; var member = f.Member(2);
        foreach (var hex in frames)
        {
            byte[] bytes = Convert.FromHexString(hex);
            var frame = new TerrariaFrame((ushort)bytes.Length, bytes[2], new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
            switch (bytes[2])
            {
                case 4:
                    Assert.Equal(TerrariaPlayerAppearanceDecodeResult.Decoded, TerrariaPlayerAppearanceCodec.TryDecode(in frame, out var a));
                    f.Apply(new PlayerAppearanceRuntimeCommand(c, new(c.Player.Slot, a.SkinVariant, a.VoiceVariant, a.VoicePitchOffset, a.Hair, a.Name, a.HairDye, a.HideVisibleAccessory, a.HideMisc, new(a.HairColor.R, a.HairColor.G, a.HairColor.B), new(a.SkinColor.R, a.SkinColor.G, a.SkinColor.B), new(a.EyeColor.R, a.EyeColor.G, a.EyeColor.B), new(a.ShirtColor.R, a.ShirtColor.G, a.ShirtColor.B), new(a.UnderShirtColor.R, a.UnderShirtColor.G, a.UnderShirtColor.B), new(a.PantsColor.R, a.PantsColor.G, a.PantsColor.B), new(a.ShoeColor.R, a.ShoeColor.G, a.ShoeColor.B), a.DifficultyFlags, a.TorchAndCartFlags, a.ConsumableUnlockFlags))); break;
                case 5:
                    Assert.Equal(TerrariaPlayerEquipmentDecodeResult.Decoded, TerrariaPlayerEquipmentCodec.TryDecode(in frame, out var item));
                    f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, item.SlotId, item.Stack, item.Prefix, item.ItemNetId, item.ItemFlags))); break;
                case 16:
                    Assert.Equal(TerrariaPlayerHealthDecodeResult.Decoded, TerrariaPlayerVitalsCodec.TryDecodeHealth(in frame, out var health));
                    f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, health.Life, health.MaxLife))); break;
                case 13:
                    Assert.Equal(TerrariaPlayerMovementDecodeResult.Decoded, TerrariaPlayerMovementDecoder.TryDecode(in frame, out var move));
                    f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, move.ControlFlags, move.MovementFlags, move.MiscFlags1, move.MiscFlags2, move.SelectedItem, member.PositionX, member.PositionY, move.HasVelocity, move.VelocityX, move.VelocityY, move.HasMount, move.MountType, false, 0, 0, 0, 0, false, 0, 0))); break;
                case 35:
                    Assert.Equal(6, bytes.Length);
                    f.Apply(new PlayerVitalsPresentationRuntimeCommand(c, 35, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(4)))); break;
                case 138: f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c)); break;
                default: throw new InvalidOperationException("Unexpected life source packet " + bytes[2]);
            }
        }
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] RandomState(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint RandomCursor(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "transferProfiles")]
    private static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateRandom")]
    private static extern ref VanillaUnifiedRandom1458? UpdateRandom(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
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
                    playerUpdateRandomSeed: new(0),
                    sourcePlayerWorld: new(400, 300, 80, false, false)
                    { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows(), JourneyMode = false });
                ToolPhaseWorldFixture1458.BindFreshInactiveEvent(state, Tiles);
                Players = Composition(state).Players;
                Random = UpdateRandom(Players)!;
            }
            else { Players = new(Events, Tiles); Random = new(0); }
            // These prefix-none references have identical clocks on both source platforms.
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false)
            { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows(), JourneyMode = false });
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
            membership.Commit(new RuntimePlayerMember
            {
                Slot = new(255),
                Revision = 1,
                Connection = new(GameCommandSourceId.FromConnection(99255),
                new(new(255), new(1))),
                ItemPhase = null,
                PhysicsPhase = null
            });
            foreach (byte slot in mode == "reverse-join" ? new byte[] { 2, 0 } : new byte[] { 0, 2 })
            {
                if (slot == 0 && mode == "inactive-first") continue;
                var session = sessions[slot]; session.ObserveWorldRequest(); session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99000 + slot), session.Handle);
                Connections.Add(slot, connection);
                short id = slot == 2 ? (short)0 : slot == 0 && mode == "potion-first" ? (short)28 : slot == 0 && mode == "weapon98-first" ? (short)98 : (short)0;
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
    private sealed class CallbackFailure : Exception;
    private sealed class EventSink : IRuntimePlayerEventSink
    {
        internal int Count;
        internal readonly List<byte> Seen = [];
        internal readonly List<ConnectionHandle> HealthConnections = [];
        internal readonly List<ConnectionHandle> BuffConnections = [];
        internal Action<ConnectionHandle, PlayerEquipmentCommitRequest>? OnEquipment = null;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) { Count++; Seen.Add(5); OnEquipment?.Invoke(c, r); }
        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r) { Count++; Seen.Add(16); HealthConnections.Add(c); }
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r) { Count++; Seen.Add(42); }
        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r) { Count++; Seen.Add(50); BuffConnections.Add(c); }
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
