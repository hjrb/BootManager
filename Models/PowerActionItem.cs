using BootManager.Services;

namespace BootManager.Models;

/// <summary>
/// A power action in the form the toolbar needs it: the action itself plus the text shown on its button.
/// </summary>
/// <remarks>
/// The labels and tooltips come from <see cref="SystemPowerService"/>, so the buttons cannot describe
/// something other than what the service actually runs.
/// </remarks>
/// <param name="Kind">The action to execute when the button is clicked.</param>
/// <param name="Label">The button caption.</param>
/// <param name="Tooltip">The explanation and the command line, shown as the button's tooltip.</param>
public sealed record PowerActionItem(PowerActionKind Kind, string Label, string Tooltip)
{
	/// <summary>Builds the display form of a power action.</summary>
	public static PowerActionItem Create(PowerActionKind kind) =>
		new(kind, SystemPowerService.GetLabel(kind), SystemPowerService.GetTooltip(kind));
}
