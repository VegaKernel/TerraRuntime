#!/usr/bin/env python3
import argparse
from pathlib import Path


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise SystemExit(f'missing source contract: {label}: {needle}')


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--npc', required=True)
    parser.add_argument('--npcid', required=True)
    parser.add_argument('--world-file', required=True)
    parser.add_argument('--shimmer-helper', required=True)
    parser.add_argument('--net-message', required=True)
    args = parser.parse_args()

    npc = Path(args.npc).read_text(errors='replace')
    npcid = Path(args.npcid).read_text(errors='replace')
    world = Path(args.world_file).read_text(errors='replace')
    helper = Path(args.shimmer_helper).read_text(errors='replace')
    net = Path(args.net_message).read_text(errors='replace')

    require(
        npcid,
        'ShimmerTownTransform = Factory.CreateBoolSet(22, 17, 18, 227, 207, 633, 588, 208, 369, 353, 38, 20, 550, 19, 107, 228, 54, 124, 441, 229, 160, 108, 178, 209, 142, 663, 37, 453, 368)',
        'NPCID.Sets.ShimmerTownTransform membership')
    require(npc, 'else if (NPCID.Sets.ShimmerTownTransform[type])', 'GetShimmered town branch')
    require(npc, 'ai[0] = 25f;', 'AI_007 shimmer state')
    require(npc, 'shimmerTransparency = 0.89f;', 'entry transparency')
    require(npc, 'shimmerTransparency += 0.01f;', 'pre-entry accumulation')
    require(npc, 'if (ai[1] >= 30f)', 'rise start tick')
    require(npc, 'shimmerTransparency = MathHelper.Clamp(shimmerTransparency - 1f / 60f, 0f, 1f);', 'exit fade')
    require(npc, 'if (ai[1] >= 75f && shimmerTransparency <= 0f && Main.netMode != 1)', 'exit gate')
    require(npc, 'int num = 560;', 'distant landing threshold')
    require(npc, 'ai[2] = 30f;', 'distant landing delay')
    require(npc, 'townNpcVariationIndex = ((townNpcVariationIndex != 1) ? 1 : 0);', 'variation toggle')
    require(npc, 'NetMessage.SendData(56, -1, -1, null, whoAmI);', 'packet 56 publication')

    require(
        helper,
        'public static Vector2? FindSpotWithoutShimmer(Entity entity, int startX, int startY, int expand, bool allowSolidTop)',
        'ShimmerHelper search')
    require(helper, 'Collision.SolidCollision(landingPosition, entity.width, entity.height)', 'landing actor collision')
    require(helper, 'entity.height + 100', '100px shimmer/ground probe')

    require(world, 'bool[] array = (bool[])NPC.ShimmeredTownNPCs.Clone();', 'SaveNPCs shimmer type flags')
    require(world, 'writer.Write(nPC.townNpcVariationIndex);', 'SaveNPCs town variation')
    require(net, 'case 56:', 'packet 56 encoder case')
    require(net, 'writer.Write((short)number);', 'packet 56 NPC slot')
    require(net, 'writer.Write(givenName);', 'packet 56 given name')
    require(net, 'writer.Write(Main.npc[number].townNpcVariationIndex);', 'packet 56 variation')

    print('Town NPC shimmer source contract: OK')


if __name__ == '__main__':
    main()
