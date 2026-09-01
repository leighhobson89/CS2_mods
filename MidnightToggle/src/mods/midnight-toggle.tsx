import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";
import iconOff from "../../icon/midnightToggle.png";
import iconOn from "../../icon/midnightToggleOn.png";

// Must match MidnightToggleUISystem.Group / the binding names registered there.
const GROUP = "MidnightToggle";
const enabled$ = bindValue<boolean>(GROUP, "Enabled", false);

// No positioning styles: the 'GameTopLeft' host this is appended to is the flex row
// that lays out mod buttons, so it self-sorts alongside the other mods' icons.
export const MidnightToggle = () => {
    const enabled = useValue(enabled$);

    return (
        <Tooltip tooltip={enabled ? "Midnight: on" : "Midnight: off"}>
            <Button
                src={enabled ? iconOn : iconOff}
                variant="floating"
                selected={enabled}
                onSelect={() => trigger(GROUP, "Toggle")}
            />
        </Tooltip>
    );
};
