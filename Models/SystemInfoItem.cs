namespace BootManager.Models;

/// <summary>
/// One piece of diagnostic information about the machine, ready to be listed in the UI.
/// </summary>
/// <remarks>
/// Deliberately a flat label/value pair rather than a strongly typed "system info" class with one
/// property per fact. The available facts differ per operating system, and each platform can report
/// extra details that the others do not have. A flat list lets every implementation contribute
/// whatever it can discover without the model having to know about it in advance, and it is trivially
/// copyable as text for a bug report.
/// </remarks>
/// <param name="Category">Grouping shown as a section header, e.g. "Firmware" or "Boot timing".</param>
/// <param name="Label">Name of the fact, e.g. "Secure Boot".</param>
/// <param name="Value">
/// The value already formatted for display. Implementations put "Unknown" or an explanatory message
/// here rather than omitting the item, so the user can tell the difference between "not applicable"
/// and "we failed to read it".
/// </param>
public sealed record SystemInfoItem(string Category, string Label, string Value);
