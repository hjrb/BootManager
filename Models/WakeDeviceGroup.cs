namespace BootManager.Models;

/// <summary>A device-type heading and the selectable wake devices beneath it.</summary>
/// <param name="Name">The device type displayed as a group heading.</param>
/// <param name="Devices">Exact device labels used by the wake configuration service.</param>
public sealed record WakeDeviceGroup(string Name, IReadOnlyList<string> Devices);