using System.Buffers;
using System.Collections.Concurrent;
using TerraRuntime.Core.Projectiles;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Protocol;
using TerraRuntime.World;
using System.IO.Compression;
using Xunit;
namespace TerraRuntime.Tests;
// Actual owned command receipts over configured captured source images.
// Private fixture initialization is managed-only; this does not assert Main/UI or Native parity.
public sealed class ShopInventoryAuthoritySource1458Tests
{
    private static bool Complete(PlayerAuthority players, ConnectionHandle connection)
    {
        bool applied = players.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
        int ticks = 0;
        while (!players.CanApplyShopSource(connection.Source))
        {
            if (++ticks > 100000) throw new InvalidOperationException("Shop continuation did not finish within its complete finite candidate bound.");
            players.TickShopJournalWork();
        }
        return applied;
    }

 [Fact] public void Captured_shop_batches_and_close_match_original_full_images() { SourceReplayCases.Run(); CloseSourceReplayCases.Run(); HiddenSourceReplayCases.Run(); }
 [Fact] public void Banks_preserve_source_gestures_quantity_and_transfer_custody() { BankSourceReplayCases.Run(); BankCustodyProbeCases.Run(); BankCoincidentCursorCases.Run(); BankRawProbeCases.Run(); TransferContextProbeCases.Run(); }
 [Fact] public void Paid_receipts_preserve_callback_and_hidden_batch_custody() { ConnectedProbeCases.Run(); FundingScratchProbeCases.Run(); HiddenExactUnitsProbeCases.Run(); HiddenAmbiguityProbeCases.Run(); ContinuationCoreCases.Run(); ContinuationApplicationCases.Run(); ContinuationCustodyCases.Run(); ContinuationBoundStateCases.Run(); }
 private static JsonDocument Read(string corpus,int platform) {
  using var stream=typeof(ShopInventoryAuthoritySource1458Tests).Assembly.GetManifestResourceStream("ShopConnectedOriginal1458");
  Assert.NotNull(stream);using var gzip=new GZipStream(stream!,CompressionMode.Decompress);using var all=JsonDocument.Parse(gzip);
  return JsonDocument.Parse(all.RootElement.GetProperty(corpus).GetProperty(platform==0?"Linux":"Windows").GetRawText());
 }
private static class SourceReplayCases { internal static void Run() {

int checks = 0, rows = 0;
void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
bool B(JsonElement x, string key) => x.GetProperty(key).GetBoolean();
RuntimePlayerInventoryItem Item(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0
    ? default : new(new(I(x,"type")), checked((short)I(x,"stack")), new(I(x,"prefix")), B(x,"favorite") ? (byte)1 : (byte)0);
ShopItem1458 Stock(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0 ? default : new(
    new(I(x,"type"), I(x,"stack"), I(x,"prefix"), B(x,"favorite")), I(x,"value"), B(x,"buyOnce"), B(x,"isAShopItem"));
T Owner<T>(PlayerAuthority players, string name) => (T)typeof(PlayerAuthority).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(players)!;
for (int platform = 0; platform < 2; platform++)
{
    using var document = Read("npc-shop-ui-original-source-v1", platform);
    foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
    {
        rows++;
        string name = row.GetProperty("spec").GetProperty("name").GetString()!;
        var before = row.GetProperty("before"); var after = row.GetProperty("afterSync");
        var spec = row.GetProperty("spec");
        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
        players.SetPlayerUpdateRandom(new(0));
        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
        players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
        var memberOwner = Owner<RuntimePlayerMembership>(players,"membership");
        var items = Owner<RuntimePlayerInventoryStore>(players,"inventory");
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
        var member = new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400,
            ItemAnimation = I(before,"animation") };
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = I(before,"itemTime") } };
        memberOwner.Commit(member); Check(items.TryAttach(connection), name+" attach");
        var image = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
        image[58] = Item(before.GetProperty("mouse"));
        var q = new short[59]; var money = new long[59];
        for (short i = 0; i < 59; i++)
        {
            Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot,i)), name+" initial inventory");
            if (VanillaCoinFacts.TryGetValue(image[i].ItemType,out long unit)) money[i] = unit * Math.Max(0,(int)image[i].Stack);
            else q[i] = Math.Max((short)0,image[i].Stack);
        }
        items.AdoptFundedQuantities(connection,q,money);
        var bankImage = before.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray();
        var bankCopper = new long[160];
        for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType,out long unit)) bankCopper[i] = unit * Math.Max(0,(int)bankImage[i].Stack);
        Check(items.TryAdoptShopBanks(connection,bankImage,bankCopper),name+" configured banks");
        var npcs = new RuntimeNpcStore();
        var body = new NpcStateUpdate(17,17,100,100,0,0,255,default,NpcSimulationState.Initial with { Life=250,LifeMax=250 });
        Check(npcs.TrySpawn(0,in body,out var vendor),name+" vendor");
        var stock = before.GetProperty("shop").EnumerateArray().Select(Stock).ToArray();
        var offers = stock.Select((item,index)=>(item,index)).Where(x=>x.item.Tuple.Type!=0)
            .Select(x=>new VanillaTownShopEntry1458(x.index,new(x.item.Tuple.Type),null,VanillaTownShopCurrency1458.Coins)).ToArray();
        var session = new RuntimeTownShopSession1458(0,new(17),RuntimeTownShopSessionKind1458.OrdinaryShop,
            spec.GetProperty("adjust").GetSingle(),false,default,offers,RuntimeTownCommerceMissingFacts1458.None) { VendorHandle=vendor.Handle };
        Check(memberOwner.TrySetTalkNpc(connection,0),name+" talk");
        Check(memberOwner.TrySetTownShopSession(connection,session),name+" session");
        Check(RuntimeTownShopQuote1458.TryCreate(session,memberOwner,npcs,connection,vendor.Handle,stock,
            B(spec,"discount")?(byte)2:(byte)1,platform!=0,out var quote,new(false,false,false)),name+" quote");
        session.Quote=quote;
        int reports = 0;
        foreach(var hex in row.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(hex.GetString()!);
            if(bytes[2]==5)
            {
                var frame = new TerrariaFrame((ushort)bytes.Length,5,new ReadOnlySequence<byte>(bytes),new ReadOnlySequence<byte>(bytes.AsMemory(3)));
                Check(TerrariaPlayerEquipmentCodec.TryDecode(in frame,out var decoded)==TerrariaPlayerEquipmentDecodeResult.Decoded,name+" original frame decode");
                Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,new(connection.Player.Slot,decoded.SlotId,decoded.Stack,decoded.Prefix,decoded.ItemNetId,decoded.ItemFlags))),name+" actual5");reports++;
            }
            else if(bytes[2]==138) Check(Complete(players, connection),name+" actual138");
        }
        var expected = after.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
        for(short i=0;i<59;i++) Check(items.TryGet(connection,i,out var current)&&current==expected[i],name+" source inventory slot"+i);
        var banks = new RuntimePlayerInventoryItem[160];var funds=new long[160];
        Check(items.TryCaptureShopBanks(connection,banks,funds),name+" capture final banks");
        Check(banks.AsSpan().SequenceEqual(after.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray()),name+" full160 source banks");
        Check(quote.TryCapture(out var economic),name+" final quote");
        Check(economic.Stock40.AsSpan().SequenceEqual(after.GetProperty("shop").EnumerateArray().Select(Stock).ToArray()),name+" source stock40");
        var memos=after.GetProperty("memo").EnumerateArray().Select(x=>new ShopMemo1458(I(x,"type"),I(x,"prefix"),I(x,"stack"))).ToArray();
        Check(economic.Memos.AsSpan().SequenceEqual(memos),name+" source memo");
    }
}
Assert.Equal(64, rows);
}

}
private static class CloseSourceReplayCases { internal static void Run() {

int checks = 0, rows = 0;
void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
bool B(JsonElement x, string key) => x.GetProperty(key).GetBoolean();
RuntimePlayerInventoryItem Item(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0
    ? default : new(new(I(x,"type")), checked((short)I(x,"stack")), new(I(x,"prefix")), B(x,"favorite") ? (byte)1 : (byte)0);
ShopItem1458 Stock(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0 ? default : new(
    new(I(x,"type"), I(x,"stack"), I(x,"prefix"), B(x,"favorite")), I(x,"value"), B(x,"buyOnce"), B(x,"isAShopItem"));
T Owner<T>(PlayerAuthority players, string name) => (T)typeof(PlayerAuthority).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(players)!;
for (int platform = 0; platform < 2; platform++)
{
    using var document = Read("npc-shop-close-sync-original-source-v1", platform);
    foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
    {
        rows++;
        string name = row.GetProperty("spec").GetProperty("name").GetString()!;
        var before = row.GetProperty("before"); var after = row.GetProperty("afterSync");
        var spec = row.GetProperty("spec");
        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
        players.SetPlayerUpdateRandom(new(0));
        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
        players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
        var memberOwner = Owner<RuntimePlayerMembership>(players,"membership");
        var items = Owner<RuntimePlayerInventoryStore>(players,"inventory");
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
        var member = new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400,
            ItemAnimation = I(before,"animation") };
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = I(before,"itemTime") } };
        memberOwner.Commit(member); Check(items.TryAttach(connection), name+" attach");
        var image = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
        image[58] = Item(before.GetProperty("mouse"));
        var q = new short[59]; var money = new long[59];
        for (short i = 0; i < 59; i++)
        {
            Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot,i)), name+" initial inventory");
            if (VanillaCoinFacts.TryGetValue(image[i].ItemType,out long unit)) money[i] = unit * Math.Max(0,(int)image[i].Stack);
            else q[i] = Math.Max((short)0,image[i].Stack);
        }
        items.AdoptFundedQuantities(connection,q,money);
        var bankImage = before.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray();
        var bankCopper = new long[160];
        for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType,out long unit)) bankCopper[i] = unit * Math.Max(0,(int)bankImage[i].Stack);
        Check(items.TryAdoptShopBanks(connection,bankImage,bankCopper),name+" configured banks");
        var npcs = new RuntimeNpcStore();
        var body = new NpcStateUpdate(17,17,100,100,0,0,255,default,NpcSimulationState.Initial with { Life=250,LifeMax=250 });
        Check(npcs.TrySpawn(0,in body,out var vendor),name+" vendor");
        var stock = before.GetProperty("shop").EnumerateArray().Select(Stock).ToArray();
        var offers = stock.Select((item,index)=>(item,index)).Where(x=>x.item.Tuple.Type!=0)
            .Select(x=>new VanillaTownShopEntry1458(x.index,new(x.item.Tuple.Type),null,VanillaTownShopCurrency1458.Coins)).ToArray();
        var session = new RuntimeTownShopSession1458(0,new(17),RuntimeTownShopSessionKind1458.OrdinaryShop,
            spec.GetProperty("adjust").GetSingle(),false,default,offers,RuntimeTownCommerceMissingFacts1458.None) { VendorHandle=vendor.Handle };
        Check(memberOwner.TrySetTalkNpc(connection,0),name+" talk");
        Check(memberOwner.TrySetTownShopSession(connection,session),name+" session");
        Check(RuntimeTownShopQuote1458.TryCreate(session,memberOwner,npcs,connection,vendor.Handle,stock,
            B(spec,"discount")?(byte)2:(byte)1,platform!=0,out var quote,new(false,false,false)),name+" quote");
        session.Quote=quote;
        int reports = 0;
        foreach(var hex in row.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(hex.GetString()!);
            if(bytes[2]==5)
            {
                var frame = new TerrariaFrame((ushort)bytes.Length,5,new ReadOnlySequence<byte>(bytes),new ReadOnlySequence<byte>(bytes.AsMemory(3)));
                Check(TerrariaPlayerEquipmentCodec.TryDecode(in frame,out var decoded)==TerrariaPlayerEquipmentDecodeResult.Decoded,name+" original frame decode");
                Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,new(connection.Player.Slot,decoded.SlotId,decoded.Stack,decoded.Prefix,decoded.ItemNetId,decoded.ItemFlags))),name+" actual5");reports++;
            }
            else if(bytes[2]==40)
            {
                Check(bytes.AsSpan(4,2).SequenceEqual(new byte[]{255,255}),name+" original NoNpc40");
                Check(players.TrySetTalkNpc(connection,-1),name+" actual NoNpc wrapper");
                Check(!quote.TryCapture(out _),name+" closed quote cannot capture new purchase");
            }
            else if(bytes[2]==138) Check(Complete(players, connection),name+" actual138");
        }
        var expected = after.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
        for(short i=0;i<59;i++) Check(items.TryGet(connection,i,out var current)&&current==expected[i],name+" source inventory slot"+i);
        var banks = new RuntimePlayerInventoryItem[160];var funds=new long[160];
        Check(items.TryCaptureShopBanks(connection,banks,funds),name+" capture final banks");
        Check(banks.AsSpan().SequenceEqual(after.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray()),name+" full160 source banks");
        Check(!quote.TryCapture(out _),name+" old closed quote stays retired");
        var actualStock=(ShopItem1458[])typeof(RuntimeTownShopQuote1458).GetField("stock",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(quote)!;
        Check(actualStock.AsSpan().SequenceEqual(after.GetProperty("shop").EnumerateArray().Select(Stock).ToArray()),name+" source stock40");
        var memos=after.GetProperty("memo").EnumerateArray().Select(x=>new ShopMemo1458(I(x,"type"),I(x,"prefix"),I(x,"stack"))).ToArray();
        var actualMemos=(ShopMemo1458[])typeof(RuntimeTownShopQuote1458).GetField("memos",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(quote)!;
        Check(actualMemos.AsSpan().SequenceEqual(memos),name+" source memo");
        Check(memberOwner.TryGetTalkNpc(connection.Player,out var talk)&&talk==-1,name+" source NoNpc talk");
        Check(Complete(players, connection),name+" repeated closing138 once");
    }
}
Assert.Equal(6, rows);
}

}
private static class HiddenSourceReplayCases { internal static void Run() {

int checks = 0, rows = 0; var elapsed = System.Diagnostics.Stopwatch.StartNew();
void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
bool B(JsonElement x, string key) => x.GetProperty(key).GetBoolean();
RuntimePlayerInventoryItem Item(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0
    ? default : new(new(I(x,"type")), checked((short)I(x,"stack")), new(I(x,"prefix")), B(x,"favorite") ? (byte)1 : (byte)0);
ShopItem1458 Stock(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0 ? default : new(
    new(I(x,"type"), I(x,"stack"), I(x,"prefix"), B(x,"favorite")), I(x,"value"), B(x,"buyOnce"), B(x,"isAShopItem"), x.TryGetProperty("customPrice", out var cp) && cp.ValueKind != JsonValueKind.Null ? cp.GetInt32() : null);
T Owner<T>(PlayerAuthority players, string name) => (T)typeof(PlayerAuthority).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(players)!;
foreach (string corpus in new[]{"npc-shop-prefix-walk-original-source-v1", "npc-shop-roundtrip-batch-original-source-v1"})
for (int platform = 0; platform < 2; platform++)
{
    using var document = Read(corpus, platform);
    foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
    {
        rows++;
        string name = row.GetProperty("spec").GetProperty("name").GetString()!;
        var before = row.GetProperty("before"); var after = row.GetProperty("afterSync");
        var spec = row.GetProperty("spec");
        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
        players.SetPlayerUpdateRandom(new(0));
        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
        players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
        var memberOwner = Owner<RuntimePlayerMembership>(players,"membership");
        var items = Owner<RuntimePlayerInventoryStore>(players,"inventory");
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
        var member = new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400,
            ItemAnimation = I(before,"animation") };
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = I(before,"itemTime") } };
        memberOwner.Commit(member); Check(items.TryAttach(connection), name+" attach");
        var image = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
        image[58] = Item(before.GetProperty("mouse"));
        var q = new short[59]; var money = new long[59];
        for (short i = 0; i < 59; i++)
        {
            Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot,i)), name+" initial inventory");
            if (VanillaCoinFacts.TryGetValue(image[i].ItemType,out long unit)) money[i] = unit * Math.Max(0,(int)image[i].Stack);
            else q[i] = Math.Max((short)0,image[i].Stack);
        }
        items.AdoptFundedQuantities(connection,q,money);
        var bankImage = before.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray();
        var bankCopper = new long[160];
        for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType,out long unit)) bankCopper[i] = unit * Math.Max(0,(int)bankImage[i].Stack);
        Check(items.TryAdoptShopBanks(connection,bankImage,bankCopper),name+" configured banks");
        var npcs = new RuntimeNpcStore();
        var body = new NpcStateUpdate(17,17,100,100,0,0,255,default,NpcSimulationState.Initial with { Life=250,LifeMax=250 });
        Check(npcs.TrySpawn(0,in body,out var vendor),name+" vendor");
        var stock = before.GetProperty("shop").EnumerateArray().Select(Stock).ToArray();
        var offers = stock.Select((item,index)=>(item,index)).Where(x=>x.item.Tuple.Type!=0)
            .Select(x=>new VanillaTownShopEntry1458(x.index,new(x.item.Tuple.Type),null,VanillaTownShopCurrency1458.Coins)).ToArray();
        var session = new RuntimeTownShopSession1458(0,new(17),RuntimeTownShopSessionKind1458.OrdinaryShop,
            spec.GetProperty("adjust").GetSingle(),false,default,offers,RuntimeTownCommerceMissingFacts1458.None) { VendorHandle=vendor.Handle };
        Check(memberOwner.TrySetTalkNpc(connection,0),name+" talk");
        Check(memberOwner.TrySetTownShopSession(connection,session),name+" session");
        Check(RuntimeTownShopQuote1458.TryCreate(session,memberOwner,npcs,connection,vendor.Handle,stock,
            B(spec,"discount")?(byte)2:(byte)1,platform!=0,out var quote,new(false,false,false)),name+" quote");
        session.Quote=quote;
        int reports = 0;
        foreach(var hex in row.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(hex.GetString()!);
            if(bytes[2]==5)
            {
                var frame = new TerrariaFrame((ushort)bytes.Length,5,new ReadOnlySequence<byte>(bytes),new ReadOnlySequence<byte>(bytes.AsMemory(3)));
                Check(TerrariaPlayerEquipmentCodec.TryDecode(in frame,out var decoded)==TerrariaPlayerEquipmentDecodeResult.Decoded,name+" original frame decode");
                Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,new(connection.Player.Slot,decoded.SlotId,decoded.Stack,decoded.Prefix,decoded.ItemNetId,decoded.ItemFlags))),name+" actual5");reports++;
            }
            else if(bytes[2]==138) Check(Complete(players, connection),name+" actual138");
        }
        var expected = after.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
        for(short i=0;i<59;i++) Check(items.TryGet(connection,i,out var current)&&current==expected[i],name+" source inventory slot"+i);
        var banks = new RuntimePlayerInventoryItem[160];var funds=new long[160];
        Check(items.TryCaptureShopBanks(connection,banks,funds),name+" capture final banks");
        Check(banks.AsSpan().SequenceEqual(after.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray()),name+" full160 source banks");
        Check(quote.TryCapture(out var economic),name+" final quote");
        Check(economic.Stock40.AsSpan().SequenceEqual(after.GetProperty("shop").EnumerateArray().Select(Stock).ToArray()),name+" source stock40");
        var memos=after.GetProperty("memo").EnumerateArray().Select(x=>new ShopMemo1458(I(x,"type"),I(x,"prefix"),I(x,"stack"))).ToArray();
        Check(economic.Memos.AsSpan().SequenceEqual(memos),name+" source memo");
    }
}
Assert.Equal(34, rows);
}

}
private static class BankSourceReplayCases { internal static void Run() {

int checks = 0, rows = 0;
void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
bool B(JsonElement x, string key) => x.GetProperty(key).GetBoolean();
RuntimePlayerInventoryItem Item(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0
    ? default : new(new(I(x,"type")), checked((short)I(x,"stack")), new(I(x,"prefix")), B(x,"favorite") ? (byte)1 : (byte)0);
T Owner<T>(PlayerAuthority players, string name) => (T)typeof(PlayerAuthority).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(players)!;
for (int corpus = 0; corpus < 2; corpus++)
for (int platform = 0; platform < 2; platform++)
{
    string sourceFolder = corpus == 0 ? "npc-bank-move-original-source-v1" : "npc-bank-right-lastfavorite-original-source-v1";
    using var document = Read(sourceFolder, platform);
    foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
    {
        rows++;
        string name = row.GetProperty("spec").GetProperty("name").GetString()!;
        var before = row.GetProperty("before"); var after = row.GetProperty("afterSync");
        var spec = row.GetProperty("spec");
        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
        players.SetPlayerUpdateRandom(new(0));
        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
        players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
        var memberOwner = Owner<RuntimePlayerMembership>(players,"membership");
        var items = Owner<RuntimePlayerInventoryStore>(players,"inventory");
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
        var member = new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400,
            ItemAnimation = I(before,"animation") };
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = I(before,"itemTime") } };
        memberOwner.Commit(member); Check(items.TryAttach(connection), name+" attach");
        var image = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
        image[58] = Item(before.GetProperty("mouse"));
        var q = new short[59]; var money = new long[59];
        for (short i = 0; i < 59; i++)
        {
            Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot,i)), name+" initial inventory");
            if (VanillaCoinFacts.TryGetValue(image[i].ItemType,out long unit)) money[i] = unit * Math.Max(0,(int)image[i].Stack);
            else q[i] = Math.Max((short)0,image[i].Stack);
        }
        items.AdoptFundedQuantities(connection,q,money);
        var bankImage = before.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray();
        var bankCopper = new long[160]; var bankQuantity = new short[160]; var bankCaps = new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount]; Array.Fill(bankCaps,(short)-1);
        for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType,out long unit)) bankCopper[i] = unit * Math.Max(0,(int)bankImage[i].Stack);
        for (int i = 0; i < 160; i++) if (bankCopper[i] == 0) bankQuantity[i] = Math.Max((short)0,bankImage[i].Stack);
        Check(items.TryAdoptShopBanks(connection,bankImage,bankCopper,bankQuantity,bankCaps),name+" configured banks");
        int reports = 0;
        foreach(var hex in row.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(hex.GetString()!);
            if(bytes[2]==5)
            {
                var frame = new TerrariaFrame((ushort)bytes.Length,5,new ReadOnlySequence<byte>(bytes),new ReadOnlySequence<byte>(bytes.AsMemory(3)));
                Check(TerrariaPlayerEquipmentCodec.TryDecode(in frame,out var decoded)==TerrariaPlayerEquipmentDecodeResult.Decoded,name+" original frame decode");
                Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,new(connection.Player.Slot,decoded.SlotId,decoded.Stack,decoded.Prefix,decoded.ItemNetId,decoded.ItemFlags))),name+" actual5");reports++;
            }
            else if(bytes[2]==138) Check(Complete(players, connection),name+" actual138");
        }
        var expected = after.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
        for(short i=0;i<59;i++) Check(items.TryGet(connection,i,out var current)&&current==expected[i],name+" source inventory slot"+i);
        var banks = new RuntimePlayerInventoryItem[160];var funds=new long[160];
        Check(items.TryCaptureShopBanks(connection,banks,funds),name+" capture final banks");
        Check(banks.AsSpan().SequenceEqual(after.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray()),name+" full160 source banks");
        Check(items.TryCaptureShopBankOwnership(connection,out var actualQuantity,out var actualCaps),name+" ownership capture");
        var expectedBanks = after.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray();
        var mainQuantity = items.CaptureFundedQuantities(connection); var mainCopper = items.CaptureFundedCoinValues(connection);
        for(int i=0;i<59;i++)
        {
            long value = VanillaCoinFacts.TryGetValue(expected[i].ItemType,out var unit) ? unit*Math.Max(0,(int)expected[i].Stack):0;
            Check(mainCopper[i]==value,name+" funded main copper"+i);
            Check(mainQuantity[i]==(value==0?Math.Max(0,(int)expected[i].Stack):0),name+" funded main quantity"+i);
        }
        for(int i=0;i<160;i++)
        {
            long value = VanillaCoinFacts.TryGetValue(expectedBanks[i].ItemType,out var unit) ? unit*Math.Max(0,(int)expectedBanks[i].Stack):0;
            Check(funds[i]==value,name+" funded bank copper"+i);
            Check(actualQuantity[i]==(value==0?Math.Max(0,(int)expectedBanks[i].Stack):0),name+" funded bank quantity"+i);
        }
        Check(memberOwner.TryGet(connection,out var acceptedMember) && acceptedMember.ItemPhase is { ShopOpen:false },name+" bank move does not open shop");

    }
}
Assert.Equal(72, rows);
}

}
private static class BankCustodyProbeCases { internal static void Run() {

int checks = 0;
void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
T Owner<T>(PlayerAuthority authority, string name) => (T)typeof(PlayerAuthority).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(authority)!;
ConnectionHandle Connection(byte slot) => new(GameCommandSourceId.FromConnection(90001 + slot), new(new(slot), new(1)));
for (int mode = 0; mode < 11; mode++)
{
    Console.WriteLine("mode" + mode);
    var sink = new Sink(); var players = new PlayerAuthority(sink, new WorldTileStore(new WorldDimensions(400,300)));
    players.SetPlayerUpdateRandom(new(0)); players.SetPlayerUpdateWorldFacts(new(400,300,80,false,false));
    players.BindReceiveEquipmentRandom(new(0),false,new(false,false,false));
    var members = Owner<RuntimePlayerMembership>(players,"membership");
    var items = Owner<RuntimePlayerInventoryStore>(players,"inventory");
    var connection = Connection(0); var other = Connection(1);
    var member = new RuntimePlayerMember { Connection=connection,Slot=connection.Player.Slot,Revision=1,HasHealth=true,Life=400,MaxLife=400 };
    members.Commit(member); Check(items.TryAttach(connection),"attach main");
    members.Commit(new RuntimePlayerMember { Connection=other,Slot=other.Player.Slot,Revision=1,HasHealth=true,Life=400,MaxLife=400 });
    Check(items.TryAttach(other),"attach disjoint");
    var bank = new RuntimePlayerInventoryItem[160]; var bankCopper = new long[160]; var bankQuantity = new short[160];
    var bankCaps = new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount]; Array.Fill(bankCaps,(short)-1);
    var mainQuantity = new short[59]; var mainCopper = new long[59];
    bool chain = mode is 4 or 5 or 6;
    bool deposit = mode == 3;
    int bankIndex = mode == 2 ? 0 : 120;
    short protocol = bankIndex == 0 ? (short)99 : (short)700;
    var source = mode == 1 ? new RuntimePlayerInventoryItem(new(71),1,new(0),1)
        : new RuntimePlayerInventoryItem(new(109),chain?(short)20:(short)3,new(0),1);
    short funded = chain ? (short)7 : (short)2;
    if (chain || deposit)
    {
        short initial = chain ? (short)0 : (short)58;
        Check(items.TrySet(connection,source.ToCommitRequest(connection.Player.Slot,initial)),"configured main source");
        mainQuantity[initial]=funded;
    }
    else
    {
        bank[bankIndex]=source;
        if(mode==1) bankCopper[bankIndex]=1; else { bankQuantity[bankIndex]=funded; bankCaps[bankIndex* RuntimePlayerInventoryStore.QuantityLaneCount]=source.Stack; }
    }
    items.AdoptFundedQuantities(connection,mainQuantity,mainCopper);
    Check(items.TryAdoptShopBanks(connection,bank,bankCopper,bankQuantity,bankCaps),"configured bank ownership");
    if (mode >= 8)
    {
        var npcs = new RuntimeNpcStore();
        var body = new NpcStateUpdate(17,17,100,100,0,0,255,default,NpcSimulationState.Initial with { Life=250,LifeMax=250 });
        Check(npcs.TrySpawn(0,in body,out var vendor),"configured quote vendor");
        var offers = new[] { new VanillaTownShopEntry1458(0,new(8),null,VanillaTownShopCurrency1458.Coins) };
        var session = new RuntimeTownShopSession1458(0,new(17),RuntimeTownShopSessionKind1458.OrdinaryShop,
            1,false,default,offers,RuntimeTownCommerceMissingFacts1458.None) { VendorHandle=vendor.Handle };
        Check(members.TrySetTalkNpc(connection,0),"configured quote talk");
        Check(members.TrySetTownShopSession(connection,session),"configured quote session");
        var stock = new ShopItem1458[40]; stock[0] = new(new(8,1,0,false),50,false,true);
        Check(RuntimeTownShopQuote1458.TryCreate(session,members,npcs,connection,vendor.Handle,stock,1,false,
            out var quote,new(false,false,false)),"configured quote");
        session.Quote = quote;
    }
    bool completing = false;
    RuntimePlayerInventoryItem finalCursor = chain || deposit ? default : source with { ItemFlags = mode==2?(byte)0:(byte)1 };
    RuntimePlayerInventoryItem finalBank = chain || deposit ? source : default;
    sink.OnEquipment = slot =>
    {
        if(chain && slot==0) return; // This existing observer sees only the eager report debit.
        Check(items.TryCaptureShopBankOwnership(connection,out var acceptedQuantity,out _),"observer bank custody");
        Check(items.TryGet(connection,58,out var acceptedCursor)&&acceptedCursor==finalCursor,"first completion observer cursor");
        if (!completing && slot == 58)
        {
            Check(acceptedQuantity[bankIndex]==bankQuantity[bankIndex],"ordinary first58 leaves bank funding unchanged");
            Check(items.CaptureFundedQuantities(connection)[58]==0 && items.CaptureFundedCoinValues(connection)[58]==0,
                "ordinary first58 cannot mint cursor funding before138");
            return;
        }
        Check(acceptedQuantity[bankIndex]==(chain||deposit?funded:0),"first completion observer bank funds");
        if(slot==58 && mode==8) throw new Exception("bank observer throw");
        if(slot==58 && mode==9)
        {
            var replacement=new RuntimePlayerInventoryItem(new(3),1,new(0),0);
            Check(items.TrySetShopBankReport(connection,protocol,in replacement),"component newer bank body");
        }
        if(slot==58 && mode==10)
            Check(players.TryApply(new PlayerEquipmentRuntimeCommand(other,new(other.Player.Slot,0,1,0,3,0))),"actual disjoint callback TryApply");
    };
    void Report(short slot, RuntimePlayerInventoryItem item, byte? sender=null) =>
        Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,item.ToCommitRequest(new(sender??0),slot))),"actual packet5");
    if(chain)
    {
        Report(0,default);
        Check(items.TryGet(connection,0,out var debit)&&debit.IsEmpty,"eager debit physical");
        Check(players.TryApply(new PlayerEquipmentRuntimeCommand(other,new(other.Player.Slot,0,1,0,3,0))),"disjoint between fragments");
        Report(protocol,finalBank);
    }
    else { Report(58,finalCursor); Report(protocol,finalBank); }
    if(mode==5) member.ItemPhase=null;
    if(mode==7) Report(protocol,finalBank,1);
    Exception? failure=null;
    completing = true;
    try { Check(Complete(players, connection),"actual138"); }
    catch(Exception ex) { failure=ex; }
    Check(mode==8 ? failure?.Message=="bank observer throw" : failure is null,"completion exception contract");
    var actualBank = new RuntimePlayerInventoryItem[160]; var actualCopper=new long[160];
    Check(items.TryCaptureShopBanks(connection,actualBank,actualCopper),"bank after");
    Check(items.TryCaptureShopBankOwnership(connection,out var actualQuantity,out var actualCaps),"bank ownership after");
    if(mode is 5 or 7)
    {
        Check(actualBank[bankIndex]==bank[bankIndex]&&actualQuantity[bankIndex]==bankQuantity[bankIndex],"retired or wrongowner no completion credit");
        Check(items.TryGetFundedQuantity(connection,58,out var q)&&q==0,"retired no cursor credit");
    }
    else
    {
        Check(actualBank[bankIndex]==(mode==9?new(new(3),1,new(0),0):finalBank),"exact final or newer body");
        Check(items.CaptureFundedQuantities(connection)[58]==(chain||deposit||mode==1?0:funded),"exact moved noncoin quantity");
        Check(items.CaptureFundedCoinValues(connection)[58]==(mode==1?1:0),"fractional exact copper budget");
        Check(actualQuantity[bankIndex]==(chain||deposit?funded:0),"noncoin bank credit once");
        if(mode==8) Check(sink.Slots.SequenceEqual(new short[]{58,700}),"throw drains remaining bank4 callback");
        if(mode==9) Check(sink.Slots.SequenceEqual(new short[]{58}),"changed bank only stale offer suppressed");
        if(mode==10) Check(sink.Slots.SequenceEqual(new short[]{58,0,700}),"disjoint actual callback report permitted");
        int events=sink.Slots.Count;
        Check(Complete(players, connection),"duplicate138");
        Check(sink.Slots.Count==events,"no duplicate publication");
        if(!chain&&!deposit&&mode!=1&&mode!=9)
        {
            Check(actualCaps[bankIndex* RuntimePlayerInventoryStore.QuantityLaneCount]==0,"historical consumed-type cap follows bank debit");
            var restore=source with { Stack=1 };
            Check(!items.TrySetShopBankReport(connection,protocol,in restore),"raw report cannot restore consumed bank cap");
        }
    }
}

}

sealed class Sink : IRuntimePlayerEventSink
{
    internal readonly List<short> Slots=[];
    internal Action<short>? OnEquipment;
    public void PlayerEquipmentUpdated(ConnectionHandle connection,in PlayerEquipmentCommitRequest request)
    { Slots.Add(request.SlotId); OnEquipment?.Invoke(request.SlotId); }
    public void PlayerAppearanceUpdated(ConnectionHandle c,in PlayerAppearanceCommitRequest r) { }
    public void PlayerSpawned(ConnectionHandle c,in PlayerSpawnCommitRequest r) { }
    public void PlayerMoved(ConnectionHandle c,in PlayerMovementCommitRequest r) { }
    public void PlayerDisconnected(ConnectionHandle c) { }
}
}
private static class BankRawProbeCases { internal static void Run() {

int checks=0;
void Check(bool value,string name) {checks++;if(!value)throw new InvalidOperationException(name);}
T Owner<T>(PlayerAuthority p,string name)=>(T)typeof(PlayerAuthority).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
ConnectionHandle Handle(byte slot)=>new(GameCommandSourceId.FromConnection(91000+slot),new(new(slot),new(1)));
for(int mode=0;mode<7;mode++)
{
 var sink=new Sink();var p=new PlayerAuthority(sink,new WorldTileStore(new WorldDimensions(400,300)));
 p.SetPlayerUpdateRandom(new(0));p.SetPlayerUpdateWorldFacts(new(400,300,80,false,false));p.BindReceiveEquipmentRandom(new(0),false,new(false,false,false));
 var membership=Owner<RuntimePlayerMembership>(p,"membership");var store=Owner<RuntimePlayerInventoryStore>(p,"inventory");
 var c=Handle(0);var other=Handle(1);
 var member=new RuntimePlayerMember {Connection=c,Slot=c.Player.Slot,Revision=1,HasHealth=true,Life=400,MaxLife=400};membership.Commit(member);Check(store.TryAttach(c),"attach");
 membership.Commit(new RuntimePlayerMember{Connection=other,Slot=other.Player.Slot,Revision=1,HasHealth=true,Life=400,MaxLife=400});Check(store.TryAttach(other),"other attach");
 var banks=new RuntimePlayerInventoryItem[160];var funds=new long[160];var quantity=new short[160];var caps=new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount];Array.Fill(caps,(short)-1);
 if(mode==1) {banks[120]=new(new(72),1,new(0),0);funds[120]=1;}
 if(mode==2) {banks[120]=new(new(109),3,new(0),0);quantity[120]=2;caps[120 * RuntimePlayerInventoryStore.QuantityLaneCount]=3;}
 if(mode==3) caps[120 * RuntimePlayerInventoryStore.QuantityLaneCount]=0;
 Check(store.TryAdoptShopBanks(c,banks,funds,quantity,caps),"configured budget/cap");
    if (mode is 4 or 6)
    {
        var npcs = new RuntimeNpcStore();
        var body = new NpcStateUpdate(17,17,100,100,0,0,255,default,NpcSimulationState.Initial with { Life=250,LifeMax=250 });
        Check(npcs.TrySpawn(0,in body,out var vendor),"configured quote vendor");
        var offers = new[] { new VanillaTownShopEntry1458(0,new(8),null,VanillaTownShopCurrency1458.Coins) };
        var session = new RuntimeTownShopSession1458(0,new(17),RuntimeTownShopSessionKind1458.OrdinaryShop,
            1,false,default,offers,RuntimeTownCommerceMissingFacts1458.None) { VendorHandle=vendor.Handle };
        Check(membership.TrySetTalkNpc(c,0),"configured quote talk");
        Check(membership.TrySetTownShopSession(c,session),"configured quote session");
        var stock = new ShopItem1458[40]; stock[0] = new(new(8,1,0,false),50,false,true);
        Check(RuntimeTownShopQuote1458.TryCreate(session,membership,npcs,c,vendor.Handle,stock,1,false,
            out var quote,new(false,false,false)),"configured quote");
        session.Quote = quote;
    }

 var reported=mode==1?new RuntimePlayerInventoryItem(new(72),2,new(0),0):new RuntimePlayerInventoryItem(new(109),mode==2?(short)1:(short)3,new(0),0);
 sink.OnEquipment=slot=>
 {
  if(slot!=700)return;
  var actual=new RuntimePlayerInventoryItem[160];var money=new long[160];
  Check(store.TryCaptureShopBanks(c,actual,money)&&actual[120]==reported,"raw first observer sees complete physical batch");
  if(mode==4)
  {
   Check(actual[121].ItemType.Value==3,"second bank image adopted before throw");
   throw new Exception("raw bank observer throw");
  }
 };
 Check(p.TryApply(new PlayerEquipmentRuntimeCommand(c,reported.ToCommitRequest(c.Player.Slot,700))),"raw original-bank-range5");
 if (mode is 0 or 1 or 2 or 5)
 {
  var immediate=new RuntimePlayerInventoryItem[160];var immediateMoney=new long[160];
  Check(store.TryCaptureShopBanks(c,immediate,immediateMoney)&&immediate[120]==reported,
    "unquoted raw bank4 uses immediate legacy physical receive");
  Check(store.TryCaptureShopBankOwnership(c,out var immediateQuantity,out var immediateCaps),"immediate raw custody");
  Check(immediateMoney[120]==(mode==1?1:0)&&immediateQuantity[120]==(mode==2?1:0),
    "immediate raw normalization cannot mint bank funds");
  Check(immediateCaps[120 * RuntimePlayerInventoryStore.QuantityLaneCount]==caps[120 * RuntimePlayerInventoryStore.QuantityLaneCount],"immediate raw cannot erase historical cap");
 }
 Check(store.TryCaptureShopBanks(c,new RuntimePlayerInventoryItem[160],new long[160]),"pending query available");
 if(mode==4) Check(p.TryApply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,701,1,0,3,0))),"second raw image");
 if(mode==5) Check(p.TryApply(new PlayerEquipmentRuntimeCommand(other,new(other.Player.Slot,0,1,0,3,0))),"disjoint command between raw fragments");
 if(mode==6) Check(store.TrySet(c,new(c.Player.Slot,49,1,0,3,0)),"unrelated same actor mutation");
 Exception? error=null;try {Check(Complete(p, c),"actual138");}catch(Exception ex){error=ex;}
 Check(mode==4?error?.Message=="raw bank observer throw":error is null,"raw publication exception contract");
 var result=new RuntimePlayerInventoryItem[160];var moneyAfter=new long[160];Check(store.TryCaptureShopBanks(c,result,moneyAfter),"after image");
 Check(store.TryCaptureShopBankOwnership(c,out var quantityAfter,out var capsAfter),"after ownership");
 if(mode is 3 or 6) Check(result[120]==banks[120],"cap or stale currentness refuses raw fallback");
 else
 {
  Check(result[120]==reported,"legitimate raw initialization/decrease/increase image");
  Check(moneyAfter[120]==(mode==1?1:0),"raw copper min preserve no mint");
  Check(quantityAfter[120]==(mode==2?1:0),"raw quantity min preserve no grant");
  Check(capsAfter[120 * RuntimePlayerInventoryStore.QuantityLaneCount]==caps[120 * RuntimePlayerInventoryStore.QuantityLaneCount],"raw cannot erase historical quantity cap");
  if(mode==4) Check(sink.Slots.SequenceEqual(new short[]{700,701}),"throw drains raw bank notification tail");
 }
 int count=sink.Slots.Count;Check(Complete(p, c),"duplicate138");Check(sink.Slots.Count==count,"raw batch once");
 Check(member.ItemPhase is {ShopOpen:false},"raw bank cannot infer shop UI");
}
}

sealed class Sink:IRuntimePlayerEventSink
{
 internal readonly List<short> Slots=[];internal Action<short>? OnEquipment;
 public void PlayerEquipmentUpdated(ConnectionHandle c,in PlayerEquipmentCommitRequest r){Slots.Add(r.SlotId);OnEquipment?.Invoke(r.SlotId);}
 public void PlayerAppearanceUpdated(ConnectionHandle c,in PlayerAppearanceCommitRequest r){}
 public void PlayerSpawned(ConnectionHandle c,in PlayerSpawnCommitRequest r){}
 public void PlayerMoved(ConnectionHandle c,in PlayerMovementCommitRequest r){}
 public void PlayerDisconnected(ConnectionHandle c){}
}
}
private static class TransferContextProbeCases { internal static void Run() {
int checks=0;
void Check(bool value,string name){checks++;if(!value)throw new InvalidOperationException(name);}
T Owner<T>(PlayerAuthority p,string name)=>(T)typeof(PlayerAuthority).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
PlayerAuthority New()=>new(null,new WorldTileStore(new WorldDimensions(400,300)));
ConnectionHandle Handle(int id,int generation)=>new(GameCommandSourceId.FromConnection(id),new(new(0),new((uint)generation)));
for(int mode=0;mode<13;mode++) {
 var p=New();var c=Handle(99001,1);var s=Owner<RuntimePlayerInventoryStore>(p,"inventory");var m=Owner<RuntimePlayerMembership>(p,"membership");
 var member=new RuntimePlayerMember{Connection=c,Slot=c.Player.Slot,Revision=1,HasHealth=true,Life=100,MaxLife=100};
 member.ItemPhase=member.ItemPhase!.Value.ApplyAcceptedShopPriceContexts(9);
 Check(member.ItemPhase.Value.CapturedShopPriceContextMask==9,"nonrectangular source contexts");
 m.Commit(member);Check(s.TryAttach(c),"source attach");
 var banks=new RuntimePlayerInventoryItem[160];var copper=new long[160];var quantity=new short[160];var caps=new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount];Array.Fill(caps,(short)-1);
 banks[0]=new(new(72),1,new(0),0);copper[0]=1;
 banks[39]=new(new(109),3,new(0),0);quantity[39]=2;caps[39 * RuntimePlayerInventoryStore.QuantityLaneCount]=3;
 caps[159 * RuntimePlayerInventoryStore.QuantityLaneCount + 4]=0;
 Check(s.TryAdoptShopBanks(c,banks,copper,quantity,caps),"configured banks/funding/caps");
 var done=new TaskCompletionSource<RuntimePlayerTransferState?>();Check(p.TryApply(new PlayerTransferDetachRuntimeCommand(c,done)),"actual detach command");
 var transfer=done.Task.GetAwaiter().GetResult();Check(transfer is not null,"detached DTO");
 Check(!m.Contains(c.Player.Slot),"source detached only after capture");
 Check(transfer!.ShopBankInventory.SequenceEqual(banks)&&transfer.FundedShopBankCopper.SequenceEqual(copper)&&transfer.FundedShopBankQuantities.SequenceEqual(quantity)&&transfer.OwnedShopBankQuantityCaps.SequenceEqual(caps),"exact physical fractional/quantity/caps transferred");
 if(mode==1) transfer=transfer with {ShopBankInventory=new RuntimePlayerInventoryItem[159]};
 if(mode==2) transfer=transfer with {FundedShopBankCopper=new long[159]};
 if(mode==3) {var a=copper.ToArray();a[0]=101;transfer=transfer with {FundedShopBankCopper=a};}
 if(mode==4) {var a=quantity.ToArray();a[39]=4;transfer=transfer with {FundedShopBankQuantities=a};}
 if(mode==5) {var a=caps.ToArray();a[39 * RuntimePlayerInventoryStore.QuantityLaneCount]=2;transfer=transfer with {OwnedShopBankQuantityCaps=a};}
 if(mode==6) transfer=transfer with {OwnedShopBankQuantityCaps=new short[159 * RuntimePlayerInventoryStore.QuantityLaneCount + 4]};
 if(mode==7) {var a=caps.ToArray();a[39 * RuntimePlayerInventoryStore.QuantityLaneCount]=10000;transfer=transfer with {OwnedShopBankQuantityCaps=a};}
 if(mode==8) {var a=quantity.ToArray();a[0]=1;transfer=transfer with {FundedShopBankQuantities=a};}
 if(mode==9) {var a=caps.ToArray();a[39 * RuntimePlayerInventoryStore.QuantityLaneCount]=9999;transfer=transfer with {OwnedShopBankQuantityCaps=a};}
 if(mode==10) transfer=transfer with {OwnedInventoryQuantities=[new(0,new(109),10000)]};
 if(mode==11) transfer=transfer with {OwnedInventoryQuantities=[new(0,new(109),9999)]};
 if(mode==12) transfer=transfer with {OwnedInventoryQuantities=[new(0,new(109),-1)]};
 var target=New();var d=Handle(99002,2);var attach=new TaskCompletionSource<bool>();Check(target.TryApply(new PlayerTransferAttachRuntimeCommand(d,transfer,10,10,false,false,attach)),"actual attach command");
 bool accepted=attach.Task.GetAwaiter().GetResult();Check(accepted==(mode is 0 or 9 or 11),$"valid transfer only mode={mode} actual={accepted}");
 var tm=Owner<RuntimePlayerMembership>(target,"membership");Check(tm.Contains(d.Player.Slot)==accepted,"membership atomic on malformed bank provenance");
 if(accepted) {
  Check(tm.TryGet(d.Player.Slot,out var adoptedMember)&&adoptedMember.ItemPhase!.Value.CapturedShopPriceContextStates==165&&adoptedMember.ItemPhase.Value.CapturedShopPriceContextMask==9,"transfer removes session with conservative correlated open/closed states");
  var ts=Owner<RuntimePlayerInventoryStore>(target,"inventory");var b=new RuntimePlayerInventoryItem[160];var f=new long[160];Check(ts.TryCaptureShopBanks(d,b,f)&&b.SequenceEqual(banks)&&f.SequenceEqual(copper),"new occupation exact fractional copper");
  Check(ts.TryCaptureShopBankOwnership(d,out var q,out var k)&&q.SequenceEqual(quantity)&&k.SequenceEqual(transfer.OwnedShopBankQuantityCaps),"new occupation exact quantity/cap");
  Check(!ts.TrySetShopBankReport(c,700,banks[0]),"old generation cannot mutate new bank owner");
  Check(!ts.TrySetShopBankReport(d,739,new(new(2209),1,new(0),0)),"transferred consumed cap0 refuses restore");
  Check(ts.TrySetShopBankReport(d,99,new(new(72),2,new(0),0)),"matching raw increase allowed physical only");
  Check(ts.TryCaptureShopBanks(d,b,f)&&f[0]==1,"raw increase cannot mint fractional copper");
 }
}
}

}
private static class ConnectedProbeCases { internal static void Run() {

int checks = 0;
void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
ConnectionHandle Connection(byte slot) => new(GameCommandSourceId.FromConnection(90001 + slot), new(new(slot), new(1)));
T Owner<T>(PlayerAuthority players, string name) => (T)typeof(PlayerAuthority).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(players)!;
for (int mode = 0; mode < 8; mode++)
{
    var sink = new Sink();
    var tiles = new WorldTileStore(new WorldDimensions(400, 300));
    var players = new PlayerAuthority(sink, tiles);
    players.SetPlayerUpdateRandom(new(0));
    players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
    players.BindReceiveEquipmentRandom(new(0), false, new(false, false, false));
    var memberOwner = Owner<RuntimePlayerMembership>(players, "membership");
    var items = Owner<RuntimePlayerInventoryStore>(players, "inventory");
    var connection = Connection(0);
    var member = new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
        Revision = 1, HasHealth = true, Life = 400, MaxLife = 400 };
    memberOwner.Commit(member);
    Check(items.TryAttach(connection), "attach");
    var rawMoney = new PlayerEquipmentCommitRequest(connection.Player.Slot, 50, 100, 0, 71, 0);
    Check(items.TrySet(connection, rawMoney), "configured money image");
    if (mode != 1)
    {
        var budget = new long[59]; budget[50] = 100;
        items.AdoptFundedQuantities(connection, new short[59], budget);
    }
    var npcs = new RuntimeNpcStore();
    var npcBody = new NpcStateUpdate(17, 17, 100, 100, 0, 0, 255, default,
        NpcSimulationState.Initial with { Life = 250, LifeMax = 250 });
    Check(npcs.TrySpawn(0, in npcBody, out var vendor), "live vendor");
    var offers = new[] { new VanillaTownShopEntry1458(0, new(24), null, VanillaTownShopCurrency1458.Coins) };
    var session = new RuntimeTownShopSession1458(0, new(17), RuntimeTownShopSessionKind1458.OrdinaryShop,
        1, false, default, offers, RuntimeTownCommerceMissingFacts1458.None) { VendorHandle = vendor.Handle };
    Check(memberOwner.TrySetTalkNpc(connection, 0), "talk");
    Check(memberOwner.TrySetTownShopSession(connection, session), "session");
    var stock = new ShopItem1458[40]; stock[0] = new(new(24, 1, 0, false), 100, false, true, 100);
    Check(RuntimeTownShopQuote1458.TryCreate(session, memberOwner, npcs, connection, vendor.Handle,
        stock, 1, false, out var quote, new(false, false, false)), "quote");
    session.Quote = quote;
    ConnectionHandle otherConnection = Connection(1);
    if (mode == 6)
    {
        memberOwner.Commit(new RuntimePlayerMember { Connection = otherConnection, Slot = otherConnection.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400 });
        Check(items.TryAttach(otherConnection), "disjoint callback player attached");
    }
    sink.OnEquipment = slot =>
    {
        Check(items.TryGet(connection, 58, out var cursor) && cursor.ItemType.Value == 24,
            "first observer already sees item grant");
        Check(items.CaptureFundedCoinValues(connection).Sum() == 0, "first observer paid budget");
        Check(quote.TryCapture(out var acceptedQuote) && acceptedQuote.Memos.Length == 1,
            "first observer sees sellback memo");
        Check(member.ItemPhase!.Value.ShopOpen, "first observer sees shop-open phase");
        if (mode == 4 && slot == 50)
        {
            Check(npcs.TryUpdate(vendor.Handle, npcBody with { PositionX = 101 }, out _), "vendor moves after adoption");
            throw new InvalidOperationException("observer throw");
        }
        if (mode == 6 && slot == 50)
        {
            Check(players.TryApply(new PlayerEquipmentRuntimeCommand(otherConnection,
                new(otherConnection.Player.Slot, 0, 1, 0, 3, 0))), "actual disjoint callback report");
            Check(items.TryGet(otherConnection, 0, out var otherItem) && otherItem.ItemType.Value == 3,
                "disjoint actual report is not fenced by unrelated publication");
            Check(items.TryGetFundedQuantity(otherConnection, 0, out var otherQuantity) && otherQuantity == 0,
                "disjoint report never mints funding");
        }
        if (mode == 7 && slot == 50)
        {
            Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,
                new(connection.Player.Slot, 58, 1, 0, 3, 0))), "actual same-player reentry command");
            Check(items.TryGet(connection, 58, out var sameActorItem) && sameActorItem.ItemType.Value == 24,
                "same-player shop publication reentry refuses its raw mutation");
        }
        if (mode == 5 && slot == 50)
        {
            Check(items.TrySet(connection, new(connection.Player.Slot, 58, 1, 0, 3, 0)), "newer cursor writer");
        }
    };
    Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,
        new(connection.Player.Slot, 50, 0, 0, 0, 0))), "actual first5");
    Check(items.TryGet(connection, 50, out var heldMoney) && heldMoney.Stack == 100, "first5 remains deferred");
    Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,
        new(connection.Player.Slot, 58, 1, 0, 24, 0))), "actual cursor5");
    Check(items.TryGet(connection, 58, out var priorCursor) && priorCursor.IsEmpty, "58 not delimiter");
    if (mode == 2)
    {
        var other = Connection(1); Check(items.TryAttach(other), "disjoint attach");
        Check(items.TrySet(other, new(other.Player.Slot, 0, 1, 0, 3, 0)), "disjoint traffic");
    }
    if (mode == 3) member.ItemPhase = null;
    Exception? failure = null;
    try { Check(Complete(players, connection), "actual138"); }
    catch (Exception ex) { failure = ex; }
    if (mode == 1 || mode == 3)
    {
        Check(failure is null && sink.Slots.Count == 0, "unknown funding or retired phase refuses without events");
        Check(items.TryGet(connection, 50, out heldMoney) && heldMoney.Stack == 100, "refusal retains money");
        Check(items.TryGet(connection, 58, out priorCursor) && priorCursor.IsEmpty, "refusal grants nothing");
    }
    else
    {
        Check(mode == 4 ? failure?.Message == "observer throw" : failure is null, "event throw status");
        Check(sink.Slots.SequenceEqual(mode == 5 ? new short[] { 50 } : mode == 6 ? new short[] { 50, 0, 58 } : new short[] { 50, 58 }),
            "independent accepted-slot tail and throw drain");
        Check(Complete(players, connection), "duplicate138");
        Check(sink.Slots.Count == (mode == 5 ? 1 : mode == 6 ? 3 : 2), "duplicate boundary cannot publish again");
        Check(items.TryGetFundedQuantity(connection, 58, out var quantity) && quantity == (mode == 5 ? 0 : 1),
            "funded cursor once or newer replacement preserved");
    }
}

}

sealed class Sink : IRuntimePlayerEventSink
{
    internal readonly List<short> Slots = [];
    internal Action<short>? OnEquipment;
    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    { Slots.Add(request.SlotId); OnEquipment?.Invoke(request.SlotId); }
    public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) { }
    public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) { }
    public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) { }
    public void PlayerDisconnected(ConnectionHandle c) { }
}
}
private static class FundingScratchProbeCases { internal static void Run() {
int checks=0;
void Check(bool b,string n){checks++;if(!b)throw new InvalidOperationException(n);}
for(int mode=0;mode<3;mode++) {
 var original=new WorldItemPickupSlot1458[59];var banks=new WorldItemPickupSlot1458[160];var stock=new ShopItem1458[40];
 original[50]=new(74,9999,0,false);original[0]=new(72,99,0,false);
 if(mode==2){original[0]=new(71,99,0,false);original[1]=new(71,1,0,false);}
 Check(VanillaItemStoreValue1458.TryGet(new(110),new(0),out int value),"source value");stock[0]=new(new(110,1,0,false),value,false,true);var context=new ShopPriceContext1458(1f,false);
 RuntimePlayerInventoryItem[] Physical(WorldItemPickupSlot1458[] a,WorldItemPickupSlot1458[] b)=>a.Concat(b).Select(x=>x.Type==0?default:new RuntimePlayerInventoryItem(new(x.Type),(short)x.Stack,new(x.Prefix),0)).ToArray();
 var before=Physical(original,banks);var budget=new long[219];budget[50]=mode==1?9999000000:999900;if(mode==1)budget[0]=9900;
 var q=new short[59];var caps=new short[59 * RuntimePlayerInventoryStore.QuantityLaneCount];Array.Fill(caps,(short)-1);var scratch=new RuntimePlayerInventoryStore.ShopFundingComparison(before,budget,q,caps);
 long[]? first=null;RuntimePlayerInventoryItem[]? firstPhysical=null;
 foreach(int n in new[]{1,2,7,100}) {
  Check(VanillaShopTransaction1458.TryPlanBuy(original,banks,stock,[],default,0,n,context,out var bought)&&bought is not null,"buy prefix");
  Check(VanillaShopTransaction1458.TryPlanSell(bought!.Wallet.Inventory,bought.Wallet.Banks,bought.Stock,bought.Memos,bought.Cursor,context,out var sold)&&sold is not null,"sell prefix");
  var bp=Physical(bought.Wallet.Inventory,bought.Wallet.Banks);var sp=Physical(sold!.Wallet.Inventory,sold.Wallet.Banks);
  Check(RuntimePlayerInventoryStore.TryPlanShopCopper(before,bp,budget,-bought.CopperDelta,0,out var bq)&&RuntimePlayerInventoryStore.TryPlanShopCopper(bp,sp,bq,0,sold.CopperDelta,out _),"existing funding helper admission");
  Check(RuntimePlayerInventoryStore.TryPlanShopCopper(bp,sp,bq,0,sold.CopperDelta,out var sq),"sold funding helper");
  firstPhysical??=sp;first??=sq;bool expected=sq.AsSpan().SequenceEqual(first);
  Check(scratch.TryCompare(bought.Wallet.Inventory,bought.Wallet.Banks,sold.Wallet.Inventory,sold.Wallet.Banks,n,-bought.CopperDelta,sold.CopperDelta,out bool same),"borrowed scratch admission");
  Check(same==expected,"borrowed funded outcome agrees actual existing helper");
  if(mode==0&&n==2)Check(!same,"actual partial funding ambiguity retained");
  if(mode==1&&sp.AsSpan().SequenceEqual(firstPhysical))Check(same,"fully funded identical physical representative converges");
 }
}
}

}
private static class HiddenExactUnitsProbeCases { internal static void Run() {

int checks = 0, rows = 0;
void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
bool B(JsonElement x, string key) => x.GetProperty(key).GetBoolean();
RuntimePlayerInventoryItem Item(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0
    ? default : new(new(I(x,"type")), checked((short)I(x,"stack")), new(I(x,"prefix")), B(x,"favorite") ? (byte)1 : (byte)0);
ShopItem1458 Stock(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0 ? default : new(
    new(I(x,"type"), I(x,"stack"), I(x,"prefix"), B(x,"favorite")), I(x,"value"), B(x,"buyOnce"), B(x,"isAShopItem"));
T Owner<T>(PlayerAuthority players, string name) => (T)typeof(PlayerAuthority).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(players)!;
foreach(int requested in new[]{17,9999})
for (int platform = 0; platform < 1; platform++)
{
    using var document = Read("configured-hidden-shape", platform);
    foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
    {
        rows++;
        string name = row.GetProperty("spec").GetProperty("name").GetString()!;
        var before = row.GetProperty("before"); var after = row.GetProperty("afterSync");
        var spec = row.GetProperty("spec");
        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
        players.SetPlayerUpdateRandom(new(0));
        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
        players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
        var memberOwner = Owner<RuntimePlayerMembership>(players,"membership");
        var items = Owner<RuntimePlayerInventoryStore>(players,"inventory");
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
        var member = new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400,
            ItemAnimation = I(before,"animation") };
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = I(before,"itemTime") } };
        memberOwner.Commit(member); Check(items.TryAttach(connection), name+" attach");
        var image = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
        image[58] = Item(before.GetProperty("mouse"));
        var q = new short[59]; var money = new long[59];
        for (short i = 0; i < 59; i++)
        {
            Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot,i)), name+" initial inventory");
            if (VanillaCoinFacts.TryGetValue(image[i].ItemType,out long unit)) money[i] = unit * Math.Max(0,(int)image[i].Stack);
            else q[i] = Math.Max((short)0,image[i].Stack);
        }
        image[0]=new(new(72),99,new(0),0);
        Check(items.TrySet(connection,image[0].ToCommitRequest(connection.Player.Slot,0)),"partial initial Silver99");
        money[0]=9900;money[50]=9999000000;
        items.AdoptFundedQuantities(connection,q,money);
        var bankImage = before.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray();
        var bankCopper = new long[160];
        for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType,out long unit)) bankCopper[i] = unit * Math.Max(0,(int)bankImage[i].Stack);
        Check(items.TryAdoptShopBanks(connection,bankImage,bankCopper),name+" configured banks");
        var npcs = new RuntimeNpcStore();
        var body = new NpcStateUpdate(17,17,100,100,0,0,255,default,NpcSimulationState.Initial with { Life=250,LifeMax=250 });
        Check(npcs.TrySpawn(0,in body,out var vendor),name+" vendor");
        var stock = before.GetProperty("shop").EnumerateArray().Select(Stock).ToArray();
        for(int i=0;i<stock.Length;i++)stock[i]=stock[i] with {CustomPrice=99};
        var offers = stock.Select((item,index)=>(item,index)).Where(x=>x.item.Tuple.Type!=0)
            .Select(x=>new VanillaTownShopEntry1458(x.index,new(x.item.Tuple.Type),null,VanillaTownShopCurrency1458.Coins)).ToArray();
        var session = new RuntimeTownShopSession1458(0,new(17),RuntimeTownShopSessionKind1458.OrdinaryShop,
            spec.GetProperty("adjust").GetSingle(),false,default,offers,RuntimeTownCommerceMissingFacts1458.None) { VendorHandle=vendor.Handle };
        Check(memberOwner.TrySetTalkNpc(connection,0),name+" talk");
        Check(memberOwner.TrySetTownShopSession(connection,session),name+" session");
        Check(RuntimeTownShopQuote1458.TryCreate(session,memberOwner,npcs,connection,vendor.Handle,stock,
            3,platform!=0,out var quote,new(false,false,false),3),name+" quote");
        session.Quote=quote;
        var original=image.Select(x=>x.IsEmpty?default:new WorldItemPickupSlot1458(x.ItemType.Value,x.Stack,x.Prefix.Value,false)).ToArray();
        var bankTuples=bankImage.Select(x=>x.IsEmpty?default:new WorldItemPickupSlot1458(x.ItemType.Value,x.Stack,x.Prefix.Value,false)).ToArray();
        var context=new ShopPriceContext1458(1f,false);
        Check(VanillaShopTransaction1458.TryPlanBuy(original,bankTuples,stock,[],default,0,requested,context,out var bought)&&bought is not null,"one prefix");
        Check(VanillaShopTransaction1458.TryPlanSell(bought!.Wallet.Inventory,bought.Wallet.Banks,bought.Stock,bought.Memos,bought.Cursor,context,out var sold)&&sold is not null,"one refund");
        for(short i=0;i<59;i++) {
            var x=sold!.Wallet.Inventory[i];var result=x.Type==0?default:new RuntimePlayerInventoryItem(new(x.Type),(short)x.Stack,new(x.Prefix),0);
            if(result!=image[i])Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,result.ToCommitRequest(connection.Player.Slot,i))),"actual changed5");
        }
        Check(Complete(players, connection),"actual ambiguous138");
        var expected = sold!.Wallet.Inventory.Select(x=>x.Type==0?default:new RuntimePlayerInventoryItem(new(x.Type),(short)x.Stack,new(x.Prefix),0)).ToArray();
        for(short i=0;i<59;i++) Check(items.TryGet(connection,i,out var current)&&current==expected[i],name+" source inventory slot"+i);
        var banks = new RuntimePlayerInventoryItem[160];var funds=new long[160];
        Check(items.TryCaptureShopBanks(connection,banks,funds),name+" capture final banks");
        Check(banks.AsSpan().SequenceEqual(before.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray()),name+" full160 source banks");
        Check(quote.TryCapture(out var economic),name+" final quote");
        Check(economic.Stock40.AsSpan().SequenceEqual(stock),name+" source stock40");
        var memos=after.GetProperty("memo").EnumerateArray().Select(x=>new ShopMemo1458(I(x,"type"),I(x,"prefix"),I(x,"stack"))).ToArray();
        Check(economic.Memos.AsSpan().SequenceEqual(memos),name+" source memo");
    }
}
}

}
private static class HiddenAmbiguityProbeCases { internal static void Run() {

int checks = 0, rows = 0;
void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
bool B(JsonElement x, string key) => x.GetProperty(key).GetBoolean();
RuntimePlayerInventoryItem Item(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0
    ? default : new(new(I(x,"type")), checked((short)I(x,"stack")), new(I(x,"prefix")), B(x,"favorite") ? (byte)1 : (byte)0);
ShopItem1458 Stock(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0 ? default : new(
    new(I(x,"type"), I(x,"stack"), I(x,"prefix"), B(x,"favorite")), I(x,"value"), B(x,"buyOnce"), B(x,"isAShopItem"));
T Owner<T>(PlayerAuthority players, string name) => (T)typeof(PlayerAuthority).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(players)!;
for (int platform = 0; platform < 1; platform++)
{
    using var document = Read("configured-hidden-shape", platform);
    foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
    {
        rows++;
        string name = row.GetProperty("spec").GetProperty("name").GetString()!;
        var before = row.GetProperty("before"); var after = row.GetProperty("afterSync");
        var spec = row.GetProperty("spec");
        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
        players.SetPlayerUpdateRandom(new(0));
        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
        players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
        var memberOwner = Owner<RuntimePlayerMembership>(players,"membership");
        var items = Owner<RuntimePlayerInventoryStore>(players,"inventory");
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
        var member = new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
            Revision = 1, HasHealth = true, Life = 400, MaxLife = 400,
            ItemAnimation = I(before,"animation") };
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = I(before,"itemTime") } };
        memberOwner.Commit(member); Check(items.TryAttach(connection), name+" attach");
        var image = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
        image[58] = Item(before.GetProperty("mouse"));
        var q = new short[59]; var money = new long[59];
        for (short i = 0; i < 59; i++)
        {
            Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot,i)), name+" initial inventory");
            if (VanillaCoinFacts.TryGetValue(image[i].ItemType,out long unit)) money[i] = unit * Math.Max(0,(int)image[i].Stack);
            else q[i] = Math.Max((short)0,image[i].Stack);
        }
        image[0]=new(new(72),99,new(0),0);
        Check(items.TrySet(connection,image[0].ToCommitRequest(connection.Player.Slot,0)),"partial initial Silver99");
        money[0]=0;money[50]=999900;
        items.AdoptFundedQuantities(connection,q,money);
        var bankImage = before.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray();
        var bankCopper = new long[160];
        for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType,out long unit)) bankCopper[i] = unit * Math.Max(0,(int)bankImage[i].Stack);
        Check(items.TryAdoptShopBanks(connection,bankImage,bankCopper),name+" configured banks");
        var npcs = new RuntimeNpcStore();
        var body = new NpcStateUpdate(17,17,100,100,0,0,255,default,NpcSimulationState.Initial with { Life=250,LifeMax=250 });
        Check(npcs.TrySpawn(0,in body,out var vendor),name+" vendor");
        var stock = before.GetProperty("shop").EnumerateArray().Select(Stock).ToArray();
        var offers = stock.Select((item,index)=>(item,index)).Where(x=>x.item.Tuple.Type!=0)
            .Select(x=>new VanillaTownShopEntry1458(x.index,new(x.item.Tuple.Type),null,VanillaTownShopCurrency1458.Coins)).ToArray();
        var session = new RuntimeTownShopSession1458(0,new(17),RuntimeTownShopSessionKind1458.OrdinaryShop,
            spec.GetProperty("adjust").GetSingle(),false,default,offers,RuntimeTownCommerceMissingFacts1458.None) { VendorHandle=vendor.Handle };
        Check(memberOwner.TrySetTalkNpc(connection,0),name+" talk");
        Check(memberOwner.TrySetTownShopSession(connection,session),name+" session");
        Check(RuntimeTownShopQuote1458.TryCreate(session,memberOwner,npcs,connection,vendor.Handle,stock,
            3,platform!=0,out var quote,new(false,false,false),3),name+" quote");
        session.Quote=quote;
        var original=image.Select(x=>x.IsEmpty?default:new WorldItemPickupSlot1458(x.ItemType.Value,x.Stack,x.Prefix.Value,false)).ToArray();
        var bankTuples=bankImage.Select(x=>x.IsEmpty?default:new WorldItemPickupSlot1458(x.ItemType.Value,x.Stack,x.Prefix.Value,false)).ToArray();
        var context=new ShopPriceContext1458(1f,false);
        Check(VanillaShopTransaction1458.TryPlanBuy(original,bankTuples,stock,[],default,0,100,context,out var bought)&&bought is not null,"one prefix");
        Check(VanillaShopTransaction1458.TryPlanSell(bought!.Wallet.Inventory,bought.Wallet.Banks,bought.Stock,bought.Memos,bought.Cursor,context,out var sold)&&sold is not null,"one refund");
        for(short i=0;i<59;i++) {
            var x=sold!.Wallet.Inventory[i];var result=x.Type==0?default:new RuntimePlayerInventoryItem(new(x.Type),(short)x.Stack,new(x.Prefix),0);
            if(result!=image[i])Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,result.ToCommitRequest(connection.Player.Slot,i))),"actual changed5");
        }
        Check(Complete(players, connection),"actual ambiguous138");
        var expected = sold!.Wallet.Inventory.Select(x=>x.Type==0?default:new RuntimePlayerInventoryItem(new(x.Type),(short)x.Stack,new(x.Prefix),0)).ToArray();
        for(short i=0;i<59;i++) Check(items.TryGet(connection,i,out var current)&&current==expected[i],name+" source inventory slot"+i);
        var banks = new RuntimePlayerInventoryItem[160];var funds=new long[160];
        Check(items.TryCaptureShopBanks(connection,banks,funds),name+" capture final banks");
        Check(banks.AsSpan().SequenceEqual(before.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray()),name+" full160 source banks");
        Check(quote.TryCapture(out var economic),name+" final quote");
        Check(economic.Stock40.AsSpan().SequenceEqual(stock),name+" source stock40");
        var memos=after.GetProperty("memo").EnumerateArray().Select(x=>new ShopMemo1458(I(x,"type"),I(x,"prefix"),I(x,"stack"))).ToArray();
        Check(economic.Memos.AsSpan().SequenceEqual(memos),name+" source memo");
    }
}
}

}

 private static class ContinuationCoreCases { internal static void Run() {
int checks=0;
void Check(bool value,string name){checks++;Assert.True(value,name);}
var a=GameCommandSourceId.FromConnection(101);var b=GameCommandSourceId.FromConnection(102);
var state=new ProbeState();
using(var loop=new AuthoritativeGameLoop<ProbeState,int>(state,
    (s,c)=>s.Applied.Enqueue(c),
    s=>Interlocked.Increment(ref s.Updates),
    new GameLoopOptions{TicksPerSecond=1000,CommandCapacity=8,MaxCommandIngressPerTick=8,MaxCommandsPerTick=8,MaxCommandsPerSourcePerTick=8,MaxPendingCommandsPerSource=2},
    (s,source,c)=>{if(source==a){Interlocked.Increment(ref s.Readiness);return Volatile.Read(ref s.Open)!=0;}return true;}))
{
 Check(loop.TryPost(a,1),"blocked first accepted");Check(loop.TryPost(a,2),"blocked second accepted");
 Check(!loop.TryPost(a,3),"blocked pending capacity retained");Check(loop.TryPost(b,10),"other source accepted");
 loop.Start();Check(SpinWait.SpinUntil(()=>Volatile.Read(ref state.Updates)>=3,TimeSpan.FromSeconds(3)),"updates progress while blocked");
 Check(state.Applied.Contains(10),"other source progresses");Check(!state.Applied.Contains(1),"blocked head not dequeued");
 Check(loop.Snapshot.PendingCommands==2,"both blocked counters retained");
 Volatile.Write(ref state.Open,1);
 Check(SpinWait.SpinUntil(()=>state.Applied.Count==3,TimeSpan.FromSeconds(3)),"unblocked work drains");
 Check(loop.Stop(TimeSpan.FromSeconds(3)),"stop success");Check(loop.Fault is null,"no fault");
 Check(state.Applied.ToArray().SequenceEqual(new[]{10,1,2}),"FIFO and other-source fairness");
 Check(state.Readiness<=state.Updates+2,"blocked source checked at most once per drain");
 Check(loop.Snapshot.PendingCommands==0,"pending counters released on actual dequeue");
}
var failed=new ProbeState();
using(var loop=new AuthoritativeGameLoop<ProbeState,int>(failed,(s,c)=>s.Applied.Enqueue(c),s=>{},
 new GameLoopOptions{TicksPerSecond=1000,CommandCapacity=2,MaxCommandIngressPerTick=2,MaxCommandsPerTick=2,MaxCommandsPerSourcePerTick=2},
 (s,source,c)=>throw new InvalidOperationException("readiness fault")))
{
 Check(loop.TryPost(a,1),"fault head accepted");loop.Start();
 Check(SpinWait.SpinUntil(()=>loop.Fault is not null,TimeSpan.FromSeconds(3)),"readiness fault surfaced");
 Check(failed.Applied.IsEmpty,"fault did not apply/dequeue");Check(loop.Fault?.Message=="readiness fault","exact fault retained");
 Check(loop.Stop(TimeSpan.FromSeconds(3)),"fault thread stopped");
}
var closed=new ProbeState();
using(var loop=new AuthoritativeGameLoop<ProbeState,int>(closed,(s,c)=>s.Applied.Enqueue(c),s=>Interlocked.Increment(ref s.Updates),
 new GameLoopOptions{TicksPerSecond=1000,CommandCapacity=2,MaxCommandIngressPerTick=2,MaxCommandsPerTick=2,MaxCommandsPerSourcePerTick=2},
 (s,source,c)=>false))
{
 Check(loop.TryPost(a,1),"shutdown head accepted");loop.Start();
 Check(SpinWait.SpinUntil(()=>Volatile.Read(ref closed.Updates)>=3,TimeSpan.FromSeconds(3)),"closed updates progress");
 Check(loop.Stop(TimeSpan.FromSeconds(3)),"shutdown does not wait for readiness");Check(closed.Applied.IsEmpty,"shutdown never applied blocked head");
}
Console.WriteLine($"{{\"checks\":{checks},\"failures\":0,\"scope\":\"actual Core source queue readiness, pending accounting, fairness, fault and shutdown\"}}");
 }
sealed class ProbeState{internal int Open,Updates,Readiness;internal ConcurrentQueue<int> Applied=new();}
 }

 private static class ContinuationApplicationCases { internal static void Run() {
int checks=0;
void Check(bool value,string name){checks++;Assert.True(value,name);}
T Owner<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
var tiles=new WorldTileStore(new WorldDimensions(400,300));var npcs=new RuntimeNpcStore();
var state=new ServerRuntimeState(worldTiles:tiles,npcs:npcs,playerUpdateRandomSeed:new(0),itemPrefixWorld:new(false,false,false));
var players=Owner<ServerRuntimeComposition>(state,"_runtime").Players;
players.SetPlayerUpdateWorldFacts(new(400,300,80,false,false));
players.SetRemotePlayerEnvironment(new(false,false),new RuntimeProjectileStore());
var members=Owner<RuntimePlayerMembership>(players,"membership");var items=Owner<RuntimePlayerInventoryStore>(players,"inventory");
var c=new ConnectionHandle(GameCommandSourceId.FromConnection(101),new(new(0),new(1)));
var other=new ConnectionHandle(GameCommandSourceId.FromConnection(102),new(new(1),new(1)));
foreach(var connection in new[]{c,other})
{
 members.Commit(new RuntimePlayerMember{Connection=connection,Slot=connection.Player.Slot,Revision=1,HasHealth=true,Life=400,MaxLife=400,HasMana=true,Mana=200,MaxMana=200,PositionX=100,PositionY=100});
 Check(items.TryAttach(connection),"attach");
 Check(Owner<RuntimePlayerTransferProfileStore>(players,"transferProfiles").TrySetBuffTypes(connection,Array.Empty<BuffTypeId>()),"owned clear buff profile");
}
Check(items.TrySet(c,new(c.Player.Slot,50,2,0,72,0)),"configured trusted money");var funds=new long[59];funds[50]=200;items.AdoptFundedQuantities(c,new short[59],funds);
var body=new NpcStateUpdate(17,17,100,100,0,0,255,default,NpcSimulationState.Initial with{Life=250,LifeMax=250});
Check(npcs.TrySpawn(0,in body,out var vendor),"vendor");
var offers=new[]{new VanillaTownShopEntry1458(0,new(8),100,VanillaTownShopCurrency1458.Coins)};
var session=new RuntimeTownShopSession1458(0,new(17),RuntimeTownShopSessionKind1458.OrdinaryShop,1,false,default,offers,RuntimeTownCommerceMissingFacts1458.None){VendorHandle=vendor.Handle};
Check(members.TrySetTalkNpc(c,0),"talk");Check(members.TrySetTownShopSession(c,session),"session");
var stock=new ShopItem1458[40];stock[0]=new(new(8,1,0,false),50,false,true,100);
Check(RuntimeTownShopQuote1458.TryCreate(session,members,npcs,c,vendor.Handle,stock,1,false,out var quote,new(false,false,false)),"quote");session.Quote=quote;
Check(players.TryTickRemotePlayerPhase(),"initial configured whole phase admitted");
var applied=new ConcurrentQueue<string>();int updates=0,done=0,maxSteps=0;
using(var loop=new AuthoritativeGameLoop<ServerRuntimeState,RuntimeCommand>(state,
 (runtime,command)=>{runtime.Apply(command);applied.Enqueue(command.GetType().Name);},
 runtime=>{runtime.Tick();Interlocked.Increment(ref updates);maxSteps=Math.Max(maxSteps,players.LastShopWorkSteps);if(items.TryGet(c,58,out var cursor)&&cursor.ItemType.Value==8&&cursor.Stack==2)Volatile.Write(ref done,1);},
 new GameLoopOptions{TicksPerSecond=1000,CommandCapacity=16,MaxCommandIngressPerTick=16,MaxCommandsPerTick=16,MaxCommandsPerSourcePerTick=16},
 static(runtime,source,command)=>runtime.CanApplyCommand(source,command)))
{
 RuntimeCommand[] commands=[new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,50,1,0,72,0)),new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,58,1,0,8,0)),new PlayerInventorySyncCompleteRuntimeCommand(c),new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,50,0,0,0,0)),new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,58,2,0,8,0)),new PlayerInventorySyncCompleteRuntimeCommand(c)];
 foreach(var command in commands)Check(loop.TryPost(c.Source,command),"two consecutive batches queued");
 Check(loop.TryPost(other.Source,new PlayerEquipmentRuntimeCommand(other,new(other.Player.Slot,0,1,0,3,0))),"disjoint source queued");
 loop.Start();bool completed=SpinWait.SpinUntil(()=>Volatile.Read(ref done)!=0||loop.Fault is not null,TimeSpan.FromSeconds(3));
 Check(loop.Stop(TimeSpan.FromSeconds(3)),"loop stopped");Console.WriteLine($"completed={completed} updates={updates} steps={maxSteps} fault={loop.Fault} pending={loop.Snapshot.PendingCommands} rejected={players.RejectedEquipmentUpdates} phase={members.TryGet(c,out var cm) && cm.ItemPhase!=null} receipt={cm?.CursorMove?.Shop?.IsSealed} applied={string.Join(",",applied)}");Check(loop.Fault is null,"no authoritative loop fault");Check(completed,"actual State.Tick resolves both queued batches");
 Check(loop.Snapshot.PendingCommands==0,"all queued commands consumed exactly once");
}
Check(items.TryGet(c,58,out var result)&&result.ItemType.Value==8&&result.Stack==2,"successive purchases grant two units");
Check(items.TryGet(c,50,out var money)&&money.IsEmpty,"both purchases debit money exactly");
Check(items.CaptureFundedCoinValues(c).Sum()==0,"no trusted copper minted");
Check(items.TryGet(other,0,out var disjoint)&&disjoint.ItemType.Value==3,"disjoint source progresses");
Check(quote.TryCapture(out var economic)&&economic.Memos.Length==1&&economic.Memos[0].Stack==2,"memo records both purchases");
Check(maxSteps<=PlayerAuthority.ShopWorkStepsPerTick&&updates>=2,"one global work budget across actual ticks");
Check(players.RejectedEquipmentUpdates==0,"legitimate consecutive batches were never rejected");
Console.WriteLine($"{{\"checks\":{checks},\"updates\":{updates},\"maxSteps\":{maxSteps},\"failures\":0,\"scope\":\"actual Core queue plus State.Apply/State.Tick consecutive shop batches and disjoint source; configured owner funding/vendor fixture\"}}");
 }

 }

 private static class ContinuationCustodyCases { internal static void Run() {
int checks=0,cases=0;
void Check(bool value,string name){checks++;Assert.True(value,name);}
T Owner<T>(PlayerAuthority owner,string name)=>(T)typeof(PlayerAuthority).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
PlayerAuthority Create(){var p=new PlayerAuthority(null,new WorldTileStore(new WorldDimensions(400,300)));p.SetPlayerUpdateRandom(new(0));p.SetPlayerUpdateWorldFacts(new(400,300,80,false,false));p.BindReceiveEquipmentRandom(new(0),false,new(false,false,false));return p;}
Fixture Prepare(PlayerAuthority p,byte slot)
{
 var members=Owner<RuntimePlayerMembership>(p,"membership");var items=Owner<RuntimePlayerInventoryStore>(p,"inventory");
 var c=new ConnectionHandle(GameCommandSourceId.FromConnection(101+slot),new(new(slot),new(1)));
 var member=new RuntimePlayerMember{Connection=c,Slot=c.Player.Slot,Revision=1,HasHealth=true,Life=400,MaxLife=400};members.Commit(member);
 Check(items.TryAttach(c),"attach");Check(items.TrySet(c,new(c.Player.Slot,0,99,0,72,0)),"Silver99");Check(items.TrySet(c,new(c.Player.Slot,50,9999,0,74,0)),"Platinum9999");
 var budget=new long[59];budget[0]=9900;budget[50]=9999000000;items.AdoptFundedQuantities(c,new short[59],budget);
 var npcs=new RuntimeNpcStore();var body=new NpcStateUpdate(17,17,100,100,0,0,255,default,NpcSimulationState.Initial with{Life=250,LifeMax=250});Check(npcs.TrySpawn(0,in body,out var vendor),"vendor");
 var offers=new[]{new VanillaTownShopEntry1458(0,new(110),99,VanillaTownShopCurrency1458.Coins)};
 var session=new RuntimeTownShopSession1458(0,new(17),RuntimeTownShopSessionKind1458.OrdinaryShop,1,false,default,offers,RuntimeTownCommerceMissingFacts1458.None){VendorHandle=vendor.Handle};
 Check(members.TrySetTalkNpc(c,0),"talk");Check(members.TrySetTownShopSession(c,session),"session");
 var stock=new ShopItem1458[40];stock[0]=new(new(110,1,0,false),100,false,true,99);
 Check(RuntimeTownShopQuote1458.TryCreate(session,members,npcs,c,vendor.Handle,stock,1,false,out var quote,new(false,false,false)),"quote");session.Quote=quote;
 var before=new RuntimePlayerInventoryItem[59];Check(items.TryCopyInventory(c,before),"before59");
 var source=before.Select(x=>x.IsEmpty?default:new WorldItemPickupSlot1458(x.ItemType.Value,x.Stack,x.Prefix.Value,false)).ToArray();var banks=new WorldItemPickupSlot1458[160];var context=new ShopPriceContext1458(1,false);
 Check(VanillaShopTransaction1458.TryPlanBuy(source,banks,stock,[],default,0,9999,context,out var bought)&&bought is not null,"configured source-shaped9999 buy");
 Check(VanillaShopTransaction1458.TryPlanSell(bought!.Wallet.Inventory,bought.Wallet.Banks,bought.Stock,bought.Memos,bought.Cursor,context,out var sold)&&sold is not null,"configured refund");
 var after=sold!.Wallet.Inventory.Select(x=>x.Type==0?default:new RuntimePlayerInventoryItem(new(x.Type),(short)x.Stack,new(x.Prefix),0)).ToArray();
 for(short i=0;i<59;i++)if(before[i]!=after[i])Check(p.TryApply(new PlayerEquipmentRuntimeCommand(c,after[i].ToCommitRequest(c.Player.Slot,i))),"actual changed5");
 Check(p.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(c)),"seal138");Check(!p.CanApplyShopSource(c.Source),"sealed source readiness blocks successors");
 Check(items.TryCopyInventory(c,before),"no debit at seal");
 return new(p,c,member,members,items,before,after,quote,sold,()=>npcs.TryUpdate(vendor.Handle,body with{PositionX=101},out _),()=>npcs.TryUpdate(vendor.Handle,body with{Type=18,NetId=18},out _),()=>offers[0]=new(0,new(109),99,VanillaTownShopCurrency1458.Coins));
}
for(int mode=0;mode<12;mode++)
{
 cases++;var p=Create();var f=Prepare(p,0);
 if(mode==0){f.Member.ItemAnimation=7;f.Member.ItemPhase=f.Member.ItemPhase!.Value with{Selected=f.Member.ItemPhase.Value.Selected with{ItemTime=7}};}
 p.TickShopJournalWork();
 Check(p.LastShopWorkSteps==PlayerAuthority.ShopWorkStepsPerTick,"one finite global slice");
 Check(f.Member.CursorMove?.Shop is {IsSealed:true},"9999 stays retained after first slice");
 switch(mode)
 {
 case 0:break;
 case 1:Check(p.TryApply(new PlayerEquipmentRuntimeCommand(f.C,new(f.C.Player.Slot,50,9998,0,74,0))),"new direct5 handled");break;
 case 2:Check(p.TryApply(new PlayerEquipmentRuntimeCommand(f.C,new(f.C.Player.Slot,59,1,0,3,0))),"equipment change handled");break;
 case 3:f.Member.IsDead=true;break;
 case 4:f.Member.ItemPhase=null;break;
 case 5:Check(f.Members.TryRemove(f.C,out _),"exact member removal");break;
 case 6:Check(f.Items.TrySet(f.C,new(f.C.Player.Slot,1,1,0,3,0)),"intervening inventory writer");break;
 case 7:f.Member.TryAdvanceRevision();break;
 case 8:f.MoveVendor();break;
 case 9:f.ReplaceVendor();break;
 case 10:f.ChangeOffers();break;
 case 11:
  Check(p.TrySetTalkNpc(f.C,TerraRuntime.Protocol.Multiplicity.TerrariaNpcTalkCodec.NoNpc),"retire captured receipt");
  var journal=f.Member.CursorMove!.Shop!;
  Check(journal.IsRetired&&f.Quote.IsRetainedJournalCurrent(journal),"retired current query validates held capability");
  Check(!f.Quote.TryPrepare(journal.Economic!,f.Plan,1,1,1,journal,out _),"held retired receipt still cannot prepare adoption");break;
 }
 if(mode is 9 or 10){p.TickShopJournalWork();Check(p.CanApplyShopSource(f.C.Source),"changed vendor/offer cancels at next bounded slice");}
 int ticks=1;while(!p.CanApplyShopSource(f.C.Source)){Check(++ticks<1000,"finite complete bound");p.TickShopJournalWork();Check(p.LastShopWorkSteps<=PlayerAuthority.ShopWorkStepsPerTick,"global operation bound");}
 if(mode is 0 or 8 or 11)
 {
  var current=new RuntimePlayerInventoryItem[59];Check(f.Items.TryCopyInventory(f.C,current)&&current.AsSpan().SequenceEqual(f.After),"captured clock family survives owned clock-only progression");
  if(mode==0)Check(f.Member.ItemAnimation==7&&f.Member.ItemPhase!.Value.Selected.ItemTime==7,"accepted payment preserves newer clocks");
  if(mode!=11)Check(f.Quote.TryCapture(out var economic)&&economic.Memos.Length==0,"source whole refund memo restored");
 }
 else
 {
  Check(f.Items.TryGet(f.C,58,out var cursor)&&cursor.IsEmpty,"cancel never grants cursor");
  Check(f.Items.TryGet(f.C,50,out var wallet)&&wallet==f.Before[50],"cancel never adopts economic debit");
  Check(f.Items.CaptureFundedCoinValues(f.C)[50]==9999000000,"cancel never changes trusted budget");
 }
}
{
 cases++;var p=Create();var first=Prepare(p,0);var second=Prepare(p,1);int ticks=0,firstCompleted=0,secondCompleted=0;
 while(!p.CanApplyShopSource(first.C.Source)||!p.CanApplyShopSource(second.C.Source))
 {
  Check(++ticks<1000,"two-player finite work bound");p.TickShopJournalWork();Check(p.LastShopWorkSteps<=PlayerAuthority.ShopWorkStepsPerTick,"single global budget, not two per-player budgets");
  if(p.CanApplyShopSource(first.C.Source)&&firstCompleted==0)firstCompleted=ticks;
  if(p.CanApplyShopSource(second.C.Source)&&secondCompleted==0)secondCompleted=ticks;
 }
 Check(Math.Abs(firstCompleted-secondCompleted)<=1,"round-robin fair completion of equal work");
 foreach(var f in new[]{first,second}){var current=new RuntimePlayerInventoryItem[59];Check(f.Items.TryCopyInventory(f.C,current)&&current.AsSpan().SequenceEqual(f.After),"both exact source-shaped images adopted");}
}
Console.WriteLine($"{{\"checks\":{checks},\"cases\":{cases},\"failures\":0,\"scope\":\"configured source-shaped9999 hidden journal continuation, clocks, new5/equipment/death/phase/membership/inventory/input cancellation and two-player global fair budget; not original Main\"}}");

 }
sealed record Fixture(PlayerAuthority P,ConnectionHandle C,RuntimePlayerMember Member,RuntimePlayerMembership Members,RuntimePlayerInventoryStore Items,RuntimePlayerInventoryItem[] Before,RuntimePlayerInventoryItem[] After,RuntimeTownShopQuote1458 Quote,ShopTransactionPlan1458 Plan,Action MoveVendor,Action ReplaceVendor,Action ChangeOffers);

 }

private static class ContinuationBoundStateCases { internal static void Run() {
int checks=0;
void Check(bool value,string name){checks++;Assert.True(value,name);}
T Owner<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
Fixture Prepare(PlayerAuthority p,byte slot)
{
 var members=Owner<RuntimePlayerMembership>(p,"membership");var items=Owner<RuntimePlayerInventoryStore>(p,"inventory");
 var c=new ConnectionHandle(GameCommandSourceId.FromConnection(101+slot),new(new(slot),new(1)));
 var member=new RuntimePlayerMember{Connection=c,Slot=c.Player.Slot,Revision=1,HasHealth=true,Life=400,MaxLife=400};members.Commit(member);
 Check(items.TryAttach(c),"attach");Check(items.TrySet(c,new(c.Player.Slot,0,99,0,72,0)),"Silver99");Check(items.TrySet(c,new(c.Player.Slot,50,9999,0,74,0)),"Platinum9999");
 var budget=new long[59];budget[0]=9900;budget[50]=9999000000;items.AdoptFundedQuantities(c,new short[59],budget);
 var npcs=new RuntimeNpcStore();var body=new NpcStateUpdate(17,17,100,100,0,0,255,default,NpcSimulationState.Initial with{Life=250,LifeMax=250});Check(npcs.TrySpawn(0,in body,out var vendor),"vendor");
 var offers=new[]{new VanillaTownShopEntry1458(0,new(110),99,VanillaTownShopCurrency1458.Coins)};
 var session=new RuntimeTownShopSession1458(0,new(17),RuntimeTownShopSessionKind1458.OrdinaryShop,1,false,default,offers,RuntimeTownCommerceMissingFacts1458.None){VendorHandle=vendor.Handle};
 Check(members.TrySetTalkNpc(c,0),"talk");Check(members.TrySetTownShopSession(c,session),"session");
 var stock=new ShopItem1458[40];stock[0]=new(new(110,1,0,false),100,false,true,99);
 Check(RuntimeTownShopQuote1458.TryCreate(session,members,npcs,c,vendor.Handle,stock,1,false,out var quote,new(false,false,false)),"quote");session.Quote=quote;
 var before=new RuntimePlayerInventoryItem[59];Check(items.TryCopyInventory(c,before),"before59");
 var source=before.Select(x=>x.IsEmpty?default:new WorldItemPickupSlot1458(x.ItemType.Value,x.Stack,x.Prefix.Value,false)).ToArray();var banks=new WorldItemPickupSlot1458[160];var context=new ShopPriceContext1458(1,false);
 Check(VanillaShopTransaction1458.TryPlanBuy(source,banks,stock,[],default,0,9999,context,out var bought)&&bought is not null,"configured source-shaped9999 buy");
 Check(VanillaShopTransaction1458.TryPlanSell(bought!.Wallet.Inventory,bought.Wallet.Banks,bought.Stock,bought.Memos,bought.Cursor,context,out var sold)&&sold is not null,"configured refund");
 var after=sold!.Wallet.Inventory.Select(x=>x.Type==0?default:new RuntimePlayerInventoryItem(new(x.Type),(short)x.Stack,new(x.Prefix),0)).ToArray();
 for(short i=0;i<59;i++)if(before[i]!=after[i])Check(p.TryApply(new PlayerEquipmentRuntimeCommand(c,after[i].ToCommitRequest(c.Player.Slot,i))),"actual changed5");
 Check(p.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(c)),"seal138");Check(!p.CanApplyShopSource(c.Source),"sealed source readiness blocks successors");
 Check(items.TryCopyInventory(c,before),"no debit at seal");
 return new(p,c,member,members,items,before,after,quote,sold,()=>npcs.TryUpdate(vendor.Handle,body with{PositionX=101},out _),()=>npcs.TryUpdate(vendor.Handle,body with{Type=18,NetId=18},out _),()=>offers[0]=new(0,new(109),99,VanillaTownShopCurrency1458.Coins));
}
var state=new ServerRuntimeState(worldTiles:new WorldTileStore(new WorldDimensions(400,300)),playerUpdateRandomSeed:new(0),itemPrefixWorld:new(false,false,false));
var p=Owner<ServerRuntimeComposition>(state,"_runtime").Players;
p.SetPlayerUpdateWorldFacts(new(400,300,80,false,false));p.SetRemotePlayerEnvironment(new(false,false),new TerraRuntime.Core.Projectiles.RuntimeProjectileStore());
var f=Prepare(p,0);
f.Member.HasMana=true;f.Member.Mana=200;f.Member.MaxMana=200;f.Member.PositionX=100;f.Member.PositionY=100;
Check(Owner<RuntimePlayerTransferProfileStore>(p,"transferProfiles").TrySetBuffTypes(f.C,Array.Empty<BuffTypeId>()),"owned profile");
int ticks=0;bool admitted=true;
while(!p.CanApplyShopSource(f.C.Source))
{
 Check(++ticks<1000,"finite actual State.Tick completion");state.Tick();
 admitted &= f.Member.ItemPhase is not null;
 Check(p.LastShopWorkSteps<=64,"bounded actual state global slice");
}
var current=new RuntimePlayerInventoryItem[59];Check(f.Items.TryCopyInventory(f.C,current)&&current.AsSpan().SequenceEqual(f.After),"whole source-shaped accepted image after owned phases");
Check(admitted,"bound phase stayed admitted across all ticks");Check(ticks>2,"real multi-tick large receipt");
Check(f.Quote.TryCapture(out var final)&&final.Memos.Length==0,"owned phase never invalidated quote economics");
Console.WriteLine($"{{\"checks\":{checks},\"ticks\":{ticks},\"failures\":0,\"scope\":\"actual State.Tick owned phases across configured source-shaped9999 receipt; not whole Main\"}}");

}
sealed record Fixture(PlayerAuthority P,ConnectionHandle C,RuntimePlayerMember Member,RuntimePlayerMembership Members,RuntimePlayerInventoryStore Items,RuntimePlayerInventoryItem[] Before,RuntimePlayerInventoryItem[] After,RuntimeTownShopQuote1458 Quote,ShopTransactionPlan1458 Plan,Action MoveVendor,Action ReplaceVendor,Action ChangeOffers);

}

private static class BankCoincidentCursorCases { internal static void Run() {
for(int mode=0;mode<3;mode++) {
var players=new PlayerAuthority(null,new WorldTileStore(new WorldDimensions(400,300)));
players.SetPlayerUpdateRandom(new(0));players.SetPlayerUpdateWorldFacts(new(400,300,80,false,false));players.BindReceiveEquipmentRandom(new(0),false,new(false,false,false));
T Owner<T>(string name)=>(T)typeof(PlayerAuthority).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(players)!;
var members=Owner<RuntimePlayerMembership>("membership");var items=Owner<RuntimePlayerInventoryStore>("inventory");var c=new ConnectionHandle(GameCommandSourceId.FromConnection(9001),new(new(0),new(1)));
var member=new RuntimePlayerMember{Connection=c,Slot=c.Player.Slot,Revision=1,HasHealth=true,Life=400,MaxLife=400};members.Commit(member);items.TryAttach(c);
var bank=new RuntimePlayerInventoryItem[160];bank[0]=new(new(109),20,new(0),0);var quantity=new short[160];quantity[0]=20;var caps=new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount];Array.Fill(caps,(short)-1);items.TryAdoptShopBanks(c,bank,new long[160],quantity,caps);
if(mode==2) {
 var other=new ConnectionHandle(GameCommandSourceId.FromConnection(9002),new(new(1),new(1)));
 members.Commit(new RuntimePlayerMember{Connection=other,Slot=other.Player.Slot,Revision=1,HasHealth=true,Life=400,MaxLife=400});items.TryAttach(other);
 players.TryApply(new PlayerEquipmentRuntimeCommand(other,new(other.Player.Slot,0,7,0,3,0)));
}
players.TryApply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,58,20,0,109,0)));items.TryGet(c,58,out var current);
Console.WriteLine($"ordinaryCursorExpected=109/20 actual={current.ItemType.Value}/{current.Stack} staged={member.CursorMove?.Shop!=null} revision={member.Revision}");
if(current.ItemType.Value!=109||current.Stack!=20)Assert.Fail("Source-valid bank alternative stole unrelated ordinary cursor receive");

if(items.CaptureFundedQuantities(c)[58]!=0)Assert.Fail("Raw first58 minted funding");
if(mode>0)players.TryApply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,99,0,0,0,0)));
long rejected=players.RejectedEquipmentUpdates;
Complete(players,c);
if(mode==0 && (players.RejectedEquipmentUpdates!=rejected||items.CaptureFundedQuantities(c)[58]!=0))Assert.Fail("ordinary138 manufactured or rejected bank intent");
if(mode>0 && (items.CaptureFundedQuantities(c)[58]!=20||!items.TryCaptureShopBankOwnership(c,out var q,out _)||q[0]!=0))Assert.Fail("Bank funding not conserved across accepted first58/disjoint writer");
Console.WriteLine($"mode={mode} passed funded={items.CaptureFundedQuantities(c)[58]} rejected={players.RejectedEquipmentUpdates}");
}
Console.WriteLine("cases=3 failures=0 scope=actual TryApply ordinary coincident58/no-bank138 and source-shaped bank completion with disjoint traffic before capture");

} }
}
