using System.Collections.ObjectModel;
using TerraRuntime.Application.Bots;
using TerraRuntime.Application.Operations;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Npcs;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.Application.TerminalUI;

/// <summary>Operator editor for one runtime-owned primary-world bot.</summary>
internal sealed class BotSettingsWindow : Window
{
    private readonly RuntimeBotOperations operations;
    private readonly RuntimeBotSnapshot bot;
    private readonly RuntimePlayerSnapshot[] players;
    private readonly NpcPresetOption[] npcPresets;
    private readonly CyclingDropDownList body;
    private readonly CyclingDropDownList npcPreset;
    private readonly CyclingDropDownList weapon;
    private readonly CyclingDropDownList flight;
    private readonly CyclingDropDownList target;
    private readonly CyclingDropDownList mode;
    private readonly Label status;
    private readonly Label feedback;
    private readonly View[] playerOnlyViews;
    private readonly View[] npcOnlyViews;

    public BotSettingsWindow(
        RuntimeBotSnapshot bot,
        RuntimePlayerSnapshot[] players,
        RuntimeBotOperations operations)
    {
        this.bot = bot;
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        npcPresets = BuildNpcPresets();

        Title = $"Bot #{bot.Id} · {bot.Name}";
        Width = 72;
        Height = 21;
        X = Pos.Center();
        Y = Pos.Center();
        SchemeName = "Base";

        body = CreateDropDown(24, 1, 28, Enum.GetNames<RuntimeBotBodyKind>());
        npcPreset = CreateDropDown(24, 3, 38, npcPresets.Select(static value => value.Label));
        weapon = CreateDropDown(24, 5, 28, Enum.GetNames<RuntimeBotWeaponPolicy>());
        flight = CreateDropDown(24, 7, 28, ["On", "Off"]);
        target = CreateDropDown(24, 9, 38, BuildTargetLabels(players));
        mode = CreateDropDown(24, 11, 28, Enum.GetNames<RuntimeBotMode>());
        status = new Label { X = 1, Y = 13, Width = Dim.Fill(1), SchemeName = "Base" };
        feedback = new Label { X = 1, Y = 17, Width = Dim.Fill(1), Height = 2, SchemeName = "Base" };

        Label npcPresetLabel = LabelAt("NPC type", 3);
        Label weaponLabel = LabelAt("Weapon policy", 5);
        Label flightLabel = LabelAt("Mobility accessories", 7);
        npcOnlyViews = [npcPresetLabel, npcPreset];
        playerOnlyViews =
        [
            weaponLabel, weapon,
            flightLabel, flight
        ];

        SelectText(body, bot.Configuration.Body.ToString());
        SelectNpc(bot.Configuration.NpcType);
        SelectText(weapon, bot.Configuration.WeaponPolicy.ToString());
        SelectText(flight, bot.Configuration.FlightEnabled ? "On" : "Off");
        SelectText(mode, bot.Configuration.Mode.ToString());
        SelectTarget(bot.Configuration.Target);
        RefreshStatus(bot);

        var apply = new Button { X = 24, Y = 15, Text = "Apply", SchemeName = "Base" };
        var close = new Button { X = 36, Y = 15, Text = "Close", SchemeName = "Base" };
        apply.Accepted += (_, _) => Apply();
        close.Accepted += (_, _) => CloseRequested?.Invoke();

        Add(
            LabelAt("Bot type", 1), body,
            npcPresetLabel, npcPreset,
            weaponLabel, weapon,
            flightLabel, flight,
            LabelAt("Follow target", 9), target,
            LabelAt("Mode", 11), mode,
            status,
            apply,
            close,
            feedback);

        body.ValueChanged += (_, _) => UpdateBodySpecificFields();
        UpdateBodySpecificFields();
    }

    public event Action? CloseRequested;

    internal int BotId => bot.Id;
    internal string FeedbackTextForSmoke => feedback.Text?.ToString() ?? string.Empty;
    internal bool NpcPresetVisibleForSmoke => npcPreset.Visible;
    internal bool PlayerFieldsVisibleForSmoke => weapon.Visible && flight.Visible;
    internal int NpcPresetCountForSmoke => npcPresets.Length;

    private void Apply()
    {
        if (!Enum.TryParse(body.Text?.ToString(), ignoreCase: false, out RuntimeBotBodyKind bodyValue) ||
            !Enum.TryParse(weapon.Text?.ToString(), ignoreCase: false, out RuntimeBotWeaponPolicy weaponValue) ||
            !Enum.TryParse(mode.Text?.ToString(), ignoreCase: false, out RuntimeBotMode modeValue) ||
            !TryParseToggle(flight, out bool flightValue))
        {
            feedback.Text = "bot: invalid selection";
            return;
        }

        NpcTypeId npcType = default;
        if (bodyValue == RuntimeBotBodyKind.Npc && !TryResolveNpcPreset(out npcType))
        {
            feedback.Text = "bot: NPC type requires a source-backed hostile preset";
            return;
        }
        if (bodyValue == RuntimeBotBodyKind.Npc)
        {
            weaponValue = RuntimeBotWeaponPolicy.Automatic;
            flightValue = false;
        }

        RuntimeBotTarget targetValue = ResolveTarget();
        if (modeValue is RuntimeBotMode.Follow or RuntimeBotMode.Guard && !targetValue.IsAssigned)
        {
            feedback.Text = "bot: Follow/Guard requires a live primary-world target";
            return;
        }

        var configuration = new RuntimeBotConfiguration(
            modeValue,
            targetValue,
            Body: bodyValue,
            NpcType: npcType,
            WeaponPolicy: weaponValue,
            FlightEnabled: flightValue);
        try
        {
            RuntimeBotSnapshot? updated = operations.ConfigureAsync(bot.Id, configuration)
                .AsTask().GetAwaiter().GetResult();
            if (updated is not RuntimeBotSnapshot snapshot)
            {
                feedback.Text = "bot: configuration rejected";
                return;
            }

            RefreshStatus(snapshot);
            feedback.Text = "bot: configuration applied";
        }
        catch (Exception exception)
        {
            feedback.Text = $"bot: configuration failed: {exception.Message}";
        }
    }

    private void UpdateBodySpecificFields()
    {
        bool npc = Enum.TryParse(body.Text?.ToString(), ignoreCase: false, out RuntimeBotBodyKind selected) &&
            selected == RuntimeBotBodyKind.Npc;
        foreach (View view in npcOnlyViews)
            view.Visible = npc;
        foreach (View view in playerOnlyViews)
            view.Visible = !npc;
    }

    private RuntimeBotTarget ResolveTarget()
    {
        string selected = target.Text?.ToString() ?? string.Empty;
        if (string.Equals(selected, "<none>", StringComparison.Ordinal))
            return default;

        for (int i = 0; i < players.Length; i++)
        {
            RuntimePlayerSnapshot player = players[i];
            if (!string.Equals(selected, FormatTarget(player), StringComparison.Ordinal))
                continue;
            return new RuntimeBotTarget(
                new PlayerHandle(new PlayerSlotId(player.Slot), new PlayerSessionGeneration(player.Generation)),
                player.Name);
        }
        return default;
    }

    private bool TryResolveNpcPreset(out NpcTypeId type)
    {
        string selected = npcPreset.Text?.ToString() ?? string.Empty;
        for (int i = 0; i < npcPresets.Length; i++)
        {
            if (!string.Equals(selected, npcPresets[i].Label, StringComparison.Ordinal))
                continue;
            type = npcPresets[i].Type;
            return true;
        }
        type = default;
        return false;
    }

    private void SelectNpc(NpcTypeId selected)
    {
        if (selected.IsAssigned)
        {
            for (int i = 0; i < npcPresets.Length; i++)
            {
                if (npcPresets[i].Type != selected)
                    continue;
                SelectText(npcPreset, npcPresets[i].Label);
                return;
            }
        }
        if (npcPresets.Length > 0)
            SelectText(npcPreset, npcPresets[0].Label);
    }

    private void SelectTarget(RuntimeBotTarget selected)
    {
        if (!selected.IsAssigned)
        {
            SelectText(target, "<none>");
            return;
        }

        for (int i = 0; i < players.Length; i++)
        {
            RuntimePlayerSnapshot player = players[i];
            var handle = new PlayerHandle(new PlayerSlotId(player.Slot), new PlayerSessionGeneration(player.Generation));
            if (handle == selected.Player)
            {
                SelectText(target, FormatTarget(player));
                return;
            }
        }
        SelectText(target, "<none>");
    }

    private void RefreshStatus(RuntimeBotSnapshot snapshot)
    {
        string bodyText = snapshot.Configuration.Body == RuntimeBotBodyKind.Npc
            ? $"NpcBot #{snapshot.Configuration.NpcType.Value}"
            : "PlayerBot";
        string targetState = snapshot.Configuration.Target.IsAssigned
            ? snapshot.TargetAvailable ? snapshot.Configuration.Target.DisplayName : $"{snapshot.Configuration.Target.DisplayName} (offline)"
            : "none";
        status.Text = $"{bodyText} · Target: {targetState} · PvP {(snapshot.PvpEnabled ? "ON" : "OFF")} · teleports {snapshot.TeleportCount}";
    }

    private static CyclingDropDownList CreateDropDown(int x, int y, int width, IEnumerable<string> values) => new()
    {
        X = x,
        Y = y,
        Width = width,
        ReadOnly = true,
        Source = new ListWrapper<string>(new ObservableCollection<string>(values.ToArray())),
        SchemeName = "Base"
    };

    private static Label LabelAt(string text, int y) => new()
    {
        X = 1,
        Y = y,
        Text = text,
        SchemeName = "Base"
    };

    private static string[] BuildTargetLabels(RuntimePlayerSnapshot[] players)
    {
        var labels = new string[players.Length + 1];
        labels[0] = "<none>";
        for (int i = 0; i < players.Length; i++)
            labels[i + 1] = FormatTarget(players[i]);
        return labels;
    }

    private static NpcPresetOption[] BuildNpcPresets()
    {
        var result = new List<NpcPresetOption>(VanillaNpcAiCoverageCatalog.Count);
        var admittedTypes = new HashSet<int>();
        foreach (VanillaNpcAiCoverage coverage in VanillaNpcAiCoverageCatalog.All)
        {
            if (!VanillaBotNpcPresetCatalog1458.IsSupported(coverage.Type) ||
                !VanillaNpcDefinitionCatalog.TryGet(coverage.Type, out VanillaNpcDefinition definition) ||
                !admittedTypes.Add(coverage.Type.Value))
            {
                continue;
            }
            result.Add(new NpcPresetOption(
                coverage.Type,
                $"#{coverage.Type.Value} {definition.BehaviorFamily}"));
        }
        return result.OrderBy(static value => value.Type.Value).ToArray();
    }

    private static bool TryParseToggle(DropDownList list, out bool value)
    {
        string selected = list.Text?.ToString() ?? string.Empty;
        if (string.Equals(selected, "On", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }
        if (string.Equals(selected, "Off", StringComparison.Ordinal))
        {
            value = false;
            return true;
        }
        value = false;
        return false;
    }

    private static string FormatTarget(RuntimePlayerSnapshot player) => $"#{player.Slot} {player.Name}";

    private static void SelectText(DropDownList list, string value) => list.Text = value;

    private readonly record struct NpcPresetOption(NpcTypeId Type, string Label);
}
