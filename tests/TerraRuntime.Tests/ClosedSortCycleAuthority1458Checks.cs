using System.Runtime.CompilerServices;
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


namespace TerraRuntime.Tests;

internal static class ClosedSortCycleAuthority1458Checks
{
    internal static void Run()
    {
        ClosedSortCycleSource1458Checks.Run();
        int checks = 0, rows = 0, slices = 0, maximumWork = 0; var failures = new List<object>();
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
        int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
        RuntimePlayerInventoryItem Item(JsonElement x)=>x[0].GetInt32()==0||x[1].GetInt32()<=0?default:new(new(x[0].GetInt32()),checked((short)x[1].GetInt32()),new(x[2].GetInt32()),x[3].GetBoolean()?(byte)1:(byte)0);

        for (int corpus=1;corpus<2;corpus++)
        for (int platform=0;platform<2;platform++)
        {
            using var input=typeof(ClosedSortCycleAuthority1458Checks).Assembly.GetManifestResourceStream("OriginalLocalizedItemSorting1458")!;
            using var gz=new GZipStream(input,CompressionMode.Decompress);
            using var document=JsonDocument.Parse(gz);
            foreach (var row in document.RootElement.GetProperty(platform==0 ? "Linux" : "Windows").GetProperty("rows").EnumerateArray())
            {

                if (!row.GetProperty("spec").GetProperty("name").GetString()!.Contains("sortmain_rank_1423", StringComparison.Ordinal)) continue;
                rows++;
                try {
                string name = row.GetProperty("spec").GetProperty("name").GetString()!;
                var before = row.GetProperty("before"); var after = row.GetProperty("afterSync");
                var spec = row.GetProperty("spec");
                var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
                players.SetPlayerUpdateRandom(new(0));
                players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
                players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
                var memberOwner = Membership(players);
                var items = Inventory(players);
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
                    else q[i] = corpus==0 ? Math.Max((short)0,image[i].Stack) : (short)Math.Min(1,Math.Max(0,(int)image[i].Stack));
                }
                items.AdoptFundedQuantities(connection,q,money);
                items.AdoptOwnedQuantities(connection, [new(18, new(110), 2), new(15, new(110), 0), new(2, new(500), 0)]);
                var bankImage = before.GetProperty("banks").EnumerateArray().SelectMany(x=>x.EnumerateArray()).Select(Item).ToArray();
                var bankCopper = new long[160]; var bankQuantity = new short[160]; var bankCaps = new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount]; Array.Fill(bankCaps,(short)-1);
                for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType,out long unit)) bankCopper[i] = unit * Math.Max(0,(int)bankImage[i].Stack);
                for (int i = 0; i < 160; i++) if (bankCopper[i] == 0) bankQuantity[i] = (short)Math.Min(1,Math.Max(0,(int)bankImage[i].Stack));
                Check(items.TryAdoptShopBanks(connection,bankImage,bankCopper,bankQuantity,bankCaps),name+" configured banks");
                var originalBudgets = new Dictionary<(int Type,int Prefix),long>();
                void AddBudget(Dictionary<(int Type,int Prefix),long> values,RuntimePlayerInventoryItem item,int budget)
                { if (budget==0) return; var key=(item.ItemType.Value,item.Prefix.Value); values.TryGetValue(key,out var previous); values[key]=previous+budget; }
                for(int i=0;i<59;i++)AddBudget(originalBudgets,image[i],q[i]);
                for(int i=0;i<160;i++)AddBudget(originalBudgets,bankImage[i],bankQuantity[i]);
                long originalCopper=money.Sum()+bankCopper.Sum();
                // Another actor advances the GLOBAL serial after player0's last own stamp.
                // The first cursor report must still retain the source-valid bank alternative.
                var other = new ConnectionHandle(GameCommandSourceId.FromConnection(90002), new(new(1), new(1)));
                memberOwner.Commit(new RuntimePlayerMember { Connection = other, Slot = other.Player.Slot,
                    Revision = 1, HasHealth = true, Life = 400, MaxLife = 400 });
                Check(items.TryAttach(other), name+" disjoint actor attach");
                Check(players.TryApply(new PlayerEquipmentRuntimeCommand(other, new(other.Player.Slot, 2, 1, 0, 9, 0))),
                    name+" actual disjoint5 before bank cursor report");
                int slicesBefore = slices;
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
                    else if(bytes[2]==138)
                    {
                        Check(players.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(connection)),name+" actual138");
                        // A harness timeout catches a stuck resolver; this is not a production
                        // gesture/search limit. The existing source readiness owns completion.
                        int rowSlices = 0;
                        while (!players.CanApplyShopSource(connection.Source))
                        {
                            Check(rowSlices++ < 100_000,name+" finite journal retirement");
                            players.TickShopJournalWork();
                            Check(players.LastShopWorkSteps is >= 0 and <= PlayerAuthority.ShopWorkStepsPerTick,name+" global owned work bound");
                            maximumWork = Math.Max(maximumWork,players.LastShopWorkSteps); slices++;
                        }
                    }
                }
                var finalMain=new RuntimePlayerInventoryItem[59];
                for(short i=0;i<59;i++)Check(items.TryGet(connection,i,out finalMain[i]),name+" final main capture");
                var banks=new RuntimePlayerInventoryItem[160];var funds=new long[160];
                Check(items.TryCaptureShopBanks(connection,banks,funds),name+" final banks");
                Check(items.TryCaptureShopBankOwnership(connection,out var actualQuantity,out var actualCaps),name+" final bank ownership");
                var finalQ=items.CaptureFundedQuantities(connection);var finalMoney=items.CaptureFundedCoinValues(connection);
                var finalBudgets=new Dictionary<(int Type,int Prefix),long>();
                for(int i=0;i<59;i++){ Check(finalQ[i]>=0 && finalQ[i]<=finalMain[i].Stack,name+" main physical ceiling");AddBudget(finalBudgets,finalMain[i],finalQ[i]); }
                for(int i=0;i<160;i++){ Check(actualQuantity[i]>=0 && actualQuantity[i]<=banks[i].Stack,name+" bank physical ceiling");AddBudget(finalBudgets,banks[i],actualQuantity[i]); }
                foreach(var pair in finalBudgets){ originalBudgets.TryGetValue(pair.Key,out var original);Check(pair.Value<=original,name+" no per-identity grant"); }
                Check(finalMoney.Sum()+funds.Sum()<=originalCopper,name+" no copper grant");
                Check(players.RejectedEquipmentUpdates == 1, name+" source-indistinguishable closed cycle must reject");
                Check(finalQ[0] == 1 && finalQ[29] == 1, name+" no guessed closed-cycle quantity allocation");
                Check(finalMain[0] == image[0] && finalMain[29] == image[29], name+" exact unchanged Wood/prefix/favorite");
                Check(banks.AsSpan().SequenceEqual(bankImage), name+" all160 unchanged physical bank lanes");
                Check(actualQuantity.AsSpan().SequenceEqual(bankQuantity), name+" all160 unchanged private bank lanes");
                Check(funds.AsSpan().SequenceEqual(bankCopper), name+" all160 unchanged copper lanes");
                Check(actualCaps.AsSpan().SequenceEqual(bankCaps), name+" all800 unchanged bank cap lanes");
                var retainedCaps = items.CaptureOwnedQuantities(connection);
                Check(retainedCaps.Contains(new(15, new(110), 0)), name+" unadopted destination cap");
                Check(retainedCaps.Contains(new(2, new(500), 0)), name+" unrelated historical cap");
                Check(memberOwner.TryGet(connection,out var acceptedMember) && acceptedMember.ItemPhase is { ShopOpen:false },name+" bank move does not open shop");
                } catch (Exception ex) { failures.Add(new { corpus, platform, name = row.GetProperty("spec").GetProperty("name").GetString(), error = ex.Message }); }
            }
        }

        Xunit.Assert.Equal(24, rows);
        Xunit.Assert.Empty(failures);
        Xunit.Assert.InRange(maximumWork, 1, 64);
        Xunit.Assert.True(slices > 0);
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
}
