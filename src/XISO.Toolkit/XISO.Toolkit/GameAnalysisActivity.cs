using System;
using System.Windows.Forms;

namespace XISO.Toolkit;

internal sealed class GameAnalysisActivity : IDisposable
{
    private static readonly string[] ActivityFrames =
    [
        "⠋", "⠙", "⠹", "⠸", "⠼",
        "⠴", "⠦", "⠧", "⠇", "⠏"
    ];

    private readonly Label _activityLabel;
    private readonly System.Windows.Forms.Timer _timer;

    private int _activityFrame;
    private string _activityMessage = string.Empty;
    private bool _disposed;

    public GameAnalysisActivity(Label activityLabel)
    {
        ArgumentNullException.ThrowIfNull(activityLabel);

        _activityLabel = activityLabel;

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 100
        };

        _timer.Tick += (_, _) =>
        {
            _activityFrame =
                (_activityFrame + 1) % ActivityFrames.Length;

            _activityLabel.Text =
                $"{ActivityFrames[_activityFrame]}  " +
                _activityMessage.ToUpperInvariant();
        };
    }

    public void Start(string message)
    {
        if (_disposed || _activityLabel.IsDisposed)
        {
            return;
        }

        if (_activityLabel.InvokeRequired)
        {
            _activityLabel.BeginInvoke(
                new Action(() => Start(message)));

            return;
        }

        _activityMessage = message;
        _activityFrame = 0;

        _activityLabel.Text =
            $"{ActivityFrames[_activityFrame]}  " +
            _activityMessage.ToUpperInvariant();

        _activityLabel.Visible = true;
        _timer.Start();
    }

    public void Update(string message)
    {
        if (_disposed || _activityLabel.IsDisposed)
        {
            return;
        }

        if (_activityLabel.InvokeRequired)
        {
            _activityLabel.BeginInvoke(
                new Action(() => Update(message)));

            return;
        }

        _activityMessage = message;

        _activityLabel.Text =
            $"{ActivityFrames[_activityFrame]}  " +
            _activityMessage.ToUpperInvariant();
    }

    public void Stop()
    {
        if (_disposed || _activityLabel.IsDisposed)
        {
            return;
        }

        if (_activityLabel.InvokeRequired)
        {
            _activityLabel.BeginInvoke(
                new Action(Stop));

            return;
        }

        _timer.Stop();
        _activityLabel.Visible = false;
        _activityLabel.Text = string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }
}
