using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

using static TerraRuntime.Application.Bots.BotPolicy;

namespace TerraRuntime.Application.Bots;

/// <summary>Owns operator PlayerBot create/configure/despawn and schedules composed controllers on the world writer.</summary>
internal sealed class RuntimeBotAuthority
{
    private readonly ServerPlayerAuthority serverPlayers;
    private readonly PlayerAuthority players;
    private readonly RuntimePlayerSnapshotLookup playerSnapshots;
    private readonly NpcAuthority npcs;
    private readonly ProjectileAuthority projectiles;
    private readonly WorldItemAuthority worldItems;
    private readonly WorldTileStore worldTiles;
    private readonly WorldTileAuthority tileAuthority;
    private readonly RuntimeBotTelemetry telemetry;
    private readonly Func<long> tickProvider;
    private readonly Random botRandom;
    private readonly float spawnX;
    private readonly float spawnY;
    private readonly Dictionary<int, BotState> bots = [];
    private readonly RuntimeBotResourceLeases leases = new();
    private readonly WorldRuntimeIdentity world;
    private readonly IRuntimeBotBrain brain;
    private int nextId = 1;
    private long lastTelemetryTick = long.MinValue;

    public RuntimeBotAuthority(
        ServerPlayerAuthority serverPlayers,
        PlayerAuthority players,
        RuntimePlayerSnapshotLookup playerSnapshots,
        NpcAuthority npcs,
        ProjectileAuthority projectiles,
        WorldItemAuthority worldItems,
        WorldTileStore worldTiles,
        WorldTileAuthority tileAuthority,
        RuntimeBotTelemetry telemetry,
        Func<long> tickProvider,
        float spawnX,
        float spawnY,
        Random? botRandom = null,
        WorldRuntimeIdentity world = default,
        IRuntimeBotBrain? brain = null)
    {
        if (!world.IsAssigned) throw new ArgumentException("Bot world identity must be assigned.", nameof(world));
        this.world = world;
        this.brain = brain ?? new DeterministicRuntimeBotBrain();
        this.serverPlayers = serverPlayers ?? throw new ArgumentNullException(nameof(serverPlayers));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.playerSnapshots = playerSnapshots ?? throw new ArgumentNullException(nameof(playerSnapshots));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.worldTiles = worldTiles ?? throw new ArgumentNullException(nameof(worldTiles));
        this.tileAuthority = tileAuthority ?? throw new ArgumentNullException(nameof(tileAuthority));
        this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        this.botRandom = botRandom ?? Random.Shared;
        if (!float.IsFinite(spawnX) || !float.IsFinite(spawnY))
            throw new ArgumentOutOfRangeException(nameof(spawnX));
        this.spawnX = spawnX;
        this.spawnY = spawnY;
    }

    public bool TryApply(RuntimeCommand command)
    {
        switch (command)
        {
            case RuntimeBotCreateCommand create:
                create.Completion.TrySetResult(Create(create.Request));
                return true;
            case RuntimeBotConfigureCommand configure:
                configure.Completion.TrySetResult(Configure(configure.Id, configure.Configuration));
                return true;
            case RuntimeBotDespawnCommand despawn:
                despawn.Completion.TrySetResult(Despawn(despawn.Id));
                return true;
            default:
                return false;
        }
    }

    public void Tick()
    {
        long tick = tickProvider();
        leases.Cleanup(tick);
        foreach (BotState bot in bots.Values)
        {
            if (bot.Configuration.Target.IsAssigned && !playerSnapshots.TryGetPlayer(bot.Configuration.Target.Player, out _))
            {
                bot.Controller.Cancel(RuntimeBotActionCancelReason.TargetChanged);
                bot.Configuration = bot.Configuration with { Target = default, Mode = RuntimeBotMode.Idle };
                bot.GoalGeneration = checked(bot.GoalGeneration + 1);
                ResetBehaviorState(bot, tick);
            }
            bot.Controller.Tick(tick);
        }
        PublishTelemetry(tick, force: false);
    }

    private RuntimeBotSnapshot? Create(RuntimeBotCreateRequest request)
    {
        if (!request.IsValid)
            return null;

        int id = nextId;
        if (id <= 0 || id == int.MaxValue)
            return null;

        var serverId = new ServerPlayerId($"bot:{id}");
        string name = RuntimeBotNameGenerator.Generate(
            botRandom,
            candidate => bots.Values.Any(existing => string.Equals(existing.Name, candidate, StringComparison.OrdinalIgnoreCase)));
        RuntimePlayerBotLoadout1458 loadout = RuntimePlayerBotLoadoutCatalog1458.Pick(botRandom);
        var configuration = new RuntimeBotConfiguration(
            RuntimeBotMode.Idle,
            default,
            WeaponPolicy: RuntimeBotWeaponPolicy.Automatic,
            FlightEnabled: true,
            GodMode: false);
        var state = new BotState(
            id,
            serverId,
            name,
            configuration,
            loadout,
            CreateVisualIdentity(botRandom.Next()),
            CreatePersonality(botRandom.Next()),
            tickProvider());

        bool created = TryCreatePlayerActor(state, spawnX, spawnY);
        if (!created)
            return null;

        nextId++;
        bots.Add(id, state);
        state.Controller = CreateController(state);
        PublishTelemetry(tickProvider(), force: true);
        return Capture(state);
    }

    private RuntimeBotSnapshot? Configure(int id, RuntimeBotConfiguration configuration)
    {
        if (!bots.TryGetValue(id, out BotState? bot) || !IsValidConfiguration(configuration) || !bot.OwnsCurrentActor(serverPlayers))
            return null;

        if (RequiresTarget(configuration.Mode) && !configuration.Target.IsAssigned)
            return null;
        if (configuration.Target.IsAssigned &&
            (!playerSnapshots.TryGetPlayer(configuration.Target.Player, out PlayerStateSnapshot target) ||
             target.Player != configuration.Target.Player ||
             target.Player == bot.Player))
        {
            return null;
        }

        if (!ApplyPlayerPresentation(bot, configuration) ||
            !EnsurePlayerLoadout(bot, configuration, addStarterAmmo: true) ||
            !serverPlayers.SetGodMode(bot.ServerPlayerId, configuration.GodMode))
        {
            // A replacement actor has already crossed an authoritative boundary. Do not attempt a lossy reverse
            // resurrection here; reject only before replacement wherever possible, and treat this as an invariant.
            throw new InvalidOperationException("A live bot player rejected its source-backed presentation/loadout.");
        }

        bot.Controller.Cancel(RuntimeBotActionCancelReason.Reconfigured);
        bot.GoalGeneration = checked(bot.GoalGeneration + 1);
        bot.Configuration = configuration;
        ResetBehaviorState(bot, tickProvider());
        if (configuration.Mode == RuntimeBotMode.Idle)
            StopActor(bot);
        if (serverPlayers.TryGet(bot.Player, out PlayerStateSnapshot configuredPlayer))
            bot.PvpEnabled = configuredPlayer.Hostile;

        PublishTelemetry(tickProvider(), force: true);
        return Capture(bot);
    }

    private bool Despawn(int id)
    {
        if (!bots.Remove(id, out BotState? bot))
            return false;

        bot.Controller.Cancel(RuntimeBotActionCancelReason.Despawned);
        leases.ReleaseBot(new(bot.Id, bot.Player, world));
        bool despawned = bot.OwnsCurrentActor(serverPlayers) && serverPlayers.Despawn(bot.ServerPlayerId);
        PublishTelemetry(tickProvider(), force: true);
        return despawned;
    }

    private bool TryCreatePlayerActor(BotState bot, float x, float y) =>
        TryCreatePlayerActor(bot, x, y, mutateHandles: true, out _);

    private bool TryCreatePlayerActor(BotState bot, float x, float y, bool mutateHandles, out PlayerHandle player)
    {
        player = default;
        ServerPlayerCreateResult created = serverPlayers.Create(bot.ServerPlayerId, x, y);
        if (created.Status != ServerPlayerCreateStatus.Created || !created.Player.IsAssigned)
            return false;

        PlayerHandle previous = bot.Player;
        bot.Player = created.Player;
        bool initialized = ApplyPlayerPresentation(bot, bot.Configuration) &&
            serverPlayers.SetVitals(
                bot.ServerPlayerId,
                new ServerPlayerVitalsState(
                    MaximumVanillaPermanentLife,
                    MaximumVanillaPermanentLife,
                    MaximumVanillaPermanentMana,
                    MaximumVanillaPermanentMana)) &&
            serverPlayers.SetGodMode(bot.ServerPlayerId, bot.Configuration.GodMode) &&
            serverPlayers.SetHostile(bot.ServerPlayerId, hostile: false) &&
            serverPlayers.SetMovementIntent(bot.ServerPlayerId, ServerPlayerMovementIntent.Stop()) &&
            EnsurePlayerLoadout(bot, bot.Configuration, addStarterAmmo: true) &&
            EnsureStarterConsumables(bot.ServerPlayerId);
        if (!initialized)
        {
            bot.Player = previous;
            _ = serverPlayers.Despawn(bot.ServerPlayerId);
            return false;
        }

        player = created.Player;
        if (!mutateHandles)
            bot.Player = previous;
        return true;
    }

    private bool ApplyPlayerPresentation(BotState bot, RuntimeBotConfiguration configuration)
    {
        if (!bot.Player.IsAssigned)
            return false;
        PlayerBotVisualIdentity visual = bot.VisualIdentity;
        ServerPlayerAppearanceState appearance = CreateAppearance(bot.Name, in visual);
        if (!serverPlayers.SetAppearance(bot.ServerPlayerId, in appearance))
            return false;

        RuntimePlayerBotLoadout1458 loadout = bot.Loadout;
        ItemTypeId firstAccessory = configuration.FlightEnabled ? loadout.Accessory1 : VanillaItemIds.None;
        return SetArmorSlot(bot.ServerPlayerId, VanillaPlayerItemSlotCatalog.ArmorStart, loadout.FunctionalHead) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 1)), loadout.FunctionalBody) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 2)), loadout.FunctionalLegs) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3)), firstAccessory) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 4)), loadout.Accessory2) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 5)), loadout.Accessory3) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 6)), loadout.Accessory4) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 7)), loadout.Accessory5) &&
               SetArmorSlot(bot.ServerPlayerId, VanillaPlayerItemSlotCatalog.VanityArmorStart, loadout.VanityHead) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.VanityArmorStart + 1)), loadout.VanityBody) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.VanityArmorStart + 2)), loadout.VanityLegs);
    }

    private bool EnsurePlayerLoadout(BotState bot, RuntimeBotConfiguration configuration, bool addStarterAmmo)
    {
        if (!bot.Player.IsAssigned)
            return false;

        ItemTypeId firstWeapon = configuration.WeaponPolicy switch
        {
            RuntimeBotWeaponPolicy.Melee => bot.Loadout.MeleeWeapon,
            RuntimeBotWeaponPolicy.Gun => bot.Loadout.GunWeapon,
            RuntimeBotWeaponPolicy.Bow => bot.Loadout.BowWeapon,
            RuntimeBotWeaponPolicy.Automatic => bot.Loadout.MeleeWeapon,
            _ => VanillaItemIds.None
        };
        if (firstWeapon.IsNone || !serverPlayers.SetItem(
                bot.ServerPlayerId,
                new ServerPlayerItemState(MeleeWeaponSlot, firstWeapon, 1, VanillaPrefixIds.None, 0)))
        {
            return false;
        }

        ItemTypeId secondWeapon = configuration.WeaponPolicy == RuntimeBotWeaponPolicy.Automatic
            ? bot.Loadout.BowWeapon
            : VanillaItemIds.None;
        ItemTypeId thirdWeapon = configuration.WeaponPolicy == RuntimeBotWeaponPolicy.Automatic
            ? bot.Loadout.GunWeapon
            : VanillaItemIds.None;
        if (!SetInventorySlot(bot.ServerPlayerId, BowWeaponSlot, secondWeapon) ||
            !SetInventorySlot(bot.ServerPlayerId, GunWeaponSlot, thirdWeapon) ||
            !serverPlayers.SetHeldItem(bot.ServerPlayerId, MeleeWeaponSlot, useItem: false))
        {
            return false;
        }

        if (!addStarterAmmo || configuration.WeaponPolicy == RuntimeBotWeaponPolicy.Melee)
            return true;

        if (configuration.WeaponPolicy == RuntimeBotWeaponPolicy.Automatic)
        {
            return EnsureStarterAmmo(bot.ServerPlayerId, bot.Loadout.ArrowAmmo) &&
                   EnsureStarterAmmo(bot.ServerPlayerId, bot.Loadout.BulletAmmo);
        }

        return TryResolveRangedLoadout(bot, configuration.WeaponPolicy, out _, out ItemTypeId ammoType, out _, out _) &&
               EnsureStarterAmmo(bot.ServerPlayerId, ammoType);
    }

    private bool EnsureStarterConsumables(ServerPlayerId id) =>
        EnsureStarterStack(id, 3, VanillaItemIds.SuperHealingPotion, StarterPotionStack) &&
        EnsureStarterStack(id, 4, VanillaItemIds.GreaterManaPotion, StarterPotionStack) &&
        EnsureStarterStack(id, MirrorSlot, VanillaItemIds.MagicMirror, 1) &&
        EnsureStarterStack(id, 6, VanillaItemIds.VortexPickaxe, 1);

    private bool EnsureStarterStack(ServerPlayerId id, short slot, ItemTypeId itemType, short stack)
    {
        if (!serverPlayers.TryGetItem(id, slot, out ServerPlayerItemState current))
            return false;
        if (!current.IsEmpty)
            return current.ItemType == itemType && current.Stack > 0;
        var item = new ServerPlayerItemState(slot, itemType, stack, VanillaPrefixIds.None, 0);
        return serverPlayers.SetItem(id, in item);
    }

    private bool EnsureStarterAmmo(ServerPlayerId id, ItemTypeId ammoType)
    {
        if (RuntimeBotInventory.TryFindAmmoSlot(serverPlayers, id, ammoType, out _, out _))
            return true;

        for (short slot = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
             slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive;
             slot++)
        {
            if (!serverPlayers.TryGetItem(id, slot, out ServerPlayerItemState current))
                return false;
            if (!current.IsEmpty)
                continue;
            var starter = new ServerPlayerItemState(slot, ammoType, StarterAmmoStack, VanillaPrefixIds.None, 0);
            return serverPlayers.SetItem(id, in starter);
        }

        return false;
    }

    private bool SetInventorySlot(ServerPlayerId id, short slot, ItemTypeId item)
    {
        ServerPlayerItemState state = item.IsNone
            ? new ServerPlayerItemState(slot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
            : new ServerPlayerItemState(slot, item, 1, VanillaPrefixIds.None, 0);
        return serverPlayers.SetItem(id, in state);
    }

    private bool SetArmorSlot(ServerPlayerId id, short slot, ItemTypeId item)
    {
        ServerPlayerItemState state = item.IsNone
            ? new ServerPlayerItemState(slot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
            : new ServerPlayerItemState(slot, item, 1, VanillaPrefixIds.None, 0);
        return serverPlayers.SetItem(id, in state);
    }

    private static bool IsValidConfiguration(RuntimeBotConfiguration configuration) =>
        Enum.IsDefined(configuration.Mode) && Enum.IsDefined(configuration.WeaponPolicy) && Enum.IsDefined(configuration.MiningOre);

    private static void ResetBehaviorState(BotState bot, long tick)
    {
        bot.TargetAvailable = false;
        bot.PvpEnabled = false;
        bot.IsStuck = false;
        bot.LastDistance = float.PositiveInfinity;
        bot.LastProgressTick = tick;
        bot.NextAttackTick = 0;
        bot.UseItemUntilTick = 0;
        bot.MirrorStartedAtTick = -1;
        bot.FlightDecisionUntilTick = 0;
        bot.TraversalUntilTick = bot.NextTraversalSearchTick = 0;
        bot.LockedGuardNpc = default;
        bot.LockedGuardPlayer = default;
        bot.GuardTargetLockUntilTick = 0;
        bot.GuardRepositionUntilTick = 0;
    }

    private void StopActor(BotState bot)
    {
        if (!bot.Player.IsAssigned)
            return;
        _ = serverPlayers.SetMovementIntent(bot.ServerPlayerId, ServerPlayerMovementIntent.Stop());
        _ = serverPlayers.SetHostile(bot.ServerPlayerId, hostile: false);
        if (serverPlayers.TryGet(bot.Player, out PlayerStateSnapshot player))
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, player.SelectedItem, useItem: false);
    }

    private static ServerPlayerAppearanceState CreateAppearance(string name, in PlayerBotVisualIdentity visual)
    {
        return new ServerPlayerAppearanceState(
            SkinVariant: visual.SkinVariant,
            VoiceVariant: visual.VoiceVariant,
            VoicePitchOffset: visual.VoicePitchOffset,
            Hair: visual.Hair,
            Name: name,
            HairDye: 0,
            HideVisibleAccessory: 0,
            HideMisc: 0,
            HairColor: visual.HairColor,
            SkinColor: visual.SkinColor,
            EyeColor: visual.EyeColor,
            ShirtColor: visual.ShirtColor,
            UnderShirtColor: visual.UnderShirtColor,
            PantsColor: visual.PantsColor,
            ShoeColor: visual.ShoeColor,
            DifficultyFlags: 0,
            TorchAndCartFlags: 0,
            ConsumableUnlockFlags: 0);
    }

    private static PlayerBotPersonality CreatePersonality(int id)
    {
        uint state = Mix(unchecked((uint)id) ^ 0xA511E9B3u);
        int ring = (id - 1) / 2;
        float side = (id & 1) == 0 ? 1f : -1f;
        float radialJitter = Next(ref state, 0, 17);
        float offsetX = side * (MinimumPlayerEscortOffsetPixels + ring % 5 * 32f + radialJitter);
        float offsetY = -72f - Next(ref state, 0, 49);
        long phase = Next(ref state, 0, checked((int)EscortWanderPeriodTicks));
        return new PlayerBotPersonality(offsetX, offsetY, phase);
    }

    private static PlayerBotVisualIdentity CreateVisualIdentity(int id)
    {
        uint state = Mix(unchecked((uint)id) ^ 0x6D2B79F5u);
        int armor = Next(ref state, 0, 6);
        (ItemTypeId head, ItemTypeId body, ItemTypeId legs) = armor switch
        {
            1 => (VanillaItemIds.WoodHelmet, VanillaItemIds.WoodBreastplate, VanillaItemIds.WoodGreaves),
            2 => (VanillaItemIds.CopperHelmet, VanillaItemIds.CopperChainmail, VanillaItemIds.CopperGreaves),
            3 => (VanillaItemIds.IronHelmet, VanillaItemIds.IronChainmail, VanillaItemIds.IronGreaves),
            4 => (VanillaItemIds.SilverHelmet, VanillaItemIds.SilverChainmail, VanillaItemIds.SilverGreaves),
            5 => (VanillaItemIds.GoldHelmet, VanillaItemIds.GoldChainmail, VanillaItemIds.GoldGreaves),
            _ => (VanillaItemIds.None, VanillaItemIds.None, VanillaItemIds.None)
        };

        (PlayerRgbColor shirt, PlayerRgbColor undershirt, PlayerRgbColor pants, PlayerRgbColor shoes) =
            Next(ref state, 0, 6) switch
            {
                0 => (new PlayerRgbColor(30, 110, 170), new PlayerRgbColor(180, 180, 180), new PlayerRgbColor(45, 70, 120), new PlayerRgbColor(65, 45, 30)),
                1 => (new PlayerRgbColor(54, 110, 63), new PlayerRgbColor(103, 148, 91), new PlayerRgbColor(62, 74, 57), new PlayerRgbColor(43, 35, 29)),
                2 => (new PlayerRgbColor(145, 45, 52), new PlayerRgbColor(92, 28, 36), new PlayerRgbColor(57, 48, 54), new PlayerRgbColor(36, 30, 33)),
                3 => (new PlayerRgbColor(110, 110, 110), new PlayerRgbColor(72, 72, 72), new PlayerRgbColor(48, 48, 48), new PlayerRgbColor(28, 28, 28)),
                4 => (new PlayerRgbColor(120, 72, 168), new PlayerRgbColor(203, 170, 228), new PlayerRgbColor(50, 43, 95), new PlayerRgbColor(40, 28, 62)),
                _ => (new PlayerRgbColor(205, 128, 34), new PlayerRgbColor(244, 211, 121), new PlayerRgbColor(58, 92, 105), new PlayerRgbColor(48, 34, 25))
            };
        PlayerRgbColor skin = Next(ref state, 0, 5) switch
        {
            0 => new(255, 215, 180),
            1 => new(232, 190, 151),
            2 => new(198, 142, 105),
            3 => new(141, 92, 65),
            _ => new(92, 61, 48)
        };
        var hair = new PlayerRgbColor(
            checked((byte)Next(ref state, 30, 221)),
            checked((byte)Next(ref state, 25, 196)),
            checked((byte)Next(ref state, 20, 171)));
        var eyes = new PlayerRgbColor(
            checked((byte)Next(ref state, 40, 181)),
            checked((byte)Next(ref state, 60, 201)),
            checked((byte)Next(ref state, 70, 221)));
        return new PlayerBotVisualIdentity(
            checked((byte)Next(ref state, 0, VanillaPlayerAppearanceNormalizer.PlayerVariantCount)),
            checked((byte)Next(ref state, 1, 5)),
            (Next(ref state, -20, 21) / 100f),
            checked((byte)Next(ref state, 0, VanillaPlayerAppearanceNormalizer.HairCount)),
            hair,
            skin,
            eyes,
            shirt,
            undershirt,
            pants,
            shoes,
            head,
            body,
            legs);
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        return value ^ (value >> 16);
    }

    private static int Next(ref uint state, int minimumInclusive, int maximumExclusive)
    {
        state = Mix(state + 0x9E3779B9u);
        return minimumInclusive + (int)(state % checked((uint)(maximumExclusive - minimumInclusive)));
    }

    private void PublishTelemetry(long tick, bool force)
    {
        if (!force && tick - lastTelemetryTick < TelemetryPeriodTicks)
            return;
        lastTelemetryTick = tick;
        RuntimeBotSnapshot[] snapshots = new RuntimeBotSnapshot[bots.Count];
        int index = 0;
        foreach (BotState bot in bots.Values.OrderBy(static value => value.Id))
            snapshots[index++] = Capture(bot);
        telemetry.Publish(snapshots);
    }

    private static RuntimeBotSnapshot Capture(BotState bot) => new(
        bot.Id,
        bot.ServerPlayerId,
        bot.Player,
        bot.Name,
        bot.Configuration,
        bot.TargetAvailable,
        bot.PvpEnabled,
        bot.IsStuck,
        bot.IsDead,
        bot.TeleportCount,
        DateTimeOffset.UtcNow)
    {
        CurrentAction = bot.Controller.Current,
        RecentActionResult = bot.Controller.RecentResult,
        Observation = bot.Controller.Observation
    };

    private RuntimeBotController CreateController(BotState bot)
    {
        var inventory = new RuntimeBotInventory(bot, serverPlayers, worldItems, leases, world);
        var combat = new RuntimeBotCombat(bot, serverPlayers, npcs, projectiles, worldTiles, inventory, bots.Values, world);
        var navigation = new RuntimeBotNavigation(bot, serverPlayers, worldTiles, combat, bots.Values, leases, world);
        var mining = new RuntimeBotMining(bot, serverPlayers, worldTiles, tileAuthority, navigation, leases, world);
        var perception = new RuntimeBotPerception(bot, serverPlayers, players, playerSnapshots, npcs,
            inventory, navigation, bots.Values, world, worldTiles, mining);
        var interaction = new RuntimeBotWorldInteraction(bot, serverPlayers, worldTiles, tileAuthority, world, mining);
        return new(bot, serverPlayers, perception, brain,
            new RuntimeBotActionExecutor(leases), navigation, combat, inventory, interaction);
    }

    internal static bool RequiresTarget(RuntimeBotMode mode) =>
        mode is RuntimeBotMode.Follow or RuntimeBotMode.Guard or RuntimeBotMode.ReturnToPlayer;


}
