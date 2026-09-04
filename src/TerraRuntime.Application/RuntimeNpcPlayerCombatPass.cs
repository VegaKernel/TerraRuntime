using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime;

/// <summary>
/// Post-NPC-simulation authoritative contact pass for the source-backed vanilla hitbox slice. Player HP is mutated
/// only by <see cref="PlayerAuthority"/> on the world loop. Creative-style god mode returns before HP/immunity
/// mutation and receives throttled combat text instead of trusting any client-reported hurt result.
/// </summary>
internal sealed class RuntimeNpcPlayerCombatPass
{
    private const int PlayerSlotCount = byte.MaxValue + 1;
    private const long GodModeMissThrottleTicks = 40;
    private readonly RuntimeNpcStore npcs;
    private readonly PlayerAuthority players;
    private readonly Random random;
    private readonly NpcSnapshot[] npcBuffer;
    private readonly long[] lastGodModeContactTick;
    private readonly NpcGeneration[] npcGenerations;
    private readonly PlayerSessionGeneration[] playerGenerations;

    public RuntimeNpcPlayerCombatPass(RuntimeNpcStore npcs, PlayerAuthority players, Random? random = null)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.random = random ?? Random.Shared;
        npcBuffer = new NpcSnapshot[npcs.Capacity];
        int cells = checked(npcs.Capacity * PlayerSlotCount);
        lastGodModeContactTick = new long[cells];
        npcGenerations = new NpcGeneration[cells];
        playerGenerations = new PlayerSessionGeneration[cells];
        Array.Fill(lastGodModeContactTick, long.MinValue);
    }

    public long CommittedHits { get; private set; }
    public long GodModeAvoidances { get; private set; }
    public long Kills { get; private set; }

    public void Tick(long tick)
    {
        int count = npcs.CopyActive(npcBuffer);
        for (int i = 0; i < count; i++)
        {
            NpcSnapshot npc = npcBuffer[i];
            if (!npc.IsActive ||
                !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out VanillaNpcDefinition definition) ||
                definition.Role == NpcArchetypeRole.Town ||
                !VanillaIncomingPlayerDamageFacts1458.TryGetNpcContactImmunityChannel(
                    npc.TypeIdentity,
                    in npc.Ai,
                    out VanillaPlayerImmunityChannel1458 immunityChannel) ||
                ResolveContactDamage(in npc, in definition) <= 0 ||
                !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
            {
                continue;
            }

            float npcLeft = npc.PositionX;
            float npcTop = npc.PositionY;
            float npcRight = npcLeft + hitbox.Width;
            float npcBottom = npcTop + hitbox.Height;
            foreach (RuntimePlayerMember target in players.Members)
            {
                PlayerHandle targetHandle = target.Connection.Player;
                if (target.IsDead || !target.HasHealth || target.Life <= 0 ||
                    !Intersects(npcLeft, npcTop, npcRight, npcBottom, target) ||
                    (target.GodMode && IsGodModeCoolingDown(npc.Handle, targetHandle, tick)))
                {
                    continue;
                }

                int rawDamage = ResolveContactDamage(in npc, in definition);
                int damage = VanillaIncomingPlayerDamageFacts1458.ResolveNpcContactDamage(rawDamage, random.Next(-15, 16));
                if (damage <= 0)
                    continue;

                int hitDirection = npcLeft + hitbox.Width * 0.5f < target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f
                    ? 1
                    : -1;
                bool killedBefore = target.IsDead;
                PlayerDamageCommitResult result = players.TryCommitAuthoritativeNpcContactDamage(
                    tick,
                    npc.Handle,
                    targetHandle,
                    damage,
                    hitDirection,
                    immunityChannel,
                    out PlayerStateSnapshot committed);
                if (result == PlayerDamageCommitResult.Rejected)
                    continue;

                if (result == PlayerDamageCommitResult.AvoidedByGodMode)
                {
                    GodModeAvoidances++;
                    MarkGodModeContact(npc.Handle, targetHandle, tick);
                    continue;
                }

                CommittedHits++;
                if (!killedBefore && committed.IsDead)
                    Kills++;
            }
        }
    }

    private static int ResolveContactDamage(in NpcSnapshot npc, in VanillaNpcDefinition definition) =>
        npc.Simulation.DamageOverride ?? definition.Damage;

    private bool IsGodModeCoolingDown(NpcHandle npc, PlayerHandle player, long tick)
    {
        int index = checked(npc.Slot * PlayerSlotCount + player.Slot.Value);
        return npcGenerations[index] == npc.Generation &&
            playerGenerations[index] == player.Generation &&
            lastGodModeContactTick[index] != long.MinValue &&
            tick - lastGodModeContactTick[index] < GodModeMissThrottleTicks;
    }

    private void MarkGodModeContact(NpcHandle npc, PlayerHandle player, long tick)
    {
        int index = checked(npc.Slot * PlayerSlotCount + player.Slot.Value);
        npcGenerations[index] = npc.Generation;
        playerGenerations[index] = player.Generation;
        lastGodModeContactTick[index] = tick;
    }

    private static bool Intersects(float left, float top, float right, float bottom, RuntimePlayerMember player) =>
        left < player.PositionX + PlayerAuthority.VanillaBasePlayerWidth &&
        right > player.PositionX &&
        top < player.PositionY + PlayerAuthority.VanillaBasePlayerHeight &&
        bottom > player.PositionY;
}
