namespace BootManager.Models;

/// <summary>Diagnostic facts that share one category heading in the user interface.</summary>
/// <param name="Category">The category heading shared by all diagnostic facts in the group.</param>
/// <param name="Items">The diagnostic facts to display beneath the category heading.</param>
public sealed record SystemInfoGroup(string Category, IReadOnlyList<SystemInfoItem> Items);