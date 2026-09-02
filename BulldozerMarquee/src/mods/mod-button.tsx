import { useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";
import { useState } from "react";
import iconOff from "../../icon/toggle-off.svg";
import iconOffHover from "../../icon/toggle-off-hover.svg";
import iconOn from "../../icon/toggle-on.svg";
import { enabled$, toggle } from "./bindings";

// No positioning styles: the 'GameTopLeft' host this is appended to is the flex
// row that lays out mod buttons, so it self-sorts alongside the other mods' icons.
// That also rules out wrapping this in a div to catch the hover — a wrapper would
// become the flex child and the button would stop sorting with its neighbours — so
// the handlers go on the Button, which forwards them (ButtonProps extends
// React.ButtonHTMLAttributes).
export const BulldozerMarqueeButton = () => {
    const enabled = useValue(enabled$);
    const [hovered, setHovered] = useState(false);

    // There is no toggle-on-hover artwork, and none is needed: while the tool is
    // active the button is drawn in the vanilla selected style, which is already
    // its own state. Hover art only has to distinguish "off" from "off, aimed at".
    const src = enabled ? iconOn : hovered ? iconOffHover : iconOff;

    return (
        <Tooltip
            tooltip={
                enabled
                    ? "Bulldozer Marquee: drag to select, right click to cancel"
                    : "Bulldozer Marquee"
            }
        >
            <Button
                src={src}
                variant="floating"
                selected={enabled}
                onSelect={toggle}
                onMouseEnter={() => setHovered(true)}
                onMouseLeave={() => setHovered(false)}
            />
        </Tooltip>
    );
};
