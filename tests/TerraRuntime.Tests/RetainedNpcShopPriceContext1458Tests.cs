using TerraRuntime.Application;
using Xunit;

namespace TerraRuntime.Tests;

// Configured runtime custody checks; this is not whole natural UI or Native parity.
public sealed class RetainedNpcShopPriceContext1458Tests
{
    [Fact]
    public void Exact_correlated_context_transitions_preserve_platform_and_latch_pairs()
    {
        int checks=0;
        void Check(bool value, string label) { checks++; Assert.True(value, label); }
        byte Contexts(byte states) {
         byte result=0;
         for(int platform=0;platform<2;platform++)for(int open=0;open<2;open++)for(int discount=0;discount<2;discount++)
          if((states & (1 << (platform*4+open*2+discount)))!=0)result|=(byte)(1 << (platform*2+discount));
         return result;
        }
        byte Advance(byte states,bool equipped) {
         byte result=0;
         for(int bit=0;bit<8;bit++)if((states & (1<<bit))!=0) {
          int target=(bit%4>=2)?bit:(bit/4*4+(equipped?1:0));result|=(byte)(1<<target);
         }
         return result;
        }
        byte Close(byte states) {
         byte result=states;
         for(int bit=0;bit<8;bit++)if(bit%4>=2 && (states & (1<<bit))!=0)result|=(byte)(1<<(bit-2));
         return result;
        }
        for(int state=1;state<=255;state++) {
         var phase=RuntimePlayerItemPhase1458.Constructor with { ShopPriceContextStates=(byte)state };
         Check(phase.CapturedShopPriceContextMask==Contexts((byte)state),"exact context projection");
         Check(phase.CapturedShopDiscountStates==((state&15)|(state>>4)),"state marginal projection");
         Check(phase.CapturedClientUiPriceArithmeticMask==(((state&15)!=0?1:0)|((state&240)!=0?2:0)),"platform marginal projection");
         Check(phase.CloseShopUiUnknownContexts().CapturedShopPriceContextStates==Close((byte)state),"close retains platform pairs");
         foreach(bool equipped in new[]{false,true})
          Check(phase.AdvanceShopDiscount(equipped).CapturedShopPriceContextStates==Advance((byte)state,equipped),"advance retains platform pairs");
         for(byte arithmetic=1;arithmetic<=3;arithmetic++) {
          if((arithmetic & phase.CapturedClientUiPriceArithmeticMask)!=arithmetic)continue;
          byte expected=(byte)(state & (((arithmetic&1)!=0?15:0)|((arithmetic&2)!=0?240:0)));
          Check(phase.ApplyAcceptedClientUiPriceArithmetic(arithmetic).CapturedShopPriceContextStates==expected,"platform narrowing retains pairs");
         }
        }
        for(byte contexts=1;contexts<=15;contexts++)foreach(bool retired in new[]{false,true}) {
         byte expected=0;
         for(int bit=0;bit<4;bit++)if((contexts & (1<<bit))!=0)expected|=(byte)(1<<(bit/2*4+2+bit%2));
         if(retired)expected=Close(expected);
         var phase=RuntimePlayerItemPhase1458.Constructor.ApplyAcceptedShopPriceContexts(contexts,retired);
         Check(phase.CapturedShopPriceContextStates==expected,"paid precise context adoption");
         Check(phase.CapturedShopPriceContextMask==contexts,"no invented price contexts");
         Check(phase.CloseShopUiUnknownContexts().CapturedShopPriceContextMask==contexts,"close preserves paid exact contexts");
        }
        var paired=RuntimePlayerItemPhase1458.Constructor.ApplyAcceptedShopPriceContexts(9);
        Check(paired.CapturedShopPriceContextStates==132,"LinuxFalse plus WindowsTrue precise state");
        Check(paired.ApplyAcceptedShopDiscount(3).CapturedShopPriceContextMask==9,"legacy discount setter preserves pairs");
        Check(paired.CloseShopUiUnknown(3).CapturedShopPriceContextMask==9,"legacy close preserves pairs");
        Check(paired.CloseShopUiUnknownContexts().CapturedShopPriceContextStates==165,"nonrectangular close pairs");
        Check(paired.CloseShopUiUnknownContexts().AdvanceShopDiscount(false).CapturedShopPriceContextMask==13,"source closed WindowsFalse joins without inventing LinuxTrue");
        Assert.Equal(2330, checks);
    }
}
