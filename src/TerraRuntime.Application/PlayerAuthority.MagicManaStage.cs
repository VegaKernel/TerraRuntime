using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

// Existing payment owns this single early local UpdateManaRegen projection.
// It contains no new balance, random stream, public report or retry history.
internal sealed record RuntimePlayerMagicManaStage1458(PlayerHandle Player,
    RuntimePlayerUpdateWorld1458 World, VanillaUnifiedRandom1458 RandomOwner,
    PlayerManaRegenerationState1458 Before, PlayerManaRegenerationState1458 Regenerated,
    PlayerManaRegenerationState1458 AfterPayment, bool Hooked, int VisualSamples);

internal sealed partial class PlayerAuthority
{
    private bool CanBeginLocalMagicUse(ConnectionHandle connection) =>
        transferProfiles.CaptureBuffState(connection) is {PreventsFreshItemUse:false, HasSilenceEffect:false};

    private bool TryPrepareLocalMagicManaStage(RuntimePlayerMember member,
        in RuntimePlayerItemPhase1458 phase, out RuntimePlayerMagicManaStage1458? stage)
    {
        stage=null;
        // Never-bound leaf components keep their documented CheckMana-only
        // boundary. Only an owned local phase can prepare the preceding Update.
        if(toolWorld is null || phase.ToolUse is not {LocalClocksKnown:true} local)return true;
        if(!playerUpdateRandomOwned || playerUpdateRandom is not { } random ||
            playerUpdateWorld is not { } world || phase.Mana.Mana!=member.Mana ||
            CaptureRemotePlayerGrappling(member.Connection.Player) is not bool hooked ||
            !transferProfiles.TryCapture(member.Connection,out var appearance,out var equipment,out _))return false;
        bool openVoid=false;
        for(short slot=0;slot<58;slot++)
        {
            if(!inventory.TryGet(member.Connection,slot,out var stored) || !stored.IsCanonical)return false;
            openVoid|=VanillaVoidStorageCatalog1458.OpensVoidStorage(stored.ItemType);
        }
        if(!TryCaptureProvenRemoteEquipment(equipment,appearance,out _,out int manaBonus,openVoid,phase.AmmoCycling is not null))return false;
        int maximum=(int)Math.Min((long)phase.BaseManaMaximum+manaBonus,400);
        if(maximum<=0)return false;
        var facts=new PlayerManaRegenerationFacts1458(member.VelocityX,member.VelocityY,hooked,false,
            ((appearance?.ConsumableUnlockFlags??0)&4)!=0,0,0f,0);
        if(!VanillaRemoteManaRegeneration1458.TryStep(phase.Mana with {Maximum=maximum},in facts,out var after,out _))return false;
        after=after with {Mana=Math.Min(after.Mana,after.Maximum),Count=Math.Max(0,after.Count)};
        int samples=LocalManaFeedbackSamples(phase.Mana,after,local.Clocks);
        stage=new(member.Connection.Player,world,random,phase.Mana,after,after,hooked,samples);
        return true;
    }

    private static int LocalManaFeedbackSamples(in PlayerManaRegenerationState1458 before,
        in PlayerManaRegenerationState1458 after,in ToolClockState1458 clocks) =>
        clocks.Selected.Animation==0 && clocks.ReuseDelay==0 && before.Mana<after.Maximum &&
        after.Mana>=after.Maximum ? 5 : 0;

    private void AdvanceLocalManaFeedback(ref RuntimePlayerItemPhase1458 item,
        in RuntimePlayerInventoryItem selected,
        in PlayerManaRegenerationState1458 before,in PlayerManaRegenerationState1458 after,
        VanillaUnifiedRandom1458 random)
    {
        if(toolWorld is null || item.ToolUse is not {LocalClocksKnown:true} local)return;
        // Dedicated remote UpdateManaRegen has no local feedback. The local
        // mana-use producer establishes this caller role, not constructor-zero
        // clocks. Once owned, it survives switching to a tool/passive selection:
        // original feedback tests player identity, not the current item family.
        bool owned=local.LocalManaFeedbackOwned || item.MagicPayment is {IsValid:true} ||
            VanillaProjectileWeaponCombatCatalog.TryGetChanneledMagicWeapon(selected.ItemType,out _) ||
            VanillaGemStaff1458.TryGetWeapon(selected.ItemType,out _);
        if(!owned)return;
        if(!local.LocalManaFeedbackOwned)
            item=item with {ToolUse=local with {LocalManaFeedbackOwned=true}};
        for(int i=0;i<LocalManaFeedbackSamples(before,after,local.Clocks);i++)random.Next(20,26);
    }

    private bool IsLocalMagicManaStageCurrent(RuntimePlayerProjectileUseCapture capture,
        RuntimePlayerMagicManaStage1458 stage)
    {
        return stage.Player==capture.Connection.Player && playerUpdateRandomOwned &&
            ReferenceEquals(playerUpdateRandom,stage.RandomOwner) && playerUpdateWorld==stage.World &&
            capture.ItemPhase is { } phase && phase.Mana==stage.Before &&
            capture.Player.Mana==stage.Before.Mana &&
            CaptureRemotePlayerGrappling(capture.Connection.Player)==stage.Hooked;
    }

    private static bool TryConsumeLocalMagicManaStage(RemoteItemPhasePlan1458 plan,
        ref RuntimePlayerItemPhase1458 item, VanillaUnifiedRandom1458 random,
        out bool skipRegeneration)
    {
        skipRegeneration=false;
        if(item.MagicPayment is not {ManaStage:{ } stage} payment)return true;
        if(stage.Player!=plan.Member.Connection.Player || stage.VisualSamples is not (0 or 5))return false;
        // Main's local full-mana feedback draws its five scale arguments even
        // while Dust.NewDust is suppressed. Their UpdatePlayers order is here,
        // after prior phase work and before ItemCheck's common Next(3).
        for(int i=0;i<stage.VisualSamples;i++)random.Next(20,26);
        skipRegeneration=item.Mana==stage.AfterPayment;
        if(!skipRegeneration && item.ToolUse is { } tools)
            item=item with {ToolUse=tools with {LocalClocksKnown=false,WeaponShoot=null,RequiresFreshUse=true}};
        // Never restore an earlier value after a changed42/private mutation.
        // Consumption is once-only even when that exact afterimage was lost.
        item=item with {MagicPayment=payment with {ManaStage=null}};
        return true;
    }
}
