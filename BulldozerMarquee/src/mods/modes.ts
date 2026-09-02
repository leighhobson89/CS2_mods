import freeformIcon from "../../icon/freeform.png";
import freeformSelectedIcon from "../../icon/freeform-selected.png";
import marqueeIcon from "../../icon/marquee.png";
import marqueeSelectedIcon from "../../icon/marquee-selected.png";

/**
 * Mirrors the SelectionMode enum in SelectionMode.cs.
 *
 * The values are persisted in the mod's settings file, not just passed over the
 * wire, so a returning player's saved mode depends on them staying put. Append
 * new modes, never reorder.
 */
export interface ModeDefinition {
    readonly value: number;
    readonly label: string;
    readonly tooltip: string;
    readonly icon: string;
    readonly selectedIcon: string;
}

export const MODES: readonly ModeDefinition[] = [
    {
        value: 0,
        label: "Marquee",
        tooltip: "Marquee — drag a box to select",
        icon: marqueeIcon,
        selectedIcon: marqueeSelectedIcon,
    },
    {
        value: 1,
        label: "Freeform",
        tooltip: "Freeform — not implemented yet",
        icon: freeformIcon,
        selectedIcon: freeformSelectedIcon,
    },
];

export const MARQUEE_MODE = 0;
