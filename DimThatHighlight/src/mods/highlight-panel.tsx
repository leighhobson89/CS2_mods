import { useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";
import { useEffect, useRef, useState } from "react";
import {
    colorRgb$,
    commit,
    defaultColorRgb$,
    enabled$,
    restoreDefault,
    setColor,
    setStrength,
    strengthPercent$,
    toggle,
} from "./bindings";
import { applyStrength, PALETTE, toHex } from "./palette";
import styles from "./highlight-panel.module.scss";

interface MoveDrag {
    readonly kind: "move";
    readonly mouseX: number;
    readonly mouseY: number;
    readonly offsetX: number;
    readonly offsetY: number;
}

interface SliderDrag {
    readonly kind: "slider";
    /** The track's box, cached at mousedown: the shield covers it, so it cannot be re-measured mid-drag. */
    readonly left: number;
    readonly width: number;
}

type Drag = MoveDrag | SliderDrag;

const clampPercent = (value: number): number => Math.min(100, Math.max(0, Math.round(value)));

/**
 * The Highlight Properties panel: a 64-swatch palette and a strength slider for the
 * outline the game draws around whatever the cursor is over.
 *
 * Only visibility and window position are decided here. The colour itself lives in
 * C#, which is what writes it into the game's RenderingSettingsData — so the panel
 * can be closed, or never opened at all, and the chosen colour still applies.
 */
export const HighlightPanel = () => {
    const enabled = useValue(enabled$);
    const rgb = useValue(colorRgb$);
    const strength = useValue(strengthPercent$);
    const defaultRgb = useValue(defaultColorRgb$);

    // Displacement from the resting position in the stylesheet. Closing the panel
    // renders null but does not unmount this component, so a position the player
    // chose survives toggling it off and on again.
    const [offset, setOffset] = useState({ x: 0, y: 0 });
    const [drag, setDrag] = useState<Drag | null>(null);
    const dragRef = useRef<Drag | null>(null);
    const panelRef = useRef<HTMLDivElement>(null);
    const trackRef = useRef<HTMLDivElement>(null);

    const beginDrag = (next: Drag) => {
        dragRef.current = next;
        setDrag(next);
    };

    const endDrag = () => {
        const finished = dragRef.current;

        dragRef.current = null;
        setDrag(null);

        // The slider does not save while it is moving — see setStrength in bindings.ts.
        if (finished !== null && finished.kind === "slider") {
            commit();
        }
    };

    // Alt-tabbing mid-drag never delivers the mouseup, which would otherwise leave the
    // drag armed with a stale anchor: the next mouse move on returning applies the
    // whole accumulated delta at once and flings the panel off-screen. The symptom is
    // deceptive — the panel looks like it has vanished while the toolbar icon still
    // reads as on.
    useEffect(() => {
        const onBlur = () => endDrag();

        window.addEventListener("blur", onBlur);

        return () => window.removeEventListener("blur", onBlur);
    }, []);

    // Closing the panel hides it without unmounting, so a drag left in progress has to
    // be cleared explicitly or it is still armed the next time it opens.
    useEffect(() => {
        if (!enabled) {
            dragRef.current = null;
            setDrag(null);
        }
    }, [enabled]);

    if (!enabled) {
        return null;
    }

    const hex = toHex(rgb);
    const appliedHex = toHex(applyStrength(rgb, strength));
    const isDefault = rgb === defaultRgb && strength === 100;

    const onHeaderMouseDown = (event: React.MouseEvent) => {
        beginDrag({
            kind: "move",
            mouseX: event.clientX,
            mouseY: event.clientY,
            offsetX: offset.x,
            offsetY: offset.y,
        });
    };

    /**
     * Keeps the panel — and specifically its header, the only drag handle — inside the
     * viewport, so it can never be dragged somewhere it cannot be dragged back from.
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

    const applySliderPosition = (clientX: number, track: SliderDrag) => {
        if (track.width <= 0) {
            return;
        }

        setStrength(clampPercent(((clientX - track.left) / track.width) * 100));
    };

    const onTrackMouseDown = (event: React.MouseEvent) => {
        const element = trackRef.current;

        if (element === null) {
            return;
        }

        const rect = element.getBoundingClientRect();
        const track: SliderDrag = { kind: "slider", left: rect.left, width: rect.width };

        // Pressing anywhere on the track jumps the handle there, so a coarse change
        // does not need a drag at all.
        applySliderPosition(event.clientX, track);
        beginDrag(track);
    };

    const onShieldMouseMove = (event: React.MouseEvent) => {
        const current = dragRef.current;

        if (current === null) {
            return;
        }

        if (current.kind === "slider") {
            applySliderPosition(event.clientX, current);
            return;
        }

        // Anchored to where the drag started rather than accumulated per event, so a
        // dropped frame cannot make the panel drift away from the cursor.
        setOffset(clamp({
            x: current.offsetX + event.clientX - current.mouseX,
            y: current.offsetY + event.clientY - current.mouseY,
        }));
    };

    return (
        <>
            {/*
                A drag needs mouse events from the whole screen, because the cursor
                outruns the panel instantly. Listening on `window` does not work here:
                the surrounding HUD is pointer-events:none, so once the cursor leaves
                the panel the UI layer stops receiving mouse events entirely — they go
                to the game. This shield is a real pointer-events:auto surface covering
                the viewport for the duration of the drag.

                It must sit ABOVE the panel (see the z-index in the stylesheet).
                Painted underneath, the panel stole every move event the moment it
                caught up with the cursor — which, since it is being dragged by that
                cursor, was immediately and constantly.
            */}
            {drag !== null && (
                <div
                    className={
                        drag.kind === "slider"
                            ? `${styles.dragShield} ${styles.dragShieldSlider}`
                            : styles.dragShield
                    }
                    onMouseMove={onShieldMouseMove}
                    onMouseUp={endDrag}
                />
            )}

            <div
                ref={panelRef}
                className={styles.panel}
                style={{ transform: `translate(${offset.x}px, ${offset.y}px)` }}
            >
                <div className={styles.header} onMouseDown={onHeaderMouseDown}>
                    <span className={styles.title}>Dim That Highlight!</span>

                    {/* Swallows the mousedown so using the button does not also drag
                        the panel out from under the cursor. */}
                    <div onMouseDown={(event) => event.stopPropagation()}>
                        <Button theme={{ button: styles.closeButton }} onSelect={toggle}>
                            &#215;
                        </Button>
                    </div>
                </div>

                <div className={styles.intro}>
                    Point at anything in the city and the game rings it in bright blue.
                    Pick a colour it can use instead, then pull Strength down until it
                    stops shouting.
                </div>

                {/* Plain divs rather than cs2/ui Buttons. A Button per swatch would buy
                    vanilla hover, focus and click sounds, but this is a grid of them —
                    the sound alone would fire on every pass of the cursor across the
                    palette, and the vanilla focus ring is drawn for a control bigger
                    than one swatch. */}
                <div className={styles.swatches}>
                    {PALETTE.map((entry, index) => (
                        // Keyed by position rather than by colour. Nothing repeats in
                        // this palette today, but a key that is also a value is one
                        // edit away from colliding.
                        <div
                            key={index}
                            className={
                                entry === rgb
                                    ? `${styles.swatch} ${styles.swatchSelected}`
                                    : styles.swatch
                            }
                            style={{ backgroundColor: toHex(entry) }}
                            onClick={() => setColor(entry)}
                        />
                    ))}
                </div>

                <div className={styles.sliderRow}>
                    <span className={styles.sliderLabel}>Strength</span>

                    {/* A plain div rather than a cs2/ui control: the game's component
                        library ships no slider, and an <input type=range> is not
                        something cohtml styles usefully. */}
                    <div
                        ref={trackRef}
                        className={styles.sliderTrack}
                        onMouseDown={onTrackMouseDown}
                    >
                        <div className={styles.sliderFill} style={{ width: `${strength}%` }} />
                        <div className={styles.sliderHandle} style={{ left: `${strength}%` }} />
                    </div>

                    <span className={styles.sliderValue}>{`${strength}%`}</span>
                </div>

                <div className={styles.footer}>
                    <Tooltip tooltip="The colour as it will be drawn, with Strength applied.">
                        <div className={styles.preview}>
                            <span
                                className={styles.previewFill}
                                style={{ backgroundColor: appliedHex }}
                            />
                            <span className={styles.previewHex}>{appliedHex.toUpperCase()}</span>
                        </div>
                    </Tooltip>

                    {/* The stock colour is carried on the Reset button rather than
                        marked in the grid. It is snapshotted from the running game, so
                        it is not one of the 16 swatches and never will be — a marker
                        looking for it in the grid would simply never draw. */}
                    <Tooltip tooltip="Put the highlight back to the colour and strength the game draws it in.">
                        <Button
                            theme={{
                                button: isDefault
                                    ? `${styles.resetButton} ${styles.resetButtonInactive}`
                                    : styles.resetButton,
                            }}
                            onSelect={restoreDefault}
                        >
                            <span
                                className={styles.resetSwatch}
                                style={{ backgroundColor: toHex(defaultRgb) }}
                            />
                            <span>Reset</span>
                        </Button>
                    </Tooltip>
                </div>

                {/* One interpolated string, not interleaved expressions and literals.
                    Written the obvious way, JSX emits separate text children and cohtml
                    lays each out as its own line. */}
                <div className={styles.hint}>
                    {strength === 0
                        ? "Strength is at zero, so the highlight is off entirely — you will get no ring at all when you point at something."
                        : `${hex.toUpperCase()} at ${strength}%. Roads, districts and building lots use this too, and it keeps applying once this panel is closed.`}
                </div>
            </div>
        </>
    );
};
