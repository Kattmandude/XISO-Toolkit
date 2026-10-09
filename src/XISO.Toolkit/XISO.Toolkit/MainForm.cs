using System.Drawing;
using System.Text;

namespace XISO.Toolkit;

internal sealed class MainForm : Form
{
    private readonly TextBox _output;
    private readonly Func<Task> _installAction;
    private readonly Func<Task> _analyzeAction;
    private readonly Func<Task> _updateAction;
    private readonly Func<Task> _startupAction;
    private readonly Func<Task>? _initialAction;
    private readonly bool _skipStartupAction;

    private readonly Button _installButton;
    private readonly Button _analyzeButton;
    private readonly Button _updateButton;

    public MainForm(
        Func<Task> installAction,
        Func<Task> analyzeAction,
        Func<Task> updateAction,
        Func<Task> startupAction,
        Func<Task>? initialAction = null,
        bool skipStartupAction = false)
    {
        _installAction = installAction;
        _analyzeAction = analyzeAction;
        _updateAction = updateAction;
        _startupAction = startupAction;
        _initialAction = initialAction;
        _skipStartupAction = skipStartupAction;

        Text = "XISO Toolkit";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        Size = new Size(1000, 700);
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18)
        };

        root.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));

        root.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));

        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        Controls.Add(root);

        var heading = new Label
        {
            Text = "XISO Toolkit",
            AutoSize = true,
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        };

        root.Controls.Add(heading, 0, 0);

        var subtitle = new Label
        {
            Text = "Original Xbox game management",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 18)
        };

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };

        header.Controls.Add(subtitle);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 14)
        };

        _installButton = CreateButton("Install Game", 150);
        _analyzeButton = CreateButton("Analyze ISO / Archive", 190);
        _updateButton = CreateButton("Check MobCat Update", 190);

        var exitButton = CreateButton("Exit", 90);

        _installButton.Click += async (_, _) =>
        {
            _output.Clear();
            await RunActionAsync(_installAction);
        };

        _analyzeButton.Click += async (_, _) =>
        {
            _output.Clear();
            await RunActionAsync(_analyzeAction);
        };

        _updateButton.Click += async (_, _) =>
        {
            _output.Clear();
            await RunActionAsync(_updateAction);
        };

        exitButton.Click += (_, _) => Close();

        buttons.Controls.AddRange(
        [
            _installButton,
            _analyzeButton,
            _updateButton,
            exitButton
        ]);

        header.Controls.Add(buttons);

        root.Controls.Add(header, 0, 1);

        _output = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10F),
            BackColor = SystemColors.Window,
            ForeColor = SystemColors.WindowText,
            HideSelection = false
        };

        root.Controls.Add(_output, 0, 2);

        Shown += async (_, _) =>
        {
            if (!_skipStartupAction)
            {
                await RunActionAsync(_startupAction);
            }

            if (_initialAction != null && !IsDisposed)
            {
                await RunActionAsync(_initialAction);
            }
        };
    }

    private static Button CreateButton(
        string text,
        int width)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = 42,
            Margin = new Padding(0, 0, 10, 10),
            UseVisualStyleBackColor = true
        };
    }

    private async Task RunActionAsync(
        Func<Task> action)
    {
        SetButtonsEnabled(false);

        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            WriteOutput("Operation cancelled.");
        }
        catch (Exception ex)
        {
            WriteOutput(
                $"Operation failed: {ex.Message}");
        }
        finally
        {
            if (!IsDisposed)
            {
                SetButtonsEnabled(true);
            }
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _installButton.Enabled = enabled;
        _analyzeButton.Enabled = enabled;
        _updateButton.Enabled = enabled;
    }

    internal void WriteOutput(string text)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(() => WriteOutput(text));
            }
            catch (InvalidOperationException)
            {
                // The form is closing.
            }

            return;
        }

        _output.AppendText(text);
        _output.SelectionStart = _output.TextLength;
        _output.ScrollToCaret();
    }
}

internal sealed class GuiTextWriter : TextWriter
{
    private readonly MainForm _form;

    public GuiTextWriter(MainForm form)
    {
        _form = form;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        _form.WriteOutput(value.ToString());
    }

    public override void Write(string? value)
    {
        if (value != null)
        {
            _form.WriteOutput(value);
        }
    }

    public override void WriteLine(string? value)
    {
        _form.WriteOutput(
            (value ?? string.Empty) +
            Environment.NewLine);
    }

    public override void WriteLine()
    {
        _form.WriteOutput(Environment.NewLine);
    }
}
