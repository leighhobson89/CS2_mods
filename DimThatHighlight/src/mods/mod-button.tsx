import { useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";
import { useState } from "react";
import iconHover from "../../icon/highlight-hover.svg";
import iconOn from "../../icon/highlight-on.svg";
import iconRest from "../../icon/highlight.svg";
import { enabled$, toggle } from "./bindings";
import styles from "./icons.module.scss";
import { StatefulIcon } from "./stateful-icon";

const ICONS = {
    rest: iconRest,
    hover: iconHover,
    active: iconOn,
};

// No positioning styles on the button: the 'GameTopLeft' host is the flex row that
// lays out mod buttons, so it self-sorts alongside the other mods' icons. That also
// rules out wrapping it to catch the hover — a wrapper would become the flex child
// and it would stop sorting with its neighbours — so the handlers go on the Button,
// which forwards them (ButtonProps extends React.ButtonHTMLAttributes).
export const DimThatHighlightButton = () => {
    const enabled = useValue(enabled$);
    const [hovered, setHovered] = useState(false);

    return (
        <Tooltip tooltip="Highlight Properties">
            {/* The artwork goes in as children rather than as the `src` prop. A `src`
                is one <img> whose source is rewritten on hover, which is the lazy
                fetch StatefulIcon exists to avoid; children let the stacked-layer
                treatment apply here. `variant` styles the button chrome, not its
                contents, so this is not fighting the vanilla component. */}
            <Button
                variant="floating"
                selected={enabled}
                onSelect={toggle}
                onMouseEnter={() => setHovered(true)}
                onMouseLeave={() => setHovered(false)}
            >
                <StatefulIcon
                    className={styles.buttonIcon}
                    states={ICONS}
                    active={enabled}
                    hovered={hovered}
                />
            </Button>
        </Tooltip>
    );
};
