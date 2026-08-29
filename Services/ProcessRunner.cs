using System.Diagnostics;
using System.Text;
using Serilog;

namespace BootManager.Services;

/// <summary>Spawns external processes and captures their output, tracing every step at Verbose level.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        Log.Verbose("Starting process {FileName} {Arguments}", fileName, arguments);

        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            Log.Verbose("[{FileName} stdout] {Line}", fileName, e.Data);
            stdOut.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            Log.Verbose("[{FileName} stderr] {Line}", fileName, e.Data);
            stdErr.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        Log.Verbose("Process {FileName} exited with code {ExitCode}", fileName, process.ExitCode);

        return new ProcessResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }
}
