# How CS2 UI mods work

The general architecture shared by every mod in this repo — what the pieces are,
how they talk to each other, and how the build/deploy toolchain wires them into
the game. Mod-specific behaviour (what a given mod actually does) belongs in that
mod's own `HOWTO.md`, not here. For the catalog of vanilla game systems mods can
hook into (e.g. `PlanetarySystem`), see [systems-glossary.md](systems-glossary.md).

## The shape of a CS2 UI mod

A mod that shows UI and reacts to it is really **two separate programs** that
never share memory:

1. A **C# assembly** (`Mod.cs`, `Systems/`) that runs inside the game process and
   can touch the simulation.
2. A **JavaScript bundle** (`src/`) that runs inside Gameface, the game's
   Coherent-Labs-based UI renderer (an embedded Chromium-like engine rendering
   React). It can only touch the DOM.

They talk to each other over a small, explicit, named **binding** channel — never
directly. That channel, and how each side hooks into the game, is the whole trick.

## The C# side

### `Mod.cs` — the entry point

```csharp
public class Mod : IMod
{
    public void OnLoad(UpdateSystem updateSystem)
    {
        updateSystem.UpdateAt<YourUISystem>(SystemUpdatePhase.UIUpdate);
    }

    public void OnDispose() { }
}
```

Every mod implements `IMod`. `OnLoad` is where you register your systems with the
game's ECS scheduler via `UpdateSystem.UpdateAt<T>(phase)`. `SystemUpdatePhase.UIUpdate`
is the phase reserved for systems that only publish state to the UI, as opposed to
the simulation phases where gameplay systems run. The full phase list, plus the
`UpdateBefore`/`UpdateAfter` ordering helpers, is in
[systems-glossary.md](systems-glossary.md#part-4--systemupdatephase-reference).

### `Systems/*UISystem.cs` — the bridge

This is a `UISystemBase` subclass — the base class the game gives you specifically
for exposing state to and receiving events from the React side. Bindings are
registered in `OnCreate`:

```csharp
AddBinding(m_SomeValue = new ValueBinding<bool>(Group, "SomeValue", false));
AddBinding(new TriggerBinding(Group, "SomeAction", OnSomeAction));
```

- **`ValueBinding<T>`** is C#-owned state that gets pushed *to* React. Calling
  `.Update(newValue)` re-renders every subscribed React component. Think of it as
  a server-pushed prop.
- **`TriggerBinding`** is a named callback React can invoke, like an RPC. When the
  TSX side calls `trigger(group, name)`, this fires the bound method in C#.

Both are addressed by a `(group, name)` string pair. There is **no compiler link**
between the strings used in C# and the strings used in TSX — a typo on either side
just silently does nothing. This is the single most common way this pattern
breaks; grep both files after any rename.

C# is the source of truth: React never mutates game state itself, it only fires a
trigger and waits for C# to update the binding with the result.

### Actually changing the game

Before reaching for a system, check [systems-glossary.md](systems-glossary.md) —
it catalogs the vanilla systems this repo's mods have already found override seams
on (fields like `PlanetarySystem.overrideTime`), so you don't have to
re-discover them from scratch.

Two ways to change game behaviour from a system:

- **Cooperative override seam**: some vanilla systems expose a `bool overrideX`
  (or similar) flag — set it `true` and the game stops computing that value
  itself and uses whatever you write instead; set it `false` and it falls back to
  normal simulated behaviour. Prefer this whenever a system offers it: no
  patching, and "off" needs no restore step because it's just letting vanilla
  logic run again.
- **Harmony patch**: a runtime IL-patching library that hard-overrides a method
  CS2 doesn't expose a supported seam for. More invasive and higher risk — reach
  for it only when there is no field to write to.

## The React/TypeScript side

### `src/index.tsx` — mounting into the game's UI tree

```tsx
const register: ModRegistrar = (moduleRegistry) => {
    moduleRegistry.append('GameTopLeft', YourComponent);
}
export default register;
```

The game hands every mod's entry file a `moduleRegistry`. `append(hostName,
Component)` drops your component into a named slot in the vanilla React tree
(`'GameTopLeft'` is one of only seven built-in mount points — the authoritative
list, taken from the SDK typings, is in
[systems-glossary.md](systems-glossary.md#part-3--ui-mount-points-moduleregistry)).
The alternative,
`extend(modulePath, exportName, callback)`, replaces/wraps a *specific* vanilla
component instead of adding alongside one — use it when modifying an existing
panel rather than adding a standalone one.

### Consuming a binding from TSX

```tsx
import { bindValue, trigger, useValue } from "cs2/api";

const someValue$ = bindValue<boolean>("Group", "SomeValue", false);

const value = useValue(someValue$);   // subscribe + re-render on C# .Update()
trigger("Group", "SomeAction");        // fire-and-forget call into C#
```

`cs2/api` is a virtual module the game injects at runtime — it's not a real npm
package, it's marked `external` in `webpack.config.js` so webpack leaves the
import alone and the game's own bundle resolves it at runtime. Four functions
matter:

- **`bindValue(group, name, fallback)`** resolves a *handle* to a C#
  `ValueBinding` — it doesn't fetch the value itself.
- **`useValue(binding)`** is the React hook that subscribes to that handle and
  re-renders the component whenever C# calls `.Update(...)`. Read path.
- **`trigger(group, name, ...args)`** fires a C# `TriggerBinding` — fire and
  forget, no return value. Write path.
- **`call<T>(group, name, ...args)`** is the request/response version — returns
  a `Promise<T>` — for when you need an answer back instead of just publishing a
  value later.

`cs2/api` has more than these four (`bindLocalValue`, `bindTrigger`,
`useValueRef`, `useValueOnChange`…) — see
[systems-glossary.md](systems-glossary.md#cs2api--the-full-surface).

**Before writing any C# binding, check `cs2/bindings` first.** The game exposes
a large set of vanilla bindings (city stats, time, tools, policies, transport
lines…) that TSX can read and often write with no C# side at all — see
[systems-glossary.md](systems-glossary.md#part-2--the-tsx-side-cs2bindings).

### Styling: let the host lay things out

Components appended to a host slot (e.g. `GameTopLeft`) become children of
whatever layout that host already is — `GameTopLeft` is a flex row, so anything
appended to it should carry **no positioning CSS**; adding `position: absolute`
takes it out of that flow and risks colliding with other mods' icons. Let the
host's layout do the work.

`cs2/ui` is the game's own component library — use its components (`Button`,
`Tooltip`, etc.) instead of raw HTML elements to inherit vanilla sizing, hover,
focus, and click-sound behaviour for free. Note `onSelect`, not `onClick`, on
`cs2/ui` buttons — it also fires for the gamepad SELECT button.

## Build & deploy toolchain

Two independent build steps produce the files the game actually loads from
`%LocalLow%\Colossal Order\Cities Skylines II\Mods\<mod id>\`:

1. **`dotnet build`** compiles the `.csproj` into `<Mod>_win_x86_64.dll` (plus
   mac/linux variants). It imports `Mod.props`/`Mod.targets` from
   `$(CSII_TOOLPATH)` (set up by the official CS2 modding toolchain installer),
   which resolves `$(ManagedPath)` to the game's `Cities2_Data/Managed` folder —
   that's where `Game.dll`, `Colossal.Core.dll`, `Colossal.UI.Binding.dll`, etc.
   actually live, and it's how the `.csproj`'s `<Reference HintPath>` entries find
   them. It also auto-copies the build output straight into the deployed Mods
   folder as a post-build step.
2. **`npm run build`** (webpack) compiles `src/index.tsx` into a single
   `<mod id>.mjs`, inlining any image assets. The `externalsType: "window"` +
   `externals` block in `webpack.config.js` is what makes `import ... from
   "cs2/api"` resolve against the game's own runtime instead of trying to bundle
   a real package.

Both outputs land in the same deployed folder, which is why the mod works after
running both commands in either order. Each mod folder is a fully independent
project (own `node_modules`, own build output) — there's no shared workspace
config, so multiple mods can live side by side under `CS2_mods/` with no
interaction between their builds.

### Scaffolding a new mod

`scripts/New-CS2Mod.ps1 -Name YourModName` clones the `MidnightToggle` template
(the ~99% of a mod's files that are generic boilerplate) and renames the
mod-specific pieces. See that script's header comment for details.

## Where this pattern is documented upstream

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
  cooperative-`overrideValue`/`overrideState` choice.
- `how-to/recipes/reversible-override-baseline.md` — the general pattern for
  making an override cleanly undoable.
