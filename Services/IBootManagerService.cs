using BootManager.Models;

namespace BootManager.Services;

/// <summary>Cross-platform abstraction over the UEFI boot manager.</summary>
public interface IBootManagerService
{
    Task<IReadOnlyList<BootEntry>> GetBootEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Selects the given entry to be used for the next boot only.</summary>
    Task SetNextBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Configures the system to enter UEFI/firmware setup on the next boot.</summary>
    Task RequestBootToFirmwareSetupAsync(CancellationToken cancellationToken = default);
}
