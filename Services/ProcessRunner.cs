using System.Diagnostics;
using System.Text;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Runs external command line tools and captures everything they print.
/// </summary>
/// <remarks>
/// The application has no NuGet package available for manipulating firmware boot entries, so it
/// drives the operating system's own tools instead. Centralizing that here means the full command
/// line and all of its output land in the log, which is what makes failures diagnosable after the
/// fact - the tools' error messages are often the only clue about what the firmware rejected.
/// </remarks>
public static class ProcessRunner
{
	/// <summary>
	/// Starts a process, waits for it to finish, and returns its exit code and output.
	/// </summary>
	/// <remarks>
	/// Output is read asynchronously through events rather than by reading the streams after exit.
	/// This avoids a classic deadlock: the operating system's pipe buffer is limited, so a tool that
	/// produces more output than fits will block forever waiting for someone to drain it, while the
	/// caller blocks forever waiting for the tool to exit.
	/// <para>
	/// The process is started without a shell and without a window, so no console flashes up in front
	/// of the user, and arguments are passed straight to the program rather than being interpreted by
	/// a command interpreter.
	/// </para>
	/// </remarks>
	/// <param name="fileName">Executable to start, e.g. <c>bcdedit.exe</c> or <c>efibootmgr</c>.</param>
	/// <param name="arguments">
	/// The command line arguments as a single string. Only used with fixed, application-controlled
	/// values here; user supplied values would need to be quoted or passed via an argument list.
	/// </param>
	/// <param name="cancellationToken">Aborts the wait if the tool hangs.</param>
	/// <returns>The exit code together with the captured standard output and standard error.</returns>
	public static async Task<ProcessResult> RunAsync(
		string fileName,
		string arguments,
		CancellationToken cancellationToken = default)
	{
		Log.Verbose("Starting process {FileName} {Arguments}", fileName, arguments);

		var startInfo = new ProcessStartInfo(fileName, arguments)
		{
			// Redirecting requires UseShellExecute = false; that also avoids going through a shell at all.
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		using var process = new Process { StartInfo = startInfo };
		var stdOut = new StringBuilder();
		var stdErr = new StringBuilder();

		// Each line is logged as it arrives, so even a tool that later hangs leaves a usable trace.
		process.OutputDataReceived += (_, e) =>
		{
			// A null Data marks the end of the stream rather than an empty line.
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

		// Must be called after Start(); these begin the asynchronous pumping of both pipes.
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

		Log.Verbose("Process {FileName} exited with code {ExitCode}", fileName, process.ExitCode);

		return new ProcessResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
	}
}
