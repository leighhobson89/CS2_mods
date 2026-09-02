# DimThatHighlight

Recolour and tone down the outline the game draws around whatever the cursor is over.

For the architecture every mod in this repo shares — bindings, mount points, the
build toolchain — see [../docs/HOWTO.md](../docs/HOWTO.md). This file covers only
what is specific to this mod.

## The interaction

1. Click the mod icon in the top-left. The **Dim That Highlight!** panel appears in
   the bottom-left corner; drag its title bar to move it.
2. Click one of the 16 swatches, then pull **Strength** down until the highlight
   stops shouting. Strength 0 turns it off entirely.
3. Hover anything in the city. The outline is drawn in the chosen colour.

The colour applies whether the panel is open or not, and is written to the mod's
settings file, so it survives closing the panel, reloading a save and restarting the
game. **Reset** — on the panel, and mirrored in Options > Mods > Dim That Highlight
— puts it back to the colour the game itself uses.

## Where the highlight colour actually lives

This was the whole research problem, and the answer is one field.

`Game.Prefabs.RenderingSettingsData` is an ECS singleton holding five colours —
`m_HoveredColor`, `m_OverrideColor`, `m_WarningColor`, `m_ErrorColor`,
`m_OwnerColor`. `Game.Rendering.BatchDataSystem.OnUpdate` reads that singleton every
update and hands it to `BatchDataJob`, whose `UpdateObjectData` / `UpdateNetData` /
`UpdateLaneData` pick one of the five per entity in strict precedence — error, then
warning, then override, then owner-of-a-temp, then **hovered** — and write it into
the `OutlineColors` per-instance property. That property is declared
`_Outlines_Color`, a `float4`, and `Game.Rendering.OutlinesWorldUIPass` (an HDRP
`CustomPass`) is what turns it into the silhouette on screen.

So the bright blue line is `m_HoveredColor`, and there is exactly one of it for the
whole game. The colour is stored **gamma-space**: `BatchDataJob` calls `.linear` on
it itself, so converting here as well would darken everything.

Two consequences worth knowing before changing anything:

- **The scope is wider than buildings.** Nets and lanes take their hover colour from
  the same field, and so do `AreaBorderRenderSystem` (district and area borders) and
  `BuildingLotRenderSystem` (the lot outline under a building). Recolouring the
  highlight recolours all of them, which is the coherent answer — they are all the
  same "you are pointing at this" signal — but it is not only the building outline.
- **Alpha is carried but is not a brightness control.** The stock value is
  `(0.5, 0.5, 1.0, 0.1)`: a pale blue at one tenth alpha. See the next section — that
  `0.1` is what cost a round of iteration.

## Strength scales the colour, because opacity did nothing

The first cut of this mod exposed the colour's alpha as an opacity slider. It had no
visible effect at any value, and that is worth writing down because everything on the
C# side says it should have worked: alpha survives the whole path — `Color.linear`
preserves it, `SetPropertyValue<Color>` writes four floats, and the outline buffer is
allocated `R8G8B8A8_SRGB` so the channel physically exists — and every stock colour
sits at `0.1`, which reads exactly like a deliberate subtlety setting.

The catch is that where alpha ends up is a **compiled shader**, and shaders ship
inside the game's asset bundles as bytecode. Reading `Game.dll` can prove where the
colour comes from; it cannot show what is done with it. What the C# does show is the
shape of the pass, and that shape explains the result:

`DrawOutlineMeshes` renders the highlighted meshes into a cleared offscreen buffer
under the `SHADERPASS_OUTLINES` keyword — filling the whole silhouette, not its edge.
`Execute` then draws one fullscreen quad with `m_FullscreenOutline`, handed that
buffer as `_OutlineBuffer`. So the visible line is produced by **edge detection over
the silhouette in the composite**, and the most natural role for alpha in that
arrangement is the mask that edge detection runs on: present or absent, not stronger
or weaker. That accounts for both symptoms at once — alpha changes nothing, *and* an
outline authored at `0.1` alpha still draws bright.

So the lever that does move is the colour. **Strength scales RGB toward black**, which
fades the outline whether the composite blends with it, replaces with it, or adds it;
every reading of that shader agrees that less colour is less outline. Alpha is passed
through at its stock value untouched, except at strength 0, where it is zeroed as well
so "off" is off under every reading rather than merely black.

Strength is a multiplier, so **100% means "the colour exactly as picked", not
"maximum"**. That is what makes the stock swatch at 100% identical to vanilla, and why
Reset restores 100 rather than some remembered number.

### The diagnostic that closes this properly

`OutlineDiagnostics` walks the loaded `CustomPassVolume`s once, finds the
`OutlinesWorldUIPass`, and logs its `m_MaxDistance` plus every shader property on
`m_FullscreenOutline` with its current value. A material's property table *is*
readable at runtime even when its shader is not, so one entry in the log says
definitively what knobs the composite exposes — including whether there is a dedicated
intensity, width or threshold parameter worth binding instead of scaling the colour.

It runs once, logs at info level, and swallows every exception: a diagnostic that can
break the mod it is diagnosing is worse than no diagnostic. It is research scaffolding,
not a feature — delete it once the question is settled.

## Why no Harmony, and why no snapshot file

Nothing in the game writes `RenderingSettingsData` after
`RenderingSettingsPrefab.Initialize` — that method is the only writer in `Game.dll`.
So this is a cooperative override in the sense [docs/HOWTO.md](../docs/HOWTO.md)
means it: write the field, and the game uses what you wrote; write the original back,
and it behaves exactly as it did before. There is no method to patch and no behaviour
to replace.

What it is *not* is self-restoring. Unlike `PlanetarySystem.overrideTime`, there is no
flag that means "go back to computing this yourself" — the field simply holds whatever
was last written. So the system snapshots `m_HoveredColor` the first time it reads the
singleton, **before** it writes anything, and restores that snapshot in `OnDestroy`.
Snapshotting rather than restoring a constant matters because a game patch or another
mod could move the stock colour, and a hard-coded restore would then quietly set it to
what it used to be. The constants in `Settings` are only a seed for the panel before a
world is loaded.

## Why the colour is re-asserted every frame

`DimThatHighlightUISystem.OnUpdate` compares the singleton against what it last
applied and rewrites it only when they differ. That is not paranoia:
`RenderingSettingsPrefab.Initialize` runs whenever prefabs are initialised, which
includes every world load, and the value lives on a runtime entity rather than in the
save — so without this, loading a save would silently drop back to vanilla blue while
the panel carried on claiming otherwise. Comparing rather than writing keeps the
steady-state cost to one singleton read per frame.

`Color`'s `==` is an epsilon compare rather than bit equality, which is the right test
here: the question is "has something else written this", not "are these the same
float".

## The slider does not save; the swatches do

`SetStrength` fires on every mouse move of a slider drag. It applies the colour but
deliberately does **not** call `ApplyAndSave()` — that writes the settings file, and
doing it per frame puts disk I/O in the middle of an interaction that has to stay
smooth. The panel calls the separate `Commit` trigger on mouse-up. Picking a swatch is
one discrete act, so it saves directly.

## The palette, and why it kept shrinking

`src/mods/palette.ts` is 16 colours in two rows of eight: the twelve-hue colour wheel
— three primaries, three secondaries, six tertiaries — plus white, two greys and
black. Row 0 runs red round to azure, row 1 finishes the wheel and adds the neutrals.

It started at 256 (the xterm palette), went to 64 (generated hue-by-lightness sweeps),
and landed at 16 hand-listed colours. The two cuts are the interesting part:

- **The Strength slider already covers lightness.** Rows of the same hue getting
  progressively darker were answering "I want that, but dimmer" — which the slider
  answers better, continuously, and without hunting for the right square. Once
  Strength existed, most of a 64-colour grid was redundant with it.
- **Sixteen nameable colours make picking a decision rather than a search.** Every
  swatch here is one somebody can name. A grid of near-identical dark blues is slower
  to use than a short list, even though it offers strictly more.

The list is written out rather than generated because sixteen named constants are
easier to read, reorder and argue with than a formula. The order is the layout — the
grid is 8 wide in the stylesheet — so a change to one needs a change to the other.

The panel is deliberately **wider than the grid**, with the swatches centred inside
it. Sixteen small swatches would otherwise make for a panel too narrow to explain
itself in, and "the game rings things in bright blue and you can change that" is not
self-evident from a grid of colours.

## The icons

`icon/highlight.svg`, `-hover` and `-on`: a roughly-drawn irregular block with three
highlight strokes ringing it, on the 64×64 canvas
[docs/ui-icon-standards.md](../docs/ui-icon-standards.md) calls for. Three flat paths
and a fill — no `<style>`, no filters, no gradients, since Gameface does not implement
the whole SVG spec.

**The three strokes are red, magenta and yellow — the one place this repo's icons
leave the accent-blue ramp.** The rule elsewhere is that mod icons stay inside that
ramp (`docs/ui-icon-standards.md` §5), and an earlier cut of this icon drew the
strokes as three different blues. It was wrong for this mod specifically: on an icon
whose entire subject is *choosing a highlight colour*, a monochrome glyph is a
picture of the problem rather than of the fix. The core block stays on the ramp at
`--accentColorDarker`, and so does the state progression.

The wrinkle that shapes the rest is that the vanilla floating button paints *itself*
from the accent ramp — `.button_ke4` is `#4bc3f1`, going to `#7ad3f5` on hover and
`#9ee2fc` when selected. The glyph therefore sits on saturated, dark colours
throughout, and **deepens** rather than brightens on hover: the background is getting
lighter at that moment, so a lighter glyph would lose contrast exactly when it should
gain it.

| State | Strokes, outer → inner | Core | Stroke width |
|---|---|---|---|
| Rest | `#d4231f` `#c4188c` `#d8a800` | `#2e3c5b` | 3.0 |
| Hover | `#f22d1e` `#e81ea6` `#ffcc00` | `#2e3c5b` | 3.4 |
| Selected | `#f22d1e` `#e81ea6` `#ffcc00` | `#141b22` | 4.4 |

What reads as "popping" between rest and hover is the saturation step, not a
lightness one; selected is the same colours again, thickened, with the core dropped
to the near-black vanilla itself uses for text on a selected control.

They render through `StatefulIcon`, the component BulldozerMarquee uses: all three
files are mounted from the start and the state change is an opacity flip, so cohtml
never has to fetch and rasterise a state on the frame the pointer arrives. No separate
preloader is needed here — the toolbar button is the mod's only icon, and `GameTopLeft`
mounts it at load and never removes it, so mounting the layers already warms every
file.

## UI notes

Most of this follows [BulldozerMarquee](../BulldozerMarquee/HOWTO.md#ui-notes), which
solved the same problems first — the `Game` host needs `pointer-events: auto`, styling
is in `rem` because CS2 scales its UI through the root font size, dragging needs a
full-screen shield rather than `window` listeners, the shield must paint *above* the
panel, and a drag must end on `blur`. What is specific here:

- **The shield serves two drags.** Moving the window and dragging the Strength slider
  both need mouse events from outside the element that started them, so they share one
  shield and one `dragRef`. The slider caches the track's bounding box at mousedown,
  because once the shield is up the track cannot be re-measured under the cursor.
- **The swatches are plain `div`s, not `cs2/ui` `Button`s.** A `Button` would bring
  vanilla hover, focus and click sounds, but this is a grid of them — the sound alone
  would fire on every pass of the cursor across the palette, and the vanilla focus ring
  is drawn for a control bigger than one swatch.
- **The panel explains itself.** Shrinking the palette to 16 freed the room for a line
  of prose at the top saying what the highlight is, which a grid of colours does not
  convey on its own.
- **The selected swatch is marked with two rings**, a dark border inside a white
  `box-shadow`, because a single ring in either colour disappears against one end of
  the palette.
- **The stock colour is a dot on the Reset button, not a marker in the grid.** It is
  snapshotted from the running game, so it is not one of the 16 swatches and
  never will be — a marker hunting for it in the grid would simply never draw. Putting
  it on Reset also answers "what am I resetting to" at the point of asking.
- **The preview shows the colour after Strength**, not the swatch that was picked. The
  swatch already says what was chosen; the useful second number is what actually gets
  drawn.

## Build order is not optional

`dotnet build` runs `RemoveDir` on the whole deploy folder before copying, so it
**wipes the webpack output**. Always `dotnet build` first, then `npm run build`. Doing
it the other way round leaves a mod with no UI bundle and no error to explain why.

## Publishing

Published to Paradox Mods as **Id 157738**, set as `<ModId>` in
[Properties/PublishConfiguration.xml](Properties/PublishConfiguration.xml). That id is
what makes every later upload an update to the existing listing rather than a second
mod, so it has to stay there.

`ModPublisher.exe` takes three commands, and which one to use depends on what changed:

| Command | Use it when | Rebuild needed |
|---|---|---|
| `Publish` | First upload of a brand-new mod. Already done; do not run it again for this mod. | yes |
| `NewVersion` | The code or UI changed. Bump `<ModVersion>` and rewrite `<ChangeLog>` first. | yes |
| `Update` | Only the listing changed — description, screenshots, tags, access level. | no |

### Shipping a new version

```powershell
cd C:\Users\Leigh\Desktop\Development\CS2_mods\DimThatHighlight
dotnet build -c Release
npm run build
& "$env:CSII_MODPUBLISHERPATH" NewVersion `
    "$PWD\Properties\PublishConfiguration.xml" `
    -c "$env:CSII_LOCALMODSPATH\DimThatHighlight" `
    -v
```

Run it from the mod folder: the `shots/` paths in the configuration resolve against the
working directory, not against the configuration file's own location.

### Why the publisher is called directly and not through `dotnet publish`

The publish profiles in `Properties/PublishProfiles` exist, and driving them with
`dotnet publish -p:PublishProfile=PublishNewVersion` looks like the tidier option. It
is a trap for this mod, and it fails silently:

- `DeployWIP` in `Mod.targets` runs `AfterTargets="AfterBuild"` and does a `RemoveDir`
  on the whole deploy folder before copying the C# output in.
- `RunModPublisher` runs later still, `AfterTargets="Publish"`, and uploads that same
  folder as `-c`.

So one `dotnet publish` goes build → **wipe the webpack output** → upload, and ships a
version with a working DLL and no UI at all. Nothing reports an error, because as far
as MSBuild is concerned every step succeeded.

`--no-build` does not rescue it either: `DeployDir` is only assigned in
`BuildGetFullPaths`, which hooks `BeforeBuild`, so skipping the build leaves the
publisher with an empty content path.

Calling `ModPublisher.exe` directly is exactly the command MSBuild would have run,
minus the rebuild that destroys the bundle — which is why the recipe above puts
`npm run build` last, immediately before the upload.

**`Update` is the one exception.** `NeedBuild` is `false` when `ModPublisherCommand`
is `Update`, which turns off `DeployWIP` along with the build, so a listing-only change
is safe to drive through the profile:

```powershell
dotnet publish -c Release -p:PublishProfile=UpdatePublishedConfiguration
```
