# UI icon and colour standards

What an icon in a Cities: Skylines II mod is supposed to look like — canvas size,
format, the vanilla palette it has to sit inside, and how the game draws the
hover / selected / disabled states so a mod's buttons behave like the game's own.

For the architecture behind mod UI (bindings, module registry, the build
toolchain) see [HOWTO.md](HOWTO.md). For vanilla systems, see
[systems-glossary.md](systems-glossary.md).

## How to read this

The same confidence markers the glossary uses.

| Marker | Meaning |
|---|---|
| **[CSS]** | Read directly out of the game's own compiled stylesheet, `Cities2_Data/Content/Game/UI/index.css`. Authoritative — this *is* what the game renders. |
| **[SDK]** | Verified against the TypeScript definitions in this repo (`*/types/*.d.ts`), which come from the game's UI SDK. |
| **[WIKI]** | From the official [Paradox UI Modding wiki](https://cs2.paradoxwikis.com/UI_Modding). Colossal's own guidance. |
| **[GUIDE]** | From the [cs2-modding-guide](https://github.com/BrokeAssSoftware/cs2-modding-guide) corpus. Credible, derived from published mods. |
| **[REPO]** | Measured from the mods in this repository. |

The stylesheet is the primary source throughout, and it is worth saying why: it
is compiled output shipped with the game, so it cannot drift from what is
actually on screen the way a wiki page or a community guide can. It also has no
stability guarantee — re-derive after a major patch rather than trusting this
document indefinitely. Every hex code below was pulled from it on game build
dated 2025-06-22.

---

## 1. The unit system

**1rem ≈ 1px at 1920×1080.** **[WIKI]** **[CSS]**

The stylesheet sets the root font size from the viewport, not from a fixed pixel
value:

```css
html { font-size: 0.0925926vh }   /* 1080 × 0.0925926 / 100 = 1.0px */
html { font-size: 0.0520833vw }   /*  1920 × 0.0520833 / 100 = 1.0px */
```

Two consequences that matter more than they look:

- **Never write `px` in mod UI.** A `px` value is fixed while everything around
  it scales, so a panel laid out in pixels is correct at exactly one resolution
  and one UI-scale setting, and wrong everywhere else.
- **`rem` is not a typography unit here.** It is the game's universal length unit.
  Treat `1rem` as "one pixel at 1080p" and the numbers below read as pixel sizes.

At 4K, 1rem becomes 2px; the player's UI-scale slider moves it again on top of
that. This is exactly why icons must be authored well above their nominal size —
see §3.

## 2. Format

| Format | Verdict | Notes |
|---|---|---|
| **SVG** | **Preferred.** | What vanilla uses for essentially all UI chrome. Scales to any resolution, small in memory, and — critically — can be *tinted* by CSS (§6). |
| **PNG** | Acceptable. | Fine for artwork that is genuinely raster (photos, thumbnails, logos). A poor choice for chrome. |
| **JPG** | Allowed. | No alpha channel, so effectively useless for an icon. |
| **GIF** | **Avoid.** | The wiki explicitly warns these "add a lot of memory overhead". **[WIKI]** |

Vanilla's own asset mix confirms the guidance rather than just asserting it
**[REPO]** — counted across `Content/Game/UI/Media`:

| Format | Count | What it is used for |
|---|---|---|
| SVG | 1183 | Icons, glyphs, arrows, all UI chrome |
| PNG | 473 | Glossary illustrations (140), achievement badges (80), menu backdrops, tutorial screenshots |
| JPG | 1 | One image, total |

Note the split: **no UI chrome is a PNG.** Every PNG in the game is either
photographic or an illustration. The 64×64 PNGs are achievement badges, not
buttons.

> **A caveat on SVG in Coherent.** Gameface requires SVGs to carry explicit
> `width` and `height`, and it does not implement the full SVG spec. Keep them
> simple: flat paths, no filters, no embedded fonts, no CSS inside the document.
> Vanilla's icons are single-path silhouettes almost without exception. **[WIKI]**

## 3. Canvas size

Vanilla icon canvases, counted from the `viewBox` of all 1041 SVGs in
`Media/Game/Icons` **[CSS]**:

| viewBox | Count | Share |
|---|---|---|
| `0 0 64 64` | 270 | 55% |
| `0 0 32 32` | 94 | 19% |
| `0 0 26 26` | 42 | 9% |
| `0 0 40 40` | 32 | 7% |
| `0 0 28 28` | 28 | 6% |
| `0 0 128 128` | 18 | 4% |

**Author on a 64×64 square canvas.** It is the plurality by a wide margin, it
divides cleanly into every render size the game asks for, and it is large enough
that a raster export from it still holds up.

### If you must ship PNG

Export at **4× the largest size the icon will ever render at**, and no larger.
The button is 40rem (§4), so 40px at 1080p, 80px at 4K, and up to roughly 120px
at 4K with the UI scale pushed high. **256×256 covers every case with headroom.**

> **[REPO] Our own icons are wildly over-budget.** Every icon in this repo is
> 1254×1254 — around 10× the pixels ever displayed. `MidnightToggle/icon/midnightToggle.png`
> is 1050 KB for a button that renders at 40px. The others are 11–60 KB, which is
> merely wasteful; the 1 MB one is worth fixing on its own. Re-exporting the set
> at 256×256 would cost nothing visually.

## 4. Component sizes

Straight from the stylesheet's `:root` **[CSS]**:

| Token | Value | What it sizes |
|---|---|---|
| `--floatingToggleSize` | `40rem` | **The floating mod button** — what `GameTopLeft` lays out |
| `--floatingToggleBorderRadius` | `6rem` | Corner radius of that button |
| `--toolbarToggleSize` | `46rem` | Main bottom-toolbar toggles |
| `--assetMenuItemSize` | `72rem` | Asset-menu cell |
| `--assetMenuImageSize` | `68rem` | Image inside that cell |
| `--assetMenuItemBorderRadius` | `4rem` | Asset-menu cell radius |
| `--policyIconSize` | `32rem` | Policy icons |
| `--panelRadius` / `--panelRadiusInner` | `4rem` / `3rem` | Panel corners, outer and inner |
| `--screenPadding` | `10rem` | Gap from the screen edge |
| `--iconSize` | `100%` | Icons fill their box by default |

The vanilla floating-button rule itself, which is the one to match:

```css
.button_ke4 {
  display: flex; justify-content: center; align-items: center;
  width:  var(--floatingToggleSize);      /* 40rem */
  height: var(--floatingToggleSize);
  padding: var(--gap2);                   /* 2rem at default UI scale */
  background-color: var(--accentColorNormal);
  border-radius: var(--floatingToggleBorderRadius);   /* 6rem */
}
.button_ke4:hover    { background-color: var(--accentColorNormal-hover); }
.button_ke4.selected { background-color: var(--accentColorLight); }
```

So the glyph inside a mod button occupies **36rem** (40 − 2 × 2 padding). Design
the 64×64 canvas with that in mind: a glyph that fills its canvas edge-to-edge
will look larger than every vanilla neighbour. Vanilla icons typically leave
6–8% of the canvas as margin.

## 5. The palette

All values are the **default (dark) theme** from `:root` **[CSS]**. The game
ships alternates — a light theme and an orange-accented one — that redefine
`--accentColor*`, which is the reason to reference the *variable* rather than
paste the hex wherever you can.

### The blue

**`--accentColorNormal: #4bc3f1`** is the CS2 blue. That is the answer to "what
is the blue".

| Token | Hex | Role |
|---|---|---|
| `--accentColorLightest` | `#ffffff` | |
| `--accentColorLighter` | `#e9f6ff` | |
| `--accentColorLight` | `#9ee2fc` | **Selected** state of a floating button |
| `--accentColorNormal-pressed` | `#c1eafa` | |
| `--accentColorNormal-hover` | `#7ad3f5` | **Hover** state |
| **`--accentColorNormal`** | **`#4bc3f1`** | **The base accent. Default button fill.** |
| `--accentColorDark-focused` | `#00c2ff` | |
| `--accentColorDark-pressed` | `#64c0e4` | |
| `--accentColorDark-hover` | `#26a4d5` | |
| `--accentColorDark` | `#1e83aa` | Sliders, progress, `--neutralColor` |
| `--accentColorDarker-pressed` | `#5c75ad` | |
| `--accentColorDarker-hover` | `#3f527d` | |
| `--accentColorDarker` | `#2e3c5b` | Desaturated navy, not a blue accent |

Two related blues that are *not* the accent ramp:

| Token | Hex | Role |
|---|---|---|
| `--highlightBrightBlue` | `#4ac0f0` | Icon tint on toolbar fields — a hair off `#4bc3f1`, and deliberately so |
| `--highlightLightBlue` | `#99d3e9` | Icon tint on close-button hover |

### Semantic colours

| Token | Hex | Role |
|---|---|---|
| `--positiveColor` | `#8bdb46` | Good / increase / unlocked |
| `--negativeColor` | `#e95f4a` | Bad / decrease |
| `--warningColor` | `#ffa42d` | Warning |
| `--lockedColor` | `#d38f07` | Locked content |
| `--unlockedColor` | `#8bdb46` | Unlocked content |
| `--linkColor` | `#63b900` | Hyperlinks |
| `--highlightGreen` | `#41d880` | |
| `--highlightYellow` | `#ffcb00` | |
| `--highlightWarningRed` | `#be3255` | |
| `--highlightWarningLightRed` | `#df5f7f` | |

### Surfaces and text

| Token | Value | Role |
|---|---|---|
| `--panelColorNormal` | `rgba(42,55,83,0.55)` | Standard panel background |
| `--panelColorDark` | `rgba(24,33,51,0.71335)` | Darker panel |
| `--toolbarFieldColor` | `rgba(6,10,16,0.45)` | Toolbar field background |
| `--toolbarFieldBorderColor` | `rgba(255,255,255,0.08)` | Its border |
| `--toolbarFieldRadius` | `8rem` | Its radius |
| `--dividerColor` | `rgba(255,255,255,0.1)` | Hairline rules |
| `--sectionBorderColor` | `rgba(56,72,104,0.7)` | Section borders |
| `--normalTextColor` | `#F0FBFF` | Body text — very slightly blue-white |
| `--normalTextColorDisabled` | `#A7C6D1` | Disabled text |
| `--symbolColor` | `rgba(255,255,255,0.8)` | Symbols and glyphs |
| `--iconColor` | `white` | Default tint for a tinted icon |
| `--tooltipColor` | `#232528` | Tooltip background |
| `--panelBlur` / `--backdropBlur` | `blur(5px)` | Backdrop blur behind panels |

## 6. States — the part with an actual standard

This is where a mod most visibly diverges from vanilla, because it is tempting to
invent hover and selected styling rather than look up what the game does.

### The universal overlay states

Vanilla does **not** recolour a generic control per state. It lays a translucent
white film over whatever is underneath. **[CSS]**

| Token | Value | State |
|---|---|---|
| `--normalColor` | `transparent` | Resting |
| `--hoverColor` / `--hoverColorNormal` | `rgba(255,255,255,0.1)` | Hover |
| `--hoverColorBright` | `rgba(255,255,255,0.15)` | Hover, emphasised |
| `--hoverColorDark` | `rgba(255,255,255,0.05)` | Hover, subdued |
| `--activeColor` / `--activeColorNormal` | `rgba(255,255,255,0.2)` | Pressed / active |
| `--activeColorBright` | `rgba(255,255,255,0.3)` | Pressed, emphasised |
| `--disabledColor` | `rgba(95,95,95,0.6)` | Disabled |

This is why it works on any background, and why a hardcoded hover colour looks
wrong the moment it sits over something unexpected.

### Toggled on — "selected"

A **selected** control gets a real blue, not a white film:

| Token | Hex | State |
|---|---|---|
| `--selectableColor-hover` | `#124e65` | Hovering something selectable |
| `--selectedColorDark` | `#176583` | Selected, dark variant |
| **`--selectedColor`** | **`#1e83aa`** | **Selected (toggled on)** |
| `--selectedColor-hover` | `#2398c5` | Selected **and** hovered |
| `--selectedColor-active` | `#25a1d1` | Selected **and** pressed |
| `--focusedColor` | `#4bc3f1` | Keyboard/gamepad focus ring |
| `--focusedColorDark` | `#20b5ee` | Focus, dark variant |

Selecting also inverts the text colour, because `#1e83aa` is light enough that
white text on it fails to read. The `.selected` rule reassigns the entire text
ramp to the `--selectedTextColor*` set, which is near-black:
`--selectedTextColor: rgba(20,27,34,0.6)`, `--selectedTextColorDark: rgba(20,27,34,0.9)`.
**If you hand-roll a selected state, invert your text with it.**

Note the two different blues in play: a **floating mod button** uses the accent
ramp (`#4bc3f1` → hover `#7ad3f5` → selected `#9ee2fc`), while a **list item or
generic toggle** uses the selected ramp (`#1e83aa` → hover `#2398c5`). They are
not interchangeable — pick the one matching the component you are imitating.

### The select animation

```css
@keyframes blinkOnSelect { 0%, 40% { background-color: #92dbf7 } }
```

Wired up as `--buttonSelectAnimation: blinkOnSelect`, `--selectDuration: 250ms`.
A control opts in with `--selectAnimation: var(--buttonSelectAnimation)`. It is a
single 250ms flash of pale blue on the frame the toggle happens — cheap
acknowledgement that a click registered, and worth copying because its absence is
noticeable once you know it is there.

## 7. Tinting: one icon, every state

The mechanism behind vanilla's state colours on *icons* is CSS masking, not
multiple image files:

```css
.tinted-icon_iKo {
  background-color: var(--iconColor);
  mask-size: contain;
  mask-position: center;
}
```

The SVG becomes a stencil and the background colour shows through it. Changing
`--iconColor` in a `:hover` or `.selected` rule recolours the icon with no second
asset. Vanilla uses this constantly — e.g. `.close-button:hover { --iconColor: var(--highlightLightBlue) }`.

The SDK exposes it directly **[SDK]** — `IconButtonProps` carries `tinted?: boolean`
(`types/ui.d.ts:398`):

```ts
export interface IconButtonProps extends ButtonProps {
    src: string;
    tinted?: boolean;
    theme?: Partial<IconButtonTheme>;
}
```

**This is the single biggest reason to author icons as monochrome SVG.** A tinted
icon needs *one* file and gets its normal, hover, selected, disabled and focused
colours from CSS for free. A pair of PNGs — an "off" and an "on" — is the
workaround for not having done that, and it does not scale: each new state means
another export.

> **[REPO] Every mod in this repo takes the two-PNG route** (`toggle-off.png` /
> `toggle-on.png`, `marquee.png` / `marquee-selected.png`). It works, and the
> mode icons genuinely differ between states rather than just changing colour, so
> it is not simply wrong. But `toggle-off`/`toggle-on` is exactly the case a
> single tinted SVG would replace.

## 8. Serving icons

Two routes, and they solve different problems.

**Bundled through webpack** — what this repo does. `import icon from "../../icon/x.png"`
and the asset is emitted beside the UI bundle and referenced by relative URL. No
C# involvement. Simplest option when the icons ship with the mod and nothing else
needs to reach them.

**Registered as a `coui://` host** — needed when the files must be addressable by
URL, e.g. an icon library other mods consume. **[GUIDE]** The UI runs in a
sandboxed Coherent browser that cannot read arbitrary disk paths, so a folder has
to be mounted in `OnLoad`:

```csharp
UIManager.defaultUISystem.AddHostLocation("yourkey", assemblyDirectory);
// referenced as coui://yourkey/Icons/Standard/Plus.svg
```

Discover the directory from the assembly at runtime — never hard-code it. **Host
keys are a global namespace shared by every installed mod**, so a collision
silently redirects URLs to whichever mod resolved first; pick something
distinctive. Unmount dynamic hosts on dispose.

**Before drawing anything, check the [Unified Icon Library](https://thunderstore.io/c/cities-skylines-ii/p/algernon/Unified_Icon_Library/).**
**[GUIDE]** ~268 icons per style, in three styles — `Standard` (matches vanilla
chrome), `Dark` (higher contrast), `Colored` — reachable as
`coui://uil/Standard/ArrowLeft.svg` once the library is installed. The `Colored`
set exposes named layer IDs (`blue`, `red`, `green`, …, or a hex) overridable
from CSS. Taking a dependency on it beats hand-drawing an icon that will not
match vanilla's line weight.

## 9. Checklist

- [ ] Authored on a **64×64** canvas, monochrome, single-path where possible
- [ ] Shipped as **SVG** with explicit `width`/`height`; no filters, no embedded fonts
- [ ] Falling back to PNG? **256×256**, not more
- [ ] Glyph sized for a **36rem** live area inside a 40rem button — leave margin
- [ ] All CSS lengths in **`rem`**, never `px`
- [ ] Colours reference **`var(--token)`**, not pasted hex, so alternate themes work
- [ ] Hover = `rgba(255,255,255,0.1)` film, **not** an invented colour
- [ ] Selected = the right blue for the component (accent ramp vs `--selectedColor`)
- [ ] Selected state **inverts text** to the `--selectedTextColor*` ramp
- [ ] Single tinted SVG considered before shipping an off/on pair
- [ ] Checked the Unified Icon Library first

## 10. Audit of this repo

Measured against the above **[REPO]**. None of these are bugs; they are drift.

| Finding | Current | Standard | Impact |
|---|---|---|---|
| Icon canvas | 1254×1254 PNG | 256×256 max, ideally SVG | ~10× the pixels ever shown |
| `MidnightToggle/icon/midnightToggle.png` | **1050 KB** | < 20 KB | Worth fixing on its own |
| Off/on icon pairs | Two PNGs each | One tinted SVG | Doubles assets, blocks free state colours |
| `$accent` in `filter-panel.module.scss` | `#4ba7d8` | `#4bc3f1` | Visibly duller than vanilla beside it |
| `$accent-bright` | `#7fc9ef` | `#7ad3f5` | Near enough; harmless |
| `kMarqueeColor` (overlay) | `#4FA8DB` | `#4bc3f1` | Comment claims "the game's UI blue"; it is not |
| `kMarkerColor` (overlay) | `#4CD96B` | `#8bdb46` (`--positiveColor`) | Reads greener//more saturated than vanilla's positive |
| SCSS colours | Hardcoded SCSS `$vars` | `var(--token)` | Breaks under the light and orange themes |

The last row is the one with real consequence: the panel is built from SCSS
variables compiled at build time, so a player on a non-default theme gets a panel
that does not follow the rest of their UI. Switching to `var(--accentColorNormal)`
and friends fixes that for free, since the game redefines those per theme.

---

## Sources

- `Cities2_Data/Content/Game/UI/index.css` — the game's compiled stylesheet, primary source for every hex code, token and size above
- `Cities2_Data/Content/Game/UI/Media/**` — the 1041 vanilla icon SVGs the canvas-size figures come from
- [UI Modding — Cities: Skylines II Wiki](https://cs2.paradoxwikis.com/UI_Modding) — formats, the rem/1080p relationship, the Coherent SVG caveat
- [cs2-modding-guide](https://github.com/BrokeAssSoftware/cs2-modding-guide) — [`coui-host-registration`](https://github.com/BrokeAssSoftware/cs2-modding-guide/blob/integration/how-to/recipes/coui-host-registration.md), [`unified-icon-library`](https://github.com/BrokeAssSoftware/cs2-modding-guide/blob/integration/reference/shared-libraries/unified-icon-library.md), [`vanilla-ui-augmentation`](https://github.com/BrokeAssSoftware/cs2-modding-guide/blob/integration/how-to/recipes/vanilla-ui-augmentation.md)
- [Unified Icon Library](https://thunderstore.io/c/cities-skylines-ii/p/algernon/Unified_Icon_Library/) ([source](https://github.com/algernon-A/UnifiedIconLibrary))
- `BulldozerMarquee/types/ui.d.ts` — the `cs2/ui` SDK definitions for `Button`, `IconButtonProps` and `tinted`
