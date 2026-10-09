
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using XISO.Core.Installation;

namespace XISO.Toolkit;

internal sealed class InstallationProgressDialog : Form
{
    private readonly Func<IProgress<InstallationProgress>,
        CancellationToken, string> _operation;

    private readonly CancellationTokenSource _cancellation = new();

    private readonly Label _statusLabel;
    private readonly Label _stageLabel;
    private readonly Label _destinationLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _startButton;
    private readonly Button _cancelButton;

    private bool _started;
    private bool _finished;

    private InstallationProgressDialog(
        string title,
        string method,
        string source,
        string destination,
        Func<IProgress<InstallationProgress>,
            CancellationToken, string> operation)
    {
        _operation = operation;

        Text = "Game Installation";
        Width = 720;
        Height = 390;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var gameLabel = new Label
        {
            Left = 18,
            Top = 18,
            Width = 665,
            Height = 24,
            Text = $"Game: {title}"
        };

        var methodLabel = new Label
        {
            Left = 18,
            Top = 48,
            Width = 665,
            Height = 24,
            Text = $"Operation: {method}"
        };

        var sourceLabel = new Label
        {
            Left = 18,
            Top = 78,
            Width = 665,
            Height = 42,
            Text = $"Source: {source}",
            AutoEllipsis = true
        };

        _destinationLabel = new Label
        {
            Left = 18,
            Top = 124,
            Width = 665,
            Height = 48,
            Text = $"Destination: {destination}",
            AutoEllipsis = false,
            AutoSize = false
        };

        _stageLabel = new Label
        {
            Left = 18,
            Top = 180,
            Width = 665,
            Height = 22,
            Text = "Ready to install.",
            AutoEllipsis = true
        };

        _progressBar = new ProgressBar
        {
            Left = 18,
            Top = 207,
            Width = 665,
            Height = 22,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };

        _statusLabel = new Label
        {
            Left = 18,
            Top = 237,
            Width = 665,
            Height = 50,
            Text = "Review the destination, then start installation.",
            AutoSize = false
        };

        _startButton = new Button
        {
            Text = "Start Installation",
            Left = 442,
            Top = 320,
            Width = 125,
            DialogResult = DialogResult.None
        };

        _cancelButton = new Button
        {
            Text = "Cancel",
            Left = 578,
            Top = 320,
            Width = 105,
            DialogResult = DialogResult.None
        };

        _startButton.Click += (_, _) => StartInstallation();

        _cancelButton.Click += (_, _) =>
        {
            if (_finished)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            if (_started)
            {
                _cancellation.Cancel();
                _cancelButton.Enabled = false;
                _statusLabel.Text =
                    "Cancellation requested. Waiting for the current operation to stop...";
            }
            else
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        Controls.AddRange(
        [
            gameLabel,
            methodLabel,
            sourceLabel,
            _destinationLabel,
            _stageLabel,
            _progressBar,
            _statusLabel,
            _startButton,
            _cancelButton
        ]);

        AcceptButton = _startButton;
        CancelButton = _cancelButton;
    }

    public static void Show(
        string title,
        string method,
        string source,
        string destination,
        Func<IProgress<InstallationProgress>,
            CancellationToken, string> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var dialog = new InstallationProgressDialog(
            title,
            method,
            source,
            destination,
            operation);

        void ShowOnStaThread()
        {
            using (dialog)
                dialog.ShowDialog();
        }

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            ShowOnStaThread();
            return;
        }

        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                ShowOnStaThread();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            throw new InvalidOperationException(
                "Could not display the installation window.", error);
    }

    private async void StartInstallation()
    {
        if (_started)
            return;

        _started = true;
        _startButton.Enabled = false;
        _cancelButton.Text = "Cancel";
        _statusLabel.Text = "Starting installation...";

        // Progress<T> captures this window's UI synchronization context.
        var progress = new Progress<InstallationProgress>(UpdateProgress);

        try
        {
            string installedPath = await Task.Run(
                () => _operation(progress, _cancellation.Token));

            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 100;
            _stageLabel.Text = "Completed";
            _statusLabel.Text =
                $"Installation completed successfully.{Environment.NewLine}{installedPath}";

            _finished = true;
            _cancelButton.Text = "Close";
            _cancelButton.Enabled = true;
        }
        catch (OperationCanceledException)
        {
            _stageLabel.Text = "Cancelled";
            _statusLabel.Text =
                "Installation was cancelled. Any incomplete staging data was cleaned up where possible.";

            _finished = true;
            _cancelButton.Text = "Close";
            _cancelButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _stageLabel.Text = "Failed";
            _statusLabel.Text = $"Installation failed: {ex.Message}";

            _finished = true;
            _cancelButton.Text = "Close";
            _cancelButton.Enabled = true;
        }
    }

    private void UpdateProgress(InstallationProgress update)
    {
        if (IsDisposed || Disposing)
            return;

        _stageLabel.Text = update.Stage;
        _statusLabel.Text = update.Message;

        if (update.Percent is int percent)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = Math.Clamp(percent, 0, 100);
        }
        else
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!_finished && _started)
            _cancellation.Cancel();

        _cancellation.Dispose();
        base.OnFormClosed(e);
    }
}