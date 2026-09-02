import { useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";
import { useState } from "react";
import iconOff from "../../icon/toggle-off.svg";
import iconOffHover from "../../icon/toggle-off-hover.svg";
import iconOn from "../../icon/toggle-on.svg";
import { enabled$, toggle } from "./bindings";
import styles from "./icons.module.scss";
import { ALL_MODE_ICONS } from "./modes";
import { IconPreloader, StatefulIcon } from "./stateful-icon";

// There is no toggle-on-hover artwork, and none is needed: while the tool is active
// the button is drawn in the vanilla selected style, which is already its own
// state. Hover art only has to tell "off" from "off, aimed at".
const TOGGLE_ICONS = {
    rest: iconOff,
    hover: iconOffHover,
    active: iconOn,
};

// Every image this mod can display. The panel is unmounted while the tool is off,
// so it cannot warm its own icons — this component can, because 'GameTopLeft'
// mounts it when the game loads and never takes it away. Without this the first
// open of the panel is the moment nine mode icons get fetched, and the mode bar
// sits blank while that happens.
const ALL_ICONS = [...Object.values(TOGGLE_ICONS), ...ALL_MODE_ICONS];

// No positioning styles on the button: the 'GameTopLeft' host is the flex row that
// lays out mod buttons, so it self-sorts alongside the other mods' icons. That also
// rules out wrapping it to catch the hover — a wrapper would become the flex child
// and it would stop sorting with its neighbours — so the handlers go on the Button,
// which forwards them (ButtonProps extends React.ButtonHTMLAttributes). The
// preloader is a sibling for the same reason, and is fixed out of flow so it adds
// nothing to the row.
export const BulldozerMarqueeButton = () => {
    const enabled = useValue(enabled$);
    const [hovered, setHovered] = useState(false);

    return (
        <>
            <Tooltip
                tooltip={
                    enabled
                        ? "Bulldozer Marquee: drag to select, right click to cancel"
                        : "Bulldozer Marquee"
                }
            >
                {/* The artwork goes in as children rather than as the `src` prop.
                    A `src` is one <img> whose source is rewritten on hover, which
                    is the lazy fetch this is trying to avoid; children let the
                    same stacked-layer treatment the mode buttons use apply here.
                    Passing children to a vanilla Button is already how the mode
                    bar renders, so this is not new ground — `variant` styles the
                    button chrome, not its contents. */}
                <Button
                    variant="floating"
                    selected={enabled}
                    onSelect={toggle}
                    onMouseEnter={() => setHovered(true)}
                    onMouseLeave={() => setHovered(false)}
                >
                    <StatefulIcon
                        className={styles.buttonIcon}
                        states={TOGGLE_ICONS}
                        active={enabled}
                        hovered={hovered}
                    />
                </Button>
            </Tooltip>

            <IconPreloader sources={ALL_ICONS} />
        </>
    );
};
