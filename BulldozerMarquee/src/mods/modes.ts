import freeformIcon from "../../icon/freeform.png";
import freeformSelectedIcon from "../../icon/freeform-selected.png";
import marqueeIcon from "../../icon/marquee.png";
import marqueeSelectedIcon from "../../icon/marquee-selected.png";
import polygonIcon from "../../icon/polygonal.png";
import polygonSelectedIcon from "../../icon/polygonal-selected.png";

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
    readonly selectedIcon: string;
}

export const MODES: readonly ModeDefinition[] = [
    {
        value: 0,
        label: "Marquee",
        tooltip: "Marquee — drag a box to select",
        hint: "Drag a box over the map to select.",
        icon: marqueeIcon,
        selectedIcon: marqueeSelectedIcon,
    },
    {
        value: 2,
        label: "Polygon",
        tooltip: "Polygon — click out corners, click the first one again to close. Right click removes the last corner.",
        hint: "Click to place corners, then click the first one again to close.",
        icon: polygonIcon,
        selectedIcon: polygonSelectedIcon,
    },
    {
        value: 1,
        label: "Freeform",
        tooltip: "Freeform — draw a lasso to select",
        hint: "Draw a loop around what you want to select.",
        icon: freeformIcon,
        selectedIcon: freeformSelectedIcon,
    },
];

/** Falls back to the first mode so the panel never renders an empty hint. */
export const getMode = (value: number): ModeDefinition =>
    MODES.find((definition) => definition.value === value) ?? MODES[0];
