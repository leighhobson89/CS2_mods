import { useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";
import iconOff from "../../icon/toggle-off.png";
import iconOn from "../../icon/toggle-on.png";
import { enabled$, toggle } from "./bindings";

// No positioning styles: the 'GameTopLeft' host this is appended to is the flex
// row that lays out mod buttons, so it self-sorts alongside the other mods' icons.
export const BulldozerMarqueeButton = () => {
    const enabled = useValue(enabled$);

    return (
        <Tooltip
            tooltip={
                enabled
                    ? "Bulldozer Marquee: drag to select, right click to cancel"
                    : "Bulldozer Marquee"
            }
        >
            <Button
                src={enabled ? iconOn : iconOff}
                variant="floating"
                selected={enabled}
                onSelect={toggle}
            />
        </Tooltip>
    );
};
