using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private Func<PlayerNpcHealthWorld1458>? npcHealthWorldFacts;
    private Func<PlayerHandle, bool?>? npcHealthGrapplingFacts;

    internal void SetNpcHealthWorldFacts(Func<PlayerNpcHealthWorld1458> capture) =>
        npcHealthWorldFacts = capture ?? throw new ArgumentNullException(nameof(capture));

    internal void SetNpcHealthGrapplingFacts(Func<PlayerHandle, bool?> capture) =>
        npcHealthGrapplingFacts = capture ?? throw new ArgumentNullException(nameof(capture));

    private bool TryPlanNpcHealth(RuntimePlayerMember member, int? maximum,
        bool outside, bool ghost, out PlayerNpcHealthState1458? next, bool selectedItemPhase = false,
        (PlayerNpcHealthWorld1458 World, bool? Grappling)? selectedHealthInputs = null)
    {
        next = null;
        var before = member.CaptureSnapshot();
        ulong serial = membership.Serial;
        ulong inventorySerial = inventory.Serial;
        bool profileCaptured = transferProfiles.TryCapture(member.Connection, out var appearance, out var equipment, out var buffs);
        bool supported = (npcHealthWorldFacts is not null || (selectedItemPhase && selectedHealthInputs is not null)) &&
            profileCaptured && buffs is not null &&
            member.NpcHealth is { SourceProfileKnown: true } && (member.ItemAnimation == 0 || selectedItemPhase) &&
            !member.HasMount && (member.MiscFlags1 & (1 << 2)) == 0 && (member.MiscFlags2 & 1) == 0 &&
            (appearance?.ConsumableUnlockFlags ?? 0) == 0 &&
            !equipment.Any(static item => item.Stack > 0 && VanillaPlayerItemSlotCatalog.IsFunctionalArmorSlot(item.SlotId));
        int regeneration = 0;
        foreach (var buff in buffs ?? [])
        {
            if (buff == VanillaBuffIds.Regeneration) regeneration++;
            else if (buff != VanillaBuffIds.Poisoned && buff != VanillaBuffIds.OnFire &&
                buff != VanillaBuffIds.CursedInferno && buff != VanillaBuffIds.Lifeforce &&
                !(selectedItemPhase && buff.Value is 21 or 23 or 93 or 94 or 112)) supported = false;
        }

        // Source remote UpdateLifeRegen precedes movement. Liquid, solid overlap and special furniture
        // are not inferred from the player's report; an unsupported writer retires the clock provenance.
        Span<WorldSectionId> sections = stackalloc WorldSectionId[4];
        Span<long> versions = stackalloc long[4];
        int sectionCount = 0;
        if (!outside && !ghost && !member.IsDead && worldTiles is { } tiles)
        {
            int x0 = (int)Math.Floor(member.PositionX / 16f);
            int x1 = (int)Math.Floor((member.PositionX + VanillaBasePlayerWidth - .001f) / 16f);
            int y0 = (int)Math.Floor(member.PositionY / 16f);
            int y1 = (int)Math.Floor((member.PositionY + VanillaBasePlayerHeight - .001f) / 16f);
            if (x0 < 0 || y0 < 0 || x1 >= tiles.Dimensions.WidthTiles || y1 >= tiles.Dimensions.HeightTiles)
                supported = false;
            else
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        WorldSectionId section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, x, y);
                        if (!sections[..sectionCount].Contains(section))
                        {
                            sections[sectionCount] = section;
                            versions[sectionCount++] = tiles.GetSectionVersion(section);
                        }
                        WorldTile tile = tiles.Get(x, y);
                        if (tile.IsActive || tile.LiquidAmount != 0) supported = false;
                    }
        }
        else if (worldTiles is null) supported = false;

        PlayerNpcHealthWorld1458? world = selectedItemPhase && selectedHealthInputs is { } supplied
            ? supplied.World : npcHealthWorldFacts?.Invoke();
        bool? grappling = selectedItemPhase && selectedHealthInputs is { } selected
            ? selected.Grappling : npcHealthGrapplingFacts?.Invoke(member.Connection.Player);
        bool moving = member.VelocityX != 0f;
        if (moving && grappling is null && member.NpcHealth?.RegenTime >= 299f &&
            buffs?.Any(static b => b == VanillaBuffIds.Poisoned || b == VanillaBuffIds.OnFire ||
                b == VanillaBuffIds.CursedInferno) != true)
            supported = false;
        if (world is { VampireSeed: true }) supported = false;
        if (serial == ulong.MaxValue || inventorySerial == ulong.MaxValue || serial != membership.Serial ||
            inventorySerial != inventory.Serial || !membership.TryGet(member.Connection, out var current) ||
            !ReferenceEquals(current, member) || current.CaptureSnapshot() != before)
            return false;
        bool currentProfileCaptured = transferProfiles.TryCapture(member.Connection, out var currentAppearance, out var currentEquipment, out var currentBuffs);
        if (currentProfileCaptured != profileCaptured ||
            currentAppearance != appearance || !equipment.AsSpan().SequenceEqual(currentEquipment) ||
            (buffs is null) != (currentBuffs is null) ||
            (buffs is not null && !buffs.AsSpan().SequenceEqual(currentBuffs)))
            return false;
        for (int i = 0; i < sectionCount; i++)
            if (worldTiles!.GetSectionVersion(sections[i]) != versions[i]) return false;

        next = VanillaRemotePlayerHealth1458.Step(member.NpcHealth, maximum ?? -1, outside, ghost,
            member.IsDead, supported, world?.ExpertMode ?? false, moving && grappling != true, regeneration,
            buffs?.Any(static b => b == VanillaBuffIds.Poisoned) == true,
            buffs?.Any(static b => b == VanillaBuffIds.OnFire) == true,
            buffs?.Any(static b => b == VanillaBuffIds.CursedInferno) == true);
        return true;
    }

    private static void ApplyNpcHealthSpawn(RuntimePlayerMember player, byte context)
    {
        if (player.NpcHealth is not { } state) return;
        if (!state.SourceProfileKnown)
        {
            player.NpcHealth = null;
            return;
        }
        // Player.Spawn preserves regeneration clocks. SpawningIntoWorld can preserve a dead actor;
        // the existing respawn command supplies that lane through RespawnTimer/IsDead.
        if (context == 1 && player.IsDead)
        {
            player.NpcLifeCurrent = true;
            return;
        }
        if (state.Life <= 0)
        {
            if (player.BaseLifeMax is not { } baseline || player.DerivedLifeMax is not { } derived)
            {
                player.NpcHealth = null;
                return;
            }
            state = state with { Life = Math.Max(100, Math.Max(baseline, derived) / 2) };
        }
        player.NpcHealth = state;
        player.NpcLifeCurrent = true;
    }

    private static void ApplyNpcHealthHurt(RuntimePlayerMember player, short previousReportedLife)
    {
        // Accepted owned Hurt resets only the natural regeneration timer, not the accumulated count.
        if (player.NpcHealth is { } state)
        {
            if (state.Life != previousReportedLife)
            {
                player.NpcHealth = null;
                player.NpcLifeCurrent = false;
                return;
            }
            player.NpcHealth = state with { Life = player.Life, RegenTime = 0f };
        }
    }
}
