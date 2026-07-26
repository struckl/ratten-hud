namespace RattenHUD;

/// <summary>
/// One place to turn a unit into the short string this plugin prints for it.
///
/// The game's own readouts name a unit by <c>definition.code</c> -- the short
/// type designation, the same one the selected target block shows -- so every
/// readout here uses that too rather than inventing a second naming scheme for
/// the same aircraft.
/// </summary>
internal static class UnitNaming
{
    /// <summary>
    /// The short type code, falling back through the display name to the object
    /// name. Definitions ship with a code for everything that can be shot at,
    /// but a modded or unfinished unit can leave it blank, and a blank tag on
    /// the glass reads as a bug rather than as a missing field.
    /// </summary>
    public static string TypeCode(Unit unit)
    {
        if (unit == null)
            return "UNKNOWN";

        if (unit.definition != null)
        {
            if (!string.IsNullOrEmpty(unit.definition.code))
                return unit.definition.code;
            if (!string.IsNullOrEmpty(unit.definition.unitName))
                return unit.definition.unitName;
        }

        return string.IsNullOrEmpty(unit.name) ? "UNKNOWN" : unit.name;
    }
}
