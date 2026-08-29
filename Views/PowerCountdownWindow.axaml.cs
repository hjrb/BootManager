using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BootManager.Services;

namespace BootManager.Views;

/// <summary>
/// Countdown window shown before the delayed reboot starts.
/// </summary>
public partial class PowerCountdownWindow : Window
{
    private readonly TimeSpan _countdown = TimeSpan.FromSeconds(20);
    private readonly DispatcherTimer _timer;
    private DateTimeOffset _startedAt;
    private bool _finished;

    public PowerCountdownWindow()
    {
        InitializeComponent();
        CountdownText.Text = "Rebooting in 20 seconds";
        CancelButton.Click += (_, _) => Cancel();
        NowButton.Click += (_, _) => _ = RebootNowAsync();

        // DispatcherTimer ticks on the UI thread, so updating CountdownText.Text here is always safe.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTimerTick;

        _startedAt = DateTimeOffset.UtcNow;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var remaining = _countdown - (DateTimeOffset.UtcNow - _startedAt);
        if (remaining <= TimeSpan.Zero)
        {
            _ = RebootNowAsync();
            return;
        }

        CountdownText.Text = $"Rebooting in {Math.Ceiling(remaining.TotalSeconds)} seconds";
    }

    private void Cancel()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _timer.Stop();
        Close();
    }

    private async Task RebootNowAsync()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _timer.Stop();
        Close();
        await SystemPowerService.ExecuteAsync(PowerActionKind.ImmediateReboot).ConfigureAwait(false);
    }
}
