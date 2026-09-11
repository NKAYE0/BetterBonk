# BetterBonk

An all-in-one Megabonk mod, built on top of a from-scratch rebuild of mzzJuice's
[Fast Reset](https://www.nexusmods.com/megabonk/mods/145) mod (credited in full below, along
with the other mods whose ideas/code went into this). Started as a Fast Reset rebuild
(previously named "FastReset+"/"FastResetUpdated") and grew into a small suite of features,
each on its own tab in one settings window:

- **Quick Reset** (the original feature, still the default tab): configurable requisites for
  auto-resetting a run instead of hardcoded ones, a "Legendary Surge" rule that relaxes those
  requisites once you have enough Legendary Shady Guys, named preset configurations you can
  save/switch between, and a quick on/off toggle (default key: **F6**) that fully disables
  auto-resetting — the game behaves exactly as if this feature weren't installed until you
  toggle it back on. The mod also turns Quick Reset off by itself once a run is accepted, so it
  never keeps resetting a run you've already started playing.
- **Pot Breaking**: automatically breaks pots as you walk near them, without ever interacting
  with anything else (chests, shrines, items) that happens to be sitting right next to one.
- **Personal Leaderboard**: adds a "Personal" tab to the in-game leaderboard screen showing your
  own best kill count per character, with an on/off switch and an optional filter to show just
  one character's score.
- **Toggle Everything**: unlocks every achievement-gated item/upgrade and lets you individually
  switch each one on or off from the game's own achievement UI, with a single on/off switch.
- An in-game settings window (default key: **F7**), tabbed across the four features above, to
  change all of the above without editing a config file.
- Builds for **both MelonLoader and BepInEx** from one shared codebase. MelonLoader is the
  priority build — if something ever has to give, keep that one working first.

> **Status:** Quick Reset has been verified against the original mod's decompiled behaviour and
> a real MelonLoader install. Pot Breaking, Personal Leaderboard and Toggle Everything are new
> in this version and haven't been through a real build/run yet — see "Assumptions worth
> knowing about" below for the specific things worth checking first, especially around Toggle
> Everything's extra DLL dependency. The BepInEx build overall remains untested against a real
> BepInEx install.

## Installation (players)

1. Make sure [MelonLoader](https://melonwiki.xyz/) is installed for Megabonk.
2. Download `BetterBonk.dll` and drop it into your Megabonk `Mods` folder.
3. If you want **Toggle Everything**, also download `MegabonkToggleEverything.dll` and drop it
   into your Megabonk `UserLibs` folder (create it next to `Mods` if it doesn't exist yet) — not
   `Mods`, since that DLL contains its own separate mod that would otherwise run unconditionally
   alongside BetterBonk's own on/off switch for the same feature. If you skip this, everything
   else in BetterBonk still works; only the Toggle Everything tab's switch will log a warning
   and do nothing when turned on.
4. Launch the game. **F6** toggles Quick Reset on/off, **F7** opens the settings menu.

If you're upgrading from FastResetUpdated/FastReset+: just install over it (or alongside it —
your old settings are copied automatically into BetterBonk's own settings folder the first time
it runs, so nothing is lost either way). Once you've confirmed BetterBonk works, you can remove
the old `FastResetUpdated.dll`/its settings folder — running both at once would double up Quick
Reset's run-reset trigger.

This works regardless of where Steam (or any other launcher) installed the game — the mod
finds its own settings folder at runtime via MelonLoader's own APIs, nothing about a
particular install path is baked into the compiled file. The `FastReset.local.props` /
project-reference setup described below only matters if you're building the mod yourself from
source; it has no effect on the compiled `.dll` you'd distribute.

## Why this exists rather than patching the originals

None of the reference mods are open source, so Quick Reset and Personal Leaderboard are
clean-room reimplementations: I decompiled each original `.dll`'s IL to confirm exactly which
game methods/fields it reads and calls (see "How this was verified" below), then wrote fresh
source implementing the same behaviour, plus fixes/additions, from that. Nothing here is copied
from Fast Reset's or Personal Leaderboard's binaries. Pot Breaking was also written from
scratch, using Potomatic only as a reference for what "automatic pot breaking" should do — its
author asked that direct copies of their code not be made, so it isn't; see `ModPotBreaker.cs`
for the different approach this takes. Toggle Everything's author did authorize exact reuse, so
that feature instead references their compiled DLL directly and layers BetterBonk's own on/off
switch on top of it — see `ModToggleEverything.cs` for why, and the installation step above for
where that DLL needs to go.

## Project layout

```
src/
  Shared/       Game logic + the in-game menu. No project of its own — both loader projects
                compile these same .cs files directly against their own copy of the game's
                interop assemblies, so the two builds never depend on one loader's interop
                DLLs matching the other's.
  MelonLoader/  MelonLoader entry point (priority build).
  BepInEx/      BepInEx IL2CPP entry point.
lib/
  MegabonkToggleEverything.dll   Third-party DLL the Toggle Everything feature drives directly
                                 (author-authorized exact-code reuse — see ModToggleEverything.cs).
```

## Building

1. Copy `FastReset.local.props.example` to `FastReset.local.props` (already gitignored) and
   set `MegabonkDir` to your Megabonk install folder.
2. Open `FastResetUpdated.sln` (still named that on disk — see note below) and build whichever
   project(s) you need — or `dotnet build` a specific `.csproj`. Only build the BepInEx project
   if you actually have BepInEx installed (and vice versa); each project references that
   loader's own DLLs.
3. Each project copies its output — and, for the MelonLoader/BepInEx projects,
   `MegabonkToggleEverything.dll` alongside it — into the right folder automatically after a
   successful build (`Mods\` + `UserLibs\` for MelonLoader, `BepInEx\plugins\BetterBonk\` for
   BepInEx) as long as the relevant `*Dir` property is set.

**Only ever have one loader installed in the game at a time** (this is a Megabonk/MelonLoader/
BepInEx requirement generally, not specific to this mod) — build the matching project.

> **Note on file/folder names:** this project was renamed from FastResetUpdated to BetterBonk
> (namespaces, assembly names, display names, config folder — all fully renamed), but the
> `.sln`/`.csproj` **filenames** and a couple of source filenames (`FastResetBehaviour.cs`) were
> deliberately left as-is rather than renamed on disk, since that rename had to be done without
> the ability to delete/move files remotely. Rename them yourself via your IDE if you'd like the
> filenames to match; nothing about how the project builds depends on it.

## Controls & settings

| Action | Default key | Configurable? |
|---|---|---|
| Enable/disable Quick Reset | F6 | Yes, in the menu or via config file |
| Open/close the settings window | F7 | Yes, in the menu or via config file |

Settings are saved as JSON:
- MelonLoader: `UserData\BetterBonk\BetterBonk.config.json`
- BepInEx: `BepInEx\config\BetterBonk\BetterBonk.config.json`

"Reset to Defaults" in the menu restores the exact thresholds the original Fast Reset mod used
(and turns the other three features off, since that was their state on a fresh install too).

## Assumptions worth knowing about

A few things here are informed engineering judgment calls rather than confirmed-by-a-real-build
facts, flagged here (and in the relevant file's comments) so they're easy to find if something
doesn't compile or behave right on your first test:

- **Toggle Everything's DLL dependency**: `MegabonkToggleEverything.dll` needs to be
  resolvable at runtime alongside BetterBonk's own compiled DLL for that tab's switch to do
  anything. The install/build steps above place it in MelonLoader's `UserLibs` folder and
  BepInEx's plugins folder respectively, based on how each loader's dependency resolution is
  documented to work — but this hasn't been confirmed against a real build yet. If turning that
  switch on logs an error mentioning "MegabonkToggleEverything" instead of "Toggle Everything
  enabled", start there.
- **Personal Leaderboard's original bug**: you reported the original mod's tab appears but never
  gets populated with scores. Static IL analysis didn't turn up a definitive cause, so this
  fixes the one concrete issue that analysis did find (the original's save file used a path
  relative to the game process's current working directory, rather than a fixed location) and
  adds logging at each step (patch registration, prefix firing, save/load, tab population) so
  your next run's log will show exactly where things stop working if this doesn't fully resolve
  it.
- **Rarity scale**: Quick Reset's original mod only ever compares a Shady Guy/Microwave's rarity
  to the literal value `3` for "Legendary". I've assumed a 4-tier scale (0=Common, 1=Uncommon,
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

Rather than guessing at Megabonk's internal API, I decompiled each original mod's IL (via
`dnfile`/`dncil`, working around a monodis limitation that silently truncated output whenever it
hit an unresolvable external reference) to confirm the exact types, members and thresholds each
one uses:

- **Quick Reset**: `Il2Cpp.GameManager` (`Instance`, `isPlaying`, `isGameOver`, `gameTimer`),
  `Il2Cpp.InteractableShadyGuy`/`InteractableMicrowave` (`rarity`), `Il2Cpp.InteractableShrineMoai`,
  `Il2Cpp.PauseUi.Pause()`, and `Il2Cpp.ResetRunUi` (`holding`, `GetHoldTime()`,
  `startedHoldingTime`, `UpdateBar()`) — plus the exact thresholds (combined Shady+Moai ≥ 8, ≥1
  Legendary Shady Guy, ≥2 Microwaves all at Common rarity) and the 1.0–2.0 second `gameTimer`
  check window.
- **Pot Breaking**: `Il2Cpp.BaseInteractable` (`CanInteract()`, `Interact()`,
  `detectInteractables`), `InteractablePot` (`broken`), and
  `DetectInteractables.interactableRange` — all called directly per-pot rather than through
  `DetectInteractables`' single nearest-target selection, which is the actual difference from
  the reference mod (see `ModPotBreaker.cs`).
- **Personal Leaderboard**: `Il2CppAssets...Progression.MapProgress` (`SetKills`,
  `OnRunFinished`), `Il2Cpp.LeaderboardUiNew` (`Refresh`, `leaderboardTypeButtons`,
  `leaderboardEntries`), `Il2Cpp.LeaderboardEntryUi`, `Il2Cpp.MyButtonTabs`,
  `Il2Cpp.ButtonNavigationSelectionOnly`, and `Il2Cpp.DataManager.GetCharacterData` — the same
  Harmony patch targets the original mod used, confirmed against the real methods' actual
  signatures.

`ModCore.cs`/`ModPotBreaker.cs`/`ModLeaderboard.cs` reproduce all of it faithfully (with the
deliberate changes noted in each file); everything past that (config, menu, toggles, presets,
Legendary Surge, dual-loader split) is new.

## Credits

- **mzzJuice** — original concept and implementation of "Fast Reset" on Nexus Mods, the basis
  for the Quick Reset feature. The "2+ Legendary Shady Guys" idea (Legendary Surge) came from a
  suggestion by Nexus user **PrismoWRLD** in the original mod's comments.
- **Potomatic's author** — reference only, for the Pot Breaking feature's intended behaviour
  (see "Why this exists" above for why the code itself isn't reused).
- **mike9k1** — original concept and implementation of "Personal Leaderboard", the basis for
  the Personal Leaderboard feature, reused with permission.
- **NanobotZ** — original concept and implementation of "MegabonkToggleEverything", reused
  directly with permission for the Toggle Everything feature.
