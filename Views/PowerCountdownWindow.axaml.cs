using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BootManager.Services;

namespace BootManager.Views;

/// <summary>
/// Countdown window shown before the delayed reboot starts.
/// </summary>
public partial class PowerCountdownWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _countdown = TimeSpan.FromSeconds(20);
    private DateTimeOffset _startedAt;

    public PowerCountdownWindow()
    {
        InitializeComponent();
        _startedAt = DateTimeOffset.UtcNow;
        CountdownText.Text = "Rebooting in 20 seconds";
        CancelButton.Click += (_, _) =>
        {
            _cts.Cancel();
            Close();
        };

        _ = RunCountdownAsync();
    }

    private async Task RunCountdownAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var remaining = _countdown - (DateTimeOffset.UtcNow - _startedAt);
                if (remaining <= TimeSpan.Zero)
                {
                    Close();
                    await SystemPowerService.ExecuteAsync(PowerActionKind.ImmediateReboot).ConfigureAwait(false);
                    return;
                }

                CountdownText.Text = $"Rebooting in {Math.Ceiling(remaining.TotalSeconds)} seconds";
                await Task.Delay(250, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // User cancelled the delayed reboot.
        }
    }
}
