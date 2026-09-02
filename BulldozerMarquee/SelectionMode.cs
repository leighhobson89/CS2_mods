namespace BulldozerMarquee
{
    /// <summary>
    /// How the player draws a selection.
    /// <para>
    /// The numeric values are persisted in the settings file and sent over the
    /// "SetMode" trigger, so they are a stored contract: renumbering them would
    /// silently change what a returning player's saved mode means. Append new modes,
    /// never reorder.
    /// </para>
    /// </summary>
    public enum SelectionMode
    {
        /// <summary>Drag a camera-aligned box. The mode everything else is built for.</summary>
        Marquee = 0,

        /// <summary>Reserved. Selects nothing yet.</summary>
        Freeform = 1,
    }
}
