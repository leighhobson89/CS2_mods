import { ModRegistrar } from "cs2/modding";
import { HighlightPanel } from "mods/highlight-panel";
import { DimThatHighlightButton } from "mods/mod-button";

const register: ModRegistrar = (moduleRegistry) => {
    moduleRegistry.append("GameTopLeft", DimThatHighlightButton);

    // The panel floats in the bottom-left corner and there is no GameBottomLeft mount
    // point, so it goes into the full-screen 'Game' host and positions itself. It
    // renders nothing at all while it is closed.
    moduleRegistry.append("Game", HighlightPanel);
};

export default register;
