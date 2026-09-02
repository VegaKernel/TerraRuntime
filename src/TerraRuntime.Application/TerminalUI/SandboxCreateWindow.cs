using System.Collections.ObjectModel;
using System.Globalization;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

/// <summary>Operator form for typed sandbox creation.</summary>
internal sealed class SandboxCreateWindow : Window
{
    private static readonly string[] GameModes = ["Classic", "Expert", "Master", "Journey"];
    private static readonly string[] EvilTypes = ["Corruption", "Crimson"];

    private readonly SandboxOperations operations;
    private readonly TextField nameField;
    private readonly CheckBox inProcessIsolation;
    private readonly CheckBox dedicatedProcessIsolation;
    private readonly CheckBox worldFile;
    private readonly TextField fileField;
    private readonly TextField generatorField;
    private readonly TextField seedField;
    private readonly CheckBox primarySize;
    private readonly TextField widthField;
    private readonly TextField heightField;
    private readonly DropDownList modeDropDown;
    private readonly DropDownList evilDropDown;
    private readonly Label feedback;
    private bool updatingIsolation;

    public SandboxCreateWindow(SandboxOperations operations)
    {
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        Title = "Create sandbox";
        Width = 72;
        Height = 22;
        X = Pos.Center();
        Y = Pos.Center();
        SchemeName = "Base";

        nameField = Field(20, 1, 47, "sandbox");
        inProcessIsolation = new CheckBox
        {
            X = 20,
            Y = 3,
            Text = "In-process sandbox isolation",
            Value = CheckState.Checked
        };
        dedicatedProcessIsolation = new CheckBox
        {
            X = 20,
            Y = 4,
            Text = "Dedicated-process sandbox isolation"
        };
        worldFile = new CheckBox { X = 20, Y = 6, Text = "Load existing .wld file" };
        fileField = Field(20, 7, 47, "worlds/example.wld");
        generatorField = Field(20, 9, 47, "terraruntime:optimized");
        seedField = Field(20, 10, 20, "0");
        primarySize = new CheckBox { X = 20, Y = 11, Text = "Use primary world size", Value = CheckState.Checked };
        widthField = Field(20, 12, 10, operations.DefaultWidthTiles.ToString(CultureInfo.InvariantCulture));
        heightField = Field(42, 12, 10, operations.DefaultHeightTiles.ToString(CultureInfo.InvariantCulture));
        modeDropDown = DropDown(20, 13, 17, GameModes, "Classic");
        evilDropDown = DropDown(47, 13, 17, EvilTypes, "Corruption");

        var create = new Button { X = 20, Y = 16, Text = "Create" };
        var cancel = new Button { X = 32, Y = 16, Text = "Cancel" };
        feedback = new Label { X = 1, Y = 18, Width = Dim.Fill(1), Text = string.Empty };

        Add(
            LabelAt(1, 1, "Name"), nameField,
            LabelAt(1, 3, "Isolation"), inProcessIsolation,
            dedicatedProcessIsolation,
            LabelAt(1, 6, "Source"), worldFile,
            LabelAt(1, 7, "World file"), fileField,
            LabelAt(1, 9, "Generator"), generatorField,
            LabelAt(1, 10, "Seed"), seedField,
            LabelAt(1, 11, "Size"), primarySize,
            LabelAt(1, 12, "Width"), widthField, LabelAt(33, 12, "Height"), heightField,
            LabelAt(1, 13, "Mode"), modeDropDown, LabelAt(40, 13, "Evil"), evilDropDown,
            create, cancel, feedback);

        inProcessIsolation.ValueChanged += (_, _) => KeepIsolationExclusive(inProcessIsolation, dedicatedProcessIsolation);
        dedicatedProcessIsolation.ValueChanged += (_, _) => KeepIsolationExclusive(dedicatedProcessIsolation, inProcessIsolation);
        worldFile.ValueChanged += (_, _) => UpdateEnabledFields();
        primarySize.ValueChanged += (_, _) => UpdateEnabledFields();
        create.Accepted += (_, _) => Submit();
        cancel.Accepted += (_, _) => CloseRequested?.Invoke();
        UpdateEnabledFields();
    }

    public event Action<SandboxOperation.Create>? CreateRequested;
    public event Action? CloseRequested;

    private void Submit()
    {
        WorldIsolationLevel isolation = dedicatedProcessIsolation.Value == CheckState.Checked
            ? WorldIsolationLevel.DedicatedProcess
            : WorldIsolationLevel.InProcess;
        string name = nameField.Text.Trim();

        SandboxOperation.Create? operation;
        string? error;
        if (worldFile.Value == CheckState.Checked)
        {
            if (!operations.TryBuildWorldFileCreate(name, isolation, fileField.Text.Trim(), out operation, out error))
            {
                feedback.Text = error ?? "Invalid world file request.";
                return;
            }
        }
        else
        {
            if (!Enum.TryParse(modeDropDown.Text.Trim(), ignoreCase: true, out WorldGenerationGameMode mode) || !Enum.IsDefined(mode))
            {
                feedback.Text = "Select a valid game mode.";
                return;
            }
            if (!Enum.TryParse(evilDropDown.Text.Trim(), ignoreCase: true, out WorldGenerationEvil evil) || !Enum.IsDefined(evil))
            {
                feedback.Text = "Select a valid world evil.";
                return;
            }

            int? width = null;
            int? height = null;
            if (primarySize.Value != CheckState.Checked)
            {
                if (!int.TryParse(widthField.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedWidth) || parsedWidth <= 0 ||
                    !int.TryParse(heightField.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedHeight) || parsedHeight <= 0)
                {
                    feedback.Text = "Width and height must be positive integers.";
                    return;
                }
                width = parsedWidth;
                height = parsedHeight;
            }

            if (!operations.TryBuildGeneratedCreate(
                    name,
                    isolation,
                    generatorField.Text.Trim(),
                    seedField.Text.Trim(),
                    width,
                    height,
                    mode,
                    evil,
                    out operation,
                    out error))
            {
                feedback.Text = error ?? "Invalid generation request.";
                return;
            }
        }

        if (operation is null)
        {
            feedback.Text = "Sandbox request was not created.";
            return;
        }

        CreateRequested?.Invoke(operation);
        CloseRequested?.Invoke();
    }

    private void KeepIsolationExclusive(CheckBox selected, CheckBox other)
    {
        if (updatingIsolation)
            return;

        updatingIsolation = true;
        try
        {
            if (selected.Value == CheckState.Checked)
            {
                other.Value = CheckState.UnChecked;
                return;
            }

            if (other.Value != CheckState.Checked)
                selected.Value = CheckState.Checked;
        }
        finally
        {
            updatingIsolation = false;
        }
    }

    private void UpdateEnabledFields()
    {
        bool file = worldFile.Value == CheckState.Checked;
        fileField.Enabled = file;
        generatorField.Enabled = !file;
        seedField.Enabled = !file;
        primarySize.Enabled = !file;
        widthField.Enabled = !file && primarySize.Value != CheckState.Checked;
        heightField.Enabled = !file && primarySize.Value != CheckState.Checked;
        modeDropDown.Enabled = !file;
        evilDropDown.Enabled = !file;
    }

    private static Label LabelAt(int x, int y, string text) => new() { X = x, Y = y, Text = text };

    private static TextField Field(int x, int y, int width, string text) => new()
    {
        X = x,
        Y = y,
        Width = width,
        Text = text
    };

    private static DropDownList DropDown(int x, int y, int width, IEnumerable<string> items, string selected) => new()
    {
        X = x,
        Y = y,
        Width = width,
        ReadOnly = true,
        Source = new ListWrapper<string>(new ObservableCollection<string>(items.ToArray())),
        Text = selected
    };
}
