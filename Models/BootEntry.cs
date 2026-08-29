namespace BootManager.Models;

/// <summary>A single UEFI boot option as reported by the underlying platform.</summary>
public sealed record BootEntry(
    string Id,
    string Description,
    bool IsCurrentDefault,
    bool IsNextBoot);
