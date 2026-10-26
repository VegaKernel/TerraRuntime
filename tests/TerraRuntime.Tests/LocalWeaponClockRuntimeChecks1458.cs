using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

internal sealed class LocalWeaponClockRuntimeChecks1458
{
    internal static int Run()
    {
        var checks=new LocalWeaponClockRuntimeChecks1458();
        checks.Earlier_remote_slot_keeps_common_before_local_feedback_and_pending_birth(false);
        checks.Earlier_remote_slot_keeps_common_before_local_feedback_and_pending_birth(true);
        checks.NoItems_added_after_the_owned_frame_does_not_reject_its_source_owner_tail(39,40);
        checks.NoItems_added_after_the_owned_frame_does_not_reject_its_source_owner_tail(113,0);
        checks.ManaFlower_checks_shortage_after_owned_regeneration_and_never_reuses_the_potion(11,false);
        checks.ManaFlower_checks_shortage_after_owned_regeneration_and_never_reuses_the_potion(11,true);
        checks.ManaFlower_checks_shortage_after_owned_regeneration_and_never_reuses_the_potion(0,false);
        checks.ManaFlower_checks_shortage_after_owned_regeneration_and_never_reuses_the_potion(0,true);
        checks.Full_inventory_does_not_replace_local_clock_or_spend_unrelated_stacks(39,40);
        checks.Full_inventory_does_not_replace_local_clock_or_spend_unrelated_stacks(98,97);
        checks.Full_inventory_does_not_replace_local_clock_or_spend_unrelated_stacks(4,0);
        checks.Full_inventory_does_not_replace_local_clock_or_spend_unrelated_stacks(113,0);
        checks.No_ammo_fails_source_start_but_preserves_idle_clock_for_a_tool(39);
        checks.No_ammo_fails_source_start_but_preserves_idle_clock_for_a_tool(98);
        checks.Last_arrow_keeps_paid_clock_when_dedicated_ammo_query_is_now_empty(false);
        checks.Last_arrow_keeps_paid_clock_when_dedicated_ammo_query_is_now_empty(true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(39,40,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(39,40,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(99,40,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(99,40,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3480,40,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3480,40,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3486,40,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3486,40,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3492,40,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3492,40,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3498,40,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3498,40,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3504,40,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3504,40,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3510,40,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3510,40,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3516,40,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3516,40,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(98,97,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(98,97,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(219,97,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(219,97,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(533,97,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(533,97,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(1929,97,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(1929,97,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(964,97,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(964,97,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(534,97,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(534,97,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(679,97,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(679,97,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(4703,97,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(4703,97,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(4,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(4,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(24,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(24,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(482,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(482,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(483,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(483,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(484,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(484,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(653,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(653,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(656,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(656,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(659,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(659,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(921,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(921,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(1185,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(1185,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(1192,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(1192,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(1199,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(1199,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(2517,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(2517,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(2745,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(2745,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3484,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3484,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3490,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3490,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3496,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3496,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3502,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3502,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3508,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3508,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3514,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3514,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3520,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(3520,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(113,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(113,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(218,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(218,0,true);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(495,0,false);
        checks.Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(495,0,true);
        checks.Input_FIFO_keeps_slot_order_for_remote_common_and_local_feedback(false);
        checks.Input_FIFO_keeps_slot_order_for_remote_common_and_local_feedback(true);
        checks.Transfer_preserves_adopted_times_but_retires_old_world_source_receipts(false);
        checks.Transfer_preserves_adopted_times_but_retires_old_world_source_receipts(true);
        checks.Changed_mana_after_payment_never_restores_the_earlier_afterimage_or_clock();
        checks.Full_mana_feedback_owns_five_source_draws_before_the_common_crit(false);
        checks.Full_mana_feedback_owns_five_source_draws_before_the_common_crit(true);
        checks.An_old_generation_capture_cannot_write_the_replacement_local_clock_or_inventory();
        checks.Source_magic_shortage_pays_once_and_retains_the_slow_local_period(0,false);
        checks.Source_magic_shortage_pays_once_and_retains_the_slow_local_period(1,false);
        checks.Source_magic_shortage_pays_once_and_retains_the_slow_local_period(200,false);
        checks.Source_magic_shortage_pays_once_and_retains_the_slow_local_period(0,true);
        checks.Source_magic_shortage_pays_once_and_retains_the_slow_local_period(1,true);
        checks.Source_magic_shortage_pays_once_and_retains_the_slow_local_period(200,true);
        checks.Source_magic_shortage_pays_once_and_retains_the_slow_local_period(13,true);
        checks.Source_magic_shortage_pays_once_and_retains_the_slow_local_period(13,false);
        checks.Early_paid_bow_cannot_be_shortened_by_a_new_tool_selection_and_success_claim();
        checks.Paid_clock_and_ammo_exist_before_reentrant_throwing_publication(false);
        checks.Paid_clock_and_ammo_exist_before_reentrant_throwing_publication(true);
        checks.Fresh_NoItems_cannot_commit_a_new_source_shoot_or_clock(39,40);
        checks.Fresh_NoItems_cannot_commit_a_new_source_shoot_or_clock(98,97);
        checks.Fresh_NoItems_cannot_commit_a_new_source_shoot_or_clock(113,0);
        checks.Owned_food_melee_speed_remains_in_the_weapon_clock_until_the_tool_transition();
        checks.The_last_available_member_revision_adopts_mana_and_clock_once();
        checks.Deferred_birth_keeps_original_launch_pose_after_owned_player_motion();
        checks.Uncompleted_source_offer_cannot_resurrect_local_clock_after_the_next_owned_frame();
        checks.Idle_owned_selection_can_accept_packet_first_without_a_fixture_release_tick(39,40);
        checks.Idle_owned_selection_can_accept_packet_first_without_a_fixture_release_tick(98,97);
        checks.Idle_owned_selection_can_accept_packet_first_without_a_fixture_release_tick(113,0);
        checks.Deferred_entropy_birth_keeps_input_currentness_and_consumes_before_observers("stale");
        checks.Deferred_entropy_birth_keeps_input_currentness_and_consumes_before_observers("throw");
        checks.Deferred_entropy_birth_keeps_input_currentness_and_consumes_before_observers("move");
        checks.Exact_preparation_refuses_inventory_mutation_and_unrelated_change_does_not_forge_a_new_epoch();
        foreach(short weapon in new short[]{113,218,495})
            foreach(bool acknowledgeFirst in new[]{false,true})checks.Late_magic_control_and_exact42_join_one_source_shoot(weapon,acknowledgeFirst);
        checks.Late_magic_without_control_or_with_changed_selector_never_borrows_a_future_epoch(false);
        checks.Late_magic_without_control_or_with_changed_selector_never_borrows_a_future_epoch(true);
        checks.Source_qualified_released_ranged_birth_does_not_invent_local_clocks();
        foreach(short mana in new short[]{0,11})foreach(bool funded in new[]{false,true})foreach(bool early in new[]{false,true})
            checks.Late_magic_refill_and42_use_one_owned_potion_and_one_payment(mana,funded,early);
        checks.Wrong_pending42_cannot_grant_mana_or_replace_the_exact_source_epoch(false);
        checks.Wrong_pending42_cannot_grant_mana_or_replace_the_exact_source_epoch(true);
        checks.Late_magic_input_ABA_cannot_requalify_the_retired_report();
        checks.Released_magic_casts_pay_again_from_fresh_clocks_without_reusing_the_first_receipt(false);
        checks.Released_magic_casts_pay_again_from_fresh_clocks_without_reusing_the_first_receipt(true);
        foreach(short weapon in new short[]{39,98})foreach(short buff in new short[]{23,47,149,156})foreach(bool paid in new[]{false,true})
            checks.Source_contact_blocks_fresh_ranged_clock_but_preserves_owned_shoot_tail(weapon,buff,paid);
        foreach(short weapon in new short[]{39,98})foreach(bool paid in new[]{false,true})
            checks.Source_contact_blocks_fresh_ranged_clock_but_preserves_owned_shoot_tail(weapon,23,paid,true);
        foreach(string edition in new[]{"Linux","Windows"})foreach(short staff in new short[]{739,740,741,742,743,744,3377})foreach(bool earlierRemote in new[]{false,true})
            checks.Seven_gem_entropy_births_keep_all_source_keys_before_tool_continuation(edition,staff,earlierRemote);
        checks.Local_mana_feedback_survives_paid_magic_to_released_tool(false);
        checks.Local_mana_feedback_survives_paid_magic_to_released_tool(true);
        return 203;
    }

     public void Original_local_weapon_common_clocks_match_both_source_editions()=>LocalWeaponClockSourceChecks1458.Run();
    public static IEnumerable<object[]> Weapons()
    {
        short[] ranged=[39,99,3480,3486,3492,3498,3504,3510,3516,98,219,533,1929,964,534,679,4703];
        short[] melee=[4,24,482,483,484,653,656,659,921,1185,1192,1199,2517,2745,3484,3490,3496,3502,3508,3514,3520];
        foreach(short id in ranged.Concat(melee).Concat(new short[]{113,218,495}))foreach(bool packetFirst in new[]{false,true})
            yield return new object[]{id,(short)(Array.IndexOf(ranged,id)<0?0:Array.IndexOf(ranged,id)<9?40:97),packetFirst};
    }

    public void Local_weapon_release_use_then_tool_keeps_the_owned_local_clock(short weapon,short ammo,bool packetFirst)
    {
        using var f=new Fixture(false);
        f.Equip(7,weapon,1);f.Equip(1,3509,1);if(ammo!=0)f.Equip(54,ammo,3);
        f.Select(7);f.SetMana();f.Control(7,false);f.State.Tick();
        // Original local ItemCheck preserves a known idle clock, even for a weapon.
        Assert.True(f.ToolClock.LocalClocksKnown);
        f.Control(7,true);
        bool shoots=ammo!=0 || weapon is 113 or 218 or 495;
        if(shoots && packetFirst)f.SourceBirth(weapon);
        f.State.Tick();
        if(shoots && !packetFirst)f.SourceBirth(weapon);
        Assert.True(f.ToolClock.LocalClocksKnown);
        f.Control(7,false);
        for(int i=0;i<80 && (f.ToolClock.Clocks.Selected.Animation!=0 || f.ToolClock.Clocks.Selected.ItemTime!=0);i++)f.State.Tick();
        Assert.Equal(0,f.ToolClock.Clocks.Selected.Animation);Assert.Equal(0,f.ToolClock.Clocks.Selected.ItemTime);
        f.Select(1);Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));f.Kill();
        Assert.False(f.Tiles.Get(50,52).IsActive);Assert.Equal(1,f.Items.ActiveCount);
    }


    public void Full_inventory_does_not_replace_local_clock_or_spend_unrelated_stacks(short weapon,short ammo)
    {
        using var f=new Fixture(true);f.Equip(7,weapon,1);f.Equip(1,3509,1);if(ammo!=0)f.Equip(54,ammo,3);
        f.Select(7);f.SetMana();f.Control(7,false);f.State.Tick();f.Control(7,true);f.State.Tick();
        if(weapon!=4)f.SourceBirth(weapon);
        Assert.True(f.ToolClock.LocalClocksKnown);Assert.Equal((short)9999,f.Item(20).Stack);
        f.Control(7,false);for(int i=0;i<90 && f.ToolClock.Clocks.Selected.Animation+f.ToolClock.Clocks.Selected.ItemTime>0;i++)f.State.Tick();
        f.Select(1);Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));f.Kill();
        Assert.False(f.Tiles.Get(50,52).IsActive);Assert.Equal((short)9999,f.Item(20).Stack);
    }

    public void Last_arrow_keeps_paid_clock_when_dedicated_ammo_query_is_now_empty(bool packetFirst)
    {
        using var f=new Fixture(false);f.Equip(7,39,1);f.Equip(1,3509,1);f.Equip(54,40,1);f.Select(7);
        f.Control(7,false);f.State.Tick();f.Control(7,true);
        if(packetFirst)f.SourceBirth(39);f.State.Tick();if(!packetFirst)f.SourceBirth(39);
        Assert.True(f.Item(54).IsEmpty);Assert.True(f.ToolClock.LocalClocksKnown);
        Assert.Equal(30,f.ToolClock.Clocks.Selected.ItemTime);Assert.Equal(29,f.ToolClock.Clocks.Selected.Animation);
        f.Control(7,false);for(int i=0;i<35;i++)f.State.Tick();f.Select(1);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));f.Kill();Assert.False(f.Tiles.Get(50,52).IsActive);
    }

    public void No_ammo_fails_source_start_but_preserves_idle_clock_for_a_tool(short weapon)
    {
        using var f=new Fixture(false);f.Equip(7,weapon,1);f.Equip(1,3509,1);f.Select(7);
        f.Control(7,false);f.State.Tick();f.Control(7,true);f.State.Tick();
        Assert.True(f.ToolClock.LocalClocksKnown);Assert.Equal(0,f.ToolClock.Clocks.Selected.Animation);
        Assert.False(f.ToolClock.LastUseSuccess);Assert.Null(f.ToolClock.WeaponShoot);
        f.Select(1);Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));f.Kill();Assert.False(f.Tiles.Get(50,52).IsActive);
    }


    public void Source_magic_shortage_pays_once_and_retains_the_slow_local_period(short mana,bool packetFirst)
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Equip(1,3509,1);f.Select(7);f.SetMana(mana);
        f.Control(7,false);f.State.Tick();f.Events.Mana.Clear();f.Control(7,true);
        if(packetFirst)f.SourceBirth(113);f.State.Tick();if(!packetFirst)f.SourceBirth(113);
        Assert.True(f.ToolClock.LocalClocksKnown);Assert.Equal(mana is 0 or 1?55:22,f.ToolClock.Clocks.Selected.ItemTime);
        Assert.True(f.State.TryCapturePlayerSnapshot(f.Player,out var player));Assert.Equal(Math.Max(0,mana-14),player.Mana);
        Assert.All(f.Events.Mana, r=>Assert.Equal(Math.Max(0,mana-14),r.Mana));
        f.Control(7,false);for(int i=0;i<65;i++)f.State.Tick();f.Select(1);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));f.Kill();Assert.False(f.Tiles.Get(50,52).IsActive);
    }

    public void Paid_clock_and_ammo_exist_before_reentrant_throwing_publication(bool packetFirst)
    {
        using var f=new Fixture(false);f.Equip(7,39,1);f.Equip(1,3509,1);f.Equip(54,40,1);f.Select(7);
        f.Control(7,false);f.State.Tick();f.Control(7,true);int calls=0;
        f.Events.OnEquipment=()=>
        {
            calls++;Assert.True(f.ToolClock.LocalClocksKnown);Assert.True(f.Item(54).IsEmpty);
            var clock=f.ToolClock.Clocks;
            // Packet-first already has all birth keys; phase-first may first
            // deliver the paid report from this observer. Neither can re-debit.
            f.SourceBirth(39);Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.Equal(clock,f.ToolClock.Clocks);Assert.True(f.ToolClock.WeaponShootHistory!.Uses[0].Born);
            throw new InvalidOperationException("owned clock observer");
        };
        var error=Assert.Throws<InvalidOperationException>(()=>
        {if(packetFirst)f.SourceBirth(39);f.State.Tick();if(!packetFirst)f.SourceBirth(39);});
        Assert.Equal("owned clock observer",error.Message);Assert.Equal(1,calls);
        Assert.True(f.Item(54).IsEmpty);Assert.True(f.ToolClock.LocalClocksKnown);f.Events.OnEquipment=null;
        Assert.Equal(30,f.ToolClock.Clocks.Selected.ItemTime);
        var unchanged=f.ToolClock.Clocks;f.SourceBirth(39);Assert.Equal(unchanged,f.ToolClock.Clocks);Assert.True(f.Item(54).IsEmpty);
        f.Control(7,false);for(int i=0;i<35;i++)f.State.Tick();f.Select(1);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));f.Kill();Assert.False(f.Tiles.Get(50,52).IsActive);
    }

    public void Exact_preparation_refuses_inventory_mutation_and_unrelated_change_does_not_forge_a_new_epoch()
    {
        // This remains the original strict neutral component/preparation contract.
        using var f=new Fixture(false,itemUseCaller:RuntimeItemUseCaller1458.Unknown);f.Equip(7,39,1);f.Equip(54,40,3);f.Select(7);f.Control(7,false);f.State.Tick();
        f.Control(7,true);f.State.Tick();var epoch=f.ToolClock.UseEpoch;
        Assert.True(f.Players.TryCaptureProjectileUse(f.Connection,out var capture));Assert.NotNull(capture);
        var debit=new RuntimePlayerInventoryMutation(54,f.Item(54) with{Stack=2});
        Assert.True(f.Players.CanCommitProjectileUse(capture,debit,0));
        f.Equip(20,3,1);
        Assert.False(f.Players.CanCommitProjectileUse(capture,debit,0));Assert.False(f.Players.TryCommitProjectileUseUnpublished(capture,debit,0));
        Assert.Equal((short)3,f.Item(54).Stack);Assert.False(f.ToolClock.LocalClocksKnown);Assert.Equal(epoch,f.ToolClock.UseEpoch);
        // A new exact capture may consume the same still-current source offer:
        // Stone in another lane does not change this weapon's clock or ammo.
        f.SourceBirth(39);Assert.True(f.ToolClock.LocalClocksKnown);Assert.Equal(epoch,f.ToolClock.UseEpoch);Assert.Equal((short)2,f.Item(54).Stack);
    }

    public void Uncompleted_source_offer_cannot_resurrect_local_clock_after_the_next_owned_frame()
    {
        // This remains the original strict neutral component/preparation contract.
        using var f=new Fixture(false,itemUseCaller:RuntimeItemUseCaller1458.Unknown);f.Equip(7,39,1);f.Equip(54,40,3);f.Select(7);f.Control(7,false);f.State.Tick();
        f.Control(7,true);f.State.Tick();Assert.NotNull(f.ToolClock.WeaponShoot);Assert.False(f.ToolClock.LocalClocksKnown);
        f.State.Tick();Assert.Null(f.ToolClock.WeaponShoot);Assert.False(f.ToolClock.LocalClocksKnown);
        f.SourceBirth(39);Assert.False(f.ToolClock.LocalClocksKnown);
    }

    public void Fresh_NoItems_cannot_commit_a_new_source_shoot_or_clock(short weapon,short ammo)
    {
        using var f=new Fixture(false);f.Equip(7,weapon,1);if(ammo!=0)f.Equip(54,ammo,3);f.Select(7);f.SetMana();
        f.Control(7,false);f.State.Tick();f.Buff23();f.Control(7,true);f.State.Tick();
        Assert.True(f.ToolClock.LocalClocksKnown);Assert.Null(f.ToolClock.WeaponShoot);Assert.Equal(0,f.ToolClock.Clocks.Selected.Animation);
        long births=f.Projectiles.PromotedClientProjectileSpawns;var clock=f.ToolClock.Clocks;var beforeAmmo=f.Item(54);
        f.SourceBirth(weapon);Assert.Equal(births,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(beforeAmmo,f.Item(54));Assert.Equal(clock,f.ToolClock.Clocks);
    }

    public void NoItems_added_after_the_owned_frame_does_not_reject_its_source_owner_tail(short weapon,short ammo)
    {
        using var f=new Fixture(false);f.Equip(7,weapon,1);if(ammo!=0)f.Equip(54,ammo,3);f.Select(7);f.SetMana();
        f.Control(7,false);f.State.Tick();f.Control(7,true);f.State.Tick();
        if(ammo==0)Assert.NotNull(f.ToolClock.WeaponShoot);
        else
        {
            Assert.Single(f.ToolClock.WeaponShootHistory!.Uses);
            Assert.Equal(2,f.Item(54).Stack);Assert.False(f.ToolClock.WeaponShootHistory.Uses[0].Born);
        }
        f.Buff23();f.SourceBirth(weapon);Assert.True(f.ToolClock.LocalClocksKnown);Assert.Null(f.ToolClock.WeaponShoot);
        if(ammo!=0){Assert.True(f.ToolClock.WeaponShootHistory!.Uses[0].Born);Assert.Equal(2,f.Item(54).Stack);}
    }

    public void The_last_available_member_revision_adopts_mana_and_clock_once()
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Select(7);f.SetMana();f.Control(7,false);f.State.Tick();
        f.Control(7,true);f.State.Tick();Assert.NotNull(f.ToolClock.WeaponShoot);
        f.Member.Revision=ulong.MaxValue-1;f.SourceBirth(113);
        Assert.Equal(ulong.MaxValue,f.Member.Revision);Assert.True(f.ToolClock.LocalClocksKnown);Assert.Null(f.ToolClock.WeaponShoot);
        Assert.Equal(22,f.ToolClock.Clocks.Selected.ItemTime);
    }

    public void An_old_generation_capture_cannot_write_the_replacement_local_clock_or_inventory()
    {
        // Exercise the strict receipt commit boundary. The local phase-paid
        // owner cannot be charged again through this legacy leaf contract.
        using var f=new Fixture(false,itemUseCaller:RuntimeItemUseCaller1458.Unknown);f.Equip(7,39,1);f.Equip(54,40,3);f.Select(7);f.Control(7,false);f.State.Tick();f.Control(7,true);f.State.Tick();
        var players=f.Players;Assert.True(players.TryCaptureProjectileUse(f.Connection,out var capture));Assert.NotNull(capture);
        var debit=new RuntimePlayerInventoryMutation(54,f.Item(54) with{Stack=2});Assert.True(players.CanCommitProjectileUse(capture,debit,0));
        using var replacement=f.Reconnect();Assert.True(players.TryGet(replacement.Slot,out var current));var phase=current.ItemPhase;
        Assert.False(players.CanCommitProjectileUse(capture,debit,0));Assert.False(players.TryCommitProjectileUseUnpublished(capture,debit,0));
        Assert.Equal(phase,current.ItemPhase);Assert.NotEqual(capture.Connection.Player,current.Connection.Player);
    }

    public void Early_paid_bow_cannot_be_shortened_by_a_new_tool_selection_and_success_claim()
    {
        using var f=new Fixture(false);f.Equip(7,39,1);f.Equip(1,2776,1);f.Equip(54,40,3);f.Select(7);
        f.Control(7,false);f.State.Tick();f.Control(7,true);f.SourceBirth(39);f.State.Tick();
        Assert.True(f.ToolClock.LocalClocksKnown);Assert.Equal(29,f.ToolClock.Clocks.Selected.Animation);Assert.Equal(30,f.ToolClock.Clocks.Selected.ItemTime);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));
        f.Control(1,true);f.RawKill();f.State.Tick();
        Assert.True(f.Tiles.Get(50,52).IsActive);Assert.Equal(0,f.Items.ActiveCount);
        Assert.Equal(28,f.ToolClock.Clocks.Selected.Animation);Assert.Equal(29,f.ToolClock.Clocks.Selected.ItemTime);
        Assert.Equal((byte)7,f.ToolClock.SourceSlot);Assert.Equal((byte?)1,f.ToolClock.DeferredSelectionSlot);
    }

    public void Idle_owned_selection_can_accept_packet_first_without_a_fixture_release_tick(short weapon,short ammo)
    {
        using var f=new Fixture(false);f.Equip(7,weapon,1);if(ammo!=0)f.Equip(54,ammo,3);f.SetMana();
        f.Control(7,true);f.SourceBirth(weapon);
        bool deferred=ammo!=0 || weapon==113 && f.ToolClock.WeaponShoot?.PendingReport is not null;
        if(deferred)f.State.Tick();
        Assert.True(f.ToolClock.LocalClocksKnown);Assert.True(f.ToolClock.Clocks.Selected.Animation>0);
        var before=f.ToolClock.Clocks;if(!deferred)f.State.Tick();Assert.Equal(before.Selected.Animation,f.ToolClock.Clocks.Selected.Animation);
        Assert.Equal(before.Selected.ItemTime,f.ToolClock.Clocks.Selected.ItemTime);Assert.Null(f.ToolClock.WeaponShoot);
    }

    public void Local_mana_feedback_survives_paid_magic_to_released_tool(bool packetFirst)
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Equip(1,3509,1);
        f.Select(7);f.SetMana();f.Control(7,false);f.State.Tick();f.Control(7,true);
        long births=f.Projectiles.PromotedClientProjectileSpawns;
        if(packetFirst)f.SourceBirth(113);f.State.Tick();if(!packetFirst)f.SourceBirth(113);
        Assert.Equal(births+1,f.Projectiles.PromotedClientProjectileSpawns);
        Assert.Equal(186,f.Member.Mana);f.Control(7,false);
        for(int i=0;i<80 && (f.ToolClock.Clocks.Selected.Animation!=0 || f.ToolClock.Clocks.Selected.ItemTime!=0);i++)f.State.Tick();
        Assert.Equal(0,f.ToolClock.Clocks.Selected.Animation);Assert.True(f.Member.Mana<200);
        // Actual normalized13 changes the selected family after the paid local
        // use. There is no mana reset, cloned clock, or fabricated input credit.
        f.Control(1,false);bool reached=false;
        for(int i=0;i<160 && !reached;i++)
        {
            var before=f.Member.ItemPhase!.Value;var local=before.ToolUse!;
            var facts=new TerraRuntime.Gameplay.Players.PlayerManaRegenerationFacts1458(
                f.Member.VelocityX,f.Member.VelocityY,false,false,false,0,0f,0);
            Assert.True(TerraRuntime.Gameplay.Players.VanillaRemoteManaRegeneration1458.TryStep(
                before.Mana,in facts,out var after,out _));
            reached=before.Mana.Mana<after.Maximum && after.Mana>=after.Maximum;
            var expected=f.PlayerRandom.Clone();
            Assert.True(TerraRuntime.Gameplay.Players.VanillaSelectedConsumable1458.TryStepHeat(
                before.ManaHeat,expected.NextDouble,expected.Next,out _));
            if(reached)
            {
                Assert.Equal(0,local.Clocks.Selected.Animation);Assert.Equal(0,local.Clocks.ReuseDelay);
                // Original Player.UpdateManaRegen19678: local identity remains
                // the caller after selection; five suppressed-dust scale draws.
                for(int d=0;d<5;d++)expected.Next(20,26);
            }
            int roll=expected.Next(3);int crit=local.Clocks.Selected.RevolverCritBonus;
            f.State.Tick();Assert.NotNull(f.Member.ItemPhase);
            Assert.True(f.PlayerRandom.HasSameState(expected));
            Assert.Equal(crit-(roll==0?2:0),f.ToolClock.Clocks.Selected.RevolverCritBonus);
            Assert.Equal(after.Mana,f.Member.Mana);
        }
        Assert.True(reached);Assert.Equal(200,f.Member.Mana);
        var next=f.PlayerRandom.Clone();next.Next(3);f.State.Tick();Assert.True(f.PlayerRandom.HasSameState(next));
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));
        f.Kill();Assert.False(f.Tiles.Get(50,52).IsActive);Assert.Equal(1,f.Items.ActiveCount);
    }

    public void Full_mana_feedback_owns_five_source_draws_before_the_common_crit(bool packetFirst)
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Select(7);f.SetMana(199);
        f.Control(7,packetFirst);var random=f.PlayerRandom.Clone();
        Assert.True(TerraRuntime.Gameplay.Players.VanillaSelectedConsumable1458.TryStepHeat(f.Member.ItemPhase!.Value.ManaHeat,random.NextDouble,random.Next,out _));
        for(int i=0;i<5;i++)random.Next(20,26);random.Next(3);
        if(packetFirst)
        {
            long births=f.Projectiles.PromotedClientProjectileSpawns;
            f.SourceBirth(113);Assert.Equal(199,f.Member.Mana);Assert.Equal(births,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.NotNull(f.ToolClock.WeaponShoot!.PendingReport);
        }
        f.State.Tick();Assert.True(f.PlayerRandom.HasSameState(random));
        Assert.Equal(packetFirst?186:200,f.Member.Mana);
        if(packetFirst)
        {Assert.Null(f.Member.ItemPhase!.Value.MagicPayment!.Value.ManaStage);Assert.Null(f.ToolClock.WeaponShoot);Assert.Equal(4f,f.Member.ItemPhase.Value.Mana.Delay);}
    }

    public void Earlier_remote_slot_keeps_common_before_local_feedback_and_pending_birth(bool duplicate) =>
        RunEarlierRemote(duplicate,false);

    public void Input_FIFO_keeps_slot_order_for_remote_common_and_local_feedback(bool localFirst) =>
        RunEarlierRemote(true,localFirst);
    private void RunEarlierRemote(bool duplicate,bool localFirst)
    {
        using var f=new Fixture(false);var remote=f.AddLocalAfterExistingRemote();
        Assert.Equal(0,remote.Slot.Value);Assert.Equal(1,f.Player.Slot.Value);
        f.Equip(7,113,1);f.Select(7);f.Control(7,false);f.State.Tick();
        bool discriminates=false;
        for(int tick=0;tick<12;tick++)
        {
            var a=f.PlayerRandom.Clone();var b=a.Clone();int first=a.Next(3);
            for(int i=0;i<5;i++)b.Next(20,26);
            bool earlyDiff=(first==0)!=(b.Next(3)==0);
            var beforeFeedback=f.PlayerRandom.Clone();beforeFeedback.Next(3);int prematureLocal=beforeFeedback.Next(3);
            var afterFeedback=f.PlayerRandom.Clone();afterFeedback.Next(3);for(int i=0;i<5;i++)afterFeedback.Next(20,26);int sourceLocal=afterFeedback.Next(3);
            if(earlyDiff && (prematureLocal==0)!=(sourceLocal==0)){discriminates=true;break;}
            f.State.Tick();
        }
        Assert.True(discriminates);f.SetMana(199);f.Control(7,true);
        var before=f.PlayerRandom.Clone();var expected=before.Clone();
        expected.Next(3);for(int i=0;i<5;i++)expected.Next(20,26);int expectedLocalRoll=expected.Next(3);
        int localCritBefore=f.ToolClock.Clocks.Selected.RevolverCritBonus;
        // Equal total draw count can hide wrong Next(max) chronology in state;
        // compare the source-derived common roll's retained crit accumulator too.
        int remoteBonus=f.Players.TryGet(remote,out var remoteMember)?remoteMember.ItemPhase!.Value.Selected.RevolverCritBonus:throw new InvalidOperationException();
        var checkpoint=before.Clone();int remoteRoll=checkpoint.Next(3);
        long births=f.Projectiles.PromotedClientProjectileSpawns;
        if(!localFirst)f.ReleaseEarlier(remote);
        f.SourceBirth(113);if(duplicate)f.SourceBirth(113);
        if(localFirst)f.ReleaseEarlier(remote);
        Assert.True(f.PlayerRandom.HasSameState(before));Assert.Equal(199,f.Member.Mana);Assert.Equal(births,f.Projectiles.PromotedClientProjectileSpawns);
        f.State.Tick();Assert.Equal(births+1,f.Projectiles.PromotedClientProjectileSpawns);
        Assert.True(f.PlayerRandom.HasSameState(expected));Assert.Equal(186,f.Member.Mana);
        Assert.True(f.Players.TryGet(remote,out remoteMember));
        Assert.Equal(remoteBonus-(remoteRoll==0?2:0),remoteMember.ItemPhase!.Value.Selected.RevolverCritBonus);
        Assert.Equal(localCritBefore-(expectedLocalRoll==0?2:0),f.ToolClock.Clocks.Selected.RevolverCritBonus);
        Assert.Null(f.ToolClock.WeaponShoot);
    }

    public void Deferred_entropy_birth_keeps_input_currentness_and_consumes_before_observers(string mode)
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Select(7);f.SetMana(199);f.Control(7,true);
        long births=f.Projectiles.PromotedClientProjectileSpawns;f.SourceBirth(113);
        Assert.Equal(births,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(199,f.Member.Mana);
        if(mode=="stale")
        {
            f.Control(7,false);f.State.Tick();Assert.Equal(births,f.Projectiles.PromotedClientProjectileSpawns);
            Assert.InRange(f.Member.Mana,199,200);Assert.Null(f.ToolClock.WeaponShoot);return;
        }
        if(mode=="move")
        {
            // Physics advances after the captured launch pose; no incoming13
            // mutation is used to fake a refreshed source preimage.
            f.Member.VelocityX=1f;f.Member.MarkRemotePhaseSnapshot();
            // A private pre-phase mutation is stale, even if only movement.
            f.State.Tick();Assert.Equal(births,f.Projectiles.PromotedClientProjectileSpawns);return;
        }
        f.Events.OnBirth=()=>
        {
            Assert.Equal(186,f.Member.Mana);Assert.Equal(4f,f.Member.ItemPhase!.Value.Mana.Delay);
            Assert.Null(f.ToolClock.WeaponShoot);throw new InvalidOperationException("deferred mana observer");
        };
        var failure=Assert.Throws<InvalidOperationException>(()=>f.State.Tick());Assert.Equal("deferred mana observer",failure.Message);
        Assert.Equal(births+1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Null(f.ToolClock.WeaponShoot);
        f.Events.OnBirth=null;f.State.Tick();Assert.Equal(births+1,f.Projectiles.PromotedClientProjectileSpawns);
    }

    public void Deferred_birth_keeps_original_launch_pose_after_owned_player_motion()
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Select(7);f.SetMana();f.Control(7,true);
        // Explicit finite source preimage, not a post-report refresh. The next
        // owned physics frame moves beyond the ordinary192px spawn envelope.
        f.Member.VelocityX=300f;f.Member.MarkRemotePhaseSnapshot();float beforeX=f.Member.PositionX;
        long births=f.Projectiles.PromotedClientProjectileSpawns;f.SourceBirth(113);
        Assert.NotNull(f.ToolClock.WeaponShoot?.PendingReport);
        f.State.Tick();Assert.True(f.Member.PositionX-beforeX>192f);
        Assert.Equal(births+1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(186,f.Member.Mana);
        Assert.True(f.ToolClock.LocalClocksKnown);Assert.Null(f.ToolClock.WeaponShoot);
    }

    public void Changed_mana_after_payment_never_restores_the_earlier_afterimage_or_clock()
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Select(7);f.Control(7,false);f.State.Tick();f.SetMana();f.Control(7,true);f.SourceBirth(113);
        Assert.Equal(186,f.Member.Mana);var stage=f.Member.ItemPhase!.Value.MagicPayment!.Value.ManaStage;Assert.NotNull(stage);
        Assert.Equal(4f,f.Member.ItemPhase!.Value.Mana.Delay);
        // Packet42 is an observation, never a second resource writer.
        f.SetMana(70);Assert.Equal(186,f.Member.Mana);
        // Explicit trusted-private mutation exercises a lost owned afterimage;
        // no raw report gains this authority in the runtime.
        f.Member.Mana=70;f.Member.ItemPhase=f.Member.ItemPhase.Value with {Mana=f.Member.ItemPhase.Value.Mana with {Mana=70}};
        f.Member.MarkRemotePhaseSnapshot();f.State.Tick();Assert.InRange(f.Member.Mana,70,71);Assert.NotEqual(186,f.Member.Mana);
        Assert.Null(f.Member.ItemPhase!.Value.MagicPayment!.Value.ManaStage);Assert.False(f.ToolClock.LocalClocksKnown);
    }

    public void Transfer_preserves_adopted_times_but_retires_old_world_source_receipts(bool pending)
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Select(7);f.SetMana();f.Control(7,true);
        if(!pending)f.State.Tick();f.SourceBirth(113);
        var clocks=f.ToolClock.Clocks;var capture=f.Detach();Assert.NotNull(capture.ItemPhase);
        f.Attach(capture);Assert.Equal(clocks.Selected,f.ToolClock.Clocks.Selected);
        Assert.Null(f.ToolClock.WeaponShoot);Assert.Null(f.Member.ItemPhase!.Value.MagicPayment?.ManaStage);
        Assert.True(f.ToolClock.RequiresFreshUse);
    }

    public void ManaFlower_checks_shortage_after_owned_regeneration_and_never_reuses_the_potion(short mana,bool packetFirst)
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Equip(62,555,1);f.Equip(2,110,3);f.Fund(2,3);
        f.Select(7);f.SetMana(mana);f.Control(7,false);f.State.Tick();Assert.True(f.Member.ItemPhase!.Value.DerivedCombat!.Value.ManaFlower);
        f.Control(7,true);if(packetFirst)f.SourceBirth(113);f.State.Tick();if(!packetFirst)f.SourceBirth(113);
        Assert.True(f.ToolClock.LocalClocksKnown);Assert.Equal(22,f.ToolClock.Clocks.Selected.ItemTime);
        Assert.Equal(mana==11?3:2,f.Item(2).Stack);Assert.Equal(mana==11?3:2,f.Funded(2));
        Assert.Equal(mana==11?0:39,f.Member.Mana);
        var after=f.Member.ItemPhase!.Value.Mana;f.SourceBirth(113);Assert.Equal(after,f.Member.ItemPhase!.Value.Mana);
        Assert.Equal(mana==11?3:2,f.Item(2).Stack);
    }

    public void Owned_food_melee_speed_remains_in_the_weapon_clock_until_the_tool_transition()
    {
        using var f=new Fixture(false);f.Equip(0,4022,1);f.Equip(7,4,1);f.Equip(1,3509,1);f.Fund(0,1);
        f.Select(0);f.Control(0,false);f.State.Tick();f.Control(0,true);f.State.Tick();f.State.Tick();
        Assert.True(f.Member.ItemPhase!.Value.DerivedCombat!.Value.WellFed);
        f.Control(0,false);for(int i=0;i<30;i++)f.State.Tick();
        Assert.Equal(0,f.ToolClock.Clocks.Selected.Animation);Assert.Equal(0,f.ToolClock.Clocks.Selected.ItemTime);
        f.Select(7);f.Control(7,true);f.State.Tick();
        Assert.True(f.ToolClock.LocalClocksKnown);// Original capped consumer item1 has the same20-frame melee animation
        // as independently captured item4: source food207 yields18 frames.
        Assert.Equal(18,f.ToolClock.Clocks.Selected.AnimationMax);Assert.Equal(17,f.ToolClock.Clocks.Selected.Animation);
        f.Control(7,false);for(int i=0;i<25;i++)f.State.Tick();f.Select(1);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));f.Kill();Assert.False(f.Tiles.Get(50,52).IsActive);
    }
    public void Late_magic_control_and_exact42_join_one_source_shoot(short weapon,bool acknowledgeFirst)
    {
        using var f=new Fixture(false);f.Equip(7,weapon,1);f.Select(7);f.SetMana();f.Control(7,false);f.State.Tick();
        int cost=weapon==113?14:21;short after=(short)(200-cost);long births=f.Projectiles.PromotedClientProjectileSpawns;
        var before=f.PlayerRandom.Clone();f.SourceBirth(weapon);Assert.NotNull(f.ToolClock.WeaponShoot?.PendingReport);
        Assert.Equal(200,f.Member.Mana);Assert.Equal(births,f.Projectiles.PromotedClientProjectileSpawns);
        if(acknowledgeFirst)f.ManaReport(after);
        f.Control(7,true);if(!acknowledgeFirst)f.ManaReport(after);f.ManaReport(after);
        Assert.Equal(200,f.Member.Mana);Assert.True(f.PlayerRandom.HasSameState(before));
        f.State.Tick();Assert.Equal(births+1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(after,f.Member.Mana);
        Assert.Equal(4f,f.Member.ItemPhase!.Value.Mana.Delay);Assert.True(f.ToolClock.LocalClocksKnown);Assert.Null(f.ToolClock.WeaponShoot);
        var state=f.Member.CaptureSnapshot();var clock=f.ToolClock;var random=f.PlayerRandom.Clone();f.ManaReport(after);
        Assert.Equal(state,f.Member.CaptureSnapshot());Assert.Equal(clock,f.ToolClock);Assert.True(f.PlayerRandom.HasSameState(random));
    }
    public void Late_magic_without_control_or_with_changed_selector_never_borrows_a_future_epoch(bool changed)
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Select(7);f.SetMana();f.Control(7,false);f.State.Tick();
        long births=f.Projectiles.PromotedClientProjectileSpawns;f.SourceBirth(113);f.ManaReport(186);
        Assert.Equal(200,f.Member.Mana);if(changed)f.Control(1,true);f.State.Tick();
        Assert.Equal(births,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(200,f.Member.Mana);
        Assert.Null(f.ToolClock.WeaponShoot);f.Control(7,true);f.State.Tick();
        Assert.Equal(births,f.Projectiles.PromotedClientProjectileSpawns);
    }
    public void Source_qualified_released_ranged_birth_does_not_invent_local_clocks()
    {
        using var f=new Fixture(false);f.Equip(7,39,1);f.Equip(54,40,3);f.Select(7);f.Control(7,false);f.State.Tick();
        f.SourceBirth(39);Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(2,f.Item(54).Stack);
        Assert.False(f.ToolClock.LocalClocksKnown);Assert.Null(f.ToolClock.WeaponShoot);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));f.Control(1,true);f.RawKill();f.State.Tick();
        Assert.True(f.Tiles.Get(50,52).IsActive);Assert.Equal(2,f.Item(54).Stack);
    }
    public void Late_magic_refill_and42_use_one_owned_potion_and_one_payment(short mana,bool funded,bool acknowledgeBeforeControl)
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Equip(62,555,1);f.Equip(2,110,3);if(funded)f.Fund(2,1);
        f.Select(7);f.SetMana(mana);f.Control(7,false);f.State.Tick();Assert.True(f.Member.ItemPhase!.Value.DerivedCombat!.Value.ManaFlower);
        short before=f.Member.Mana;short paid=mana==0&&funded?(short)39:(short)0;long births=f.Projectiles.PromotedClientProjectileSpawns;
        f.SourceBirth(113);Assert.NotNull(f.ToolClock.WeaponShoot?.PendingReport);
        if(acknowledgeBeforeControl)f.ManaReport(paid);f.Control(7,true);if(!acknowledgeBeforeControl)f.ManaReport(paid);
        Assert.Equal(before,f.Member.Mana);Assert.Equal(3,f.Item(2).Stack);Assert.Equal(funded?1:0,f.Funded(2));
        f.State.Tick();Assert.Equal(births+1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(paid,f.Member.Mana);
        bool refilled=mana==0&&funded;
        Assert.Equal(refilled?2:3,f.Item(2).Stack);Assert.Equal(funded&&!refilled?1:0,f.Funded(2));
        Assert.Equal(mana==0&&!funded?55:22,f.ToolClock.Clocks.Selected.ItemTime);
        Assert.Null(f.ToolClock.WeaponShoot);var clocks=f.ToolClock.Clocks;var resource=f.Member.ItemPhase.Value.Mana;var random=f.PlayerRandom.Clone();
        f.ManaReport(paid);Assert.Equal(resource,f.Member.ItemPhase.Value.Mana);Assert.Equal(clocks,f.ToolClock.Clocks);
        Assert.True(f.PlayerRandom.HasSameState(random));Assert.Equal(refilled?2:3,f.Item(2).Stack);
    }
    public void Wrong_pending42_cannot_grant_mana_or_replace_the_exact_source_epoch(bool wrongMaximum)
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Select(7);f.SetMana();f.Control(7,false);f.State.Tick();
        f.SourceBirth(113);f.Control(7,true);f.ManaReport(399,wrongMaximum?(short)400:(short)200);
        Assert.Equal(200,f.Member.Mana);f.State.Tick();Assert.Equal(186,f.Member.Mana);
        Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(1,f.State.RejectedPlayerManaUpdates);
        Assert.True(f.ToolClock.LocalClocksKnown);Assert.Null(f.ToolClock.WeaponShoot);
    }
    public void Late_magic_input_ABA_cannot_requalify_the_retired_report()
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Select(7);f.SetMana();f.Control(7,false);f.State.Tick();
        f.SourceBirth(113);f.Control(7,true);f.Control(7,false);f.Control(7,true);f.ManaReport(186);
        f.State.Tick();Assert.Equal(0,f.Projectiles.PromotedClientProjectileSpawns);
        Assert.False(f.ToolClock.LocalClocksKnown);
    }
    public void Released_magic_casts_pay_again_from_fresh_clocks_without_reusing_the_first_receipt(bool packetFirst)
    {
        using var f=new Fixture(false);f.Equip(7,113,1);f.Select(7);f.SetMana();f.Control(7,false);f.State.Tick();
        f.Control(7,true);if(packetFirst)f.SourceBirth(113);f.State.Tick();if(!packetFirst)f.SourceBirth(113);
        Assert.False(f.Member.ItemPhase!.Value.MagicPayment!.Value.AwaitingPhase);Assert.Equal(1UL,f.Member.ItemPhase.Value.MagicPayment.Value.Attempt);
        f.Control(7,false);for(int i=0;i<30;i++)f.State.Tick();
        Assert.True(f.ToolClock.LocalClocksKnown);Assert.Equal(0,f.ToolClock.Clocks.Selected.ItemTime);Assert.Equal(0,f.ToolClock.Clocks.Selected.Animation);
        var facts=new TerraRuntime.Gameplay.Players.PlayerManaRegenerationFacts1458(f.Member.VelocityX,f.Member.VelocityY,false,false,false,0,0,0);
        Assert.True(TerraRuntime.Gameplay.Players.VanillaRemoteManaRegeneration1458.TryStep(f.Member.ItemPhase.Value.Mana,in facts,out var next,out _));
        int expected=Math.Max(0,next.Mana-14);f.Control(7,true);if(packetFirst)f.SourceBirth(113,903);
        f.State.Tick();if(!packetFirst)f.SourceBirth(113,903);
        Assert.Equal(2,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(expected,f.Member.Mana);
        Assert.Equal(2UL,f.Member.ItemPhase!.Value.MagicPayment!.Value.Attempt);Assert.False(f.Member.ItemPhase.Value.MagicPayment.Value.AwaitingPhase);
        var after=f.Member.ItemPhase.Value.Mana;f.SourceBirth(113,903);Assert.Equal(after,f.Member.ItemPhase.Value.Mana);
    }
    public void Source_contact_blocks_fresh_ranged_clock_but_preserves_owned_shoot_tail(short weapon,short buff,bool paid,bool finalTick=false)
    {
        using var f=new Fixture(false);f.Equip(7,weapon,1);f.Equip(54,weapon==39?(short)40:(short)97,3);
        f.Select(7);f.Control(7,false);f.State.Tick();f.Control(7,true);if(paid)f.State.Tick();
        f.ContactBlock(buff,finalTick);var mana=f.Member.Mana;short ammo=f.Item(54).Stack;var before=f.ToolClock;var random=f.PlayerRandom.Clone();
        f.SourceBirth(weapon);Assert.Equal(paid?1:0,f.Projectiles.PromotedClientProjectileSpawns);
        Assert.Equal(ammo,f.Item(54).Stack);Assert.Equal(mana,f.Member.Mana);Assert.True(f.PlayerRandom.HasSameState(random));
        if(paid)Assert.True(f.ToolClock.LocalClocksKnown);else Assert.Equal(before,f.ToolClock);
    }
    public void Seven_gem_entropy_births_keep_all_source_keys_before_tool_continuation(string edition,short staff,bool earlierRemote)
    {
        using var f=new Fixture(false,12345);if(earlierRemote)f.AddLocalAfterExistingRemote();
        f.Equip(7,staff,1);f.Equip(1,3509,1);f.Select(7);f.SetMana();f.Control(7,false);f.State.Tick();f.SetMana(199);f.Control(7,true);
        var source=Fixture.GemSource(edition,staff);var before=f.PlayerRandom.Clone();var expected=before.Clone();
        if(earlierRemote)expected.Next(3);for(int i=0;i<5;i++)expected.Next(20,26);int crit=expected.Next(3);
        int beforeCrit=f.ToolClock.Clocks.Selected.RevolverCritBonus;long births=f.Projectiles.PromotedClientProjectileSpawns;
        f.SourceGemBirths(edition,staff);Assert.Equal(births,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(199,f.Member.Mana);
        Assert.NotNull(f.ToolClock.WeaponShoot?.PendingReport);Assert.True(f.PlayerRandom.HasSameState(before));
        if(source.Packets.Length==2)Assert.NotNull(f.ToolClock.WeaponShoot!.PendingSecondReport);
        f.State.Tick();
        Assert.Equal(births+source.Packets.Length,f.Projectiles.PromotedClientProjectileSpawns);
        Assert.Equal(200-source.Cost,f.Member.Mana);Assert.True(f.PlayerRandom.HasSameState(expected));Assert.Null(f.ToolClock.WeaponShoot);
        Assert.True(f.ToolClock.LocalClocksKnown);Assert.Equal(source.Clocks with {RevolverCritBonus=beforeCrit-(crit==0?2:0)},f.ToolClock.Clocks.Selected);
        Assert.Equal(source.Clocks.ItemTime,f.ToolClock.Clocks.ToolTime);
        f.Control(7,false);for(int i=0;i<50;i++)f.State.Tick();f.Select(1);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles,50,52));f.Kill();Assert.False(f.Tiles.Get(50,52).IsActive);
        Assert.Equal(births+source.Packets.Length,f.Projectiles.PromotedClientProjectileSpawns);
    }
    internal sealed class WeaponClockEvents : IRuntimePlayerEventSink,IProjectileStateCommitSink
    {
        internal Action? OnEquipment;

        internal Action? OnBirth;
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind,in ProjectileSnapshot snapshot)
        {if(kind==ProjectileStateCommitKind.Spawn)OnBirth?.Invoke();}
        internal readonly List<PlayerManaCommitRequest> Mana=[];
        internal readonly List<PlayerHealthCommitRequest> Health=[];
        public void PlayerAppearanceUpdated(ConnectionHandle c,in PlayerAppearanceCommitRequest r){}
        public void PlayerEquipmentUpdated(ConnectionHandle c,in PlayerEquipmentCommitRequest r)=>OnEquipment?.Invoke();
        public void PlayerHealthUpdated(ConnectionHandle c,in PlayerHealthCommitRequest r){Health.Add(r);}
        public void PlayerManaUpdated(ConnectionHandle c,in PlayerManaCommitRequest r){Mana.Add(r);}
        public void PlayerItemAnimationUpdated(ConnectionHandle c,float r,short a){}
        public void PlayerBuffTypesUpdated(ConnectionHandle c,in PlayerBuffTypesCommitRequest r){}
        public void PlayerSpawned(ConnectionHandle c,in PlayerSpawnCommitRequest r){}
        public void PlayerMoved(ConnectionHandle c,in PlayerMovementCommitRequest r){}
        public void PlayerDisconnected(ConnectionHandle c){}
    }

    internal sealed class Fixture : IDisposable
    {
        private PlayerBootstrapFrameSink bootstrap;
        private PlayerBootstrapFrameSink? earlierBootstrap;
        private ProjectileLifecycleFrameSink tileSink;
        private TileManipulationFrameSink ammoTileSink;
        private PlayerVitalsFrameSink vitalsSink=null!;
        private readonly TerrariaConnectionOutboundQueue outbound = new(
            new OutboundQueueOptions(maxFrames: 1024, maxQueuedBytes: 131072, maxFrameBytes: 2048));
        private sealed class ProjectileFanout(IProjectileStateCommitSink first,IProjectileStateCommitSink second):IProjectileStateCommitSink
        {
            public void ProjectileStateCommitted(ProjectileStateCommitKind kind,in ProjectileSnapshot snapshot)
            {
                Exception? failure=null;try{first.ProjectileStateCommitted(kind,in snapshot);}catch(Exception e){failure=e;}
                try{second.ProjectileStateCommitted(kind,in snapshot);}catch(Exception e){failure??=e;}
                if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        private readonly PlayerSlotPool pool=new(2);
        public WorldTileStore Tiles { get; } = new(new WorldDimensions(200, 150));
        public RuntimeWorldItemReplicationRegistry Replication { get; } = new();
        private readonly RuntimeProjectileReplicationRegistry projectileReplication = new();
        public WeaponClockEvents Events { get; }=new();
        public RuntimeWorldItemStore Items { get; }
        public ServerRuntimeState State { get; }
        public WorldItemFrameSink ItemSink { get; private set; }
        public PlayerHandle Player => bootstrap.AssignedPlayerHandle!.Value;

        private readonly VanillaUnifiedRandom1458 sourceMain;
        private static VanillaUnifiedRandom1458 SourceLaunchCheckpoint(int seed=0)
        {
            // Source first152 is after common ItemCheck Next(3), before owner
            // Shoot/PickAmmo. The separate local phase stream owns that common
            // sample; this explicit world/launch adapter starts at its checkpoint.
            var random=new VanillaUnifiedRandom1458(seed);random.Next(3);return random;
        }
        public Fixture(bool fullInventory,int launchSeed=0,bool sourceLaunchCheckpoint=true, IProjectileStateStepper? sourceStepper=null, RuntimeItemUseCaller1458 itemUseCaller=RuntimeItemUseCaller1458.LocalOwnerProjection,bool selectInitially=true)
        {
            sourceMain=sourceLaunchCheckpoint?SourceLaunchCheckpoint(launchSeed):new VanillaUnifiedRandom1458(launchSeed);
            Items = new(Replication);
            State = new(projectiles:new RuntimeProjectileStore(commitSink:new ProjectileFanout(projectileReplication,Events)),playerEvents: new RuntimePlayerEventFanout(new RuntimePlayerEventFanout(Replication, projectileReplication), Events),
                projectileStepper: sourceStepper, projectileReplication: projectileReplication, worldItems: Items, worldTiles: Tiles, worldItemReplication: Replication,
                sourcePlayerWorld: MiningSourceScene1458.Facts(Tiles),
                worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
                playerUpdateRandomSeed: new(0),
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(sourceMain),
                townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 });
            ToolPhaseWorldFixture1458.BindFreshInactiveEvent(State, Tiles);
            for (int x = 43; x <= 55; x++) Tiles.Set(x, 54, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var source = GameCommandSourceId.FromConnection(9740);
            Assert.True(Replication.TryRegister(source, outbound));
            var ingress = new ApplyingIngress(State);
            bootstrap = new(pool, outbound,
                PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, [], new byte[] { 3, 0, 49 }), source,
                new RuntimePlayerSpawnCommitIngress(ingress), appearanceIngress: null,
                new RuntimePlayerEquipmentIngress(ingress), new RuntimePlayerMovementIngress(ingress,new RuntimePlayerMovementAuthority(),itemUseCaller));
            tileSink = new(source, bootstrap, new Passthrough(), new RuntimeProjectileNetworkIngress(ingress));
            ammoTileSink=new(source,bootstrap,tileSink,new RuntimeTileNetworkIngress(ingress));
            vitalsSink=new(source,bootstrap,new RuntimePlayerHealthIngress(ingress),new RuntimePlayerManaIngress(ingress));
            ItemSink = new(source, bootstrap, tileSink, new RuntimeWorldItemIngress(ingress, Items));
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(1,
                [11, (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a', (byte)'3', (byte)'2', (byte)'6'])));
            // Every packet 5 precedes packet 6, as the real client does after receiving its slot.
            for (short slot = 0; slot < 59; slot++)
                Equip(slot, fullInventory && slot < 58 ? (short)3509 : (short)0,
                    fullInventory && slot < 58 ? (short)9999 : (short)0);
            Equip(49, 2, 1);
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(6, [])));
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(8, new byte[9])));
            byte[] spawn = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
            BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(1), 50);
            BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(3), 52);
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(12, spawn)));
            Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
            var connection = MiningSourceScene1458.Connection(State, Player);
            State.Apply(new PlayerHealthRuntimeCommand(connection, new(Player.Slot, 100, 100)));
            State.Apply(new PlayerManaRuntimeCommand(connection, new(Player.Slot, 0, 20)));
            State.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
            if(selectInitially)Select(7);
            Drain();
        }

        public PlayerHandle AddLocalAfterExistingRemote()
        {
            // Keep genuine slot0 membership/source idle phase. The new local
            // endpoint is slot1; no two-local original process is invented.
            var earlier=Player;Control(7,false);earlierBootstrap=bootstrap;
            var source=GameCommandSourceId.FromConnection(9741);
            Assert.True(Replication.TryRegister(source,outbound));
            var ingress=new ApplyingIngress(State);
            bootstrap=new(pool,outbound,PlayerBootstrapPacketSet.CreateForTesting(new byte[]{3,0,7},[],new byte[]{3,0,49}),
                source,new RuntimePlayerSpawnCommitIngress(ingress),appearanceIngress:null,
                new RuntimePlayerEquipmentIngress(ingress),new RuntimePlayerMovementIngress(ingress));
            tileSink=new(source,bootstrap,new Passthrough(),new RuntimeProjectileNetworkIngress(ingress));
            ammoTileSink=new(source,bootstrap,tileSink,new RuntimeTileNetworkIngress(ingress));
            vitalsSink=new(source,bootstrap,new RuntimePlayerHealthIngress(ingress),new RuntimePlayerManaIngress(ingress));
            ItemSink=new(source,bootstrap,tileSink,new RuntimeWorldItemIngress(ingress,Items));
            Assert.Equal(TerrariaFrameSinkResult.Continue,bootstrap.OnFrame(Frame(1,
                [11,(byte)'T',(byte)'e',(byte)'r',(byte)'r',(byte)'a',(byte)'r',(byte)'i',(byte)'a',(byte)'3',(byte)'2',(byte)'6'])));
            for(short slot=0;slot<59;slot++)Equip(slot,0,0);
            Assert.Equal(TerrariaFrameSinkResult.Continue,bootstrap.OnFrame(Frame(6,[])));
            Assert.Equal(TerrariaFrameSinkResult.Continue,bootstrap.OnFrame(Frame(8,new byte[9])));
            byte[] spawn=new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];spawn[0]=Player.Slot.Value;
            BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(1),50);BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(3),52);
            Assert.Equal(TerrariaFrameSinkResult.Continue,bootstrap.OnFrame(Frame(12,spawn)));
            State.Apply(new PlayerHealthRuntimeCommand(Connection,new(Player.Slot,100,100)));
            State.Apply(new PlayerManaRuntimeCommand(Connection,new(Player.Slot,0,20)));
            State.Apply(new PlayerInventorySyncCompleteRuntimeCommand(Connection));
            Select(7);return earlier;
        }

        public void ReleaseEarlier(PlayerHandle earlier)
        {
            Assert.NotNull(earlierBootstrap);byte[] payload=new byte[14];payload[0]=earlier.Slot.Value;
            payload[2]=16;payload[4]=64;payload[5]=7;
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(6),736f);BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(10),822f);
            Assert.Equal(TerrariaFrameSinkResult.Continue,earlierBootstrap.OnFrame(Frame(13,payload)));
        }
        public long ReportedToolCalls { get; private set; }
        [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_runtime")]
        private static extern ref ServerRuntimeComposition Runtime(ServerRuntimeState state);
        public RuntimePlayerToolUse1458 ToolClock
        {
            get { Assert.True(Runtime(State).Players.TryGet(Player.Slot,out var member));return member.ItemPhase!.Value.ToolUse!; }
        }
        public VanillaUnifiedRandom1458 GameplayRandom => sourceMain;
        public void AmmoReport(in TerrariaProjectileUpdateState packet) =>
            Assert.Equal(TerrariaFrameSinkResult.Continue,tileSink.OnFrame(MiningSourceScene1458.ObservationFrame(
                new ClientProjectileUpdateRuntimeCommand(Connection,packet))));
        public void OriginalAmmoFrame(byte[] wire)
        {
            Assert.Equal(wire.Length,BinaryPrimitives.ReadUInt16LittleEndian(wire));
            var frame=Frame(wire[2],wire.AsSpan(3).ToArray());
            var result=wire[2] switch
            {
                5 or 13 or 138 => bootstrap.OnFrame(frame),
                27 => tileSink.OnFrame(frame),
                17 or 125 => ammoTileSink.OnFrame(frame),
                50 => new PlayerBuffFrameSink(Connection.Source,bootstrap,tileSink,
                    new RuntimePlayerBuffNetworkIngress(new ApplyingIngress(State))).OnFrame(frame),
                _ => TerrariaFrameSinkResult.Continue
            };
            Assert.Equal(TerrariaFrameSinkResult.Continue,result);
        }

        public void ManaReport(short mana,short maximum=200)
        {
            byte[] data=new byte[5];data[0]=Player.Slot.Value;BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(1),mana);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(3),maximum);
            Assert.Equal(TerrariaFrameSinkResult.Continue,vitalsSink.OnFrame(Frame(42,data)));
        }
        public void SetMana(short mana=200)=>State.Apply(new PlayerManaRuntimeCommand(MiningSourceScene1458.Connection(State,Player),new(Player.Slot,mana,200)));
        public PlayerAuthority Players=>Runtime(State).Players;

        [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field,Name="playerUpdateRandom")]
        private static extern ref VanillaUnifiedRandom1458? RandomOwner(PlayerAuthority authority);
        public VanillaUnifiedRandom1458 PlayerRandom=>RandomOwner(Players)!;
        [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field,Name="transferProfiles")]
        private static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority authority);
        public void ContactBlock(short type,bool finalTick=false)
        {
            var contact=Profiles(Players).CaptureBuffState(Connection)!.Clone();
            Assert.True(contact.TryApplyNpcContactBuff(new(type),finalTick?1:60));
            if(finalTick)contact.TickNpcContactBuffs();Profiles(Players).RestoreBuffState(Connection,contact);
        }
        public void Fund(short slot,short count)
        {
            var inventory=Inventory(Players);var q=inventory.CaptureFundedQuantities(Connection);q[slot]=count;inventory.AdoptFundedQuantities(Connection,q);
        }
        public short Funded(short slot)
        {
            Assert.True(Inventory(Players).TryGetFundedQuantity(Connection,slot,out short quantity));return quantity;
        }
        public RuntimePlayerTransferState Detach()
        {
            var done=new TaskCompletionSource<RuntimePlayerTransferState?>();State.Apply(new PlayerTransferDetachRuntimeCommand(Connection,done));
            Assert.True(done.Task.IsCompletedSuccessfully);return Assert.IsType<RuntimePlayerTransferState>(done.Task.Result);
        }
        public void Attach(RuntimePlayerTransferState transfer)
        {
            var done=new TaskCompletionSource<bool>();State.Apply(new PlayerTransferAttachRuntimeCommand(
                new ConnectionHandle(GameCommandSourceId.FromConnection(9740),Player),transfer,50,52,true,false,done));
            Assert.True(done.Task.IsCompletedSuccessfully);Assert.True(done.Task.Result);
        }
        public RuntimePlayerMember Member {get {Assert.True(Players.TryGet(Player.Slot,out var member));return member;}}
        public ProjectileAuthority Projectiles=>Runtime(State).Projectiles;
        public void Buff23()=>State.Apply(new PlayerBuffTypesRuntimeCommand(Connection,new(Player.Slot,new TerraRuntime.Contracts.Gameplay.BuffTypeId[]{new(23)})));
        public PlayerJoinSession Reconnect()
        {
            var old=Connection;State.Apply(new PlayerDisconnectRuntimeCommand(old));bootstrap.Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));var session=new PlayerJoinSession(lease!);
            session.ObserveWorldRequest();session.ObserveSectionRequest();var connection=new ConnectionHandle(GameCommandSourceId.FromConnection(9741),session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(connection,new(session.Slot,100,100)));
            State.Apply(new PlayerManaRuntimeCommand(connection,new(session.Slot,20,20)));
            State.Apply(new PlayerSpawnRuntimeCommand(connection,session,new(session.Slot,50,52,0,0,0,0,0)));
            return session;
        }

        public ConnectionHandle Connection=>MiningSourceScene1458.Connection(State,Player);
        public RuntimePlayerInventoryItem Item(short slot)
        {
            var field=Inventory(Players);Assert.True(field.TryGet(Connection,slot,out var item));return item;
        }
        [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field,Name="inventory")]
        private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority authority);
        private static readonly Lazy<Dictionary<short,TerrariaProjectileUpdateState[]>> SourcePackets=new(()=>
        {
            using var stream=typeof(LocalWeaponClockRuntimeChecks1458).Assembly.GetManifestResourceStream("LocalWeaponClockLinuxfirst")!;
            using var zip=new System.IO.Compression.GZipStream(stream,System.IO.Compression.CompressionMode.Decompress);
            using var doc=System.Text.Json.JsonDocument.Parse(zip);
            var values=new Dictionary<short,TerrariaProjectileUpdateState[]>();
            foreach(var row in doc.RootElement.EnumerateArray().Where(r=>r.GetProperty("mode").GetString()=="buffer-held" && r.GetProperty("seed").GetInt32()==0))
            {
                var packets=new List<TerrariaProjectileUpdateState>();
                foreach(var value in row.GetProperty("steps")[0].GetProperty("checkFrames").EnumerateArray())
                {
                    byte[] bytes=Convert.FromHexString(value.GetString()!);if(bytes[2]!=27)continue;
                    var buffer=new ReadOnlySequence<byte>(bytes);Assert.Equal(TerrariaFrameReadResult.Frame,TerrariaFrameDecoder.TryRead(ref buffer,out var frame));
                    Assert.Equal(TerrariaProjectileDecodeResult.Decoded,TerrariaProjectileDecoder.TryDecodeUpdate(frame,out var packet));packets.Add(packet);
                }
                if(packets.Count>0)values.Add((short)row.GetProperty("item").GetInt32(),packets.ToArray());
            }
            return values;
        });
        internal sealed record GemSourceCase(TerrariaProjectileUpdateState[] Packets,
            TerraRuntime.Gameplay.Players.PlayerSelectedConsumableState1458 Clocks,int Cost);
        private static readonly Lazy<Dictionary<(string,short),GemSourceCase>> GemSources=new(()=>
        {
            var found=new Dictionary<(string,short),GemSourceCase>();
            foreach(string edition in new[]{"Linux","Windows"})
            {
                using var stream=typeof(LocalWeaponClockRuntimeChecks1458).Assembly.GetManifestResourceStream("GemStaffBehavior"+edition+"1458")!;
                using var zip=new System.IO.Compression.GZipStream(stream,System.IO.Compression.CompressionMode.Decompress);
                using var doc=System.Text.Json.JsonDocument.Parse(zip);
                foreach(var row in doc.RootElement.EnumerateArray().Where(r=>r.GetProperty("local").GetBoolean() &&
                    r.GetProperty("mode").GetString()=="ordinary" && r.GetProperty("robe").GetInt32()==0))
                {
                    var sourceSeed=new VanillaUnifiedRandom1458(12345);
                    Assert.Equal(row.GetProperty("beforeRandom").GetProperty("state").EnumerateArray().Select(v=>v.GetInt32()).ToArray(),GemRandomCells(sourceSeed));
                    Assert.Equal(row.GetProperty("beforeRandom").GetProperty("cursor").GetUInt32(),GemRandomCursor(sourceSeed));
                    var first=row.GetProperty("phases")[0];var after=first.GetProperty("state");var packets=new List<TerrariaProjectileUpdateState>();
                    foreach(var born in first.GetProperty("born").EnumerateArray())
                        packets.Add(new(new(0,checked((ushort)(980+packets.Count)),1),(short)born.GetProperty("type").GetInt32(),
                            born.GetProperty("position").GetProperty("x").GetSingle(),born.GetProperty("position").GetProperty("y").GetSingle(),
                            born.GetProperty("velocity").GetProperty("x").GetSingle(),born.GetProperty("velocity").GetProperty("y").GetSingle(),
                            BitConverter.Int32BitsToSingle(born.GetProperty("aiBits")[0].GetInt32()),born.GetProperty("ai")[1].GetSingle(),0,0,
                            (short)born.GetProperty("damage").GetInt32(),born.GetProperty("knockBack").GetSingle(),0));
                    var clock=new TerraRuntime.Gameplay.Players.PlayerSelectedConsumableState1458(after.GetProperty("itemTime").GetInt32(),after.GetProperty("itemTimeMax").GetInt32(),
                        after.GetProperty("itemAnimation").GetInt32(),after.GetProperty("itemAnimationMax").GetInt32(),after.GetProperty("releaseUseItem").GetBoolean(),
                        after.GetProperty("potionDelay").GetInt32(),after.GetProperty("revolverCritChanceBonus").GetInt32());
                    found.Add((edition,(short)row.GetProperty("id").GetInt32()),new(packets.ToArray(),clock,
                        row.GetProperty("before").GetProperty("statMana").GetInt32()-after.GetProperty("statMana").GetInt32()));
                }
            }
            return found;
        });
        [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field,Name="seedArray")]
        private static extern ref int[] GemRandomCells(VanillaUnifiedRandom1458 random);
        [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field,Name="inext")]
        private static extern ref uint GemRandomCursor(VanillaUnifiedRandom1458 random);
        internal static GemSourceCase GemSource(string edition,short staff)=>GemSources.Value[(edition,staff)];
        public void SourceGemBirths(string edition,short staff)
        {
            var source=GemSource(edition,staff);Assert.True(State.TryCapturePlayerSnapshot(Player,out var player));
            // The full original seed state is independently checked above.
            // Real bootstrap/setup has its own Main draws. Six deterministic
            // reports remain literal; ArPenSpread's randomized center uses the
            // independently source-differential producer on this actual clone.
            var beforeMain=sourceMain.Clone();
            Span<TerraRuntime.Gameplay.Items.GemStaffLaunch1458> launches=stackalloc TerraRuntime.Gameplay.Items.GemStaffLaunch1458[2];
            foreach(var packet in source.Packets)
            {
                var translated=packet with {Key=packet.Key with {Spawner=Player.Slot.Value},
                    PositionX=packet.PositionX+player.PositionX-400f,PositionY=packet.PositionY+player.PositionY-400f};
                if((BitConverter.SingleToInt32Bits(packet.Ai0)&32)!=0)
                {
                    Assert.True(TerraRuntime.Gameplay.Items.VanillaGemStaff1458.TryGetWeapon(new(staff),out var weapon));
                    Assert.True(TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(weapon.Projectile,out var definition));
                    var planned=beforeMain.Clone();
                    Assert.True(TerraRuntime.Gameplay.Items.VanillaGemStaff1458.TryPlanVolley(new(staff),default,
                        player.PositionX+PlayerAuthority.VanillaBasePlayerWidth*.5f,player.PositionY+PlayerAuthority.VanillaBasePlayerHeight*.5f,
                        packet.VelocityX,packet.VelocityY,weapon.BaseDamage,weapon.KnockBack,planned.NextDouble,launches,out int count));
                    Assert.Equal(1,count);var launch=launches[0];
                    Assert.Equal(packet.Damage,launch.Damage);Assert.Equal(packet.KnockBack,launch.KnockBack);
                    Assert.Equal(BitConverter.SingleToInt32Bits(packet.VelocityX),BitConverter.SingleToInt32Bits(launch.VelocityX));
                    Assert.Equal(BitConverter.SingleToInt32Bits(packet.VelocityY),BitConverter.SingleToInt32Bits(launch.VelocityY));
                    Assert.Equal(BitConverter.SingleToInt32Bits(packet.Ai0),BitConverter.SingleToInt32Bits(launch.Ai0));Assert.Equal(packet.Ai1,launch.Ai1);
                    translated=translated with {PositionX=launch.CenterX-definition.Width*.5f,PositionY=launch.CenterY-definition.Height*.5f};
                }
                Assert.True(sourceMain.HasSameState(beforeMain));
                Assert.Equal(TerrariaFrameSinkResult.Continue,tileSink.OnFrame(MiningSourceScene1458.ObservationFrame(new ClientProjectileUpdateRuntimeCommand(Connection,translated))));
            }
        }
        public void SourceBirth(short weapon,ushort identity=902)
        {
            var source=SourcePackets.Value[weapon][0];
            Assert.True(State.TryCapturePlayerSnapshot(Player,out var player));
            // Only authenticated occupation and scene translation change; launch
            // velocity/damage/KB/AI are the independent original packet fields.
            var packet=source with {Key=new(Player.Slot.Value,identity,1),
                PositionX=source.PositionX+player.PositionX-400f,
                PositionY=source.PositionY+player.PositionY-400f};
            if(ammoFamilyIsBullet(weapon))
            {
                Assert.True(Runtime(State).Players.TryGet(Player.Slot,out var member));
                var combat=member.ItemPhase!.Value.DerivedCombat!.Value;
                Assert.True(TerraRuntime.Gameplay.Items.VanillaProjectileWeaponCombatCatalog.TryGetWeapon(new(weapon),out var definition));
                Assert.True(TerraRuntime.Gameplay.Items.VanillaProjectileWeaponCombatCatalog.TryGetAmmo(
                    TerraRuntime.Gameplay.Items.VanillaProjectileAmmoFamily.Bullet,new(97),out var ammunition));
                Assert.True(TerraRuntime.Gameplay.Items.VanillaBulletWeaponStats1458.TryGetPrefixModifiers(new(weapon),default,
                    TerraRuntime.Gameplay.Items.VanillaBulletSourceArithmetic1458.CoreClrSingle,out var prefix));
                var speed=TerraRuntime.Gameplay.Items.VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(definition,ammunition,prefix,combat);
                // Local phase scheduling uses its source UpdatePlayers cursor.
                // Replay only the detached preimage through the source-qualified
                // kernel; retain every literal nonrandom field of the capsule.
                var paid=ToolClock.WeaponShootHistory?.Uses.FirstOrDefault(static u=>!u.Born && !u.Retired);
                var cursor=member.ItemUseCaller==RuntimeItemUseCaller1458.LocalOwnerProjection && ToolClock.LocalClocksKnown
                    ? paid?.RandomBefore.Clone()??PlayerRandom.Clone():sourceMain.Clone();
                if(member.ItemUseCaller==RuntimeItemUseCaller1458.LocalOwnerProjection && ToolClock.LocalClocksKnown && paid is null)
                {
                    Assert.True(TerraRuntime.Gameplay.Players.VanillaSelectedConsumable1458.TryStepHeat(
                        member.ItemPhase!.Value.ManaHeat,cursor.NextDouble,cursor.Next,out _));
                    cursor.Next(3);
                }
                TerraRuntime.Gameplay.Items.VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation(definition,ammunition,combat,cursor.Next);
                Span<TerraRuntime.Gameplay.Items.VanillaBulletLaunchShot1458> shots=stackalloc TerraRuntime.Gameplay.Items.VanillaBulletLaunchShot1458[8];
                Assert.True(TerraRuntime.Gameplay.Items.VanillaBulletWeaponLaunch1458.TryPlanFromAim(new(weapon),210f,-11f,1,
                    speed.CanonicalMagnitude,new((ushort)packet.ProjectileType),cursor.Next,cursor.NextDouble,shots,out int count));
                for(int i=0;i<count;i++)
                {
                    var child=packet with {Key=new(Player.Slot.Value,(ushort)(identity+i),1),VelocityX=shots[i].VelocityX,VelocityY=shots[i].VelocityY};
                    Send(child);
                }
            }
            else Send(packet);
            void Send(TerrariaProjectileUpdateState child)=>Assert.Equal(TerrariaFrameSinkResult.Continue,
                tileSink.OnFrame(MiningSourceScene1458.ObservationFrame(new ClientProjectileUpdateRuntimeCommand(
                    MiningSourceScene1458.Connection(State,Player),child))));
            static bool ammoFamilyIsBullet(short id)=>id is 98 or 219 or 533 or 1929 or 964 or 534 or 679 or 4703;
        }
        public void Control(byte slot,bool held)
        {
            byte[] payload=new byte[14];payload[0]=Player.Slot.Value;payload[1]=held?(byte)32:(byte)0;payload[2]=16;payload[4]=64;payload[5]=slot;
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(6),736f);BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(10),822f);
            Assert.Equal(TerrariaFrameSinkResult.Continue,bootstrap.OnFrame(Frame(13,payload)));
        }


        public void Equip(short slot, short type, short stack, byte prefix = 0, bool favorite = false)
        {
            if (bootstrap.JoinState == PlayerJoinState.Playing &&
                State.TryCapturePlayerSnapshot(Player, out var before) && before.SelectedItem == slot)
                MiningSourceScene1458.FinishUse(State, MiningSourceScene1458.Connection(State, Player));
            byte[] payload = new byte[9];payload[0]=Player.Slot.Value;
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), slot);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), stack);
            payload[5] = prefix;
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(6), type);
            payload[8] = favorite ? (byte)1 : (byte)0;
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(5, payload)));
            Assert.Equal(0, State.RejectedPlayerEquipmentUpdates);
            // Each fixture operation is one complete source inventory sync.
            // A later changed image of the same slot belongs AFTER138, not to
            // a contradictory second image inside the previous observed batch.
            if (bootstrap.JoinState == PlayerJoinState.Playing)
                Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(138, [])));
        }

        public void Select(byte slot)
        {
            if (bootstrap.JoinState == PlayerJoinState.Playing &&
                State.TryCapturePlayerSnapshot(Player, out var before) && before.SelectedItem != slot)
                MiningSourceScene1458.FinishUse(State, MiningSourceScene1458.Connection(State, Player));
            byte[] payload = new byte[14];payload[0]=Player.Slot.Value;
            payload[1] = 32;
            payload[2] = 16;
            payload[5] = slot;
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(6), 736);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(10), 822);
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(13, payload)));
        }

        public void Kill(short x = 50, short y = 52, short fail = 0)
        {
            var connection = MiningSourceScene1458.Connection(State, Player);
            MiningSourceScene1458.Apply(State, Tiles,
                new ClientTileManipulationRuntimeCommand(connection, new(0, x, y, fail, 0)), SendTool);
            ReportedToolCalls += MiningSourceScene1458.ReportedCalls(State, connection);
        }

        public void Shovel()
        {
            var connection = MiningSourceScene1458.Connection(State, Player);
            MiningSourceScene1458.ShovelAction(State, Tiles, connection, 50, 52, SendTool);
            ReportedToolCalls += MiningSourceScene1458.ReportedCalls(State, connection);
        }

        public void RawKill()=>SendTool(new ClientTileManipulationRuntimeCommand(Connection,new(0,50,52,0,0)));
        private void SendTool(RuntimeCommand command) =>
            Assert.Equal(TerrariaFrameSinkResult.Continue, tileSink.OnFrame(MiningSourceScene1458.ObservationFrame(command)));

        public bool SpawnSourceDrop(in WorldItemDropStateUpdate drop, out WorldItemSnapshot item)
        {
            item = default;
            return Items.TryReserveDrop(drop,out var reserved) && Items.TryCommitReservedDrop(reserved,out item);
        }

        public void Remove(short slot)
        {
            // Reservation22 chooses an owner; actual151 still needs the owned
            // body inside the source grab region. Approach through real13,
            // rather than treating an ownership label as remote pickup power.
            if (Items.TryGetActive(slot, out var item) && State.TryCapturePlayerSnapshot(Player, out var body))
            {
                byte[] approach = new byte[14]; approach[1] = 64; approach[2] = 16;
                approach[5] = body.SelectedItem;
                BinaryPrimitives.WriteSingleLittleEndian(approach.AsSpan(6), Math.Max(640f, item.PositionX - 16f));
                BinaryPrimitives.WriteSingleLittleEndian(approach.AsSpan(10), Math.Max(640f, Math.Min(body.PositionY, item.PositionY)));
                Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(13, approach)));
            }
            byte[] payload = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(payload, slot);
            Assert.Equal(TerrariaFrameSinkResult.Continue, ItemSink.OnFrame(Frame(151, payload)));
        }

        public void DiscoverOwner() { for (int i = 0; i < 5; i++) State.Tick(); }

        public RuntimeCommand CaptureRemoval(short slot)
        {
            var capture = new CapturingIngress();
            var sink = new WorldItemFrameSink(GameCommandSourceId.FromConnection(9740), bootstrap,
                new Passthrough(), new RuntimeWorldItemIngress(capture, Items));
            byte[] payload = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(payload, slot);
            Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(151, payload)));
            return Assert.IsType<WorldItemRemoveRuntimeCommand>(capture.Command);
        }

        public byte[] Drain()
        {
            var ids = new List<byte>();
            while(outbound.QueuedFrames>0)ids.Add(MiningSourceScene1458.TakeFrame(outbound).MessageId);
            return ids.ToArray();
        }

        public void Dispose(){bootstrap.Dispose();earlierBootstrap?.Dispose();}
    }

    private static TerrariaFrame Frame(byte id, byte[] payload) =>
        new(checked((ushort)(3 + payload.Length)), id, ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(payload));

    private sealed class ApplyingIngress(ServerRuntimeState state) : IGameCommandIngress<RuntimeCommand>
    {
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { state.Apply(command); return true; }
    }
    private sealed class CapturingIngress : IGameCommandIngress<RuntimeCommand>
    {
        public RuntimeCommand? Command { get; private set; }
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { Command = command; return true; }
    }
    private sealed class Passthrough : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
    }
}
