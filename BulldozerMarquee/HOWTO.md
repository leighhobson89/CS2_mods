# BulldozerMarquee

Drag a box over the map, keep only the asset types you ticked, delete them.

For the architecture every mod in this repo shares — bindings, mount points, the
build toolchain — see [../docs/HOWTO.md](../docs/HOWTO.md). This file covers only
what is specific to this mod.

## The interaction

1. Click the mod icon in the top-left. The icon swaps to its "on" art, the tool
   takes over the cursor, and the filter panel appears in the bottom-left corner.
   Drag its header to move it.
2. Tick the asset categories the marquee is allowed to catch.
3. Drag a box over the map. Everything the box currently covers is ringed **live**
   as you drag, and the count updates with it; releasing commits that set.
4. **Bulldoze** deletes the selection and empties it. **Clear** drops the
   selection without deleting anything.

Right click backs out one step at a time: it cancels an in-progress drag, then
clears a selection, then leaves the tool.

## Build order is not optional

`dotnet build` runs `RemoveDir` on the whole deploy folder before copying, so it
**wipes the webpack output**. Always `dotnet build` first, then `npm run build`.
Doing it the other way round leaves a mod with no UI bundle and no error to
explain why.

## Why it is a ToolBaseSystem

The marquee could have been a mouse handler in the React layer — a full-screen
div is an easy way to capture a drag. It is a `ToolBaseSystem` instead because
that inherits the game's tool semantics rather than fighting them: the apply and
cancel actions are whatever the player has bound, activating the tool cancels
whichever tool was running, and pressing Escape or picking another tool
deactivates this one. `ToolSystem.EventToolChanged` feeds that back to the panel,
so the icon and panel stay honest about state they did not change themselves.

The cost is that the tool must run in `SystemUpdatePhase.ToolUpdate`; that is the
phase that reads input, and `applyAction` sees nothing anywhere else.

## Why the marquee is world-space, not screen-space

`MarqueeArea` is a rectangle on the XZ plane, rotated to the camera's yaw — the
same approach Move It takes in Cities: Skylines II, and the reason is worth
recording because screen-space looks like the obvious choice:

- **Containment is two dot products.** Projecting every candidate entity back to
  screen space would mean a camera matrix multiply per entity, per drag, over
  queries that can hold six figures of trees in a mature city.
- **The outline can be drawn as world geometry.** The four edges go to
  `OverlayRenderSystem` as ordinary lines with `StyleFlags.Projected`, so they
  drape over terrain instead of floating as a flat rectangle over a hillside.

Aligning the rectangle to camera yaw is what keeps it feeling like the
axis-aligned box the player dragged on screen. The drag corners come from a
terrain raycast, which is why `InitializeRaycast` narrows `typeMask` to
`TypeMask.Terrain` — without that the box would snap to whatever building
happened to sit under the cursor.

## What each filter actually queries

The filter list is modelled on Move It's marquee filter panel, which gates a
drag by asset category in the same way. `AssetFilter` in `Filters.cs` and
`FILTERS` in `src/mods/filters.ts` are two halves of one wire contract — the bit
values are what cross the binding — so they must be edited together.

| Filter | Query | Position tested |
|---|---|---|
| Trees | `Game.Objects.Tree` | transform |
| Props | `Game.Objects.Static`, minus every category with its own checkbox | transform |
| Nodes | `Game.Net.Node` | node position |
| Segments | `Game.Net.Edge` + `Curve` | curve midpoint |
| Buildings | `Game.Buildings.Building` | transform |
| Surfaces | `Game.Areas.Surface` + its node buffer | centroid of nodes |
| Netlanes | `Game.Net.Lane` + `Curve` + `Standalone` | curve midpoint |

Three deliberate choices in that table:

- **"Prop" is defined by exclusion.** There is no prop component; a prop is a
  static object that is not a building, tree, plant or piece of network. Adding a
  new checkbox therefore means adding its component to the prop query's `None`
  list too, or the same entity matches two filters.
- **Curves and surfaces are judged by their middle**, not by overlap. Enclosing
  the midpoint of a road is far more predictable than "any part intersects",
  which would drag in long roads clipped by a corner of the box.
- **Nearly every query excludes `Owner`.** That component marks sub-elements —
  the props inside a park, the sub-nets under a building — so excluding it means
  a marquee deletes whole objects instead of quietly gutting something the player
  did not target. `Standalone` lanes are the exception that proves the rule:
  fences and hedges are lanes that exist in their own right, and are the only
  lanes that can meaningfully be deleted alone.

## Deleting

Bulldozing tags each entity with `Game.Common.Deleted`. That is the game's own
removal path, not a shortcut: `PrepareCleanUpSystem` queries for the tag and
hands the entities to `CleanUpSystem`, which cascades through sub-objects and
references. Vanilla `BulldozeToolSystem` goes the longer way round — building
`CreationDefinition` entities with `CreationFlags.Delete` and letting the tool
pipeline apply them — which buys refunds and undo integration this mod does not
attempt.

## A UI click cannot delete anything, and must not try

The panel's triggers fire in `UIUpdate`. That is the wrong place to touch the
world, for two independent reasons, and both of them cost a debugging round to
find.

**The barrier is shut by then.** `ToolOutputBarrier` is a
`SafeCommandBufferSystem`:

```csharp
public new EntityCommandBuffer CreateCommandBuffer()
{
    if (m_IsAllowed) return base.CreateCommandBuffer();
    throw new Exception("Trying to create EntityCommandBuffer when it's not allowed!");
}

protected override void OnUpdate() { m_IsAllowed = false; ... }
```

An `AllowBarrier<ToolOutputBarrier>` system reopens the window just before
`ToolUpdate`, and the barrier itself plays back and shuts it just after. Ask for
a command buffer anywhere else in the frame and you get that exception — which is
what `OnStopRunning` did.

**And `Deleted` applied that late does nothing useful.** Within `MainLoop` the
order is `ToolSystem` → `ModificationSystem` → … → `UIUpdateSystem` →
`PrepareCleanUpSystem`, with `CleanUpSystem` in the `Cleanup` phase. Tag an entity
`Deleted` during `UIUpdate` and the modification systems have already run, so
nothing cascades through its sub-objects, lanes or references. `CleanUpSystem`
still calls `DestroyEntity` at the end of the frame, but the render batch is never
rebuilt — the mesh stays on screen and the bulldoze looks like it silently failed.

So the Bulldoze button only sets `m_BulldozePending`, and `OnUpdate` acts on it one
frame later during `ToolUpdate`: barrier open, modification systems still ahead.
No Harmony is involved or needed.

Highlighting is the exception that stays on `EntityManager`. It is a tag plus
`BatchesUpdated` with no cascade to schedule, and `OnStopRunning` has to be able to
clear it at a point where no barrier will have it.

## The live preview, and why it touches no components

`RebuildSelection` runs on every frame of the drag, not just on release, so the
rings track the box as it grows and the player can see what they are about to
take. It deliberately changes **no** components while dragging: adding and
removing `Highlighted` across thousands of entities every frame is a structural
change, and therefore a sync point, per frame. The overlay carries the preview on
its own, and the real highlight is applied exactly once on release —
`m_SelectionHighlighted` records which of those two states the selection is in so
`ClearSelection` knows whether there is anything to undo.

The rescan is skipped on frames where the box has not changed shape, which is why
`MarqueeArea` implements `IEquatable`. Holding the mouse still costs nothing.

## Selection size is a hard safety limit, not a preference

`MaxSelection` (5000) and `MaxDrawnMarkers` (1000) exist because exceeding them
crashed the game to desktop with a native fault and an empty managed stacktrace.

The cause was the overlay. `DrawOverlays` drew one ring **per selected entity, per
frame**, and each ring is one instance in `OverlayRenderSystem`'s projected-curve
buffer. That buffer is reallocated and re-uploaded to the GPU whenever the
instance count grows (`GetCurveBuffer` → `ComputeBuffer.SetData`). A marquee over a
dense district selects tens of thousands of trees, so the game was rebuilding a
five-figure-instance GPU buffer every frame of the drag. That is why the crash
reproduced both mid-drag while panning *and* on the Bulldoze click — the drawing
is live in both, and neither had a managed frame to report.

Two lessons worth keeping:

- **An empty managed stacktrace means the fault is native**, so look at what the
  mod hands to native code — GPU buffers, native containers, `EntityManager` batch
  operations — not at the C# control flow.
- **Anything linear in selection size needs a ceiling.** Structural changes,
  deletes and overlay instances are all per-entity, and a region tool can select
  an unbounded number of entities. The cap is the fix; the marker limit alone
  would only have moved the crash to the delete.

`ApplyPendingBulldoze` also deduplicates and skips entities already tagged
`Deleted`. Duplicates reach `CleanUpSystem`'s `EntityManager.DestroyEntity` call,
which is another native-level fault with nothing to debug from.

## The highlight gotcha

Adding or removing `Game.Tools.Highlighted` does nothing visually unless
`BatchesUpdated` goes on the same entity as well, **every time, in both
directions**. Without it the component changes but the render batch is never
rebuilt, so the highlight either never appears or never clears.

Because that only renders for some entity types, the tool also draws its own
circle marker per selected item through `OverlayRenderSystem`. That is the
guaranteed feedback; the highlight is a bonus where it works.

Both overlays are deliberately minimal: the marquee is four plain lines in the
game's UI blue with no fill inside the box, and each selected item gets a hollow
green ring — the `fillColor` argument is fully transparent. Filled shapes tint or
bury whatever they are meant to be pointing at, which turns a large selection
into noise. Widths are in **metres**, not pixels, because the overlay is world
geometry; `LineWidth = 1.5f` is what reads as a hairline at normal zoom.

## UI notes

- There is **no `GameBottomLeft` mount point**. The panel is appended to the
  full-screen `Game` host and positions itself; the toolbar icon goes to
  `GameTopLeft` and carries no positioning CSS, because that host is already the
  flex row that lays mod buttons out.
- **The panel must set `pointer-events: auto`.** The `Game` host is a full-screen
  HUD layer with `pointer-events: none`, so the mouse reaches the 3D world;
  vanilla widgets opt back in through their own classes. Miss this and the panel
  is entirely click-through, and the failure is deeply misleading: presses land on
  the marquee tool underneath as zero-extent drags, so the buttons look dead
  *and* every click wipes the current selection. Anything appended to `Game` needs
  this line.
- Checkbox rows are `cs2/ui` `Button`s with `theme={{ button: ... }}`. Passing a
  theme *replaces* the vanilla button class instead of layering on top of it,
  which keeps the click sounds, hover and gamepad focus without also inheriting
  the padding and background of a full-size button. Note that replacing the class
  is also what drops the vanilla `pointer-events` opt-in, hence the rule above.
- Styling is in `rem`, not `px`. CS2 scales its entire UI through the root font
  size, so `px` would leave the panel the wrong size at every UI scale but the
  default. 1rem is one pixel at the reference resolution.
- The panel is draggable by its header. The stylesheet holds the resting
  position and the drag applies a `translate` on top, so the two never fight. The
  offset lives in component state and survives closing the panel, since rendering
  `null` does not unmount the component.
- **Dragging needs a full-screen shield, not `window` listeners.** The obvious
  implementation — `window.addEventListener("mousemove")` — does not work here.
  The surrounding HUD is `pointer-events: none`, so the moment the cursor leaves
  the panel the UI layer stops receiving mouse events at all; they go to the game
  instead, and the drag dies after a few pixels. `.dragShield` is a real
  `pointer-events: auto` element covering the viewport, mounted only while
  dragging.
- **The shield must paint above the panel** (`z-index: 20` against the panel's
  `10`). Underneath, the panel stole every move event as soon as it caught up with
  the cursor — which, since the cursor is what is dragging it, was instantly and
  constantly. That is what made dragging feel temperamental.
- **A drag must end on `blur`.** Alt-tabbing mid-drag never delivers the mouseup,
  leaving the drag armed with a stale anchor; the next mouse move on return
  applied the whole accumulated delta in one jump and threw the panel off-screen.
  The symptom is deceptive — the panel looks like it has vanished while the
  toolbar icon still reads as enabled, which sends you hunting through the
  `enabled` binding instead of the drag code. (`ValueBinding.OnSubscribe` calls
  `TriggerUpdate`, so a binding never loses its value across a remount; if state
  and UI disagree, the binding is not the culprit.) `clamp()` is the belt to that
  braces, keeping the header on screen so the panel can always be dragged back.

## The drag cursor

`BulldozeCursor` swaps in the game's bulldozer pointer for the duration of a
marquee drag. This has to happen in C#: the cursor over the 3D world belongs to
the game, and a CSS `cursor` rule would need a `pointer-events: auto` element
under the pointer — which would swallow the very drag it is decorating.

`UICursorCollection` maps a name to a texture and calls `Cursor.SetCursor`. The
collection is found via `Resources.FindObjectsOfTypeAll` because nothing public
exposes it, and the cursor is matched by substring (`"bulldoz"`) rather than an
exact name, because the names live in a game asset rather than in code.

**Do not call `UICursorCollection.SetCursor(string)` with a bare name.** Its
dictionary is keyed on the form cohtml sends from CSS:

```csharp
m_NamedCursorsDict["cursor://" + namedCursorInfo.m_Name] = namedCursorInfo;
```

so `SetCursor("bulldoze")` misses — and the miss branch calls `ResetCursor()`.
Called per frame, that does not fail to set the cursor so much as actively pin it
to the default, which is a genuinely confusing thing to debug. This code holds the
`NamedCursorInfo` and calls its public `Apply()` directly, skipping the lookup.

The cursor is also re-asserted every frame of the drag rather than once on
mouse-down, because cohtml reports the hovered element's cursor whenever hover
state is recalculated and will otherwise overwrite a one-shot call.

## Persisted panel state, and a defaults trap

The filter mask lives in the settings file as a `SettingsUIHidden` property. It
is panel state rather than an option — nobody wants to edit a bitmask in a menu —
but the settings file is the mod's only durable store, and hiding it keeps the
options page honest. `ApplyFilters` is the single write path: binding, tool and
settings move together, so what the player sees, what the tool uses and what gets
saved cannot drift apart.

The selection mode is deliberately *not* persisted. Marquee is what the tool is
built around, so every session opens on it rather than on whatever the last one
happened to end with.

**`SetDefaults()` must be called before `LoadSettings`.** Without it, a property
added since the settings file was last written gets C#'s zero value rather than
its intended default — which is how "Confirm before bulldozing" first shipped
switched off despite defaulting to on, and would have made a returning player's
filter mask `0` (nothing selectable) the moment `SavedFilters` was introduced. Any
option added from here on hits the same trap.

## Modes

The mode bar is `SelectionMode` on the C# side and `MODES` in `src/mods/modes.ts`.
The numbering is the wire format for the `SetMode` trigger, so the two files have
to be edited together. `SetMode` always seeds `Marquee` on create; there is no
saved mode to restore.

**Button order comes from the array, not the numbering.** Polygon is value `2`
because it was appended to the enum, but it sits second in `MODES` because that is
where it belongs on screen. Keeping those two independent is what lets a mode be
inserted into the bar without renumbering the wire format.

Each mode also carries its own `hint`, the line shown under the actions row while
nothing is selected. It is per-mode data rather than a ternary in the panel so
that adding a mode cannot leave a stale hint behind.

Each mode also carries three icons — resting, hover and selected — and
`getModeIcon` is the single place that decides between them, so the precedence
cannot drift between the panel and anything else that renders a mode button.
**Selected beats hovered beats resting:** an active mode keeps its selected icon
even under the cursor, because "which mode am I in" is more useful than "what am
I pointing at". The toolbar button in `mod-button.tsx` follows the same rule and
needs no on-hover artwork, since the active state is already drawn in the vanilla
selected style.

Hover is tracked as one `hoveredMode` value rather than a flag per button. cohtml
does not reliably deliver a `mouseleave` when an element is *covered* mid-hover,
and both the drag shield and the confirm scrim do exactly that — with independent
flags the abandoned button would stay stuck on its hover icon. A single value
cannot have two buttons hovered at once, and it is cleared on the way into a drag,
into the prompt, and whenever the panel closes.

Switching mode calls `CancelGesture` before `ClearSelection`. A gesture belongs to
the mode that started it, and half a polygon left standing would be reinterpreted
by whichever mode was switched to.

Switching mode clears the selection, since carrying one across would leave the
player holding a selection they can no longer see how they made.

### Polygon

Click-driven, not drag-driven, and that is the whole reason it takes its own path
through `OnUpdate` rather than being threaded through
`BeginDrag`/`UpdateDrag`/`EndDrag`. There is no held button, no mouse-up to wait
for, and the preview is rebuilt on a click rather than on every frame the cursor
moves — the two gestures share nothing but `m_Path`, so folding them together
would have meant a mode test inside each of those three methods.

`m_PolygonActive` is deliberately separate from `m_Dragging` for the same reason:
nothing about a held mouse button applies.

- **Apply** places a vertex. Applying on the first vertex again closes the loop,
  but only once there are three vertices, so the second click of a small outline
  cannot shut it by accident.
- **Cancel** steps back one vertex. On the last remaining vertex there is nothing
  to step back to, so the gesture is abandoned and the selection dropped. With no
  outline in progress it falls through to the same escalation the drag modes use —
  drop the selection, then leave the tool — so cancel never stops being a way out.

`pathVertexCount` is mode-aware: the lasso's vertices are the committed trail
*plus the cursor*, which is what closes its loop, while the polygon's are only the
committed ones. Its region is decided by the clicks, not by where the cursor
happens to be resting. That one property is what lets `PathContains`,
`UpdatePathBounds` and `HasValidRegion` serve both modes unchanged — the lasso
needs two committed vertices to make a triangle, the polygon needs three.

The closing target's radius is a fraction of the camera's distance to the first
vertex rather than a constant, because the tolerance is in world metres while the
player is aiming in screen pixels. A fixed radius would be an enormous target
zoomed in and a sub-pixel one zoomed out — and zoomed out is exactly where a large
outline gets drawn. `IsOnFirstVertex` compares flat, on x and z only: the click
lands on the terrain and the vertex was recorded on it, but on a slope the two can
differ in height by more than the radius, which would make the loop refuse to
close on the terrain where an outline is hardest to draw in the first place.

The ring is drawn only once closing is possible, so the ring appearing *is* the
cue that the loop can be shut, and its radius is the click tolerance rather than a
decorative size — what is shown is exactly what can be hit.

### Freeform (lasso)

The cursor's trail is recorded as world-space vertices in `m_Path`, and the
current cursor closes the loop back to the start — so the polygon is always
`m_Path + cursor`, and `GetPathVertex` exposes it that way rather than rebuilding
a list every frame just to append one moving point. `PathContains` is an even-odd
ray crossing on the XZ plane, and the same vertex loop feeds the drawing, so what
is outlined is by construction exactly what is highlighted.

Two limits keep it affordable, and neither is cosmetic. A vertex is only committed
once the cursor has travelled `MinVertexDistanceSq`, because the containment test
is **linear in vertex count and runs against every entity in every enabled query,
every frame** — recording a vertex per frame would make a long lasso quadratic.
`MaxPathVertices` is the hard ceiling on that. The bounding-box check at the top of
`PathContains` does most of the real work: it rejects the overwhelming majority of
entities in two comparisons, and without it a per-frame preview would not be
viable in a large city.

### Panning mid-drag

Both regions are stored in world space, so the camera is free to pan or rotate
while the button is held: the shape stays pinned to the ground, and the raycast
just keeps reporting wherever the cursor now points. Nothing special was needed
to support this — it falls out of the world-space decision, and is the third time
that choice has paid for itself.

## Keeping the selection in sync with the filters

Unticking a filter after a selection has been committed leaves the checkboxes
describing the *next* drag while the green rings still describe the *last* one,
with nothing on screen admitting the two disagree. `Settings.PruneOnFilterChange`
(on by default, mirrored by the panel's "Sync" checkbox) closes that gap:
`ApplyFilters` diffs the old mask against the new, and any bit that was just
switched off goes to `BulldozerMarqueeToolSystem.PruneSelection`.

Because it hangs off `ApplyFilters` rather than off the individual checkbox, the
"All / None" button is covered by the same code path.

Pruning is *asymmetric on purpose*. Ticking a filter back on does nothing: those
entities were never run through the region test for this selection, so there is
nothing to restore without re-running the drag.

`PruneSelection` needs to know which category each selected entity was collected
under, and that is only knowable at collection time — it is defined by which query
matched. Rather than re-derive it from component composition (a second copy of the
`OnCreate` query rules, guaranteed to drift), the category is recorded into
`m_SelectionCategories`, a third list index-parallel to `m_Selection` and
`m_SelectionPositions`. **Every site that clears one of those three must clear all
three**, or the index mapping corrupts and pruning drops the wrong entities.

The compaction is done in place with a read/write cursor for the same reason.
Only the dropped entities have `Highlighted` taken off them — hence `ApplyHighlight`
being split out of `SetHighlighted` — and only when the selection was actually
tagged, which it is not mid-drag. The clamp warning is cleared too: it quotes the
selection count as the ceiling that was hit, and once a filter edit has cut that
count the message is simply wrong.

## The confirmation prompt

`Settings.ConfirmBulldoze` (on by default) gates a modal between the Bulldoze
button and the deletion. The whole flow lives in React: pressing Bulldoze opens
the prompt instead of firing the trigger, Confirm fires it, and Cancel fires
`ClearSelection` instead — so the C# side needed no changes at all.

Firing the same `Bulldoze` trigger on confirm rather than on the original click is
what makes the SFX land at the right moment: the sound is played by the C#
handler, so a cancelled prompt is silent for free.

`.confirmScrim` is a `pointer-events: auto` surface over the whole viewport at the
mod's highest `z-index`. That blocks the panel and the vanilla HUD directly, and
the marquee tool too — it stops raycasting once the pointer is over UI — so the
prompt really is modal over the whole game rather than just over the panel.

State that lives in React needs clearing when the tool is switched off, because
hiding the panel renders `null` without unmounting it: an unanswered prompt would
otherwise still be open the next time the panel appears.

**Interpolate text into one string; do not interleave expressions and literals.**
Written the natural JSX way:

```tsx
{selectionCount} item{selectionCount === 1 ? "" : "s"} will be permanently removed.
```

that is four separate text children, and cohtml lays each out as its own line —
the prompt broke across four lines and split `item` from `s` mid-word. A single
template literal is one text node and wraps normally. Widening the dialog treats
the symptom; the child count is the cause.

`Reset()` is called on release, on cancel, and in `OnStopRunning` — leaving a
bulldozer cursor behind on the default tool would be worse than never setting it.

## Sound effects and the options page

Custom audio *is* possible, and does not need Harmony or an AudioSource of the
mod's own. `UnityWebRequestMultimedia` decodes `sfx/bulldoze.mp3` once at load
(the only route from an mp3 on disk to an `AudioClip` at runtime), and
`AudioManager.instance.PlayUISound(clip)` plays it on the game's UI mixer — so it
respects the player's volume settings and pauses with everything else. A missing
file logs one line and the mod runs silently.

The clip has to reach the deployed mod folder, which the `DeploySfx` target in the
`.csproj` handles. It runs `AfterTargets="DeployWIP"` for the same reason the
build order matters: `DeployWIP` deletes the folder first.

`Settings` is a `ModSetting` registered in `OnLoad`, so the page is reachable from
the main menu as well as in game. It is the single source of truth for the SFX
option; the panel's checkbox is a second view of the same property, and
`BulldozerMarqueeUISystem.OnUpdate` compares the binding against it each frame so
a change made on the options page shows up on the panel. That one bool comparison
is cheaper and harder to get wrong than a settings-changed subscription with a
lifetime to manage. `LocaleEN` is not optional decoration — without a dictionary
source the options page displays raw key IDs.

## Still placeholder

`icon/toggle-off.png` and `icon/toggle-on.png` are the MidnightToggle art, and
the bulldoze button reuses `toggle-on.png`. All three want replacing before this
ships.

`sfx/bulldoze.mp3` is not in the repo yet — drop one in and it works with no code
change. See [sfx/README.md](sfx/README.md).

Panel labels are hard-coded English rather than going through `cs2/l10n`; only
the options page is localised, because it has to be.
