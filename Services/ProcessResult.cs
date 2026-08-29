namespace BootManager.Services;

/// <summary>
/// The outcome of running an external command line tool.
/// </summary>
/// <remarks>
/// Both output streams are captured separately because the tools used here are inconsistent about
/// where they report problems: <c>bcdedit</c>, for instance, prints its "access denied" message to
/// standard output rather than standard error. Error messages should therefore consider both.
/// </remarks>
/// <param name="ExitCode">
/// The process exit code. By convention 0 means success and anything else is a failure; some tools
/// use the value to identify the specific error (e.g. Windows error 203, "environment option not found").
/// </param>
/// <param name="StandardOutput">Everything the tool wrote to stdout, including trailing newlines.</param>
/// <param name="StandardError">Everything the tool wrote to stderr, including trailing newlines.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Whether the tool reported success, i.e. an exit code of 0.</summary>
    public bool Succeeded => ExitCode == 0;
}
