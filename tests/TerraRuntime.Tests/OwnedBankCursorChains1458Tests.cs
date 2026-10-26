using System.Reflection;
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
using System.IO.Compression;

namespace TerraRuntime.Tests;

public sealed class OwnedBankCursorChains1458Tests
{
 [Fact]
 public void Original_multi_bank_images_preserve_complete_owned_custody()
 {
int checks = 0, rows = 0;var diagnostics=new List<object>();
void Check(bool value, string name) { checks++; Assert.True(value,name); }
int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
bool B(JsonElement x, string key) => x.GetProperty(key).GetBoolean();
RuntimePlayerInventoryItem Item(JsonElement x) => I(x,"type") == 0 || I(x,"stack") <= 0
    ? default : new(new(I(x,"type")), checked((short)I(x,"stack")), new(I(x,"prefix")), B(x,"favorite") ? (byte)1 : (byte)0);
T Owner<T>(PlayerAuthority players, string name) => (T)typeof(PlayerAuthority).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(players)!;
for (int platform = 0; platform < 2; platform++)
{
    using var stream=typeof(OwnedBankCursorChains1458Tests).Assembly.GetManifestResourceStream("OriginalMultiBankCursor1458")!;
    using var gzip=new GZipStream(stream,CompressionMode.Decompress);
    using var document=JsonDocument.Parse(gzip);
    foreach (var row in document.RootElement.GetProperty(platform == 0 ? "Linux" : "Windows").GetProperty("rows").EnumerateArray())
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
        var target=after.GetProperty("inventory").EnumerateArray().Select(Item).Concat(after.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item)).ToArray();
        var seen=new bool[219];var original=image.Concat(bankImage).ToArray();for(int i=0;i<219;i++)seen[i]=target[i]!=original[i];
        int reports=seen.Count(x=>x);var accepted=new List<bool>();
        Check(items.TryCaptureShopJournal(connection,out var capture),name+" capture");
        if(!original.AsSpan().SequenceEqual(target))
        {
            var work=items.TryCreateShopBankChainWork(connection,capture!,target,seen,I(before,"animation"),I(before,"itemTime"));
            Check(work is not null,name+" work");
            while(work!.MoveNext()) { }
            Check(!work.Ambiguous,name+" nonambiguous");
            Check(work.Preparation is not null,name+" accepted source chain");
            Check(work.Preparation!.TryAdoptUnpublished(),name+" adopt");accepted.Add(true);
        }
        var finalMain=new RuntimePlayerInventoryItem[59];for(short i=0;i<59;i++)Check(items.TryGet(connection,i,out finalMain[i]),name+" capture main");
        var finalBanks=new RuntimePlayerInventoryItem[160];var finalBankCopper=new long[160];Check(items.TryCaptureShopBanks(connection,finalBanks,finalBankCopper),name+" capture banks");
        Check(items.TryCaptureShopBankOwnership(connection,out var finalBankQuantity,out var caps),name+" bank q");
        var finalMainQuantity=items.CaptureFundedQuantities(connection);var finalMainCopper=items.CaptureFundedCoinValues(connection);
        var expectedMain=after.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();var expectedBank=after.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray();
        bool physical=finalMain.AsSpan().SequenceEqual(expectedMain)&&finalBanks.AsSpan().SequenceEqual(expectedBank);
        long beforeCopper=money.Sum()+bankCopper.Sum(),afterCopper=finalMainCopper.Sum()+finalBankCopper.Sum();
        long beforeQuantity=q.Sum(x=>(long)x)+bankQuantity.Sum(x=>(long)x),afterQuantity=finalMainQuantity.Sum(x=>(long)x)+finalBankQuantity.Sum(x=>(long)x);
        bool funded=true;for(int i=0;i<219;i++){var expected=i<59?expectedMain[i]:expectedBank[i-59];long copper=i<59?finalMainCopper[i]:finalBankCopper[i-59];short quantity=i<59?finalMainQuantity[i]:finalBankQuantity[i-59];long expectedCopper=VanillaCoinFacts.TryGetValue(expected.ItemType,out var unit)?unit*Math.Max(0,(int)expected.Stack):0;funded &= copper==expectedCopper&&quantity==(expectedCopper==0?Math.Max(0,(int)expected.Stack):0);}
        Check(physical,name+" physical");Check(funded,name+" fullfunding");
        diagnostics.Add(new{name,platform,reports,accepted,physicalSourceFinal=physical,fundedSourceFinal=funded,beforeCopper,afterCopper,beforeQuantity,afterQuantity});

    }
}
 Assert.Equal(144,rows);
 Assert.Equal(18560,checks);
 }
 [Fact]
 public void Partial_quantity_fractional_copper_caps_and_stale_epochs_never_mint()
 {
     var invalidStore = new RuntimePlayerInventoryStore();
     var invalidConnection = new ConnectionHandle(GameCommandSourceId.FromConnection(91004), new(new(0), new(1)));
     Assert.True(invalidStore.TryAttach(invalidConnection));
     int[] identities = [3, 9, 23, 109, 110, 189, 500, 2209];
     var invalidQuantity = new short[59];
     for (short slot = 0; slot < 58; slot++)
     {
         var item = new RuntimePlayerInventoryItem(new(identities[slot % identities.Length]), 100, new(0), 0);
         Assert.True(invalidStore.TrySet(invalidConnection, item.ToCommitRequest(invalidConnection.Player.Slot, slot)));
         invalidQuantity[slot] = 100;
     }
     invalidStore.AdoptFundedQuantities(invalidConnection, invalidQuantity);
     var fullBanks = new RuntimePlayerInventoryItem[160]; var fullFunding = new short[160];
     var fullCaps = new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount]; Array.Fill(fullCaps, (short)-1);
     for (int slot = 0; slot < fullBanks.Length; slot++)
     { fullBanks[slot] = new(new(identities[slot % identities.Length]), 100, new(0), 0); fullFunding[slot] = 100; }
     Assert.True(invalidStore.TryAdoptShopBanks(invalidConnection, fullBanks, new long[160], fullFunding, fullCaps));
     Assert.True(invalidStore.TryCaptureShopJournal(invalidConnection, out var invalidCapture));
     var lostUnits = invalidCapture!.Before.ToArray(); var lostSeen = new bool[219];
     lostUnits[10] = lostUnits[10] with { Stack = 99 }; lostSeen[10] = true;
     for (int slot = 59; slot < 219; slot++)
     { lostUnits[slot] = lostUnits[slot] with { Stack = 99 }; lostSeen[slot] = true; }
     // Town's actual adversarial journal loses 161 physical units. Refuse before
     // allocating the DFS, not after a timing/operation-count cutoff.
     Assert.Null(invalidStore.TryCreateShopBankChainWork(invalidConnection, invalidCapture, lostUnits, lostSeen, 0, 0));
     var inventedFavorite = invalidCapture.Before.ToArray(); inventedFavorite[59] = inventedFavorite[59] with { ItemFlags = 1 };
     var favoriteSeen = new bool[219]; favoriteSeen[59] = true;
     Assert.Null(invalidStore.TryCreateShopBankChainWork(invalidConnection, invalidCapture, inventedFavorite, favoriteSeen, 0, 0));
     Assert.True(invalidCapture.IsCurrent);

     // Source-certified primitive composition: a favorite last-one Void item
     // retains favorite on both Left and Right pickup, so only an actual normal
     // bank relay can explain its nonfavorite Void destination.
     var relayStore = new RuntimePlayerInventoryStore();
     var relayConnection = new ConnectionHandle(GameCommandSourceId.FromConnection(91003), new(new(0), new(1)));
     Assert.True(relayStore.TryAttach(relayConnection));
     var relayBanks = new RuntimePlayerInventoryItem[160]; relayBanks[120] = new(new(109), 1, new(0), 1);
     var relayQuantity = new short[160]; relayQuantity[120] = 1;
     var relayCaps = new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount]; Array.Fill(relayCaps, (short)-1);
     Assert.True(relayStore.TryAdoptShopBanks(relayConnection, relayBanks, new long[160], relayQuantity, relayCaps));
     Assert.True(relayStore.TryCaptureShopJournal(relayConnection, out var relayCapture));
     var relayTarget = relayCapture!.Before.ToArray(); relayTarget[179] = default; relayTarget[180] = new(new(109), 1, new(0), 0);
     var relaySeen = new bool[219]; relaySeen[179] = relaySeen[180] = true;
     using var relayWork = relayStore.TryCreateShopBankChainWork(relayConnection, relayCapture, relayTarget, relaySeen, 0, 0);
     Assert.NotNull(relayWork); while (relayWork.MoveNext()) { }
     Assert.False(relayWork.Ambiguous); Assert.NotNull(relayWork.Preparation);
     Assert.True(relayWork.Preparation.TryAdoptUnpublished());
     foreach (bool coin in new[] { false, true })
     foreach (short funding in new short[] { 0, 1 })
     {
         var store = new RuntimePlayerInventoryStore();
         var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(91001), new(new(0), new(1)));
         Assert.True(store.TryAttach(connection));
         var banks = new RuntimePlayerInventoryItem[160];
         banks[0] = new(new(coin ? 74 : 109), coin ? (short)1 : (short)3, new(0), 0);
         var money = new long[160]; var quantities = new short[160]; var caps = new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount];
         Array.Fill(caps, (short)-1);
         if (coin) money[0] = funding;
         else { quantities[0] = funding; caps[0] = 3; caps[40 * RuntimePlayerInventoryStore.QuantityLaneCount] = 0; }
         Assert.True(store.TryAdoptShopBanks(connection, banks, money, quantities, caps));
         Assert.True(store.TryCaptureShopJournal(connection, out var capture));
         var target = capture!.Before.ToArray(); target[99] = target[59]; target[59] = default;
         var seen = new bool[219]; seen[59] = seen[99] = true;
         var work = store.TryCreateShopBankChainWork(connection, capture, target, seen, 0, 0);
         Assert.NotNull(work);
         while (work.MoveNext()) { }
         Assert.False(work.Ambiguous);
         Assert.NotNull(work.Preparation);
         Assert.True(work.Preparation.TryAdoptUnpublished());
         var final = new RuntimePlayerInventoryItem[160]; var copper = new long[160];
         Assert.True(store.TryCaptureShopBanks(connection, final, copper));
         Assert.True(store.TryCaptureShopBankOwnership(connection, out var q, out var ceilings));
         Assert.Equal(banks[0], final[40]); Assert.Equal(default, final[0]);
         Assert.Equal(coin ? funding : 0, copper[40]);
         Assert.Equal(coin ? (short)0 : funding, q[40]);
         Assert.Equal(funding, coin ? copper.Sum() : q.Sum(x => (long)x));
         if (!coin) { Assert.Equal(0, ceilings[0]); Assert.Equal(3, ceilings[40 * RuntimePlayerInventoryStore.QuantityLaneCount]); }
         Assert.False(work.Preparation.TryAdoptUnpublished());

         Assert.True(store.TryCaptureShopJournal(connection, out var stale));
         var returned = stale!.Before.ToArray(); returned[59] = returned[99]; returned[99] = default;
         var delayed = store.TryCreateShopBankChainWork(connection, stale, returned, seen, 0, 0);
         Assert.NotNull(delayed); Assert.True(delayed.MoveNext());
         Assert.True(store.TrySet(connection, new RuntimePlayerInventoryItem(new(3), 1, new(0), 0).ToCommitRequest(connection.Player.Slot, 2)));
         Assert.False(delayed.MoveNext()); Assert.Null(delayed.Preparation);
         Assert.True(store.TryCaptureShopJournal(connection, out var abandoned));
         returned = abandoned!.Before.ToArray(); returned[59] = returned[99]; returned[99] = default;
         var canceled = store.TryCreateShopBankChainWork(connection, abandoned, returned, seen, 0, 0);
         Assert.NotNull(canceled); Assert.True(canceled.MoveNext());
         canceled.Dispose(); Assert.False(canceled.MoveNext()); Assert.Null(canceled.Preparation);
         Assert.True(abandoned.IsCurrent);
         Assert.True(store.TryCaptureShopJournal(connection, out var disconnected));
         returned = disconnected!.Before.ToArray(); returned[59] = returned[99]; returned[99] = default;
         var retired = store.TryCreateShopBankChainWork(connection, disconnected, returned, seen, 0, 0);
         Assert.NotNull(retired);
         store.Clear(connection);
         var replacement = new ConnectionHandle(connection.Source, new(connection.Player.Slot, new(2)));
         Assert.True(store.TryAttach(replacement));
         Assert.False(retired.MoveNext()); Assert.Null(retired.Preparation);
     }
 }
}
