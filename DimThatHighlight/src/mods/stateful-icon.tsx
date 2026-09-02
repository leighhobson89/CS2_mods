import styles from "./icons.module.scss";

/**
 * An icon whose states are separate image files, rendered so that changing state
 * costs nothing.
 *
 * The problem this solves: swapping an `<img src>` to a URL that has never been
 * shown makes cohtml fetch it over `coui://` and rasterise it right then, on the
 * frame the pointer arrives. The element renders empty until that finishes, which
 * on BulldozerMarquee's icons was a visible one-to-two second hole where the
 * artwork should be. The files are small, so the cost is not their size — it is
 * that the work happens at all, at the worst possible moment.
 *
 * The fix is to stop treating a state change as a load. Every state is mounted from
 * the start and the change is a pure style flip, so the artwork is already fetched,
 * decoded and on the GPU long before anyone hovers it. It also means the component
 * cannot regress if cohtml's image cache ever evicts, which a preload-and-hope
 * approach would.
 *
 * This mod needs no separate preloader: its only icon is the toolbar button, which
 * `GameTopLeft` mounts when the game loads and never removes, so mounting the three
 * layers here already warms every file the mod can display.
 */
export interface IconStates {
    readonly rest: string;
    readonly hover: string;
    readonly active: string;
}

export interface StatefulIconProps {
    readonly states: IconStates;
    readonly active: boolean;
    readonly hovered: boolean;
    /** Must carry the size: the layers are absolute, so this box has none of its own. */
    readonly className?: string;
}

/** Selected beats hovered beats resting. */
const pick = (states: IconStates, active: boolean, hovered: boolean): string => {
    if (active) {
        return states.active;
    }

    return hovered ? states.hover : states.rest;
};

export const StatefulIcon = ({ states, active, hovered, className }: StatefulIconProps) => {
    const shown = pick(states, active, hovered);
    const layers = [states.rest, states.hover, states.active];

    return (
        <div className={className ? `${styles.stack} ${className}` : styles.stack}>
            {layers.map((src, index) => (
                // Keyed by position, not by src: two states are allowed to share a
                // file, and a src key would collide when they do.
                <img
                    key={index}
                    className={
                        src === shown ? `${styles.layer} ${styles.layerShown}` : styles.layer
                    }
                    src={src}
                />
            ))}
        </div>
    );
};
