
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

    private readonly Label _stageLabel;
    private readonly Label _destinationLabel;
    private readonly ProgressBar _progressBar;
    private readonly TextBox _summaryBox;
    private readonly Button _closeButton;

    private bool _started;
    private bool _finished;
    private bool _installationSucceeded;
    private string? _installedPath;
    private bool _cancellationDisposed;

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
        Width = 740;
        Height = 540;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var gameLabel = new Label
        {
            Left = 18,
            Top = 18,
            Width = 685,
            Height = 24,
            Text = $"Game: {title}",
            AutoEllipsis = true
        };

        var methodLabel = new Label
        {
            Left = 18,
            Top = 48,
            Width = 685,
            Height = 24,
            Text = $"Operation: {method}",
            AutoEllipsis = true
        };

        var sourceLabel = new Label
        {
            Left = 18,
            Top = 78,
            Width = 685,
            Height = 42,
            Text = $"Source: {source}",
            AutoEllipsis = true
        };

        _destinationLabel = new Label
        {
            Left = 18,
            Top = 124,
            Width = 685,
            Height = 42,
            Text = $"Destination: {destination}",
            AutoEllipsis = true
        };

        _stageLabel = new Label
        {
            Left = 18,
            Top = 176,
            Width = 685,
            Height = 24,
            Text = "Preparing installation..."
        };

        _progressBar = new ProgressBar
        {
            Left = 18,
            Top = 205,
            Width = 685,
            Height = 24,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30
        };

        _summaryBox = new TextBox
        {
            Left = 18,
            Top = 241,
            Width = 685,
            Height = 190,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text = "Starting installation..."
        };

        _closeButton = new Button
        {
            Text = "Cancel",
            Left = 583,
            Top = 448,
            Width = 120,
            Height = 32,
            DialogResult = DialogResult.None
        };

        _closeButton.Click += (_, _) =>
        {
            if (_finished)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            RequestCancellation();
        };

        Controls.AddRange(
        [
            gameLabel,
            methodLabel,
            sourceLabel,
            _destinationLabel,
            _stageLabel,
            _progressBar,
            _summaryBox,
            _closeButton
        ]);

        CancelButton = _closeButton;

        Shown += (_, _) => StartInstallation();
    }

    public static (bool Succeeded, string? InstalledPath) Show(
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

            return (
                dialog._installationSucceeded,
                dialog._installedPath);
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
        {
            throw new InvalidOperationException(
                "Could not display the installation window.", error);
        }

        return (
            dialog._installationSucceeded,
            dialog._installedPath);
    }

    private async void StartInstallation()
    {
        if (_started)
            return;

        _started = true;
        _closeButton.Text = "Cancel";
        _summaryBox.Text = "Starting installation...";

        // Created on the UI thread so progress updates are marshalled
        // back to this form.
        var progress = new Progress<InstallationProgress>(UpdateProgress);

        try
        {
            string installedPath = await Task.Run(
                () => _operation(progress, _cancellation.Token));

            _installedPath = installedPath;
            _installationSucceeded = true;

            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Value = 100;

            _stageLabel.Text = "Completed";

            if (string.IsNullOrWhiteSpace(_summaryBox.Text) ||
                _summaryBox.Text == "Starting installation...")
            {
                _summaryBox.Text =
                    "Installation completed successfully.";
            }

            if (!_summaryBox.Text.Contains(
                    installedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _summaryBox.AppendText(
                    Environment.NewLine +
                    Environment.NewLine +
                    "Installed to:" +
                    Environment.NewLine +
                    installedPath);
            }

            _destinationLabel.Text = $"Installed location: {installedPath}";

            Finish();
        }
        catch (OperationCanceledException)
        {
            _stageLabel.Text = "Cancelled";
            _summaryBox.AppendText(
                Environment.NewLine +
                Environment.NewLine +
                "The operation was cancelled. The game may already have been installed if cancellation occurred during Title Update downloading.");

            Finish();
        }
        catch (Exception ex)
        {
            _stageLabel.Text = "Failed";
            _summaryBox.AppendText(
                Environment.NewLine +
                Environment.NewLine +
                "Operation failed:" +
                Environment.NewLine +
                ex.Message);

            Finish();
        }
    }

    private void UpdateProgress(InstallationProgress update)
    {
        if (IsDisposed || Disposing || _finished)
            return;

        _stageLabel.Text = update.Stage;
        _summaryBox.Text = update.Message;

        if (update.Percent is int percent)
        {
            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = Math.Clamp(percent, 0, 100);
        }
        else
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 30;
        }
    }

    private void RequestCancellation()
    {
        if (_finished || _cancellation.IsCancellationRequested)
            return;

        _cancellation.Cancel();
        _closeButton.Enabled = false;
        _closeButton.Text = "Cancelling...";
        _summaryBox.AppendText(
            Environment.NewLine +
            Environment.NewLine +
            "Cancellation requested. Waiting for the current operation to stop...");
    }

    private void Finish()
    {
        _finished = true;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.MarqueeAnimationSpeed = 0;
        _closeButton.Text = "Close";
        _closeButton.Enabled = true;
        AcceptButton = _closeButton;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_finished)
        {
            // Keep the window visible until cancellation or installation
            // has actually finished. The user closes it from the summary.
            e.Cancel = true;

            if (_started)
                RequestCancellation();

            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!_cancellationDisposed)
        {
            _cancellationDisposed = true;
            _cancellation.Dispose();
        }

        base.OnFormClosed(e);
    }
}
