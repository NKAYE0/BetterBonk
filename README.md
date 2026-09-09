# FastReset+

A from-scratch rebuild of mzzJuice's [Fast Reset](https://www.nexusmods.com/megabonk/mods/145)
mod for Megabonk, credited in full below. It reproduces the original's behaviour exactly by
default, and adds:

- Configurable requisites instead of hardcoded ones (with a one-click reset back to the
  original's exact defaults).
- A "Legendary Surge" rule: once you have enough Legendary Shady Guys, the other requisites
  are automatically relaxed by a configurable amount.
- An in-game settings window (default key: **F7**) to change all of the above without editing
  a config file.
- A quick on/off toggle (default key: **F6**) that fully disables auto-resetting — the game
  behaves exactly as if the mod weren't installed until you toggle it back on.
- Builds for **both MelonLoader and BepInEx** from one shared codebase. MelonLoader is the
  priority build — if something ever has to give, keep that one working first.

> **Status:** the MelonLoader build has been verified against the original mod's decompiled
> behaviour and against a real MelonLoader install. The BepInEx build is untested — nobody
> involved in writing it has a working BepInEx install to compile or run it against yet. Only
> the MelonLoader release is recommended for upload until someone verifies BepInEx works.

## Installation (players)

1. Make sure [MelonLoader](https://melonwiki.xyz/) is installed for Megabonk.
2. Download `FastResetUpdated.dll` and drop it into your Megabonk `Mods` folder.
3. Launch the game. **F6** toggles the mod on/off, **F7** opens the settings menu.

This works regardless of where Steam (or any other launcher) installed the game — the mod
finds its own settings folder at runtime via MelonLoader's own APIs, nothing about a
particular install path is baked into the compiled file. The `FastReset.local.props` /
project-reference setup described below only matters if you're building the mod yourself from
source; it has no effect on the compiled `.dll` you'd distribute.

**Don't run this alongside the original Fast Reset mod** — both would fight over the same
run-reset trigger. Uninstall the original before installing this.

## Why this exists rather than patching the original

The original mod isn't open source, so this is a clean-room reimplementation: I decompiled the
original `.dll`'s IL to confirm exactly which game methods/fields it reads and calls (see
"How this was verified" below), then wrote fresh source implementing the same behaviour plus
the requested additions. Nothing here is copied from the original binary.

## Project layout

```
src/
  Shared/       Game logic + the in-game menu. No project of its own — both loader projects
                compile these same .cs files directly against their own copy of the game's
                interop assemblies, so the two builds never depend on one loader's interop
                DLLs matching the other's.
  MelonLoader/  MelonLoader entry point (priority build).
  BepInEx/      BepInEx IL2CPP entry point.
```

## Building

1. Copy `FastReset.local.props.example` to `FastReset.local.props` (already gitignored) and
   set `MegabonkDir` to your Megabonk install folder.
2. Open `FastResetUpdated.sln` and build whichever project(s) you need — or `dotnet build` a
   specific `.csproj`. Only build the BepInEx project if you actually have BepInEx installed
   (and vice versa); each project references that loader's own DLLs.
3. Each project copies its output into the right folder automatically after a successful build
   (`Mods\` for MelonLoader, `BepInEx\plugins\FastResetUpdated\` for BepInEx) as long as the
   relevant `*Dir` property is set.

**Only ever have one loader installed in the game at a time** (this is a Megabonk/MelonLoader/
BepInEx requirement generally, not specific to this mod) — build the matching project.

## Controls & settings

| Action | Default key | Configurable? |
|---|---|---|
| Enable/disable the mod entirely | F6 | Yes, in the menu or via config file |
| Open/close the settings window | F7 | Yes, in the menu or via config file |

Settings are saved as JSON:
- MelonLoader: `UserData\FastResetUpdated\FastResetUpdated.config.json`
- BepInEx: `BepInEx\config\FastResetUpdated\FastResetUpdated.config.json`

"Reset to Defaults" in the menu restores the exact thresholds the original mod used.

## Assumptions worth knowing about

I only had the original mod's compiled `.dll` to verify behaviour against (no access to the
game's own interop assembly), so a couple of things are informed assumptions rather than
confirmed facts — flagged here and in code comments so they're easy to find if something
doesn't compile or behave right:

- **Rarity scale**: the original mod only ever compares a Shady Guy/Microwave's rarity to the
  literal value `3` for "Legendary". I've assumed a 4-tier scale (0=Common, 1=Uncommon,
  2=Rare, 3=Legendary) for the menu's labels — the actual logic only ever uses the raw integers
  from your config, so if the game turns out to have more tiers, nothing breaks; only the
  labels in `RarityLabels.cs` would be worth updating.
- **BepInEx folder layout & namespace** (`src/BepInEx/FastResetUpdated.BepInEx.csproj` and
  `BepInExEntry.cs`): written against the current BepInEx 6 IL2CPP pack layout and API from
  general knowledge, not verified against your actual install. If a reference or the
  `BepInEx.Unity.IL2CPP` using fails to resolve, check what's actually under your `BepInEx`
  folder and adjust the `HintPath`s / using accordingly — the MelonLoader build doesn't depend
  on any of this and was verified directly against the original mod's decompiled IL.
- **`UnityEngine.InputLegacyModule`**: assumed to be where `Input.GetKeyDown` lives on this
  Unity version. If it's actually in `UnityEngine.CoreModule` for your game version instead,
  just drop that `<Reference>` — the code doesn't change either way.

## How this was verified

Rather than guessing at Megabonk's internal API, I decompiled the original mod's IL (via
`dnfile`/`dncil`) to confirm the exact types, members and thresholds it uses:
`Il2Cpp.GameManager` (`Instance`, `isPlaying`, `isGameOver`, `gameTimer`),
`Il2Cpp.InteractableShadyGuy`/`InteractableMicrowave` (`rarity`), `Il2Cpp.InteractableShrineMoai`,
`Il2Cpp.PauseUi.Pause()`, and `Il2Cpp.ResetRunUi` (`holding`, `GetHoldTime()`,
`startedHoldingTime`, `UpdateBar()`) — plus the exact thresholds (combined Shady+Moai ≥ 8, ≥1
Legendary Shady Guy, ≥2 Microwaves all at Common rarity) and the 1.0–2.0 second `gameTimer`
check window. `ModCore.cs` reproduces all of it faithfully; everything past that (config,
menu, toggle, Legendary Surge, dual-loader split) is new.

## Credits

Original concept and implementation: **mzzJuice** ("Fast Reset" on Nexus Mods). The
"2+ Legendary Shady Guys" idea came from a suggestion by Nexus user **PrismoWRLD** in the
original mod's comments.
