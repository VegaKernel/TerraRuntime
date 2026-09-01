#!/usr/bin/env python3
import argparse, re
from pathlib import Path

p=argparse.ArgumentParser()
p.add_argument('--npc', required=True)
p.add_argument('--netmessage', required=True)
p.add_argument('--messagebuffer', required=True)
p.add_argument('--emotebubble', required=True)
a=p.parse_args()

def text(path): return Path(path).read_text(encoding='utf-8-sig')
def require(haystack, pattern, label):
    if re.search(pattern, haystack, re.S) is None:
        raise SystemExit(f'missing source contract: {label}')

npc=text(a.npc); net=text(a.netmessage); mb=text(a.messagebuffer); eb=text(a.emotebubble)
require(npc, r'Main\.rand\.Next\(300\) == 0.*?ai\[0\] = 3f;.*?nPC4\.ai\[0\] = 4f;', 'AI_007 conversation pair 3/4')
require(npc, r'Main\.rand\.Next\(1800\) == 0.*?ai\[0\] = 16f;.*?localAI\[2\] = Main\.rand\.Next\(4\);.*?nPC5\.ai\[0\] = 17f;', 'AI_007 RPS pair 16/17')
require(npc, r'type == 208.*?ai\[0\] = 6f;.*?ai\[1\] = num106;', 'Party Girl player state 6')
require(npc, r'type == 550.*?ai\[0\] = 18f;.*?ai\[1\] = num110;', 'Tavernkeep player state 18')
require(npc, r'Main\.rand\.Next\(1800\) == 0\).*?ai\[0\] = 2f;.*?ai\[1\] = 45 \* Main\.rand\.Next\(1, 2\);', 'ordinary idle state 2')
require(npc, r'type == 229.*?ai\[0\] = 11f;.*?ai\[1\] = 30 \* Main\.rand\.Next\(1, 4\);', 'Pirate idle state 11')
require(npc, r'Main\.rand\.Next\(1200\) == 0\).*?ai\[0\] = 7f;.*?ai\[1\] = num114;', 'generic player reaction state 7')
require(npc, r'else if \(ai\[0\] == 2f \|\| ai\[0\] == 11f\).*?localAI\[3\]--;.*?ai\[1\]--;.*?velocity\.X \*= 0\.8f;', 'idle timer behavior')
require(npc, r'ai\[0\] == 3f \|\| ai\[0\] == 4f.*?ai\[0\] == 16f \|\| ai\[0\] == 17f.*?ai\[0\] == 20f.*?ai\[0\] == 23f.*?velocity\.X \*= 0\.8f;.*?ai\[1\]--;', 'conversation and pet timer group')
require(npc, r'ai\[0\] == 6f \|\| ai\[0\] == 7f \|\| ai\[0\] == 18f \|\| ai\[0\] == 19f.*?Distance\(base\.Center\) > 200f.*?Collision\.CanHitLine', 'player state keep-distance/LOS')
require(npc, r'AI_007_AttemptToPlayIdleAnimationsForPets\(int petIdleChance\).*?type == 638.*?num = 2;.*?IsTownSlime\[type\].*?num = 0;.*?ai\[0\] = \(\(num == 0\) \? 20 : Main\.rand\.Next\(20, 20 \+ num\)\);', 'pet idle state selection')
require(npc, r'ai\[0\] == 20f && type == 637.*?500 \+ Main\.rand\.Next\(200\).*?ai\[0\] == 21f && type == 638.*?100 \+ Main\.rand\.Next\(100\).*?ai\[0\] == 22f && type == 656.*?200 \+ Main\.rand\.Next\(200\).*?IsTownSlime\[type\].*?180 \+ Main\.rand\.Next\(240\)', 'pet idle durations')
require(npc, r'else if \(CanTalk && \(ai\[0\] == 16f \|\| ai\[0\] == 17f\)\)', 'RPS FindFrame state owner')
require(npc, r'frameCounter == 40\.0', 'RPS bubble frame 40')
require(npc, r'frameCounter == 100\.0', 'RPS bubble frame 100')
require(npc, r'frameCounter != 160\.0', 'RPS bubble frame 160')
require(npc, r'Utils\.SelectRandom<int>\(Main\.rand, 38, 37, 36\)', 'RPS explicit emote set')
require(npc, r'EmoteBubble\.NewBubble\([^;]+\);.*?EmoteBubble\.NewBubble\([^;]+\);', 'RPS paired explicit bubbles')
require(net, r'case 91:.*?writer\.Write\(number\);.*?writer\.Write\(\(byte\)number2\);.*?writer\.Write\(\(ushort\)number3\);.*?writer\.Write\(\(ushort\)number4\);.*?writer\.Write\(\(byte\)number5\);', 'packet 91 wire writer')
require(mb, r'case 91:.*?ReadInt32\(\).*?ReadByte\(\).*?ReadUInt16\(\).*?ReadUInt16\(\).*?ReadByte\(\).*?DeserializeNetAnchor', 'packet 91 client reader')
require(eb, r'anch\.entity is NPC.*?item = 0;.*?anch\.entity is Player.*?item = 1;.*?anch\.entity is Projectile.*?item = 2;', 'emote anchor tags')
print('Town NPC social/emote TerrariaServer 1.4.5.8 source contract OK')
