import { bindValue, trigger } from "cs2/api";

// Must match DimThatHighlightUISystem.Group and the binding names registered
// there. Nothing checks these strings at compile time — a typo on either side is a
// silent no-op — so they live in one file rather than being repeated per view.
const GROUP = "DimThatHighlight";

/** True while the Highlight Properties panel is open. */
export const enabled$ = bindValue<boolean>(GROUP, "Enabled", false);

/** The chosen highlight colour, packed 0xRRGGBB. */
export const colorRgb$ = bindValue<number>(GROUP, "ColorRgb", 0x8080ff);

/**
 * How hard the highlight reads, 0-100. This replaced an opacity slider that did
 * nothing: the outline shader takes the colour's alpha but does not appear to use
 * it as a blend factor, so the only lever that actually moves the outline is the
 * colour itself. 100 is the chosen colour as picked; lower values scale it toward
 * black, and 0 turns the highlight off outright.
 */
export const strengthPercent$ = bindValue<number>(GROUP, "StrengthPercent", 100);

/**
 * The colour the game itself uses, snapshotted in C# before this mod writes
 * anything. Published so the palette can mark it and Reset can be honest about what
 * it restores, rather than the panel carrying its own copy of a constant that a
 * game patch could move.
 */
export const defaultColorRgb$ = bindValue<number>(GROUP, "DefaultColorRgb", 0x8080ff);

export const toggle = () => trigger(GROUP, "Toggle");
export const setColor = (rgb: number) => trigger(GROUP, "SetColor", rgb);

/**
 * Live, and not saved: this fires on every mouse move of a slider drag, and the C#
 * side deliberately keeps the settings file out of that loop. Call {@link commit}
 * when the drag ends.
 */
export const setStrength = (percent: number) => trigger(GROUP, "SetStrength", percent);

/** Writes whatever is currently applied to the settings file. See {@link setStrength}. */
export const commit = () => trigger(GROUP, "Commit");

export const restoreDefault = () => trigger(GROUP, "RestoreDefault");
