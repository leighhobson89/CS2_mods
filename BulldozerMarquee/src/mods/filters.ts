/**
 * Mirrors the AssetFilter flags in Filters.cs.
 *
 * The bit value is the wire format for the "ToggleFilter" trigger, so these are
 * not display-order indices and the two files have to be edited together.
 */
export interface FilterDefinition {
    readonly bit: number;
    readonly label: string;
    /** Shown on hover, to explain what the category actually covers. */
    readonly tooltip: string;
}

export const FILTERS: readonly FilterDefinition[] = [
    { bit: 1 << 0, label: "Trees", tooltip: "Standalone trees" },
    { bit: 1 << 1, label: "Props", tooltip: "Standalone props and decorations" },
    { bit: 1 << 2, label: "Nodes", tooltip: "Network nodes — road and track junctions" },
    { bit: 1 << 3, label: "Segments", tooltip: "Network segments — the spans between nodes" },
    { bit: 1 << 4, label: "Buildings", tooltip: "Buildings, both service and zoned" },
    { bit: 1 << 5, label: "Surfaces", tooltip: "Ploppable surfaces and painted areas" },
    { bit: 1 << 6, label: "Netlanes", tooltip: "Standalone lanes — fences, hedges, markings" },
];

export const ALL_FILTERS = FILTERS.reduce((mask, filter) => mask | filter.bit, 0);
