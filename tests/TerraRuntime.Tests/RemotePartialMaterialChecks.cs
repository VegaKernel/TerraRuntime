using System.Text.Json;
using System.Buffers.Binary;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;
using TerraRuntime.Tests.RemotePartialMaterials;

namespace TerraRuntime.Tests.RemotePartialMaterials;

internal static class RemotePartialMaterialChecks
{
    internal static int Run()
    {
int checks=0,cases=0;
foreach(var spec in new (int Type,int Amount,int Quantity,long Copper,int ResultType,int ResultStack,int ResultQ,long ResultCopper,int LocalStack)[]{
    (9,10,0,0L,36,1,0,0L,0),
    (9,10,5,0L,36,1,0,0L,0),(9,10,10,0L,36,1,1,0L,0),(9,20,10,0L,36,2,1,0L,0),
    (72,1,0,0L,71,100,0,0L,0),(72,1,0,1L,71,100,0,1L,0),(72,1,0,100L,71,100,0,100L,0),
    (71,200,0,1L,72,2,0,1L,0),
    (9,7,6,0L,36,1,0,0L,3),(9,7,7,0L,36,1,1,0L,3)})
{
    using var row=JsonDocument.Parse("{\"name\":\"owner-open\",\"before\":[{\"type\":"+spec.Type+",\"stack\":"+spec.Amount+",\"prefix\":0}]}");
    var chest=new WorldChest(0,20,20,"",[new(spec.Amount,spec.Type,0)]);
    var custody=new RuntimeWorldChestCustodySnapshot(Guid.NewGuid(),1,1,[new(chest,new short[]{(short)spec.Quantity},new long[]{spec.Copper})]);
    using var fixture=new Fixture(row.RootElement,customChests:[chest],custody:custody);
    fixture.Member.HasHealth=true;fixture.Member.Life=100;fixture.Member.MaxLife=100;
    if(spec.LocalStack>0)
    {
        Check(fixture.ActorInventory.TryApplyAtomic(fixture.Connection,[new RuntimePlayerInventoryMutation((byte)0,new(new(9),(short)spec.LocalStack,default,0))]),"configured local material");
        Fixture.Funding(fixture.ActorInventory)[0]=(short)spec.LocalStack;
    }
    var before=fixture.CapturePlayer();
    void Dispatch(byte[] encoded)
    {
        var frame=Fixture.Decode(encoded);fixture.Bootstrap.OnFrame(in frame);
        while(fixture.Commands.Commands.Count>0){var command=fixture.Commands.Commands[0];fixture.Commands.Commands.RemoveAt(0);if(!fixture.Processor.TryApply(command))fixture.State.Apply(command);}
    }
    // Configured module12 request using the source-pinned BCL format. These are
    // runtime custody fields, not private budgets claimed by original Terraria.
    using var body=new MemoryStream();using(var writer=new BinaryWriter(body,System.Text.Encoding.UTF8,true))
    {writer.Write((ushort)12);writer.Write7BitEncodedInt(1);writer.Write(spec.Type);writer.Write7BitEncodedInt(spec.Amount);writer.Write7BitEncodedInt(1);writer.Write7BitEncodedInt(0);}
    var request=new byte[3+body.Length];BinaryPrimitives.WriteUInt16LittleEndian(request,(ushort)request.Length);request[2]=82;body.ToArray().CopyTo(request,3);Dispatch(request);
    if(spec.LocalStack>0)
    {
        Dispatch(Convert.FromHexString("0C0005000000000000000000"));Dispatch(Convert.FromHexString("03008A"));
    }
    for(int i=0;i<2048 && fixture.Processor.AppliedCraftingRequests==0 && fixture.Processor.RejectedCraftingRequests==0;i++)
    {fixture.State.Tick();Check(fixture.ShopSteps+fixture.Processor.LastCraftingWorkSteps<=64,"request global64");}
    Check(fixture.Processor.AppliedCraftingRequests==1,"actual remote material debit");
    var certificate=fixture.Member.CursorMove?.Shop?.RemoteCosts;Check(certificate is not null,"cost retained");
    Check(certificate!.Materials.Length==1 && certificate.Materials[0].Amount==spec.Amount &&
        certificate.Materials[0].FundedQuantity==spec.Quantity && certificate.Materials[0].FundedCopper==spec.Copper,"actual partial certificate");
    int callbacks=0;
    fixture.Events.OnEquipment=()=>
    {
        callbacks++;
        var current=fixture.CapturePlayer();
        Check(current.Items[58].ItemType.Value==spec.ResultType && current.Items[58].Stack==spec.ResultStack,"observer finalphysical");
        Check(current.Quantity[58]==spec.ResultQ && current.Copper[58]==spec.ResultCopper,"observer exactfunding");
        Check(Fixture.CostConsumed(certificate) && !certificate.IsCurrent,"certificate consumedbeforeobserver");
    };
    var output=new byte[12];BinaryPrimitives.WriteUInt16LittleEndian(output,12);output[2]=5;output[3]=fixture.Connection.Player.Slot.Value;BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(4),58);BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(6),(short)spec.ResultStack);BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(9),(short)spec.ResultType);
    Dispatch(output);Dispatch(Convert.FromHexString("03008A"));
    for(int i=0;i<2048 && fixture.Member.CursorMove?.Shop is not null;i++){fixture.State.Tick();Check(fixture.ShopSteps+fixture.Processor.LastCraftingWorkSteps<=64,"final global64");}
    Check(fixture.Member.CursorMove?.Shop is null && callbacks==(spec.LocalStack>0?2:1),"once completed");
    var image=fixture.CapturePlayer();var world=fixture.Store.CaptureCustodySnapshot(custody.WorldUniqueId,1,2);
    Check(world.Chests[0].Chest.Items[0].IsEmpty && world.Chests[0].Quantity[0]==0 && world.Chests[0].Copper[0]==0,"exactworld debit");
    Check(image.Items[58].ItemType.Value==spec.ResultType && image.Items[58].Stack==spec.ResultStack,"exact resultphysical");
    Check(image.Quantity[58]==spec.ResultQ && image.Copper[58]==spec.ResultCopper,"exactexternalfunding");
    for(int slot=0;slot<219;slot++)if(slot!=58){Check(image.Items[slot]==(spec.LocalStack>0 && slot==0?default:before.Items[slot]),"otherphysical"+slot);Check(image.Quantity[slot]==(spec.LocalStack>0 && slot==0?0:before.Quantity[slot]) && image.Copper[slot]==before.Copper[slot],"otherfunding"+slot);}
    for(int lane=0;lane<219* RuntimePlayerInventoryStore.QuantityLaneCount;lane++)Check(image.Caps[lane]==(spec.LocalStack>0 && lane<RuntimePlayerInventoryStore.QuantityLaneCount && before.Caps[lane]>=0 ? (short)Math.Max(0,before.Caps[lane]-spec.LocalStack):before.Caps[lane]),"caps"+lane);
    Check(!certificate.TryConsumeUnpublished(fixture.Connection),"no secondcostconsumption");
    Console.WriteLine("PASS "+spec+" callbacks="+callbacks);cases++;
}
Console.WriteLine("REMOTE_PARTIAL cases="+cases+" checks="+checks);return checks;
void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException("Assert "+message+" at"+checks);}

    }
}
