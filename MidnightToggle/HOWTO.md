# MidnightToggle

For the general CS2 UI-mod architecture (bindings, `moduleRegistry`, the
`cs2/api` bridge, build/deploy toolchain), see
[../docs/HOWTO.md](../docs/HOWTO.md). For the vanilla systems catalog, see
[../docs/systems-glossary.md](../docs/systems-glossary.md#planetarysystem).

## What it does

A floating button in `GameTopLeft` that pins the in-game clock to midnight
while toggled on, via `PlanetarySystem`'s cooperative time override — see the
[systems glossary entry](../docs/systems-glossary.md#planetarysystem) for what's
confirmed about that system's fields.

## Binding contract

Group `"MidnightToggle"`, registered in
[Systems/MidnightToggleUISystem.cs](Systems/MidnightToggleUISystem.cs) and
consumed in [src/mods/midnight-toggle.tsx](src/mods/midnight-toggle.tsx). Both
sides must match byte-for-byte — there's no compiler link between them.

| Name | Kind | Direction | Purpose |
|---|---|---|---|
| `Enabled` | `ValueBinding<bool>` | C# → React | Current toggle state, drives icon/tooltip. |
| `Toggle` | `TriggerBinding` | React → C# | Fired on button click; flips `Enabled` and re-applies the override. |

## Why not Harmony

`PlanetarySystem` already exposes the exact override flag needed
(`overrideTime`), so this mod uses that cooperative seam instead of an IL
patch — see [../docs/HOWTO.md](../docs/HOWTO.md#actually-changing-the-game) for
when Harmony would actually be warranted.

## File map

- [Mod.cs](Mod.cs) — registers `MidnightToggleUISystem` on `UIUpdate`.
- [Systems/MidnightToggleUISystem.cs](Systems/MidnightToggleUISystem.cs) —
  owns the binding, and the actual `PlanetarySystem.overrideTime`/`.time` calls.
- [src/index.tsx](src/index.tsx) — mounts the button into `GameTopLeft`.
- [src/mods/midnight-toggle.tsx](src/mods/midnight-toggle.tsx) — the button
  component; carries no positioning CSS on purpose (`GameTopLeft` is a flex row
  that lays mod icons out itself).
- [icon/midnightToggle.png](icon/midnightToggle.png) /
  [icon/midnightToggleOn.png](icon/midnightToggleOn.png) — off/on icon states.
