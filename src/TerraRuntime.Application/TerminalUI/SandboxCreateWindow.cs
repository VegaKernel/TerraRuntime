using System.Globalization;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

/// <summary>Operator form for the typed sb1/sb2 sandbox creation surface.</summary>
internal sealed class SandboxCreateWindow : Window
{
    private readonly SandboxOperations operations;
    private readonly TextField nameField;
    private readonly CheckBox dedicatedProcess;
    private readonly CheckBox worldFile;
    private readonly TextField fileField;
    private readonly TextField generatorField;
    private readonly TextField seedField;
    private readonly CheckBox primarySize;
    private readonly TextField widthField;
    private readonly TextField heightField;
    private readonly TextField modeField;
    private readonly TextField evilField;
    private readonly Label feedback;

    public SandboxCreateWindow(SandboxOperations operations)
    {
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        Title = "Create sandbox";
        Width = 68;
        Height = 20;
        X = Pos.Center();
        Y = Pos.Center();
        SchemeName = "Base";

        nameField = Field(17, 1, 46, "sandbox");
        dedicatedProcess = new CheckBox { X = 17, Y = 3, Text = "Dedicated process (sb2)" };
        worldFile = new CheckBox { X = 17, Y = 4, Text = "Load existing .wld file" };
        fileField = Field(17, 5, 46, "worlds/example.wld");
        generatorField = Field(17, 7, 46, "terraruntime:optimized");
        seedField = Field(17, 8, 20, "0");
        primarySize = new CheckBox { X = 17, Y = 9, Text = "Use primary world size", Value = CheckState.Checked };
        widthField = Field(17, 10, 10, operations.DefaultWidthTiles.ToString(CultureInfo.InvariantCulture));
        heightField = Field(37, 10, 10, operations.DefaultHeightTiles.ToString(CultureInfo.InvariantCulture));
        modeField = Field(17, 11, 14, "classic");
        evilField = Field(44, 11, 14, "corruption");

        var create = new Button { X = 17, Y = 14, Text = "Create" };
        var cancel = new Button { X = 29, Y = 14, Text = "Cancel" };
        feedback = new Label { X = 1, Y = 16, Width = Dim.Fill(1), Text = string.Empty };

        Add(
            LabelAt(1, 1, "Name"), nameField,
            LabelAt(1, 3, "Isolation"), dedicatedProcess,
            LabelAt(1, 4, "Source"), worldFile,
            LabelAt(1, 5, "World file"), fileField,
            LabelAt(1, 7, "Generator"), generatorField,
            LabelAt(1, 8, "Seed"), seedField,
            LabelAt(1, 9, "Size"), primarySize,
            LabelAt(1, 10, "Width"), widthField, LabelAt(29, 10, "Height"), heightField,
            LabelAt(1, 11, "Mode"), modeField, LabelAt(34, 11, "Evil"), evilField,
            LabelAt(1, 12, "mode: classic/expert/master/journey · evil: corruption/crimson"),
            create, cancel, feedback);

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
        WorldIsolationLevel isolation = dedicatedProcess.Value == CheckState.Checked
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
            if (!Enum.TryParse(modeField.Text.Trim(), ignoreCase: true, out WorldGenerationGameMode mode) || !Enum.IsDefined(mode))
            {
                feedback.Text = "Mode must be classic, expert, master or journey.";
                return;
            }
            if (!Enum.TryParse(evilField.Text.Trim(), ignoreCase: true, out WorldGenerationEvil evil) || !Enum.IsDefined(evil))
            {
                feedback.Text = "Evil must be corruption or crimson.";
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

    private void UpdateEnabledFields()
    {
        bool file = worldFile.Value == CheckState.Checked;
        fileField.Enabled = file;
        generatorField.Enabled = !file;
        seedField.Enabled = !file;
        primarySize.Enabled = !file;
        widthField.Enabled = !file && primarySize.Value != CheckState.Checked;
        heightField.Enabled = !file && primarySize.Value != CheckState.Checked;
        modeField.Enabled = !file;
        evilField.Enabled = !file;
    }

    private static Label LabelAt(int x, int y, string text) => new() { X = x, Y = y, Text = text };

    private static TextField Field(int x, int y, int width, string text) => new()
    {
        X = x,
        Y = y,
        Width = width,
        Text = text
    };
}
