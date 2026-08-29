using System.Diagnostics;
using System.Reflection;
using Serilog;

namespace BootManager.Services;

/// <summary>
/// Supplies the facts shown on the About screen: the version the running binary was built as, and
/// where to find the project and its licence documents.
/// </summary>
/// <remarks>
/// The version is read from the assembly rather than hard-coded, so it always matches the binary the
/// user actually started. The release pipeline stamps it in through <c>/p:Version=</c>, which means a
/// bug report quoting this number identifies the exact release.
/// </remarks>
public static class AppInfo
{
	/// <summary>Home of the project; the About screen links to it.</summary>
	public const string RepositoryUrl = "https://github.com/hjrb/BootManager";

	/// <summary>The project's own licence, used when no local copy was deployed.</summary>
	public const string LicenseUrl = "https://github.com/hjrb/BootManager/blob/main/LICENSE";

	/// <summary>The open source compliance document, used when no local copy was deployed.</summary>
	public const string ThirdPartyNoticesUrl = "https://github.com/hjrb/BootManager/blob/main/THIRD-PARTY-NOTICES.md";

	/// <summary>File name of the licence as it is deployed next to the executable.</summary>
	public const string LicenseFileName = "LICENSE";

	/// <summary>File name of the compliance document as it is deployed next to the executable.</summary>
	public const string ThirdPartyNoticesFileName = "THIRD-PARTY-NOTICES.md";

	private static readonly Assembly EntryAssembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;

	/// <summary>The product name recorded in the binary, for example "BootManager".</summary>
	public static string ProductName =>
		EntryAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "BootManager";

	/// <summary>
	/// The version of the running binary, for example "1.4.0".
	/// </summary>
	/// <remarks>
	/// Taken from the informational version, which is what the build sets from <c>/p:Version=</c>. The
	/// .NET SDK appends the source revision as "+&lt;commit sha&gt;" to that attribute; everything from
	/// the plus sign on is cut off, because it is noise to a user reading an About box.
	/// </remarks>
	public static string Version
	{
		get
		{
			var informational = EntryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			if (string.IsNullOrWhiteSpace(informational))
			{
				return EntryAssembly.GetName().Version?.ToString() ?? "unknown";
			}

			var metadataStart = informational.IndexOf('+');
			return metadataStart < 0 ? informational : informational[..metadataStart];
		}
	}

	/// <summary>The copyright line recorded in the binary.</summary>
	public static string Copyright =>
		EntryAssembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Copyright BootManager contributors";

	/// <summary>
	/// Returns the full path of a document deployed next to the executable, or <see langword="null"/>
	/// when it is not there.
	/// </summary>
	/// <param name="fileName">File name as it appears in the publish output, e.g. "LICENSE".</param>
	/// <remarks>
	/// A published build ships these files, but a developer running from a source checkout may not have
	/// copied them yet, and a user is free to delete them. The caller falls back to the online copy in
	/// that case, so the About screen never offers a dead link.
	/// </remarks>
	public static string? FindDeployedDocument(string fileName)
	{
		var path = Path.Combine(AppContext.BaseDirectory, fileName);
		return File.Exists(path) ? path : null;
	}

	/// <summary>
	/// Hands a URL or a file path to the desktop so it opens in whatever the user has configured.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>UseShellExecute</c> is what makes this work at all and what makes it portable: without it the
	/// target would have to be an executable. With it, Windows resolves the registered handler, macOS
	/// goes through <c>open</c> and Linux through <c>xdg-open</c>.
	/// </para>
	/// <para>
	/// Failures are swallowed on purpose. A machine without a browser, or a Linux session without
	/// <c>xdg-open</c>, is not a reason to interrupt the user with an error - the About screen also
	/// shows the addresses as selectable text, so they can still be copied.
	/// </para>
	/// </remarks>
	/// <param name="target">An absolute URL or an existing local path.</param>
	public static void OpenInDefaultApplication(string target)
	{
		try
		{
			Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Could not open {Target} in the default application", target);
		}
	}
}
