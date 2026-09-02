using System;

namespace BulldozerMarquee
{
    /// <summary>
    /// The asset categories a marquee drag is allowed to pick up.
    /// <para>
    /// The numeric values travel over the "ToggleFilter" trigger as a bit index and
    /// come back to React as a packed mask, so they are part of the UI contract:
    /// reordering them silently remaps every checkbox. src/mods/filters.ts mirrors
    /// this list and must be edited alongside it.
    /// </para>
    /// </summary>
    [Flags]
    public enum AssetFilter
    {
        None = 0,

        Trees = 1 << 0,
        Props = 1 << 1,
        Nodes = 1 << 2,
        Segments = 1 << 3,
        Buildings = 1 << 4,
        Surfaces = 1 << 5,
        NetLanes = 1 << 6,

        All = Trees | Props | Nodes | Segments | Buildings | Surfaces | NetLanes,
    }
}
