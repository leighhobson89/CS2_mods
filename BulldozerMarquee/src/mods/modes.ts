import freeformIcon from "../../icon/freeform.svg";
import freeformHoverIcon from "../../icon/freeform-hover.svg";
import freeformSelectedIcon from "../../icon/freeform-selected.svg";
import marqueeIcon from "../../icon/marquee.svg";
import marqueeHoverIcon from "../../icon/marquee-hover.svg";
import marqueeSelectedIcon from "../../icon/marquee-selected.svg";
import polygonIcon from "../../icon/polygonal.svg";
import polygonHoverIcon from "../../icon/polygonal-hover.svg";
import polygonSelectedIcon from "../../icon/polygonal-selected.svg";

/**
 * Mirrors the SelectionMode enum in SelectionMode.cs.
 *
 * The values are the wire format for the "SetMode" trigger, so the two files have
 * to be edited together. The order of this array — not the numbering — is what
 * decides the order of the buttons, which is why Polygon sits in the middle here
 * while still carrying the value it was appended with.
 */
export interface ModeDefinition {
    readonly value: number;
    readonly label: string;
    readonly tooltip: string;
    /** Shown under the actions row while nothing is selected. */
    readonly hint: string;
    readonly icon: string;
    /**
     * Shown while the pointer is over the button and the mode is not active.
     * Selected wins over hover: an active mode keeps its selected icon even
     * under the cursor, since "which mode am I in" matters more than "what am I
     * pointing at".
     */
    readonly hoverIcon: string;
    readonly selectedIcon: string;
}

export const MODES: readonly ModeDefinition[] = [
    {
        value: 0,
        label: "Marquee",
        tooltip: "Marquee — drag a box to select",
        hint: "Drag a box over the map to select.",
        icon: marqueeIcon,
        hoverIcon: marqueeHoverIcon,
        selectedIcon: marqueeSelectedIcon,
    },
    {
        value: 2,
        label: "Polygon",
        tooltip: "Polygon — click out corners, click the first one again to close. Right click removes the last corner.",
        hint: "Click to place corners, then click the first one again to close.",
        icon: polygonIcon,
        hoverIcon: polygonHoverIcon,
        selectedIcon: polygonSelectedIcon,
    },
    {
        value: 1,
        label: "Freeform",
        tooltip: "Freeform — draw a lasso to select",
        hint: "Draw a loop around what you want to select.",
        icon: freeformIcon,
        hoverIcon: freeformHoverIcon,
        selectedIcon: freeformSelectedIcon,
    },
];

/** Falls back to the first mode so the panel never renders an empty hint. */
export const getMode = (value: number): ModeDefinition =>
    MODES.find((definition) => definition.value === value) ?? MODES[0];

/** Icon precedence for a mode button: selected beats hovered beats resting. */
export const getModeIcon = (
    definition: ModeDefinition,
    active: boolean,
    hovered: boolean,
): string => {
    if (active) {
        return definition.selectedIcon;
    }

    return hovered ? definition.hoverIcon : definition.icon;
};
