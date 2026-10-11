using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using TerraRuntime.Core.Worlds;
using System.IO.Compression;
using System.Text.Json;
using System.Buffers;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

using Xunit;
namespace TerraRuntime.Tests;
public sealed class OwnedOversizedCoinPickupAuthority1458Tests {
[Fact]
public void Source_owned_oversized_coins_preserve_physical_images_and_exact_private_copper(){ VerifySourcePickup(); VerifyCapabilityLifecycle(); VerifyConnectedPickupCursorChain(); }
private static void VerifySourcePickup() {
int checks = 0, rows = 0, slices = 0, maximumWork = 0; var failures = new List<object>();
void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
RuntimePlayerInventoryItem Item(JsonElement x)=>I(x,"type")==0||I(x,"stack")<=0?default:new(new(I(x,"type")),checked((short)I(x,"stack")),new(I(x,"prefix")),x.GetProperty("favorite").GetBoolean()?(byte)1:(byte)0);
int corpus=0;
for (int platform=0;platform<2;platform++)
{
    using var compressed=typeof(OwnedOversizedCoinPickupAuthority1458Tests).Assembly.GetManifestResourceStream(platform==0 ? "OwnedOversizedPickupLinux1458" : "OwnedOversizedPickupWindows1458")!;
    using var input=new GZipStream(compressed,CompressionMode.Decompress);
    using var document=JsonDocument.Parse(input);
    for(int fundingMode=0;fundingMode<4;fundingMode++)
    foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
    {

        rows++;
        try {
        string name = row.GetProperty("profile").GetString()!;
        var before = row.GetProperty("before219"); var after = row.GetProperty("afterSync219");
        var incoming = row.GetProperty("beforeIncoming");
        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
        players.SetPlayerUpdateRandom(new(0));
        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
        players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
        var memberOwner = Membership(players);
        var items = Inventory(players);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
        var member = new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400,
            PositionX = 400, PositionY = 400, ItemAnimation = 0 };
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = 0 } };
        memberOwner.Commit(member); Check(items.TryAttach(connection), name+" attach");
        var image = before.EnumerateArray().Take(59).Select(Item).ToArray();

        var q = new short[59]; var money = new long[59];
        for (short i = 0; i < 59; i++)
        {
            Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot,i)), name+" initial inventory");
            if (VanillaCoinFacts.TryGetValue(image[i].ItemType,out long unit)) money[i] = unit * Math.Max(0,(int)image[i].Stack);
            else q[i] = Math.Max((short)0,image[i].Stack);
        }
        long physicalCopper=(long)I(incoming,"stack")*1_000_000;
        long requestedCopper=fundingMode switch {0=>0,1=>1,2=>physicalCopper-1,_=>physicalCopper};
        items.AdoptFundedQuantities(connection,q,money);
        var bankImage = before.EnumerateArray().Skip(59).Select(Item).ToArray();
        var bankCopper = new long[160]; var bankQuantity = new short[160]; var bankCaps = new short[800]; Array.Fill(bankCaps,(short)-1);
        for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType,out long unit)) bankCopper[i] = unit * Math.Max(0,(int)bankImage[i].Stack);
        for (int i = 0; i < 160; i++) if (bankCopper[i] == 0) bankQuantity[i] = Math.Max((short)0,bankImage[i].Stack);
        Check(items.TryAdoptShopBanks(connection,bankImage,bankCopper,bankQuantity,bankCaps),name+" configured banks");
        // Another actor advances the GLOBAL serial after player0's last own stamp.
        // The first cursor report must still retain the source-valid bank alternative.
        var other = new ConnectionHandle(GameCommandSourceId.FromConnection(90002), new(new(1), new(1)));
        memberOwner.Commit(new RuntimePlayerMember { Connection = other, Slot = other.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400 });
        Check(items.TryAttach(other), name+" disjoint actor attach");
        Check(players.TryApply(new PlayerEquipmentRuntimeCommand(other, new(other.Player.Slot, 2, 1, 0, 9, 0))),
            name+" actual disjoint5 before bank cursor report");
        var world = new RuntimeWorldItemStore();
        var random = new SystemWorldItemSpawnRandom(0);
        var authority = new WorldItemAuthority(players,world,random,null);
        var proposal=new WorldItemDropStateUpdate(402,413,-.5f,-2.5f,checked((short)I(incoming,"stack")),0,WorldItemOwnershipMode.ReserveForLocalPlayer,74,false,0,100);
        using(var allocation=world.CreateAllocationPreview([new WorldItemAllocationPlayer1458(0,400,400,20,42)]))
        {
            Check(allocation.TrySpawnFundedCoinSource(proposal,out var slot,0,requestedCopper),name+" configured genuine incoming physical/fractional ground");
            Check(allocation.TryClaim()&&allocation.TryAdoptUnpublished(out var publication),name+" prepared incoming ground adoption");
        }
        var randomBefore=random.SourceRandom.Clone();
        authority.TickOwnedHumanPickups();
        Check(random.SourceRandom.HasSameState(randomBefore),name+" contact pickup consumes no spawn RNG");
        int expectedResidual=I(row.GetProperty("residual"),"stack");
        if(expectedResidual==0)Check(world.ActiveCount==0,name+" source whole oversized pickup ground retired");
        else Check(world.TryGetActive(0,out var residual)&&residual.Stack==expectedResidual,name+" exact source residual or unknown-budget retention");
        long retainedGround=0;
        if(world.TryGetActive(0,out var remainingGround))Check(world.TryGetFundedCoinValue(remainingGround,out retainedGround),name+" residual ground budget query");
        Check(items.CaptureFundedCoinValues(connection).Sum()+retainedGround==money.Sum()+requestedCopper,name+" exact fractional coin conservation inventory plus residual");
        var expected = after.EnumerateArray().Take(59).Select(Item).ToArray();
        for(short i=0;i<59;i++) Check(items.TryGet(connection,i,out var current)&&current==expected[i],name+" source inventory slot"+i);
        var banks = new RuntimePlayerInventoryItem[160];var funds=new long[160];
        Check(items.TryCaptureShopBanks(connection,banks,funds),name+" capture final banks");
        Check(banks.AsSpan().SequenceEqual(after.EnumerateArray().Skip(59).Select(Item).ToArray()),name+" full160 source banks");
        Check(items.TryCaptureShopBankOwnership(connection,out var actualQuantity,out var actualCaps),name+" ownership capture");
        var expectedBanks = after.EnumerateArray().Skip(59).Select(Item).ToArray();
        var mainQuantity = items.CaptureFundedQuantities(connection); var mainCopper = items.CaptureFundedCoinValues(connection);
        for(int i=0;i<59;i++)
        {
            long value = VanillaCoinFacts.TryGetValue(expected[i].ItemType,out var unit) ? unit*Math.Max(0,(int)expected[i].Stack):0;
            Check(mainCopper[i]>=0&&mainCopper[i]<=value,name+" exact main copper bounds"+i);
            Check(mainQuantity[i]==(value==0?Math.Max(0,(int)expected[i].Stack):0),name+" funded main quantity"+i);
        }
        for(int i=0;i<160;i++)
        {
            long value = VanillaCoinFacts.TryGetValue(expectedBanks[i].ItemType,out var unit) ? unit*Math.Max(0,(int)expectedBanks[i].Stack):0;
            Check(funds[i]==value,name+" funded bank copper"+i);
            Check(actualQuantity[i]==(value==0?Math.Max(0,(int)expectedBanks[i].Stack):0),name+" funded bank quantity"+i);
        }
        // Original context1 refuses a noncoin mouse. Forged raw images must not
        // acquire custody even though the legacy raw receive may conservatively destroy it.
        if(expectedResidual==0) {
            short sourceSlot=-1;
            for(short k=50;k<54;k++) if(expected[k].ItemType.Value==74 && expected[k].Stack==I(incoming,"stack")) sourceSlot=k;
            Check(sourceSlot>=50,name+" whole source coin slot");
            var sourceCoin=expected[sourceSlot];var oldCursor=expected[58];
            Check(!items.TryPrepareReportedCursorMove(connection,sourceSlot,oldCursor,0,0,out _),name+" context1 noncoin cursor refuses owned exchange");
            long beforeBudget=items.CaptureFundedCoinValues(connection).Sum();long beforeQuantity=items.CaptureFundedQuantities(connection).Sum(x=>(long)x);
            Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,oldCursor.ToCommitRequest(connection.Player.Slot,sourceSlot))),name+" forged coin exchange first raw image");
            Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,sourceCoin.ToCommitRequest(connection.Player.Slot,58))),name+" forged coin exchange raw cursor");
            Check(players.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(connection)),name+" forged delimiter138");
            int workTicks=0;while(!players.CanApplyShopSource(connection.Source)){Check(++workTicks<=10000,name+" finite forged journal retirement");players.TickShopJournalWork();}
            Check(items.CaptureFundedCoinValues(connection).Sum()<=beforeBudget,name+" forged exchange cannot mint copper");
            Check(items.CaptureFundedCoinValues(connection)[58]==0,name+" refused coin UI does not grant cursor value");
            Check(items.CaptureFundedQuantities(connection).Sum(x=>(long)x)<=beforeQuantity,name+" forged exchange cannot mint noncoin quantity");
        }
        Check(memberOwner.TryGet(connection,out var acceptedMember) && acceptedMember.ItemPhase is { ShopOpen:false },name+" bank move does not open shop");
        } catch (Exception ex) { failures.Add(new { corpus, platform, name = row.GetProperty("profile").GetString(), fundingMode, error = ex.Message }); }
    }
}

Assert.True(failures.Count==0,JsonSerializer.Serialize(new {checks,rows,slices,maximumWork,failures}));

}
private static void VerifyCapabilityLifecycle() {
int checks = 0, rows = 0, slices = 0, maximumWork = 0; var failures = new List<object>();
void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
RuntimePlayerInventoryItem Item(JsonElement x)=>I(x,"type")==0||I(x,"stack")<=0?default:new(new(I(x,"type")),checked((short)I(x,"stack")),new(I(x,"prefix")),x.GetProperty("favorite").GetBoolean()?(byte)1:(byte)0);
int corpus=0;
for (int platform=0;platform<2;platform++)
{
    using var compressed=typeof(OwnedOversizedCoinPickupAuthority1458Tests).Assembly.GetManifestResourceStream(platform==0 ? "OwnedOversizedPickupLinux1458" : "OwnedOversizedPickupWindows1458")!;
    using var input=new GZipStream(compressed,CompressionMode.Decompress);
    using var document=JsonDocument.Parse(input);
    for(int fundingMode=0;fundingMode<1;fundingMode++)
    for(int capabilityMode=0;capabilityMode<4;capabilityMode++)
    foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
    {

        rows++;
        try {
        string name = row.GetProperty("profile").GetString()!;
        var before = row.GetProperty("before219"); var after = row.GetProperty("afterSync219");
        var incoming = row.GetProperty("beforeIncoming");
        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
        players.SetPlayerUpdateRandom(new(0));
        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
        players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
        var memberOwner = Membership(players);
        var items = Inventory(players);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
        var member = new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400,
            PositionX = 400, PositionY = 400, ItemAnimation = 0 };
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = 0 } };
        memberOwner.Commit(member); Check(items.TryAttach(connection), name+" attach");
        var image = before.EnumerateArray().Take(59).Select(Item).ToArray();

        var q = new short[59]; var money = new long[59];
        for (short i = 0; i < 59; i++)
        {
            Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot,i)), name+" initial inventory");
            if (VanillaCoinFacts.TryGetValue(image[i].ItemType,out long unit)) money[i] = unit * Math.Max(0,(int)image[i].Stack);
            else q[i] = Math.Max((short)0,image[i].Stack);
        }
        long physicalCopper=(long)I(incoming,"stack")*1_000_000;
        long requestedCopper=fundingMode switch {0=>0,1=>1,2=>physicalCopper-1,_=>physicalCopper};
        items.AdoptFundedQuantities(connection,q,money);
        var bankImage = before.EnumerateArray().Skip(59).Select(Item).ToArray();
        var bankCopper = new long[160]; var bankQuantity = new short[160]; var bankCaps = new short[800]; Array.Fill(bankCaps,(short)-1);
        for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType,out long unit)) bankCopper[i] = unit * Math.Max(0,(int)bankImage[i].Stack);
        for (int i = 0; i < 160; i++) if (bankCopper[i] == 0) bankQuantity[i] = Math.Max((short)0,bankImage[i].Stack);
        Check(items.TryAdoptShopBanks(connection,bankImage,bankCopper,bankQuantity,bankCaps),name+" configured banks");
        // Another actor advances the GLOBAL serial after player0's last own stamp.
        // The first cursor report must still retain the source-valid bank alternative.
        var other = new ConnectionHandle(GameCommandSourceId.FromConnection(90002), new(new(1), new(1)));
        memberOwner.Commit(new RuntimePlayerMember { Connection = other, Slot = other.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400 });
        Check(items.TryAttach(other), name+" disjoint actor attach");
        Check(players.TryApply(new PlayerEquipmentRuntimeCommand(other, new(other.Player.Slot, 2, 1, 0, 9, 0))),
            name+" actual disjoint5 before bank cursor report");
        var world = new RuntimeWorldItemStore();
        var random = new SystemWorldItemSpawnRandom(0);
        var authority = new WorldItemAuthority(players,world,random,null);
        var proposal=new WorldItemDropStateUpdate(402,413,-.5f,-2.5f,checked((short)I(incoming,"stack")),0,WorldItemOwnershipMode.ReserveForLocalPlayer,74,false,0,100);
        using(var allocation=world.CreateAllocationPreview([new WorldItemAllocationPlayer1458(0,400,400,20,42)]))
        {
            Check(capabilityMode==0?allocation.TrySpawnSource(proposal,0,out _,sourceLocalPlayerId:0,creationSource:WorldItemCreationSource1458.ClientSynchronization):allocation.TrySpawnFundedCoinSource(proposal,out _,0,requestedCopper),name+" configured genuine incoming physical/fractional ground");
            Check(allocation.TryClaim()&&allocation.TryAdoptUnpublished(out var publication),name+" prepared incoming ground adoption");
        }
        Check(world.TryGetActive(0,out var original),name+" active source generation capture");
        if(capabilityMode==0)Check(!world.HasOwnedCoinPhysical(original),name+" raw0 no physical capability");
        else Check(world.HasOwnedCoinPhysical(original),name+" prepared0 exact-generation physical capability");
        if(capabilityMode==1)Check(world.TryApplyDrop(0,proposal,out var rawEcho)&&!world.HasOwnedCoinPhysical(rawEcho),name+" exact raw echo clears physical capability");
        if(capabilityMode==2)
        {
            Check(world.TryRemove(0,out _),name+" remove source generation");
            Check(world.TryApplyDrop(0,proposal,out var reused)&&reused.Handle!=original.Handle&&!world.HasOwnedCoinPhysical(reused),name+" raw reuse cannot inherit physical capability");
        }
        if(capabilityMode==3)
        {
            world.TickReservationTimers();
            Check(!world.HasOwnedCoinPhysical(original),name+" stale pre-timer snapshot cannot prove physical capability");
            Check(world.TryGetActive(0,out var timed)&&world.HasOwnedCoinPhysical(timed),name+" source timer preserves current physical capability");
        }
        var randomBefore=random.SourceRandom.Clone();
        authority.TickOwnedHumanPickups();
        Check(random.SourceRandom.HasSameState(randomBefore),name+" contact pickup consumes no spawn RNG");
        int expectedResidual=capabilityMode<3?I(incoming,"stack"):I(row.GetProperty("residual"),"stack");
        if(expectedResidual==0)Check(world.ActiveCount==0,name+" source whole oversized pickup ground retired");
        else Check(world.TryGetActive(0,out var residual)&&residual.Stack==expectedResidual,name+" exact source residual or unknown-budget retention");
        long retainedGround=0;
        if(world.TryGetActive(0,out var remainingGround))Check(world.TryGetFundedCoinValue(remainingGround,out retainedGround),name+" residual ground budget query");
        Check(items.CaptureFundedCoinValues(connection).Sum()+retainedGround==money.Sum()+requestedCopper,name+" exact fractional coin conservation inventory plus residual");
        var expected = (capabilityMode<3?before:after).EnumerateArray().Take(59).Select(Item).ToArray();
        for(short i=0;i<59;i++) Check(items.TryGet(connection,i,out var current)&&current==expected[i],name+" source inventory slot"+i);
        var banks = new RuntimePlayerInventoryItem[160];var funds=new long[160];
        Check(items.TryCaptureShopBanks(connection,banks,funds),name+" capture final banks");
        Check(banks.AsSpan().SequenceEqual(after.EnumerateArray().Skip(59).Select(Item).ToArray()),name+" full160 source banks");
        Check(items.TryCaptureShopBankOwnership(connection,out var actualQuantity,out var actualCaps),name+" ownership capture");
        var expectedBanks = after.EnumerateArray().Skip(59).Select(Item).ToArray();
        var mainQuantity = items.CaptureFundedQuantities(connection); var mainCopper = items.CaptureFundedCoinValues(connection);
        for(int i=0;i<59;i++)
        {
            long value = VanillaCoinFacts.TryGetValue(expected[i].ItemType,out var unit) ? unit*Math.Max(0,(int)expected[i].Stack):0;
            Check(mainCopper[i]>=0&&mainCopper[i]<=value,name+" exact main copper bounds"+i);
            Check(mainQuantity[i]==(value==0?Math.Max(0,(int)expected[i].Stack):0),name+" funded main quantity"+i);
        }
        for(int i=0;i<160;i++)
        {
            long value = VanillaCoinFacts.TryGetValue(expectedBanks[i].ItemType,out var unit) ? unit*Math.Max(0,(int)expectedBanks[i].Stack):0;
            Check(funds[i]==value,name+" funded bank copper"+i);
            Check(actualQuantity[i]==(value==0?Math.Max(0,(int)expectedBanks[i].Stack):0),name+" funded bank quantity"+i);
        }
        Check(memberOwner.TryGet(connection,out var acceptedMember) && acceptedMember.ItemPhase is { ShopOpen:false },name+" bank move does not open shop");
        } catch (Exception ex) { failures.Add(new { corpus, platform, name = row.GetProperty("profile").GetString(), fundingMode, capabilityMode, error = ex.Message }); }
    }
}

Assert.True(failures.Count==0,JsonSerializer.Serialize(new {checks,rows,slices,maximumWork,failures}));

}
private static void VerifyConnectedPickupCursorChain() {
int checks = 0, rows = 0, slices = 0, maximumWork = 0; var failures = new List<object>();
void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
RuntimePlayerInventoryItem Item(JsonElement x)=>I(x,"type")==0||I(x,"stack")<=0?default:new(new(I(x,"type")),checked((short)I(x,"stack")),new(I(x,"prefix")),x.GetProperty("favorite").GetBoolean()?(byte)1:(byte)0);
int corpus=0;
for (int platform=0;platform<2;platform++)
{
    using var compressed=typeof(OwnedOversizedCoinPickupAuthority1458Tests).Assembly.GetManifestResourceStream(platform==0 ? "OwnedOversizedCursorChainLinux1458" : "OwnedOversizedCursorChainWindows1458")!;
    using var input=new GZipStream(compressed,CompressionMode.Decompress);
    using var document=JsonDocument.Parse(input);
    for(int fundingMode=0;fundingMode<4;fundingMode++)
    foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
    {

        rows++;
        try {
        string name = row.GetProperty("profile").GetString()!;
        var before = row.GetProperty("before219"); var after = row.GetProperty("afterPickup219");
        var incoming = row.GetProperty("beforeIncoming");
        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
        players.SetPlayerUpdateRandom(new(0));
        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
        players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
        var memberOwner = Membership(players);
        var items = Inventory(players);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
        var member = new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400,
            PositionX = 400, PositionY = 400, ItemAnimation = 0 };
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = 0 } };
        memberOwner.Commit(member); Check(items.TryAttach(connection), name+" attach");
        var image = before.EnumerateArray().Take(59).Select(Item).ToArray();

        var q = new short[59]; var money = new long[59];
        for (short i = 0; i < 59; i++)
        {
            Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot,i)), name+" initial inventory");
            if (VanillaCoinFacts.TryGetValue(image[i].ItemType,out long unit)) money[i] = unit * Math.Max(0,(int)image[i].Stack);
            else q[i] = Math.Max((short)0,image[i].Stack);
        }
        long physicalCopper=(long)I(incoming,"stack")*1_000_000;
        long requestedCopper=fundingMode switch {0=>0,1=>1,2=>physicalCopper-1,_=>physicalCopper};
        items.AdoptFundedQuantities(connection,q,money);
        var bankImage = before.EnumerateArray().Skip(59).Select(Item).ToArray();
        var bankCopper = new long[160]; var bankQuantity = new short[160]; var bankCaps = new short[800]; Array.Fill(bankCaps,(short)-1);
        for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType,out long unit)) bankCopper[i] = unit * Math.Max(0,(int)bankImage[i].Stack);
        for (int i = 0; i < 160; i++) if (bankCopper[i] == 0) bankQuantity[i] = Math.Max((short)0,bankImage[i].Stack);
        Check(items.TryAdoptShopBanks(connection,bankImage,bankCopper,bankQuantity,bankCaps),name+" configured banks");
        // Another actor advances the GLOBAL serial after player0's last own stamp.
        // The first cursor report must still retain the source-valid bank alternative.
        var other = new ConnectionHandle(GameCommandSourceId.FromConnection(90002), new(new(1), new(1)));
        memberOwner.Commit(new RuntimePlayerMember { Connection = other, Slot = other.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400 });
        Check(items.TryAttach(other), name+" disjoint actor attach");
        Check(players.TryApply(new PlayerEquipmentRuntimeCommand(other, new(other.Player.Slot, 2, 1, 0, 9, 0))),
            name+" actual disjoint5 before bank cursor report");
        var world = new RuntimeWorldItemStore();
        var random = new SystemWorldItemSpawnRandom(0);
        var authority = new WorldItemAuthority(players,world,random,null);
        var proposal=new WorldItemDropStateUpdate(402,413,-.5f,-2.5f,checked((short)I(incoming,"stack")),0,WorldItemOwnershipMode.ReserveForLocalPlayer,74,false,0,100);
        using(var allocation=world.CreateAllocationPreview([new WorldItemAllocationPlayer1458(0,400,400,20,42)]))
        {
            Check(allocation.TrySpawnFundedCoinSource(proposal,out var slot,0,requestedCopper),name+" configured genuine incoming physical/fractional ground");
            Check(allocation.TryClaim()&&allocation.TryAdoptUnpublished(out var publication),name+" prepared incoming ground adoption");
        }
        var randomBefore=random.SourceRandom.Clone();
        authority.TickOwnedHumanPickups();
        Check(random.SourceRandom.HasSameState(randomBefore),name+" contact pickup consumes no spawn RNG");
        int expectedResidual=I(row.GetProperty("residual"),"stack");
        if(expectedResidual==0)Check(world.ActiveCount==0,name+" source whole oversized pickup ground retired");
        else Check(world.TryGetActive(0,out var residual)&&residual.Stack==expectedResidual,name+" exact source residual or unknown-budget retention");
        long retainedGround=0;
        if(world.TryGetActive(0,out var remainingGround))Check(world.TryGetFundedCoinValue(remainingGround,out retainedGround),name+" residual ground budget query");
        Check(items.CaptureFundedCoinValues(connection).Sum()+retainedGround==money.Sum()+requestedCopper,name+" exact fractional coin conservation inventory plus residual");
        var expected = after.EnumerateArray().Take(59).Select(Item).ToArray();
        for(short i=0;i<59;i++) Check(items.TryGet(connection,i,out var current)&&current==expected[i],name+" source inventory slot"+i);
        var banks = new RuntimePlayerInventoryItem[160];var funds=new long[160];
        Check(items.TryCaptureShopBanks(connection,banks,funds),name+" capture final banks");
        Check(banks.AsSpan().SequenceEqual(after.EnumerateArray().Skip(59).Select(Item).ToArray()),name+" full160 source banks");
        Check(items.TryCaptureShopBankOwnership(connection,out var actualQuantity,out var actualCaps),name+" ownership capture");
        var expectedBanks = after.EnumerateArray().Skip(59).Select(Item).ToArray();
        var mainQuantity = items.CaptureFundedQuantities(connection); var mainCopper = items.CaptureFundedCoinValues(connection);
        for(int i=0;i<59;i++)
        {
            long value = VanillaCoinFacts.TryGetValue(expected[i].ItemType,out var unit) ? unit*Math.Max(0,(int)expected[i].Stack):0;
            Check(mainCopper[i]>=0&&mainCopper[i]<=value,name+" exact main copper bounds"+i);
            Check(mainQuantity[i]==(value==0?Math.Max(0,(int)expected[i].Stack):0),name+" funded main quantity"+i);
        }
        for(int i=0;i<160;i++)
        {
            long value = VanillaCoinFacts.TryGetValue(expectedBanks[i].ItemType,out var unit) ? unit*Math.Max(0,(int)expectedBanks[i].Stack):0;
            Check(funds[i]==value,name+" funded bank copper"+i);
            Check(actualQuantity[i]==(value==0?Math.Max(0,(int)expectedBanks[i].Stack):0),name+" funded bank quantity"+i);
        }
        // Genuine client sync emits only58: initial and final coin53 are Air.
        foreach(var hex in row.GetProperty("syncFrames").EnumerateArray()) {
          var bytes=Convert.FromHexString(hex.GetString()!);
          if(bytes[2]==5) {
            var frame=new TerrariaFrame((ushort)bytes.Length,5,new ReadOnlySequence<byte>(bytes),new ReadOnlySequence<byte>(bytes.AsMemory(3)));
            Check(TerrariaPlayerEquipmentCodec.TryDecode(in frame,out var decoded)==TerrariaPlayerEquipmentDecodeResult.Decoded,name+" genuine source packet5 decode");
            Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,new(connection.Player.Slot,decoded.SlotId,decoded.Stack,decoded.Prefix,decoded.ItemNetId,decoded.ItemFlags))),name+" actual connected coin cursor image");
          } else if(bytes[2]==138) {
            Check(players.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(connection)),name+" actual genuine delimiter138");
            int ticks=0;while(!players.CanApplyShopSource(connection.Source)){Check(++ticks<=10000,name+" bounded genuine chain");players.TickShopJournalWork();Check(players.LastShopWorkSteps<=64,name+" global64");}
          }
        }
        var final=row.GetProperty("afterSync219").EnumerateArray().Select(Item).ToArray();
        for(short i=0;i<59;i++)Check(items.TryGet(connection,i,out var current)&&current==final[i],name+" genuine full59 chain after"+i);
        Check(items.TryCaptureShopBanks(connection,banks,funds)&&banks.AsSpan().SequenceEqual(final.AsSpan(59)),name+" genuine full160 banks after chain");
        Check(items.CaptureFundedCoinValues(connection).Sum()==money.Sum()+requestedCopper,name+" connected chain copper conserved");
        Check(items.CaptureFundedCoinValues(connection)[58]==requestedCopper,name+" connected cursor exact zero/fractional/full value");
        Check(memberOwner.TryGet(connection,out var acceptedMember) && acceptedMember.ItemPhase is { ShopOpen:false },name+" bank move does not open shop");
        } catch (Exception ex) { failures.Add(new { corpus, platform, name = row.GetProperty("profile").GetString(), fundingMode, error = ex.Message }); }
    }
}

Assert.True(failures.Count==0,JsonSerializer.Serialize(new {checks,rows,slices,maximumWork,failures}));
}
[UnsafeAccessor(UnsafeAccessorKind.Field,Name="membership")]
private static extern ref RuntimePlayerMembership Membership(PlayerAuthority owner);
[UnsafeAccessor(UnsafeAccessorKind.Field,Name="inventory")]
private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
}
