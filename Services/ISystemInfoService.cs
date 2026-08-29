using BootManager.Models;

namespace BootManager.Services;

/// <summary>
/// Collects boot related diagnostic information about the machine.
/// </summary>
/// <remarks>
/// This exists to answer the questions that come up when a machine boots into the wrong system, boots
/// slowly, or refuses a boot entry: which firmware is in use, is the machine actually running in UEFI
/// mode, is Secure Boot blocking an entry, how long did the last boot take, and is a platform feature
/// such as Windows Fast Startup interfering.
/// <para>
/// Implementations are expected to be tolerant: a single fact that cannot be read must not prevent the
/// remaining ones from being reported, because partial information is still useful for troubleshooting.
/// </para>
/// </remarks>
public interface ISystemInfoService
{
    /// <summary>
    /// Gathers everything that is known about the machine's boot configuration and history.
    /// </summary>
    /// <param name="cancellationToken">Allows the caller to abort slow platform queries.</param>
    /// <returns>
    /// The collected facts, grouped by category and in a sensible display order. Values that could not
    /// be determined are still present, carrying an explanatory text instead of being dropped.
    /// </returns>
    Task<IReadOnlyList<SystemInfoItem>> GetSystemInfoAsync(CancellationToken cancellationToken = default);
}
