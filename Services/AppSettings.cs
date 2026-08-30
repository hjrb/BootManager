using Microsoft.Extensions.Configuration;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// The values of the <c>BootManager</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a decision a user may reasonably want to change without rebuilding the program:
/// how long a countdown runs, and which processes must never be asked to close. The list of protected
/// processes in particular cannot be complete - every desktop environment names its session processes
/// differently - so it lives in a text file the user can extend when a name is missing.
/// </para>
/// <para>
/// Each value is read on first use and then kept. The configuration is loaded without
/// <c>reloadOnChange</c>, so re-reading would not see later edits anyway, and a running countdown must
/// not change its length halfway through.
/// </para>
/// </remarks>
public static class AppSettings
{
	private const string SectionName = "BootManager";

	private static readonly Lazy<TimeSpan> LazyPowerCountdown =
		new(() => TimeSpan.FromSeconds(ReadPositiveInt("PowerCountdownSeconds", 20)));

	private static readonly Lazy<TimeSpan> LazyCloseApplicationsGracePeriod =
		new(() => TimeSpan.FromSeconds(ReadPositiveInt("CloseApplicationsGracePeriodSeconds", 10)));

	private static readonly Lazy<IReadOnlyList<string>> LazyProtectedProcessNamesWindows =
		new(() => ReadStringList(ProtectedProcessNamesWindowsKey));

	private static readonly Lazy<IReadOnlyList<string>> LazyProtectedProcessNamesUnix =
		new(() => ReadStringList(ProtectedProcessNamesUnixKey));

	/// <summary>Name of the setting holding the Windows list, for use in error messages.</summary>
	public const string ProtectedProcessNamesWindowsKey = "ProtectedProcessNamesWindows";

	/// <summary>Name of the setting holding the Linux and macOS list, for use in error messages.</summary>
	public const string ProtectedProcessNamesUnixKey = "ProtectedProcessNamesUnix";

	/// <summary>How long the countdown window waits before it carries out its action.</summary>
	public static TimeSpan PowerCountdown => LazyPowerCountdown.Value;

	/// <summary>How long an application may take to disappear before it counts as "still running".</summary>
	public static TimeSpan CloseApplicationsGracePeriod => LazyCloseApplicationsGracePeriod.Value;

	/// <summary>
	/// Windows processes that own a window but are part of the shell rather than an application.
	/// Closing Explorer's window, for example, can take the taskbar and the desktop with it.
	/// </summary>
	public static IReadOnlyList<string> ProtectedProcessNamesWindows => LazyProtectedProcessNamesWindows.Value;

	/// <summary>
	/// Linux and macOS processes that run the session: the init system, the display server, the
	/// desktop shell, the session bus and the audio stack. Terminating any of these logs the user out.
	/// </summary>
	public static IReadOnlyList<string> ProtectedProcessNamesUnix => LazyProtectedProcessNamesUnix.Value;

	/// <summary>
	/// Reads a whole number that must be greater than zero.
	/// </summary>
	/// <remarks>
	/// A missing or nonsensical value falls back to the built-in default rather than failing, because
	/// none of these settings is worth refusing to start over. Zero and negatives are rejected as well:
	/// a countdown of zero would remove the very pause the window exists for.
	/// </remarks>
	private static int ReadPositiveInt(string key, int fallback)
	{
		var text = GetSection()?[key];
		if (text is null)
		{
			return fallback;
		}

		if (int.TryParse(text, out var value) && value > 0)
		{
			return value;
		}

		Log.Warning("Setting {Section}:{Key} is not a positive whole number ({Value}); using {Fallback}",
			SectionName, key, text, fallback);
		return fallback;
	}

	/// <summary>
	/// Reads a JSON array of strings.
	/// </summary>
	/// <returns>
	/// The entries, or an empty list when the setting is absent. Callers decide what an empty list
	/// means - for the protected process names it is a refusal to continue, not a permission.
	/// </returns>
	private static IReadOnlyList<string> ReadStringList(string key)
	{
		// Get<string[]>() understands both a JSON array and the "Key:0", "Key:1" form that environment
		// variables and the command line have to use for the same setting.
		var values = GetSection()?.GetSection(key).Get<string[]>() ?? [];
		Log.Verbose("Read {Count} entries from setting {Section}:{Key}", values.Length, SectionName, key);
		return values;
	}

	/// <summary>
	/// Returns the configuration section, or <see langword="null"/> when there is no configuration.
	/// </summary>
	/// <remarks>
	/// <c>Program.Main</c> assigns the configuration before anything else runs, so null only happens in
	/// the XAML designer, which instantiates windows without going through the entry point.
	/// </remarks>
	private static IConfigurationSection? GetSection() => App.Configuration?.GetSection(SectionName);
}
