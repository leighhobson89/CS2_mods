import { ModRegistrar } from "cs2/modding";
import { BulldozerMarqueePanel } from "mods/filter-panel";
import { BulldozerMarqueeButton } from "mods/mod-button";

const register: ModRegistrar = (moduleRegistry) => {
    moduleRegistry.append("GameTopLeft", BulldozerMarqueeButton);

    // The panel floats in the bottom-left corner and there is no GameBottomLeft
    // mount point, so it goes into the full-screen 'Game' host and positions
    // itself. It renders nothing at all while the tool is inactive.
    moduleRegistry.append("Game", BulldozerMarqueePanel);
};

export default register;
