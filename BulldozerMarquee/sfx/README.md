# Sound effects

Drop **`bulldoze.mp3`** in this folder and it plays when the Bulldoze button is
used. Nothing else is needed — `dotnet build` copies the file to
`<mod folder>/sfx/` and `BulldozeAudio` loads it at startup.

If the file is absent the mod logs a single line and runs silently, so this is
safe to leave empty.

Only `.mp3`, `.ogg` and `.wav` are deployed. The clip is decoded once at load
through `UnityWebRequestMultimedia` and played via `AudioManager.PlayUISound`,
which puts it on the game's UI mixer — so it follows the player's volume
settings rather than blasting at full volume.

Keep it short. This is a button click, not a cutscene.
