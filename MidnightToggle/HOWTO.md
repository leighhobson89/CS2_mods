# How MidnightToggle Works

A walkthrough of what this mod does, the CS2 modding APIs it taps into, and why it's
built the way it is. Written as a learning reference for building the next mod.

## The shape of a CS2 UI mod

A mod that shows a button and reacts to clicks is really **two separate programs**
that never share memory:

1. A **C# assembly** (`Mod.cs`, `Systems/`) that runs inside the game process and
   can touch the simulation.
2. A **JavaScript bundle** (`src/`) that runs inside Gameface, the game's
   Coherent-Labs-based UI renderer (basically an embedded Chromium-like engine
   rendering React). It can only touch the DOM.

They talk to each other over a small, explicit, named **binding** channel — never
directly. That channel, and how each side hooks into the game, is the whole trick.

## The C# side

### `Mod.cs` — the entry point

```csharp
public class Mod : IMod
{
    public void OnLoad(UpdateSystem updateSystem)
    {
        updateSystem.UpdateAt<MidnightToggleUISystem>(SystemUpdatePhase.UIUpdate);
    }

    public void OnDispose() { }
}
```

Every mod implements `IMod`. `OnLoad` is where you register your systems with the
game's ECS scheduler via `UpdateSystem.UpdateAt<T>(phase)` — this is what actually
makes `MidnightToggleUISystem` run every frame. `SystemUpdatePhase.UIUpdate` is the
phase reserved for systems that only publish state to the UI (as opposed to
`MainLoop`, which is for gameplay/simulation systems).

### `Systems/MidnightToggleUISystem.cs` — the bridge

This is a `UISystemBase` subclass — the base class the game gives you specifically
for exposing state to and receiving events from the React side. Two things happen
in `OnCreate`:

```csharp
AddBinding(m_Enabled = new ValueBinding<bool>(Group, "Enabled", false));
AddBinding(new TriggerBinding(Group, "Toggle", Toggle));
```

- **`ValueBinding<T>`** is C#-owned state that gets pushed *to* React. Calling
  `.Update(newValue)` re-renders every subscribed React component. Think of it as
  a server-pushed prop.
- **`TriggerBinding`** is a named callback React can invoke, like an RPC. When the
  TSX button is clicked, it calls `trigger("MidnightToggle", "Toggle")` and this
  fires `Toggle()` in C#.

Both are addressed by a `(group, name)` string pair — here `"MidnightToggle"` is
the group (a constant, `Group`), and `"Enabled"` / `"Toggle"` are the names. There
is **no compiler link** between these strings and the ones used in the TSX file —
a typo on either side just silently does nothing. This is the single most common
way this pattern breaks.

C# is the source of truth: React never mutates game state itself, it only fires a
trigger and waits for C# to update the binding with the result.

### Actually changing the game: `PlanetarySystem`

```csharp
m_PlanetarySystem = World.GetOrCreateSystemManaged<PlanetarySystem>();
...
m_PlanetarySystem.overrideTime = enabled;
if (enabled) m_PlanetarySystem.time = MidnightHour; // 0f
```

`PlanetarySystem` is a vanilla game system (not something this mod defines) that
owns the simulated clock/sun position. It exposes a **cooperative override seam**:
set `.overrideTime = true` and the game stops computing time itself and uses
whatever you write to `.time` instead (which drives lighting, since lighting is
derived from time of day). Set `.overrideTime = false` and it falls back to
normal simulated time — nothing to restore or snapshot, because the "off" state
*is* just letting vanilla logic run again.

This is deliberately **not** a Harmony patch. Harmony (a runtime IL-patching
library used elsewhere in the modding ecosystem to hard-override methods CS2
doesn't expose a seam for) is the more invasive option — reach for it only when
the game gives you no supported field to write to. Here, `PlanetarySystem`
already exposes exactly the override flag we need, so patching would be
unnecessary risk.

`OnGameLoadingComplete` re-applies the override on every load (the flag lives on
a runtime system, not in the save file, so it doesn't persist automatically), and
`OnDestroy` forces `overrideTime = false` so the mod never leaves the clock
stuck if it's unloaded mid-session.

## The React/TypeScript side

### `src/index.tsx` — mounting into the game's UI tree

```tsx
const register: ModRegistrar = (moduleRegistry) => {
    moduleRegistry.append('GameTopLeft', MidnightToggle);
}
export default register;
```

The game hands every mod's entry file a `moduleRegistry`. `append(hostName,
Component)` drops your component into a named slot in the vanilla React tree —
`'GameTopLeft'` is one of the game's built-in mount points. (The alternative,
`extend(modulePath, exportName, callback)`, replaces/wraps a *specific* vanilla
component instead of adding alongside one — not needed here since we're adding a
standalone button, not modifying an existing panel.)

### `src/mods/midnight-toggle.tsx` — consuming the binding

```tsx
import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";

const enabled$ = bindValue<boolean>("MidnightToggle", "Enabled", false);

export const MidnightToggle = () => {
    const enabled = useValue(enabled$);
    return (
        <Tooltip tooltip={enabled ? "Midnight: on" : "Midnight: off"}>
            <Button
                src={enabled ? iconOn : iconOff}
                variant="floating"
                selected={enabled}
                onSelect={() => trigger("MidnightToggle", "Toggle")}
            />
        </Tooltip>
    );
};
```

`cs2/api` is a virtual module the game injects at runtime (it's not a real npm
package — it's marked `external` in `webpack.config.js` so webpack leaves the
import alone and the game's own bundle resolves it). Four functions matter:

- **`bindValue(group, name, fallback)`** resolves a *handle* to a C#
  `ValueBinding` — it doesn't fetch the value itself.
- **`useValue(binding)`** is the React hook that subscribes to that handle and
  re-renders the component whenever C# calls `.Update(...)`. This is the read
  path.
- **`trigger(group, name, ...args)`** fires a C# `TriggerBinding` — fire and
  forget, no return value. This is the write path.
- **`call<T>(group, name, ...args)`** (not used here) is the request/response
  version — returns a `Promise<T>` — for when you need an answer back
  synchronously instead of just publishing a value later.

### Styling: let the host lay the button out

The button deliberately carries **no positioning CSS**. `GameTopLeft` is itself a
flex row, and every mod that appends to it becomes another child in that row — so
the icons self-sort and stay lined up with each other automatically. An earlier
version of this file used `position: absolute; top: 10px; left: 100px`, which took
the button *out* of that flow and pinned it at a fixed spot that would collide with
other mods. Removing the positioning is the entire fix.

For the button itself, `cs2/ui` is the game's own component library, so using
`Button` instead of a raw `<button>` inherits the vanilla size, hover, focus, and
click-sound behaviour for free. The props that matter:

- **`variant="floating"`** — the vanilla style used for free-standing HUD icon
  buttons, which is what the other top-left mod icons use.
- **`src`** — the icon image (from `IconButtonProps`), swapped between the on/off
  PNGs based on the binding.
- **`selected`** — vanilla's toggled/highlighted state, so the button also *looks*
  active, not just the icon.
- **`onSelect`** — the vanilla click handler (note: not `onClick`); it also fires
  for the gamepad SELECT button.

`Tooltip` wraps it to get the standard hover balloon. This exact shape
(`Tooltip` > `Button variant="floating"` appended to `GameTopLeft`) is the pattern
Write Everywhere uses, documented in the guide's `explanation/react-ui.md`.

The `(group, name)` strings here — `"MidnightToggle"` / `"Enabled"` /
`"Toggle"` — must match the C# side byte-for-byte. Since there's no shared
constant across the C#/TS language boundary, the only real safeguard is
discipline (and testing in-game after any rename).

## Build & deploy toolchain

Two independent build steps produce the files the game actually loads from
`%LocalLow%\Colossal Order\Cities Skylines II\Mods\MidnightToggle\`:

1. **`dotnet build`** compiles `MidnightToggle.csproj` into
   `MidnightToggle_win_x86_64.dll` (plus mac/linux variants). It imports
   `Mod.props`/`Mod.targets` from `$(CSII_TOOLPATH)` (set up by the official CS2
   modding toolchain installer), which resolves `$(ManagedPath)` to the game's
   `Cities2_Data/Managed` folder — that's where `Game.dll`, `Colossal.Core.dll`,
   `Colossal.UI.Binding.dll`, etc. actually live, and it's how the csproj's
   `<Reference HintPath>` entries find them. It also auto-copies the build
   output straight into the deployed Mods folder as a post-build step.
2. **`npm run build`** (webpack) compiles `src/index.tsx` into a single
   `MidnightToggle.mjs`, inlining the two PNGs as `images/*.png` assets. The
   `externalsType: "window"` + `externals` block in `webpack.config.js` is what
   makes `import ... from "cs2/api"` resolve against the game's own runtime
   instead of trying to bundle a real package.

Both outputs land in the same deployed folder, which is why the mod works after
running both commands in either order.

## Where this pattern is documented

Everything above follows the patterns in the
[BrokeAssSoftware/cs2-modding-guide](https://github.com/BrokeAssSoftware/cs2-modding-guide)
(`integration` branch), specifically:

- `explanation/ui-cs-communication.md` — the `ValueBinding`/`TriggerBinding`
  model, C#-authoritative-state principle.
- `explanation/react-ui.md` — the `cs2/api` bridge (`bindValue`, `useValue`,
  `trigger`, `call`) and `moduleRegistry.append`/`extend`.
- `how-to/recipes/uisystembase-react-binding.md` — concrete `AddBinding` call
  shapes, and the "base class varies" / "string contract, typo = silent no-op"
  pitfalls.
- `how-to/recipes/weather-climate-override.md` — the Harmony-prefix vs.
  cooperative-`overrideValue`/`overrideState` choice for time/climate; this mod
  uses the cooperative path via `PlanetarySystem`, cross-checked against the
  `TimeWeatherAnarchy` mod's source (`rodrigmatrix/TimeWeatherAnarchy` on
  GitHub) to confirm the exact field names (`overrideTime`, `time`).
- `how-to/recipes/reversible-override-baseline.md` — the general pattern for
  making an override cleanly undoable; this mod's version of it is the
  simplest case ("off" needs no stored baseline because it's just the vanilla
  default).
