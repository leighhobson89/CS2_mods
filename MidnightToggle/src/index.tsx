import { ModRegistrar } from "cs2/modding";
import { MidnightToggle } from "mods/midnight-toggle";

const register: ModRegistrar = (moduleRegistry) => {

    moduleRegistry.append('GameTopLeft', MidnightToggle);
}

export default register;