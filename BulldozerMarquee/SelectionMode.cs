namespace BulldozerMarquee
{
    /// <summary>
    /// How the player draws a selection.
    /// <para>
    /// The numeric values are the wire format for the "SetMode" trigger, so
    /// src/mods/modes.ts mirrors this list and the two have to be edited together.
    /// They are no longer persisted, so the numbering is not a stored contract —
    /// but the order the buttons appear in is decided by the MODES array on the
    /// TypeScript side, not here, so there is still never a reason to renumber.
    /// </para>
    /// </summary>
    public enum SelectionMode
    {
        /// <summary>Drag a camera-aligned box. The mode everything else is built for.</summary>
        Marquee = 0,

        /// <summary>Drag a freehand loop; the cursor closes it back to the start.</summary>
        Freeform = 1,

        /// <summary>
        /// Click out an outline vertex by vertex, closing it by clicking the first
        /// vertex again. Unlike the other two this is not a drag at all, which is why
        /// it takes its own input path in the tool system.
        /// </summary>
        Polygon = 2,
    }
}
