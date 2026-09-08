using System.Collections.ObjectModel;
using TerraRuntime.Application.Bots;
using TerraRuntime.Application.Operations;
using TerraRuntime.Contracts.Runtime;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.Application.TerminalUI;

/// <summary>Operator editor for one runtime-owned fake player.</summary>
internal sealed class BotSettingsWindow : Window
{
    private readonly RuntimeBotOperations operations;
    private readonly RuntimeBotSnapshot bot;
    private readonly RuntimePlayerSnapshot[] players;
    private readonly CyclingDropDownList weapon;
    private readonly CyclingDropDownList flight;
    private readonly CyclingDropDownList godMode;
    private readonly CyclingDropDownList target;
    private readonly CyclingDropDownList mode;
    private readonly Label status;
    private readonly Label feedback;

    public BotSettingsWindow(
        RuntimeBotSnapshot bot,
        RuntimePlayerSnapshot[] players,
        RuntimeBotOperations operations)
    {
        this.bot = bot;
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));

        Title = $"Bot #{bot.Id} · {bot.Name}";
        Width = 72;
        Height = 19;
        X = Pos.Center();
        Y = Pos.Center();
        SchemeName = "Base";

        weapon = CreateDropDown(24, 1, 28, Enum.GetNames<RuntimeBotWeaponPolicy>());
        flight = CreateDropDown(24, 3, 28, ["On", "Off"]);
        godMode = CreateDropDown(24, 5, 28, ["Off", "On"]);
        target = CreateDropDown(24, 7, 38, BuildTargetLabels(players));
        mode = CreateDropDown(24, 9, 28, Enum.GetNames<RuntimeBotMode>());
        status = new Label { X = 1, Y = 11, Width = Dim.Fill(1), SchemeName = "Base" };
        feedback = new Label { X = 1, Y = 15, Width = Dim.Fill(1), Height = 2, SchemeName = "Base" };

        SelectText(weapon, bot.Configuration.WeaponPolicy.ToString());
        SelectText(flight, bot.Configuration.FlightEnabled ? "On" : "Off");
        SelectText(godMode, bot.Configuration.GodMode ? "On" : "Off");
        SelectText(mode, bot.Configuration.Mode.ToString());
        SelectTarget(bot.Configuration.Target);
        RefreshStatus(bot);

        var apply = new Button { X = 24, Y = 13, Text = "Apply", SchemeName = "Base" };
        var close = new Button { X = 36, Y = 13, Text = "Close", SchemeName = "Base" };
        apply.Accepted += (_, _) => Apply();
        close.Accepted += (_, _) => CloseRequested?.Invoke();

        Add(
            LabelAt("Weapon policy", 1), weapon,
            LabelAt("Mobility accessories", 3), flight,
            LabelAt("God mode", 5), godMode,
            LabelAt("Follow target", 7), target,
            LabelAt("Mode", 9), mode,
            status,
            apply,
            close,
            feedback);
    }

    public event Action? CloseRequested;

    internal int BotId => bot.Id;
    internal string StatusTextForSmoke => status.Text?.ToString() ?? string.Empty;

    internal void RefreshLiveStatus(RuntimeBotSnapshot snapshot)
    {
        if (snapshot.Id == bot.Id)
            RefreshStatus(snapshot);
    }
    internal string FeedbackTextForSmoke => feedback.Text?.ToString() ?? string.Empty;
    internal bool NpcPresetVisibleForSmoke => false;
    internal bool PlayerFieldsVisibleForSmoke => weapon.Visible && flight.Visible && godMode.Visible;
    internal int NpcPresetCountForSmoke => 0;
    internal bool GodModeForSmoke => string.Equals(godMode.Text?.ToString(), "On", StringComparison.Ordinal);

    internal void SetGodModeForSmoke(bool enabled) => SelectText(godMode, enabled ? "On" : "Off");

    private void Apply()
    {
        if (!Enum.TryParse(weapon.Text?.ToString(), ignoreCase: false, out RuntimeBotWeaponPolicy weaponValue) ||
            !Enum.TryParse(mode.Text?.ToString(), ignoreCase: false, out RuntimeBotMode modeValue) ||
            !TryParseToggle(flight, out bool flightValue) ||
            !TryParseToggle(godMode, out bool godModeValue))
        {
            feedback.Text = "bot: invalid selection";
            return;
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
            WeaponPolicy: weaponValue,
            FlightEnabled: flightValue,
            GodMode: godModeValue);
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
        string targetState = snapshot.Configuration.Target.IsAssigned
            ? snapshot.TargetAvailable ? snapshot.Configuration.Target.DisplayName : $"{snapshot.Configuration.Target.DisplayName} (offline)"
            : "none";
        string lifeState = snapshot.IsDead ? "DEAD" : "alive";
        status.Text = $"PlayerBot · {lifeState} · Target: {targetState} · PvP {(snapshot.PvpEnabled ? "ON" : "OFF")} · God {(snapshot.Configuration.GodMode ? "ON" : "OFF")} · teleports {snapshot.TeleportCount}";
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
}
