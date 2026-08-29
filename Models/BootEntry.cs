namespace BootManager.Models;

/// <summary>
/// A single boot option as reported by the platform, in a form the UI can display.
/// </summary>
/// <remarks>
/// This is a C# <c>record</c>: an immutable value type-like class where two instances with equal
/// property values are considered equal. Immutability is intentional - an entry is a snapshot of the
/// firmware state at the time it was read, so it must never be mutated in place. To reflect a change,
/// re-read the entries from the platform instead.
/// <para>
/// <see cref="IsCurrentDefault"/> and <see cref="IsNextBoot"/> are independent: an entry can be the
/// default, the one-time next boot, both, or neither.
/// </para>
/// </remarks>
/// <param name="Id">
/// The platform's identifier for the entry, used when applying changes. Its shape differs per OS:
/// a GUID or alias such as <c>{bootmgr}</c> on Windows, a four hex digit number such as <c>0003</c>
/// on Linux, and a device node name such as <c>disk0s1</c> on macOS.
/// </param>
/// <param name="Description">Human readable label to show in the UI, e.g. "Windows Boot Manager".</param>
/// <param name="IsCurrentDefault">
/// <see langword="true"/> if this is the persistent default, i.e. what the machine boots every time
/// unless overridden.
/// </param>
/// <param name="IsNextBoot">
/// <see langword="true"/> if this entry will be used for the next boot. That is either because a
/// one-time override is armed for it, or - when no override exists - because it is the default.
/// </param>
public sealed record BootEntry(
    string Id,
    string Description,
    bool IsCurrentDefault,
    bool IsNextBoot);
