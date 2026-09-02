/**
 * The 16 colours the swatch grid offers, as two rows of eight.
 *
 * Row 0 is the twelve-step colour wheel's warm half and then some — red through
 * azure — and row 1 finishes the wheel and adds the neutrals. Between them the two
 * rows are the standard twelve-hue wheel (three primaries, three secondaries, six
 * tertiaries) plus white, two greys and black.
 *
 * Sixteen rather than a generated ramp is deliberate. Every colour here is one
 * somebody can name, so picking is a decision rather than a hunt, and the Strength
 * slider covers everything a lightness ramp used to: any of these at 30% is that
 * colour, darker. A grid of near-identical dark blues was answering a question the
 * slider already answers better.
 */

/** Row 0: red round to azure. */
const WHEEL_WARM: readonly number[] = [
    0xff0000, // red — primary
    0xff8000, // orange — tertiary
    0xffff00, // yellow — secondary
    0x80ff00, // chartreuse — tertiary
    0x00ff00, // green — primary
    0x00ff80, // spring green — tertiary
    0x00ffff, // cyan — secondary
    0x0080ff, // azure — tertiary
];

/** Row 1: the rest of the wheel, then the neutrals. */
const WHEEL_COOL_AND_NEUTRALS: readonly number[] = [
    0x0000ff, // blue — primary
    0x8000ff, // violet — tertiary
    0xff00ff, // magenta — secondary
    0xff0080, // rose — tertiary
    0xffffff, // white
    0xb4b4b4, // light grey
    0x585858, // dark grey
    0x000000, // black
];

export const PALETTE: readonly number[] = [...WHEEL_WARM, ...WHEEL_COOL_AND_NEUTRALS];

/** Packed 0xRRGGBB to the `#rrggbb` a stylesheet wants. */
export const toHex = (rgb: number): string =>
    `#${(rgb & 0xffffff).toString(16).padStart(6, "0")}`;

/**
 * The chosen colour as it will actually be applied, for the preview. Strength scales
 * the colour toward black, which is what the C# side does before writing it — see
 * DimThatHighlightUISystem.ToColor.
 */
export const applyStrength = (rgb: number, strengthPercent: number): number => {
    const factor = Math.max(0, Math.min(100, strengthPercent)) / 100;
    const scale = (channel: number) => Math.round(channel * factor);

    return (
        (scale((rgb >> 16) & 0xff) << 16) |
        (scale((rgb >> 8) & 0xff) << 8) |
        scale(rgb & 0xff)
    );
};
