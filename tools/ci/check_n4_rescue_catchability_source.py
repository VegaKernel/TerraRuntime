import argparse
import re
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--npc', required=True)
p.add_argument('--message-buffer', required=True)
p.add_argument('--npcid', required=True)
p.add_argument('--item', required=True)
p.add_argument('--projectile', required=True)
a = p.parse_args()

npc = Path(a.npc).read_text(errors='replace')
message = Path(a.message_buffer).read_text(errors='replace')
npcid = Path(a.npcid).read_text(errors='replace')
item = Path(a.item).read_text(errors='replace')
projectile = Path(a.projectile).read_text(errors='replace')


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise SystemExit(f'missing 1.4.5.8 source contract: {label}: {needle!r}')

# Packet 70 -> NPC.CatchNPC authoritative server boundary.
require(message, 'case 70:', 'packet 70 switch')
require(message, 'reader.ReadInt16()', 'packet 70 Int16 slot')
require(message, 'NPC.CatchNPC(num16, whoAmI)', 'packet 70 catch call')

# CatchNPC special branches and player assignment semantics.
for needle, label in [
    ('public static void CatchNPC(int i, int who = -1)', 'CatchNPC signature'),
    ('!Main.npc[i].active || Main.npc[i].catchItem <= 0', 'active/catchItem gate'),
    ('Main.npc[i].type == 687', 'Mystic Frog special case'),
    ('Main.npc[i].TryTeleportingCaughtMysticFrog()', 'Mystic Frog teleport'),
    ('Main.npc[i].SpawnedFromStatue', 'statue branch'),
    ('Item.DefaultAssignNewItemsToPlayer(who)', 'player-owned caught item scope'),
    ('Main.player[who].Center.X', 'caught item player center X'),
    ('Main.player[who].Center.Y', 'caught item player center Y'),
    ('Main.npc[i].active = false', 'caught NPC despawn')
]:
    require(npc, needle, label)

# Seven classic talk-rescue transforms.
for source, target in [(589,588),(105,107),(106,108),(123,124),(354,353),(376,369),(579,550)]:
    require(npc, f'type == {source}', f'bound NPC {source}')
    require(npc, f'AI_000_TransformBoundNPC(i, {target})', f'rescue {source}->{target}')
require(npc, 'public void AI_000_TransformBoundNPC(int playerID, int npcType)', 'bound transform method')
require(npc, 'AI_007_TownEntities_UpdateSavedStates()', 'saved-state update after rescue')
require(npc, 'Main.player[playerID].SetTalkNPC(whoAmI)', 'talk retarget after rescue')

for target, saved in [(588,'savedGolfer'),(441,'savedTaxCollector'),(107,'savedGoblin'),(108,'savedWizard'),(124,'savedMech'),(353,'savedStylist'),(369,'savedAngler'),(550,'savedBartender')]:
    pattern = rf'case\s+{target}:\s*{saved}\s*=\s*true;'
    if not re.search(pattern, npc):
        raise SystemExit(f'missing saved-state source contract for resident {target}/{saved}')

# Exact critter classification list.
expected = [46,303,337,540,443,74,297,298,442,611,689,377,446,612,613,356,444,595,596,597,598,599,600,601,604,605,357,448,374,484,355,358,606,359,360,485,486,487,148,149,55,230,592,593,299,538,539,300,447,361,445,362,363,364,365,367,366,583,584,585,602,603,607,608,609,610,616,617,625,626,627,615,639,640,641,642,643,644,645,646,647,648,649,650,651,652,653,654,655,661,669,671,672,673,674,675,677,687,688]
m = re.search(r'CountsAsCritter\s*=\s*Factory\.CreateBoolSet\(([^;]+)\);', npcid)
if not m:
    raise SystemExit('missing CountsAsCritter source set')
actual = [int(x) for x in re.findall(r'\d+', m.group(1))]
if actual != expected:
    raise SystemExit(f'CountsAsCritter drift: expected {expected}, got {actual}')

require(item, 'public void DefaultToCapturedCritter(short npcIdToSpawnOnUse)', 'captured critter defaults')
require(item, 'width = 12;', 'captured critter width')
require(item, 'height = 12;', 'captured critter height')
require(item, 'public static IDisposable DefaultAssignNewItemsToPlayer(int plr)', 'captured item ownership helper')

# Tax Collector is deliberately not a talk rescue: projectile 10 powder transform is a separate source path.
require(projectile, 'private void Damage_TryUsingPowders(Rectangle projRectangle)', 'powder path')
require(projectile, 'type == 10 && Main.netMode != 1', 'Purification Powder projectile')
require(projectile, 'nPC.type == 534', 'Demon Tax Collector')
require(projectile, 'nPC.Transform(441)', 'Tax Collector transform')

print('TerrariaServer 1.4.5.8 N4 rescue/catchability source contract passed')
