using System.Drawing;
using System.Text;
using System.Runtime.InteropServices;

namespace XISO.Toolkit;

internal sealed class MainForm : Form
{
    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int EM_LINESCROLL = 0x00B6;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);

    private readonly RichTextBox _output;
    private readonly Label _activityLabel;
    private readonly GameAnalysisActivity _gameAnalysisActivity;

    private readonly System.Windows.Forms.Timer _scrollTimer = new()
    {
        Interval = 15
    };

    private int _scrollStartLine;
    private long _scrollStartTime;
    private bool _suppressNextActionScrollToTop;

    private readonly Func<Task> _installAction;
    private readonly Func<Task> _analyzeAction;
    private readonly Func<Task> _updateAction;
    private readonly Func<Task>? _x360UpdateAction;
    private readonly Func<Task> _startupAction;
    private readonly Func<Task>? _initialAction;
    private readonly bool _skipStartupAction;

    private readonly Button _installButton;
    private readonly Button _analyzeButton;
    private readonly Button _updateButton;
    private readonly Button _x360UpdateButton;

    private readonly PictureBox _artworkPictureBox;
    private readonly Label _artworkLabel;
    private readonly Image _noArtworkImage;

    public MainForm(
        Func<Task> installAction,
        Func<Task> analyzeAction,
        Func<Task> updateAction,
        Func<Task> startupAction,
        Func<Task>? initialAction = null,
        bool skipStartupAction = false,
        Func<Task>? x360UpdateAction = null)
    {
        _installAction = installAction;
        _analyzeAction = analyzeAction;
        _updateAction = updateAction;
        _x360UpdateAction = x360UpdateAction;
        _startupAction = startupAction;
        _initialAction = initialAction;
        _skipStartupAction = skipStartupAction;

        string placeholderPath = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "NoArtwork.png");

        if (!File.Exists(placeholderPath))
        {
            throw new FileNotFoundException(
                "The default artwork image was not found.",
                placeholderPath);
        }

        using (var stream = File.OpenRead(placeholderPath))
        using (var image = Image.FromStream(stream))
        {
            _noArtworkImage = new Bitmap(image);
        }

        Text = "XISO Toolkit";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 560);
        Size = new Size(1100, 740);
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
            Font = new Font(
                "Segoe UI",
                22F,
                FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        };

        root.Controls.Add(heading, 0, 0);

        var subtitle = new Label
        {
            Text = "Original Xbox and Xbox 360 game management",
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

        _installButton =
            CreateButton("Install Game", 140);

        _analyzeButton =
            CreateButton("Analyze ISO / Archive", 190);

        _updateButton =
            CreateButton("Update OG Xbox DB", 170);

        _x360UpdateButton =
            CreateButton("Update Xbox 360 DB", 185);

        _installButton.Click += async (_, _) =>
        {
            await RunActionAsync(_installAction);
        };

        _analyzeButton.Click += async (_, _) =>
        {
            await RunActionAsync(_analyzeAction);
        };

        _updateButton.Click += async (_, _) =>
        {
            ClearOutput();
            SetArtwork(null);

            await RunActionAsync(_updateAction);
        };

        _x360UpdateButton.Click += async (_, _) =>
        {
            ClearOutput();
            SetArtwork(null);

            if (_x360UpdateAction == null)
            {
                WriteOutput(
                    "Xbox 360 database update is not connected yet.");

                ScrollOutputToTop();
                return;
            }

            await RunActionAsync(_x360UpdateAction);
        };

        buttons.Controls.AddRange(
        [
            _installButton,
            _analyzeButton,
            _updateButton,
            _x360UpdateButton
        ]);

        header.Controls.Add(buttons);
        root.Controls.Add(header, 0, 1);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        content.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        content.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 190));

        content.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(content, 0, 2);

        _output = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = new Font("Consolas", 10F),
            BackColor = SystemColors.Window,
            ForeColor = SystemColors.WindowText,
            HideSelection = false,
            DetectUrls = false,
            BorderStyle = BorderStyle.Fixed3D,
            Margin = new Padding(0, 0, 12, 0)
        };

        var outputPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(0)
        };

        outputPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        outputPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        outputPanel.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));

        _output.Margin = new Padding(0);

        _activityLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = Color.FromArgb(16, 124, 16),
            BackColor = Color.FromArgb(235, 242, 230),
            Font = new Font("Consolas", 11F, FontStyle.Bold),
            Padding = new Padding(8, 10, 8, 10),
            Margin = new Padding(0, 8, 0, 0),
            Visible = false
        };

        _gameAnalysisActivity =
            new GameAnalysisActivity(_activityLabel);

        _scrollTimer.Tick += ScrollTimer_Tick;

        outputPanel.Controls.Add(_output, 0, 0);
        outputPanel.Controls.Add(_activityLabel, 0, 1);

        content.Controls.Add(outputPanel, 0, 0);

        var artworkPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        artworkPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        artworkPanel.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));

        artworkPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        artworkPanel.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));

        _artworkLabel = new Label
        {
            Text = "Game Artwork",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(
                "Segoe UI",
                10F,
                FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 0, 8)
        };

        _artworkPictureBox = new PictureBox
        {
            Anchor = AnchorStyles.Top,
            Height = 220,
            Width = 160,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0)
        };

        _artworkPictureBox.Image = new Bitmap(_noArtworkImage);

        artworkPanel.Controls.Add(
            _artworkLabel,
            0,
            0);

        artworkPanel.Controls.Add(
            _artworkPictureBox,
            0,
            1);

        var exitButton = CreateButton("Exit", 90);

        exitButton.Anchor = AnchorStyles.Right;
        exitButton.Margin = new Padding(0, 8, 0, 0);
        exitButton.Click += (_, _) => Close();

        artworkPanel.Controls.Add(
            exitButton,
            0,
            2);

        content.Controls.Add(
            artworkPanel,
            1,
            0);

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

        FormClosed += (_, _) =>
        {
            _gameAnalysisActivity.Dispose();

            _scrollTimer.Stop();
            _scrollTimer.Dispose();

            _artworkPictureBox.Image?.Dispose();
            _artworkPictureBox.Image = null;
            _noArtworkImage.Dispose();
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
            Height = 38,
            AutoSize = false,
            Margin = new Padding(0, 0, 8, 8),
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
                if (_suppressNextActionScrollToTop)
                {
                    _suppressNextActionScrollToTop = false;
                }
                else
                {
                    ScrollOutputToTop();
                }

                SetButtonsEnabled(true);
            }
        }
    }

    internal void SuppressNextActionScrollToTop()
    {
        _suppressNextActionScrollToTop = true;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _installButton.Enabled = enabled;
        _analyzeButton.Enabled = enabled;
        _updateButton.Enabled = enabled;

        _x360UpdateButton.Enabled =
            enabled && _x360UpdateAction != null;
    }

    internal void SetArtwork(string? imagePath)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(
                new Action(() => SetArtwork(imagePath)));

            return;
        }

        Image? newImage = null;

        if (!string.IsNullOrWhiteSpace(imagePath) &&
            File.Exists(imagePath))
        {
            try
            {
                // Read the file into memory so the cached image
                // is not locked by the PictureBox.
                byte[] imageBytes =
                    File.ReadAllBytes(imagePath);

                using var stream =
                    new MemoryStream(imageBytes);

                using var sourceImage =
                    Image.FromStream(stream);

                newImage = new Bitmap(sourceImage);
            }
            catch (Exception ex)
            {
                WriteOutput(
                    $"Could not load artwork: {ex.Message}");
            }
        }

        Image? previousImage =
            _artworkPictureBox.Image;

        _artworkPictureBox.Image =
            newImage ?? new Bitmap(_noArtworkImage);

        if (previousImage != null &&
            !ReferenceEquals(previousImage, _noArtworkImage))
        {
            previousImage.Dispose();
        }
    }

    internal void ClearOutput()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            Invoke(new Action(ClearOutput));
            return;
        }

        _output.Clear();
    }


    internal void WriteOutput(string text)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(
                new Action(() => WriteOutput(text)));

            return;
        }

        _output.SelectionStart = _output.TextLength;
        _output.SelectionLength = 0;

        // Indent continuation lines when output wraps.
        _output.SelectionIndent = 0;
        _output.SelectionHangingIndent = 30;

        _output.AppendText(text);
    }

    internal void StartActivity(string message)
    {
        _gameAnalysisActivity.Start(message);
    }

    internal void UpdateActivity(string message)
    {
        _gameAnalysisActivity.Update(message);
    }

    internal void StopActivity()
    {
        _gameAnalysisActivity.Stop();
    }

    internal void ScrollOutputToTop()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(ScrollOutputToTop));
            return;
        }

        _scrollTimer.Stop();

        _scrollStartLine = SendMessage(
            _output.Handle,
            EM_GETFIRSTVISIBLELINE,
            IntPtr.Zero,
            IntPtr.Zero).ToInt32();

        if (_scrollStartLine <= 0)
        {
            return;
        }

        _scrollStartTime = Environment.TickCount64;
        _scrollTimer.Start();
    }

    private void ScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing || _output.IsDisposed)
        {
            _scrollTimer.Stop();
            return;
        }

        double progress =
            (Environment.TickCount64 - _scrollStartTime) / 1000.0;

        if (progress >= 1.0)
        {
            _scrollTimer.Stop();

            int currentLine = SendMessage(
                _output.Handle,
                EM_GETFIRSTVISIBLELINE,
                IntPtr.Zero,
                IntPtr.Zero).ToInt32();

            if (currentLine > 0)
            {
                SendMessage(
                    _output.Handle,
                    EM_LINESCROLL,
                    IntPtr.Zero,
                    new IntPtr(-currentLine));
            }

            return;
        }

        // Ease out so the scroll slows down near the top.
        double easedProgress = 1.0 - Math.Pow(1.0 - progress, 2);

        int targetLine = (int)Math.Round(
            _scrollStartLine * (1.0 - easedProgress));

        int visibleLine = SendMessage(
            _output.Handle,
            EM_GETFIRSTVISIBLELINE,
            IntPtr.Zero,
            IntPtr.Zero).ToInt32();

        int linesToScroll = targetLine - visibleLine;

        if (linesToScroll != 0)
        {
            SendMessage(
                _output.Handle,
                EM_LINESCROLL,
                IntPtr.Zero,
                new IntPtr(linesToScroll));
        }
    }
}

internal sealed class GuiTextWriter : TextWriter
{
    private readonly MainForm _form;

    public GuiTextWriter(MainForm form)
    {
        _form = form;
    }

    public override Encoding Encoding =>
        Encoding.UTF8;

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
        _form.WriteOutput(
            Environment.NewLine);
    }
}
