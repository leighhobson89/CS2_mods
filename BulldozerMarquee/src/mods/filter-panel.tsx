import { useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";
import { useEffect, useRef, useState } from "react";
import bulldozeIcon from "../../icon/toggle-on.png";
import {
    bulldoze,
    clearSelection,
    confirmBulldoze$,
    enabled$,
    filters$,
    mode$,
    playSfx$,
    pruneOnFilterChange$,
    selectionClamped$,
    selectionCount$,
    setAllFilters,
    setMode,
    toggleConfirmBulldoze,
    toggleFilter,
    togglePruneOnFilterChange,
    toggleSfx,
} from "./bindings";
import { ALL_FILTERS, FILTERS } from "./filters";
import { getMode, MODES } from "./modes";
import styles from "./filter-panel.module.scss";

interface DragOrigin {
    readonly mouseX: number;
    readonly mouseY: number;
    readonly offsetX: number;
    readonly offsetY: number;
}

/**
 * The filter list, modelled on Move It's marquee filter panel: a checkbox per
 * asset category that gates what a drag is allowed to pick up, with the bulldoze
 * action for the resulting selection underneath.
 *
 * Only visibility and window position are decided here — the filter mask, the
 * selection and the SFX setting all live in C#.
 */
export const BulldozerMarqueePanel = () => {
    const enabled = useValue(enabled$);
    const filters = useValue(filters$);
    const selectionCount = useValue(selectionCount$);
    const playSfx = useValue(playSfx$);
    const confirmBeforeBulldoze = useValue(confirmBulldoze$);
    const pruneOnFilterChange = useValue(pruneOnFilterChange$);
    const mode = useValue(mode$);
    const selectionClamped = useValue(selectionClamped$);

    // Displacement from the resting position in the stylesheet. Closing the panel
    // renders null but does not unmount this component, so a position the player
    // chose survives toggling the tool off and on again.
    const [offset, setOffset] = useState({ x: 0, y: 0 });
    const [dragging, setDragging] = useState(false);
    const dragOrigin = useRef<DragOrigin | null>(null);
    const panelRef = useRef<HTMLDivElement>(null);
    const [confirming, setConfirming] = useState(false);

    // Alt-tabbing mid-drag never delivers the mouseup, which used to leave the drag
    // armed with a stale anchor: the next mouse move on returning applied the whole
    // accumulated delta at once and flung the panel off-screen, which looked like
    // the panel had disappeared while the toolbar icon still read as enabled.
    useEffect(() => {
        const onBlur = () => {
            dragOrigin.current = null;
            setDragging(false);
        };

        window.addEventListener("blur", onBlur);

        return () => window.removeEventListener("blur", onBlur);
    }, []);

    // Leaving the tool (Escape, or picking another tool) hides the panel without
    // unmounting it, so any transient state has to be cleared explicitly or it is
    // still sitting there — mid-prompt, mid-drag — the next time it opens.
    useEffect(() => {
        if (!enabled) {
            setConfirming(false);
            setDragging(false);
            dragOrigin.current = null;
        }
    }, [enabled]);

    if (!enabled) {
        return null;
    }

    const allEnabled = (filters & ALL_FILTERS) === ALL_FILTERS;
    const hasSelection = selectionCount > 0;

    const onHeaderMouseDown = (event: React.MouseEvent) => {
        dragOrigin.current = {
            mouseX: event.clientX,
            mouseY: event.clientY,
            offsetX: offset.x,
            offsetY: offset.y,
        };
        setDragging(true);
    };

    /**
     * Keeps the panel — and specifically its header, the only drag handle — inside
     * the viewport, so it can never be dragged somewhere it cannot be dragged back
     * from.
     */
    const clamp = (next: { x: number; y: number }) => {
        const element = panelRef.current;

        if (element === null) {
            return next;
        }

        const rect = element.getBoundingClientRect();

        // The rect already includes the current transform; subtracting it gives the
        // stylesheet's resting position, which is what the offset is relative to.
        const restLeft = rect.left - offset.x;
        const restTop = rect.top - offset.y;
        const margin = 30;

        return {
            x: Math.min(
                Math.max(next.x, margin - restLeft - rect.width),
                window.innerWidth - margin - restLeft,
            ),
            // Clamped so the top of the panel stays on screen rather than merely some
            // of it — losing the header would make the panel unmovable.
            y: Math.min(
                Math.max(next.y, margin - restTop),
                window.innerHeight - margin - restTop,
            ),
        };
    };

    const onShieldMouseMove = (event: React.MouseEvent) => {
        const origin = dragOrigin.current;

        if (origin === null) {
            return;
        }

        // Anchored to where the drag started rather than accumulated per event, so a
        // dropped frame cannot make the panel drift away from the cursor.
        setOffset(clamp({
            x: origin.offsetX + event.clientX - origin.mouseX,
            y: origin.offsetY + event.clientY - origin.mouseY,
        }));
    };

    const endDrag = () => {
        dragOrigin.current = null;
        setDragging(false);
    };

    const onBulldozePressed = () => {
        if (!hasSelection) {
            return;
        }

        if (confirmBeforeBulldoze) {
            setConfirming(true);
            return;
        }

        bulldoze();
    };

    // The sound is fired by the C# Bulldoze handler, so routing confirmation
    // through the same trigger means it plays on the confirm rather than on the
    // click that only opened a prompt.
    const onConfirm = () => {
        setConfirming(false);
        bulldoze();
    };

    const onCancel = () => {
        setConfirming(false);
        clearSelection();
    };

    return (
        <>
            {/*
                A drag needs mouse events from the whole screen, because the cursor
                outruns the panel instantly. Listening on `window` does not work
                here: the surrounding HUD is pointer-events:none, so once the cursor
                leaves the panel the UI layer stops receiving mouse events entirely —
                they go to the game. This shield is a real pointer-events:auto surface
                covering the viewport for the duration of the drag.

                It must sit ABOVE the panel (see the z-index in the stylesheet).
                Painted underneath, the panel stole every move event the moment it
                caught up with the cursor — which, since it is being dragged by that
                cursor, was immediately and constantly.
            */}
            {dragging && (
                <div
                    className={styles.dragShield}
                    onMouseMove={onShieldMouseMove}
                    onMouseUp={endDrag}
                />
            )}

            <div
                ref={panelRef}
                className={styles.panel}
                style={{ transform: `translate(${offset.x}px, ${offset.y}px)` }}
            >
                {/* Doubles as extra drag surface: the whole top of the panel moves
                    it, and the buttons stop the mousedown from reaching it. */}
                <div className={styles.modeBar} onMouseDown={onHeaderMouseDown}>
                    {MODES.map((definition) => {
                        const active = mode === definition.value;

                        return (
                            <Tooltip key={definition.value} tooltip={definition.tooltip}>
                                <div onMouseDown={(event) => event.stopPropagation()}>
                                    <Button
                                        theme={{
                                            button: active
                                                ? `${styles.modeButton} ${styles.modeButtonActive}`
                                                : styles.modeButton,
                                        }}
                                        selected={active}
                                        onSelect={() => setMode(definition.value)}
                                    >
                                        <img
                                            className={styles.modeIcon}
                                            src={active ? definition.selectedIcon : definition.icon}
                                        />
                                    </Button>
                                </div>
                            </Tooltip>
                        );
                    })}
                </div>

                <div className={styles.header} onMouseDown={onHeaderMouseDown}>
                    <span className={styles.title}>Filter</span>

                    {/* Swallows the mousedown so using the button does not also drag
                        the panel out from under the cursor. */}
                    <div onMouseDown={(event) => event.stopPropagation()}>
                        <Button
                            theme={{ button: styles.allButton }}
                            onSelect={setAllFilters}
                        >
                            {allEnabled ? "None" : "All"}
                        </Button>
                    </div>
                </div>

                <div className={styles.filters}>
                    {FILTERS.map(({ bit, label, tooltip }) => {
                        const checked = (filters & bit) !== 0;

                        return (
                            <Tooltip key={bit} tooltip={tooltip}>
                                <Button
                                    theme={{ button: styles.row }}
                                    selected={checked}
                                    onSelect={() => toggleFilter(bit)}
                                >
                                    <span
                                        className={
                                            checked
                                                ? `${styles.box} ${styles.boxChecked}`
                                                : styles.box
                                        }
                                    />
                                    <span className={styles.label}>{label}</span>
                                </Button>
                            </Tooltip>
                        );
                    })}
                </div>

                <div className={styles.footer}>
                    <div className={styles.actions}>
                        <Button
                            theme={{
                                button: hasSelection
                                    ? styles.bulldozeButton
                                    : `${styles.bulldozeButton} ${styles.bulldozeButtonEmpty}`,
                            }}
                            onSelect={onBulldozePressed}
                        >
                            <img className={styles.bulldozeIcon} src={bulldozeIcon} />
                            <span>Bulldoze</span>
                        </Button>

                        <Tooltip tooltip="Ask for confirmation before deleting. Same setting as Options > Mods > Bulldozer Marquee.">
                            <Button
                                theme={{ button: styles.miniToggle }}
                                selected={confirmBeforeBulldoze}
                                onSelect={toggleConfirmBulldoze}
                            >
                                <span
                                    className={
                                        confirmBeforeBulldoze
                                            ? `${styles.box} ${styles.boxChecked}`
                                            : styles.box
                                    }
                                />
                                <span className={styles.miniLabel}>Ask</span>
                            </Button>
                        </Tooltip>

                        <Tooltip tooltip="Keep the selection in sync with the filters: unticking a filter drops everything of that type out of the current selection straight away. Same setting as Options > Mods > Bulldozer Marquee.">
                            <Button
                                theme={{ button: styles.miniToggle }}
                                selected={pruneOnFilterChange}
                                onSelect={togglePruneOnFilterChange}
                            >
                                <span
                                    className={
                                        pruneOnFilterChange
                                            ? `${styles.box} ${styles.boxChecked}`
                                            : styles.box
                                    }
                                />
                                <span className={styles.miniLabel}>Sync</span>
                            </Button>
                        </Tooltip>

                        <Tooltip tooltip="Play a sound when bulldozing. Same setting as Options > Mods > Bulldozer Marquee.">
                            <Button
                                theme={{ button: styles.miniToggle }}
                                selected={playSfx}
                                onSelect={toggleSfx}
                            >
                                <span
                                    className={
                                        playSfx
                                            ? `${styles.box} ${styles.boxChecked}`
                                            : styles.box
                                    }
                                />
                                <span className={styles.miniLabel}>SFX</span>
                            </Button>
                        </Tooltip>
                    </div>

                    {selectionClamped && (
                        <div className={styles.warning}>
                            {`Limit reached — only the first ${selectionCount} are selected. Draw a smaller area.`}
                        </div>
                    )}

                    {hasSelection ? (
                        <div className={styles.status}>
                            <span>{selectionCount} selected</span>
                            <Button
                                theme={{ button: styles.clearButton }}
                                onSelect={clearSelection}
                            >
                                Clear
                            </Button>
                        </div>
                    ) : (
                        <div className={styles.hint}>{getMode(mode).hint}</div>
                    )}
                </div>
            </div>

            {/*
                The scrim is a pointer-events:auto surface over the entire viewport at
                the highest z-index in the mod, so nothing behind it — the panel, the
                vanilla HUD, or the marquee tool, which stops raycasting once the
                pointer is over UI — can be reached until the prompt is answered.
            */}
            {confirming && (
                <div className={styles.confirmScrim}>
                    <div className={styles.confirmDialog}>
                        <div className={styles.confirmTitle}>Bulldoze selection?</div>
                        {/*
                            One interpolated string, not interleaved expressions and
                            literals. Written the obvious way — `{count} item{s} will
                            be...` — JSX emits four separate text children, and cohtml
                            lays each out as its own line, so the prompt broke across
                            four lines and split "item" from "s" mid-word.
                        */}
                        <div className={styles.confirmBody}>
                            {`${selectionCount} ${selectionCount === 1 ? "item" : "items"} will be permanently removed.`}
                        </div>
                        <div className={styles.confirmActions}>
                            <Button
                                theme={{ button: `${styles.confirmButton} ${styles.confirmCancel}` }}
                                onSelect={onCancel}
                            >
                                Cancel
                            </Button>
                            <Button
                                theme={{ button: `${styles.confirmButton} ${styles.confirmAccept}` }}
                                onSelect={onConfirm}
                            >
                                Bulldoze
                            </Button>
                        </div>
                    </div>
                </div>
            )}
        </>
    );
};
