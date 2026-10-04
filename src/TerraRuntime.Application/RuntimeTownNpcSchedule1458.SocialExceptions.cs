using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    // Weighted emitter/partner exceptions from EmoteBubble.ProbeExceptions, preserving duplicates and first removal.
    private static void SocialExceptions(in NpcStateUpdate actor, in NpcStateUpdate other,
        in RuntimeTownSocialWorld1458 world, ref SocialCandidates choices)
    {
        NpcTypeId type = new(actor.Type), peer = new(other.Type);
        ReadOnlySpan<byte> fixedChoices = [];
        if (type == VanillaNpcIds.Merchant) fixedChoices = [80, 85, 85, 85, 85];
        else if (type == VanillaNpcIds.Nurse) fixedChoices = [73, 73, 84, 75];
        else if (type == VanillaNpcIds.ArmsDealer)
            fixedChoices = peer == VanillaNpcIds.Guide ? [1, 1, 93, 92] : [82, 82, 85, 85, 77, 93];
        else if (type == VanillaNpcIds.Dryad)
        {
            if (choices.Contains(121)) choices.Add([121, 121]);
            fixedChoices = [14, 14];
        }
        else if (type == VanillaNpcIds.Guide)
        {
            if (!world.BloodMoon) choices.Add(peer == VanillaNpcIds.ArmsDealer ? [1, 1, 93, 92] : [79]);
            if (!world.DayTime) choices.Add([16, 16, 16]);
        }
        else if (type == VanillaNpcIds.OldMan) fixedChoices = [43, 43, 43, 72, 72];
        else if (type == VanillaNpcIds.Demolitionist)
            fixedChoices = world.BloodMoon ? [77, 77, 77, 81] : [77, 77, 81, 81, 81, 90, 90];
        else if (type == VanillaNpcIds.Clothier)
        {
            if (world.BloodMoon) fixedChoices = [43, 72, 1];
            else { if (choices.Contains(111)) choices.Add(111); fixedChoices = [17]; }
        }
        else if (type == VanillaNpcIds.GoblinTinkerer)
        {
            if (peer == VanillaNpcIds.Mechanic)
            { choices.Remove(111); fixedChoices = [0, 0, 0, 17, 17, 86, 88, 88]; }
            else
            { if (choices.Contains(111)) choices.Add([111, 111, 111]); fixedChoices = [91, 92, 91, 92]; }
        }
        else if (type == VanillaNpcIds.Wizard) fixedChoices = [100, 89, 11];
        else if (type == VanillaNpcIds.Mechanic)
        {
            if (peer == VanillaNpcIds.GoblinTinkerer)
            { choices.Remove(111); fixedChoices = [0, 0, 0, 17, 17, 88, 88]; }
            else
            {
                if (choices.Contains(109)) choices.Add([109, 109, 109]);
                if (choices.Contains(108)) { choices.Remove(108); choices.Add(world.HardMode ? [108, 108] : [106, 106]); }
                fixedChoices = [43, 2];
            }
        }
        else if (type == VanillaNpcIds.SantaClaus) fixedChoices = [32, 66, 17, 15, 15];
        else if (type == VanillaNpcIds.Truffle) fixedChoices = [10, 89, 94, 8];
        else if (type == VanillaNpcIds.Steampunker) fixedChoices = [83, 83];
        else if (type == VanillaNpcIds.DyeTrader) fixedChoices = [28, 95, 93];
        else if (type == VanillaNpcIds.PartyGirl) fixedChoices = [94, 17, 3, 77];
        else if (type == VanillaNpcIds.Cyborg) fixedChoices = [48, 83, 5, 5];
        else if (type == VanillaNpcIds.Painter) fixedChoices = [63, 68];
        else if (type == VanillaNpcIds.WitchDoctor) fixedChoices = [24, 24, 95, 8];
        else if (type == VanillaNpcIds.Pirate) fixedChoices = [93, 9, 65, 120, 59];
        else if (type == VanillaNpcIds.Stylist)
        { if (choices.Contains(104)) choices.Add([104, 104]); if (choices.Contains(111)) choices.Add([111, 111]); fixedChoices = [67]; }
        else if (type == VanillaNpcIds.TravellingMerchant) fixedChoices = [85, 7, 79];
        else if (type == VanillaNpcIds.Angler && !world.BloodMoon)
        { choices.Add([70, 70, 76, 76, 79, 79]); if (actor.PositionY < world.Metadata.WorldSurface) choices.Add(29); }
        else if (type == VanillaNpcIds.SkeletonMerchant) fixedChoices = [72, 69, 87, 3];
        else if (type == VanillaNpcIds.TaxCollector) fixedChoices = [100, 100, 1, 1, 1, 87];
        choices.Add(fixedChoices);
    }
}
