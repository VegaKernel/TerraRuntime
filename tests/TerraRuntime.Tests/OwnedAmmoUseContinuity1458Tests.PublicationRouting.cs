using System.Buffers.Binary;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed partial class OwnedAmmoUseContinuity1458Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Ammo_history_rebinding_keeps_the_real_paid_health_publication_afterimage(int deliveryTiming)
    {
        foreach(int weapon in new[]{39,98})
        {
            using var source=Read(weapon,"repeat",0);using var f=Start(source.RootElement);
            f.Equip(10,28,2);f.Fund(10,2);f.Fund(54,12);
            var reports=new List<byte[]>();var acks=new List<byte[]>();
            ApplyDelayedInputs(f,source.RootElement.GetProperty("steps")[0],reports,acks);f.State.Tick();
            f.Control(0,false);for(int tick=0;tick<40;tick++)f.State.Tick();
            f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection,new(f.Player.Slot,50,100)));
            f.Control(10,false);f.State.Tick();f.Events.Health.Clear();f.Drain();
            bool delivered=false;
            void Deliver()
            {
                if(delivered)return;delivered=true;
                foreach(var report in reports)
                {
                    if(deliveryTiming==2)
                    {
                        var invalid=Decode(report);invalid=invalid with {Damage=(short)(invalid.Damage+1)};
                        f.AmmoReport(in invalid);
                    }
                    else f.OriginalAmmoFrame(report);
                }
                if(deliveryTiming==3)f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection,new(f.Player.Slot,70,100)));
            }
            if(deliveryTiming==0)Deliver();else f.Events.OnEquipment=Deliver;
            f.Control(10,true);f.State.Tick();
            f.Events.OnEquipment=null;Assert.True(delivered);
            int expectedLife=deliveryTiming==3?70:100;
            Assert.Equal(expectedLife,f.Member.NpcHealth!.Value.Life);Assert.Equal(1,f.Item(10).Stack);
            Assert.Equal(1,f.Funded(10));Assert.Single(f.ToolClock.WeaponShootHistory!.Uses);
            Assert.Equal(expectedLife,Assert.Single(f.Events.Health).Life);
            var random=f.PlayerRandom.Clone();foreach(var report in reports)f.OriginalAmmoFrame(report);
            Assert.Equal(deliveryTiming==2?0:1,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.Equal(deliveryTiming==2,f.ToolClock.WeaponShootHistory!.Uses[0].Retired);
            Assert.Equal(11,f.Item(54).Stack);Assert.Equal(11,f.Funded(54));
            Assert.True(f.PlayerRandom.HasSameState(random));
        }
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void Historical_ammo_slot_yields_to_owned_moves_and_last_placement_tail(bool matchingEmpty,bool probeCursor)
    {
        using var f=new LocalWeaponClockRuntimeChecks1458.Fixture(false);
        short stock=matchingEmpty?(short)1:(short)3;
        f.Equip(7,39,1);f.Equip(0,40,stock);f.Equip(49,matchingEmpty?(short)0:(short)2,matchingEmpty?(short)0:(short)1);
        f.Fund(0,stock);if(!matchingEmpty)f.Fund(49,1);
        f.Control(7,false);f.State.Tick();f.Control(7,true);f.State.Tick();f.SourceBirth(39);
        Assert.Equal((short)0,Assert.Single(f.ToolClock.WeaponShootHistory!.Uses).AmmoSlot);
        Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);
        f.Control(7,false);for(int tick=0;tick<40;tick++)f.State.Tick();
        if(matchingEmpty)
        {
            var drop=new WorldItemDropStateUpdate(f.Member.PositionX+4,f.Member.PositionY+4,0,0,1,0,
                WorldItemOwnershipMode.None,2,false,0,0);
            Assert.True(f.SpawnSourceDrop(in drop,out _));f.DiscoverOwner();
        }
        else
        {
            InventoryReport(f,0,0,0);InventoryReport(f,58,40,2);f.OriginalAmmoFrame([3,0,138]);
            Assert.True(f.Item(0).IsEmpty);Assert.Equal(2,f.Funded(58));
            InventoryReport(f,10,40,2);InventoryReport(f,58,0,0);f.OriginalAmmoFrame([3,0,138]);
            InventoryReport(f,49,0,0);InventoryReport(f,58,2,1);f.OriginalAmmoFrame([3,0,138]);
            InventoryReport(f,0,2,1);InventoryReport(f,58,0,0);f.OriginalAmmoFrame([3,0,138]);
            Assert.Equal(2,f.Funded(10));
        }
        Assert.Equal(2,f.Item(0).ItemType.Value);Assert.Equal(1,f.Item(0).Stack);Assert.Equal(1,f.Funded(0));
        Assert.Equal(0,f.State.RejectedPlayerEquipmentUpdates);
        if(probeCursor)
        {
            // This is also an exact historical AmmoAfter. Alone it must not
            // eagerly debit the newly picked-up unit; the existing cursor
            // alternative can complete only on its exact paired58 image.
            InventoryReport(f,0,0,0);Assert.Equal(1,f.Item(0).Stack);Assert.Equal(1,f.Funded(0));
            InventoryReport(f,58,2,1);Assert.True(f.Item(0).IsEmpty);Assert.Equal(1,f.Funded(58));
            f.OriginalAmmoFrame([3,0,138]);
            InventoryReport(f,0,2,1);InventoryReport(f,58,0,0);f.OriginalAmmoFrame([3,0,138]);
            Assert.Equal(1,f.Item(0).Stack);Assert.Equal(1,f.Funded(0));
        }
        f.Control(0,false);f.State.Tick();f.Control(0,true);
        f.OriginalAmmoFrame([11,0,17,1,50,0,53,0,0,0,0]);
        InventoryReport(f,0,0,0);f.OriginalAmmoFrame([3,0,138]);
        Assert.False(f.Tiles.Get(50,53).IsActive);f.State.Tick();
        Assert.True(f.Tiles.Get(50,53).IsActive);Assert.True(f.Item(0).IsEmpty);Assert.Equal(0,f.Funded(0));
        long rejected=f.State.RejectedPlayerEquipmentUpdates;
        InventoryReport(f,0,matchingEmpty?(short)0:(short)40,matchingEmpty?(short)0:(short)2);
        Assert.Equal(rejected,f.State.RejectedPlayerEquipmentUpdates);
        Assert.True(f.Item(0).IsEmpty);Assert.Equal(0,f.Funded(0));
        // Establish an actual existing quantity-capped producer in that same
        // slot. Falling through from an unmatched ammo report cannot bypass
        // its funded109 ceiling or grant resource custody.
        f.Control(0,false);for(int tick=0;tick<45;tick++)f.State.Tick();
        var manaDrop=new WorldItemDropStateUpdate(f.Member.PositionX+4,f.Member.PositionY+4,0,0,1,0,
            WorldItemOwnershipMode.None,109,false,0,0);
        Assert.True(f.SpawnSourceDrop(in manaDrop,out _));f.DiscoverOwner();
        Assert.Equal(109,f.Item(0).ItemType.Value);Assert.Equal(1,f.Funded(0));
        rejected=f.State.RejectedPlayerEquipmentUpdates;var random=f.PlayerRandom.Clone();
        InventoryReport(f,0,109,5);f.OriginalAmmoFrame([3,0,138]);
        Assert.Equal(rejected+1,f.State.RejectedPlayerEquipmentUpdates);Assert.Equal(1,f.Item(0).Stack);
        Assert.Equal(1,f.Funded(0));Assert.True(f.PlayerRandom.HasSameState(random));
    }

    private static void InventoryReport(LocalWeaponClockRuntimeChecks1458.Fixture f,short slot,short type,short stack)
    {
        byte[] wire=new byte[12];BinaryPrimitives.WriteUInt16LittleEndian(wire,12);wire[2]=5;wire[3]=f.Player.Slot.Value;
        BinaryPrimitives.WriteInt16LittleEndian(wire.AsSpan(4),slot);BinaryPrimitives.WriteInt16LittleEndian(wire.AsSpan(6),stack);
        BinaryPrimitives.WriteInt16LittleEndian(wire.AsSpan(9),type);f.OriginalAmmoFrame(wire);
    }
}
