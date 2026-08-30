using Avalonia.Controls;
using Avalonia.Interactivity;
using BootManager.Services;

namespace BootManager.Views;

/// <summary>
/// The About dialog: which build is running, who it belongs to, and where the licence documents are.
/// </summary>
/// <remarks>
/// The texts are assigned in code rather than bound to a view model because they are constants of the
/// running binary - there is no state here that could change while the dialog is open.
/// </remarks>
public partial class AboutWindow : Window
{
	public AboutWindow()
	{
		InitializeComponent();

		VersionText.Text = $"Version {AppInfo.Version}";
		CopyrightText.Text = AppInfo.Copyright;
	}

	private void OnOpenRepository(object? sender, RoutedEventArgs e) =>
		AppInfo.OpenInDefaultApplication(AppInfo.RepositoryUrl);

	private void OnOpenLicense(object? sender, RoutedEventArgs e) =>
		OpenDocument(AppInfo.LicenseUrl, AppInfo.LicenseFileName);

	private void OnOpenThirdPartyNotices(object? sender, RoutedEventArgs e) =>
		OpenDocument(AppInfo.ThirdPartyNoticesUrl, AppInfo.ThirdPartyNoticesHtmlFileName, AppInfo.ThirdPartyNoticesFileName);

	/// <summary>
	/// Opens the copy of a licence document that was deployed with the application, falling back to
	/// the version on the project page when it is not present.
	/// </summary>
	/// <remarks>
	/// The local copy is preferred because it is the one that belongs to <em>this</em> build, and
	/// because it also works without a network connection.
	/// </remarks>
	/// <param name="fallbackUrl">Address of the same document on the project page.</param>
	/// <param name="fileNames">
	/// Names of the deployed file, most readable form first: a published build carries the compliance
	/// document as HTML, a plain source build only as Markdown.
	/// </param>
	private static void OpenDocument(string fallbackUrl, params string[] fileNames) =>
		AppInfo.OpenInDefaultApplication(AppInfo.FindDeployedDocument(fileNames) ?? fallbackUrl);

	private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
