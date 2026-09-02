import { bindValue, trigger } from "cs2/api";

// Must match BulldozerMarqueeUISystem.Group and the binding names registered
// there. Nothing checks these strings at compile time — a typo on either side is
// a silent no-op — so they live in one file rather than being repeated per view.
const GROUP = "BulldozerMarquee";

/** True while the tool owns the cursor and the panel is open. */
export const enabled$ = bindValue<boolean>(GROUP, "Enabled", false);

/** Packed AssetFilter mask; see filters.ts. */
export const filters$ = bindValue<number>(GROUP, "Filters", 0);

/** How many entities the marquee currently covers — updates live during a drag. */
export const selectionCount$ = bindValue<number>(GROUP, "SelectionCount", 0);

/** Mirrors the mod's "Play bulldoze sound" option in the game settings. */
export const playSfx$ = bindValue<boolean>(GROUP, "PlaySfx", true);

/** Mirrors the mod's "Confirm before bulldozing" option in the game settings. */
export const confirmBulldoze$ = bindValue<boolean>(GROUP, "ConfirmBulldoze", true);

/** Selected SelectionMode; persisted between sessions. See modes.ts. */
export const mode$ = bindValue<number>(GROUP, "Mode", 0);

export const toggle = () => trigger(GROUP, "Toggle");
export const toggleFilter = (bit: number) => trigger(GROUP, "ToggleFilter", bit);
export const setAllFilters = () => trigger(GROUP, "SetAllFilters");
export const bulldoze = () => trigger(GROUP, "Bulldoze");
export const clearSelection = () => trigger(GROUP, "ClearSelection");
export const toggleSfx = () => trigger(GROUP, "ToggleSfx");
export const toggleConfirmBulldoze = () => trigger(GROUP, "ToggleConfirmBulldoze");
export const setMode = (mode: number) => trigger(GROUP, "SetMode", mode);
