namespace BootManager.Models;

/// <summary>A device-type heading and the selectable wake devices beneath it.</summary>
/// <param name="Name">The device type displayed as a group heading.</param>
/// <param name="Devices">Exact device labels used by the wake configuration service.</param>
public sealed record WakeDeviceGroup(string Name, IReadOnlyList<WakeDeviceItem> Devices);

/// <summary>A selectable wake device with its reported Windows wake capability.</summary>
/// <param name="Name">Exact device label used for arming and disarming.</param>
/// <param name="CanWakeFromAny">Whether Windows lists the device in wake_from_any.</param>
public sealed record WakeDeviceItem(string Name, bool CanWakeFromAny)
{
	/// <summary>Explains the reported capability without promising that firmware permits waking.</summary>
	public string Tooltip => CanWakeFromAny
		? "Windows lists this device in wake_from_any. Actual waking still depends on firmware, power and driver settings."
		: "Not reported by Windows as wake_from_any; waking may be limited to particular states.";
}