using BootManager.Models;

namespace BootManager.Services;

/// <summary>
/// Cross-platform abstraction over the firmware's boot manager.
/// </summary>
/// <remarks>
/// <para>
/// UEFI firmware stores its boot configuration in NVRAM variables. The two that matter here are:
/// <list type="bullet">
///   <item><description>
///     <c>BootOrder</c> - the persistent list of boot entries in priority order. Its first entry is
///     what the machine boots by default, every time.
///   </description></item>
///   <item><description>
///     <c>BootNext</c> - a one-time override. The firmware boots this entry on the next start and
///     then deletes the variable, so the normal <c>BootOrder</c> applies again afterwards.
///   </description></item>
/// </list>
/// That distinction is why this interface has two separate "set" methods: changing the next boot
/// entry is a one-shot action that reverts by itself, while changing the default is permanent.
/// </para>
/// <para>
/// Every implementation needs elevated privileges (Administrator on Windows, root on Linux/macOS),
/// because reading and writing firmware variables is a privileged operation. Implementations do not
/// check for this themselves; they let the underlying tool or API fail and surface its error, which
/// keeps the original diagnostic message intact for the user.
/// </para>
/// </remarks>
public interface IBootManagerService
{
	/// <summary>The command line or API call used to list the boot entries.</summary>
	/// <remarks>
	/// These four descriptions exist purely for the tooltips. Showing the real command turns every
	/// button into something the user can verify and reproduce in a terminal instead of having to trust
	/// a label, and it makes bug reports far more precise.
	/// </remarks>
	string EnumerateCommand { get; }

	/// <summary>The command line or API call used to set the one-time next boot entry.</summary>
	string SetNextBootCommand { get; }

	/// <summary>The command line or API call used to set the persistent default boot entry.</summary>
	string SetDefaultBootCommand { get; }

	/// <summary>The command line or API call used to request the firmware setup screen.</summary>
	string FirmwareSetupCommand { get; }
	/// <summary>
	/// Reads the list of boot entries the firmware currently offers.
	/// </summary>
	/// <param name="cancellationToken">Allows the caller to abort a slow enumeration.</param>
	/// <returns>
	/// The entries in firmware boot order, so the first element is the current default. Each entry
	/// carries flags telling whether it is the default and/or the next-boot entry.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The platform tool used for enumeration failed, typically because of missing privileges.
	/// </exception>
	Task<IReadOnlyList<BootEntry>> GetBootEntriesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Selects the given entry to be used for the <b>next boot only</b>.
	/// </summary>
	/// <remarks>
	/// This is a one-time override: the firmware consumes it during the next start and then falls
	/// back to the default entry. Use it to boot into another OS once without changing the machine's
	/// normal behavior. It does not modify the default entry.
	/// </remarks>
	/// <param name="entry">The entry to boot next time. Must come from <see cref="GetBootEntriesAsync"/>.</param>
	/// <param name="cancellationToken">Allows the caller to abort the operation.</param>
	/// <exception cref="InvalidOperationException">The platform tool rejected the change.</exception>
	Task SetNextBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default);

	/// <summary>
	/// Makes the given entry the <b>persistent default</b> that is used for every boot.
	/// </summary>
	/// <remarks>
	/// Unlike <see cref="SetNextBootEntryAsync"/> this change survives reboots and stays in effect
	/// until it is changed again.
	/// </remarks>
	/// <param name="entry">The entry to boot by default. Must come from <see cref="GetBootEntriesAsync"/>.</param>
	/// <param name="cancellationToken">Allows the caller to abort the operation.</param>
	/// <exception cref="InvalidOperationException">The platform tool rejected the change.</exception>
	Task SetDefaultBootEntryAsync(BootEntry entry, CancellationToken cancellationToken = default);

	/// <summary>
	/// Whether <see cref="RequestBootToFirmwareSetupAsync"/> restarts the machine as part of the request.
	/// </summary>
	/// <remarks>
	/// Platforms differ here: Windows can arm the request and leave the machine running, while systemd
	/// only offers <c>systemctl reboot --firmware-setup</c>, which arms and restarts in one step. Callers
	/// use this to warn the user - or offer a countdown - before invoking the request.
	/// </remarks>
	bool FirmwareSetupRestartsImmediately { get; }

	/// <summary>
	/// Configures the machine to open the firmware (UEFI/BIOS) setup screen on the next boot,
	/// so the user does not have to catch the vendor-specific hotkey during startup.
	/// </summary>
	/// <remarks>
	/// Whether this also restarts the machine depends on the platform; see
	/// <see cref="FirmwareSetupRestartsImmediately"/>. Not every firmware supports the request, and
	/// macOS has no scriptable equivalent at all.
	/// </remarks>
	/// <param name="cancellationToken">Allows the caller to abort the operation.</param>
	/// <exception cref="NotSupportedException">
	/// The firmware or operating system cannot be asked to enter setup programmatically.
	/// </exception>
	/// <exception cref="InvalidOperationException">The request failed, typically due to missing privileges.</exception>
	Task RequestBootToFirmwareSetupAsync(CancellationToken cancellationToken = default);
}
