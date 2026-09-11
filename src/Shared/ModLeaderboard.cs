using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
// 0Harmony.dll defines a legacy, non-nested "Harmony" namespace (backward-compat shims from
// Harmony 1.x) alongside the real HarmonyLib.Harmony class. That namespace sits at the global
// level, so it beats the bare "Harmony" identifier (CS0118) AND conflicts with a using-alias
// of that same name (CS0576) — the alias itself has to be spelled differently.
using HarmonyApi = HarmonyLib.Harmony;
using Il2Cpp;
using Il2CppAssets.Scripts.Saves___Serialization.Progression;
using Il2CppTMPro;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BetterBonk.Shared
{
    // Personal Leaderboard: adds a "Personal" tab to the in-game leaderboard screen showing a
    // top-10 list of your own best runs — across every character, or just one if
    // Config.PersonalLeaderboardCharacterFilter is set from the BetterBonk menu. Reconstructed
    // from PersonalLeaderboard.dll (by mike9k1), whose author authorized reusing its logic
    // exactly — kept faithful to the original wherever it worked, with these deliberate changes:
    //
    //   1. The save file now lives under this mod's own settings folder (via ConfigStore's
    //      already-loader-correct ConfigDirectory) instead of the original's hardcoded relative
    //      path "Mods\PersonalLeaderboard\", which depends on the game process's current working
    //      directory at launch rather than any fixed location.
    //   2. Game types (MapProgress, LeaderboardUiNew, etc.) are referenced directly at compile
    //      time instead of the original's runtime FindType(string-name)/reflection lookup — this
    //      project already references Assembly-CSharp directly for every other feature, so nothing
    //      here can silently fail to resolve a type the way a string-keyed lookup could.
    //   3. LeaderboardPostfix only does its one-time "find and wire up the Personal tab" work
    //      once (guarded by _lbPersonalLeaderboardButton being set) rather than repeating a full
    //      scene-wide GameObject scan and re-registering a duplicate click listener every single
    //      time the leaderboard's Refresh() runs, which is what the original unconditionally did.
    //   4. Character-filter support (a BetterBonk-menu-only addition, not in the original mod).
    //   5. A death doesn't actually call MapProgress.OnRunFinished at all — confirmed by a real
    //      death run's log, which showed GameManager.OnDied and the whole DeathScreen sequence
    //      (StartDeathScreen/ShowStats/GoToMenu) firing in order while OnRunFinished never fired,
    //      even after clicking all the way through to the main menu. That hook is the original
    //      mod's only "record this run" trigger, and is presumably why the original mod (and this
    //      reconstruction of it) never populated any scores from a death, only ever a genuine
    //      victory (which does appear to reach OnRunFinished, per its own "victory" parameter).
    //      Fixed by adding a second, independent trigger for the death case specifically:
    //      DeathScreen.ShowStats (confirmed to reliably fire on every death) now reads the played
    //      character directly off GameManager.Instance.GetPlayerMovement().currentCharacter and
    //      feeds it into the same save path CharPrefix uses.
    //   6. Per-run history instead of the original's per-character best-only tracking. After
    //      testing, the original design (one slot per character, only ever overwritten by a new
    //      personal best) meant three runs with the same character showed as just one row — by
    //      design, not a bug, but not what was wanted here. Per explicit direction: with no
    //      character filter set, this shows a top-10 list of the best individual runs across
    //      every character (which can include the same character more than once); with a filter
    //      set, it shows that one character's own top-10 runs. Each character's own best 10 runs
    //      are kept in the save file (exactly enough to answer both views correctly — any run
    //      that would place in the global top 10 is necessarily also among its own character's
    //      top 10, since only 10 total slots exist to begin with), rather than every run ever
    //      played, so the file doesn't grow without bound.
    public sealed partial class ModCore
    {
        private const string LeaderboardHarmonyId = "nk.betterbonk.leaderboard";

        private HarmonyApi _leaderboardHarmony;
        private bool _leaderboardApplied;
        private bool _leaderboardScoresLoaded;

        // Mirrors the original mod's own static fields exactly (Harmony prefix/postfix methods,
        // and the button's own click handler, all have to be static methods, so this state can't
        // live on the ModCore instance itself). See _instance (ModCore.cs) for how these static
        // methods reach back into the current instance's Config/_logger/_configStore.
        private static bool _lbCharSet;
        private static bool _lbKillsSet;
        private static ECharacter _lbLastPlayedCharacter;
        private static int _lbLastNumKills;

        // Each character's own best runs (kill counts), highest first, capped at
        // MaxKeptRunsPerCharacter — see point 6 in the file header for why that cap is both
        // sufficient and necessary.
        private static Dictionary<ECharacter, List<int>> _lbRecordedRuns = new Dictionary<ECharacter, List<int>>();
        private const int MaxKeptRunsPerCharacter = 10;

        private static LeaderboardUiNew _lbLeaderboardDisplay;
        private static MyButtonTabs _lbPersonalLeaderboardButton;

        private string LeaderboardSaveDirectory => Path.Combine(_configStore.ConfigDirectory, "PersonalLeaderboard");
        private string LeaderboardSaveFilePath => Path.Combine(LeaderboardSaveDirectory, "highscores.json");

        private void UpdateLeaderboard()
        {
            if (Config.PersonalLeaderboardEnabled == _leaderboardApplied)
                return;

            if (Config.PersonalLeaderboardEnabled)
                ApplyLeaderboard();
            else
                RemoveLeaderboard();
        }

        private void ApplyLeaderboard()
        {
            try
            {
                if (!_leaderboardScoresLoaded)
                {
                    LoadHighScores();
                    _leaderboardScoresLoaded = true;
                }

                _leaderboardHarmony ??= new HarmonyApi(LeaderboardHarmonyId);

                PatchWithLogging(
                    AccessTools.Method(typeof(MapProgress), nameof(MapProgress.SetKills)),
                    nameof(KillsPrefix), prefix: true);
                PatchWithLogging(
                    AccessTools.Method(typeof(MapProgress), nameof(MapProgress.OnRunFinished)),
                    nameof(CharPrefix), prefix: true);
                PatchWithLogging(
                    AccessTools.Method(typeof(LeaderboardUiNew), nameof(LeaderboardUiNew.Refresh)),
                    nameof(LeaderboardPostfix), prefix: false);

                // A full death run's log confirmed SetKills fires but MapProgress.OnRunFinished
                // never does — not even after clicking all the way through to the main menu — so
                // OnRunFinished (CharPrefix above) is kept only for whatever run outcome actually
                // does call it (a genuine victory, most likely), while DeathStatsPostfix below
                // does the real work for a death. OnDied/StartDeathScreen/GoToMenu/Restart remain
                // as one-time diagnostic log lines confirming the death flow's order; harmless to
                // keep, safe to remove later if not wanted.
                PatchWithLogging(
                    AccessTools.Method(typeof(GameManager), nameof(GameManager.OnDied)),
                    nameof(DiagOnDied), prefix: false);
                PatchWithLogging(
                    AccessTools.Method(typeof(DeathScreen), nameof(DeathScreen.StartDeathScreen)),
                    nameof(DiagStartDeathScreen), prefix: false);
                PatchWithLogging(
                    AccessTools.Method(typeof(DeathScreen), nameof(DeathScreen.ShowStats)),
                    nameof(DeathStatsPostfix), prefix: false);
                PatchWithLogging(
                    AccessTools.Method(typeof(DeathScreen), nameof(DeathScreen.GoToMenu)),
                    nameof(DiagGoToMenu), prefix: false);
                PatchWithLogging(
                    AccessTools.Method(typeof(DeathScreen), nameof(DeathScreen.Restart)),
                    nameof(DiagRestart), prefix: false);

                _leaderboardApplied = true;
                _logger.Msg("BetterBonk: Personal Leaderboard enabled.");
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: failed to enable Personal Leaderboard ({ex.Message}).");
            }
        }

        private void PatchWithLogging(System.Reflection.MethodBase target, string ourMethodName, bool prefix)
        {
            if (target == null)
            {
                _logger.Warning($"BetterBonk: Personal Leaderboard target method for {ourMethodName} not found — game may have updated.");
                return;
            }

            HarmonyMethod ours = new HarmonyMethod(AccessTools.Method(typeof(ModCore), ourMethodName));
            if (prefix)
                _leaderboardHarmony.Patch(target, prefix: ours);
            else
                _leaderboardHarmony.Patch(target, postfix: ours);
        }

        private void RemoveLeaderboard()
        {
            try
            {
                // Harmony 2's instance UnpatchAll(string) is obsolete-as-error; unpatching by id
                // is now a static method on Harmony itself.
                if (_leaderboardHarmony != null)
                    HarmonyApi.UnpatchID(LeaderboardHarmonyId);
                _leaderboardApplied = false;
                _logger.Msg("BetterBonk: Personal Leaderboard disabled.");
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: failed to disable Personal Leaderboard ({ex.Message}).");
            }
        }

        // --- Harmony patches (must be static — see the static fields above) ---

        // Diagnostic only: logs the very first time each patch fires after (re)enabling the
        // feature, so a test run's log conclusively shows whether MapProgress.SetKills and
        // MapProgress.OnRunFinished are actually being called at all — the missing piece so far,
        // since a run that never logged a save could mean either patch simply never fired.
        private static bool _lbKillsPrefixLogged;
        private static bool _lbCharPrefixLogged;

        private static bool KillsPrefix(int kills)
        {
            try
            {
                if (!_lbKillsPrefixLogged)
                {
                    _lbKillsPrefixLogged = true;
                    _instance?._logger.Msg($"BetterBonk: Personal Leaderboard KillsPrefix fired (kills={kills}).");
                }

                _lbLastNumKills = kills;
                _lbCharSet = true;
                if (_lbCharSet && _lbKillsSet)
                {
                    SaveHighScores();
                    _lbCharSet = false;
                    _lbKillsSet = false;
                }
            }
            catch (Exception ex)
            {
                _instance?._logger.Warning($"BetterBonk: Personal Leaderboard KillsPrefix failed ({ex.Message}).");
            }
            return true;
        }

        private static bool CharPrefix(ECharacter character, bool victory, int tier)
        {
            try
            {
                if (!_lbCharPrefixLogged)
                {
                    _lbCharPrefixLogged = true;
                    _instance?._logger.Msg($"BetterBonk: Personal Leaderboard CharPrefix fired (character={character}, victory={victory}, tier={tier}).");
                }

                RecordCharacterAndMaybeSave(character);
            }
            catch (Exception ex)
            {
                _instance?._logger.Warning($"BetterBonk: Personal Leaderboard CharPrefix failed ({ex.Message}).");
            }
            return true;
        }

        // Shared by CharPrefix (MapProgress.OnRunFinished — confirmed real, but only fires for
        // some run outcomes) and DeathStatsPostfix below (DeathScreen.ShowStats — confirmed to
        // fire on every death via diagnostic logging, unlike OnRunFinished which a full death run
        // never triggered even after clicking all the way through to the main menu).
        private static void RecordCharacterAndMaybeSave(ECharacter character)
        {
            _lbLastPlayedCharacter = character;
            _lbKillsSet = true;
            if (_lbCharSet && _lbKillsSet)
            {
                SaveHighScores();
                _lbCharSet = false;
                _lbKillsSet = false;
            }
        }

        private static bool _lbDiagOnDiedLogged;
        private static void DiagOnDied()
        {
            if (_lbDiagOnDiedLogged) return;
            _lbDiagOnDiedLogged = true;
            _instance?._logger.Msg("BetterBonk: [diag] GameManager.OnDied fired.");
        }

        private static bool _lbDiagStartDeathScreenLogged;
        private static void DiagStartDeathScreen()
        {
            if (_lbDiagStartDeathScreenLogged) return;
            _lbDiagStartDeathScreenLogged = true;
            _instance?._logger.Msg("BetterBonk: [diag] DeathScreen.StartDeathScreen fired.");
        }

        // MapProgress.OnRunFinished is the mod this was reconstructed from (mike9k1's
        // PersonalLeaderboard.dll)'s only hook for "record this run" — and it's a real, existing
        // method (confirmed via signature lookup), so it likely does fire for some run outcome
        // (probably a genuine victory). But a full death run's log showed GameManager.OnDied,
        // then DeathScreen.StartDeathScreen/ShowStats/GoToMenu firing in order, with
        // OnRunFinished never firing at all — not even after clicking all the way through to the
        // main menu. So for a death specifically, this instead reads the played character
        // directly off GameManager.Instance.GetPlayerMovement().currentCharacter (confirmed real
        // property via signature lookup) the moment the stats screen appears, and records the run
        // through the same save path as CharPrefix. This runs unconditionally for every death —
        // no "logged once" guard on the save itself — since ShowStats can legitimately fire once
        // per real run, and the save path already collapses to a no-op unless a kill count is
        // also pending, so there's no risk of double-recording.
        private static bool _lbDiagShowStatsLogged;
        private static void DeathStatsPostfix()
        {
            try
            {
                if (!_lbDiagShowStatsLogged)
                {
                    _lbDiagShowStatsLogged = true;
                    _instance?._logger.Msg("BetterBonk: [diag] DeathScreen.ShowStats fired.");
                }

                GameManager gameManager = GameManager.Instance;
                PlayerMovement movement = gameManager?.GetPlayerMovement();
                if (movement == null)
                {
                    _instance?._logger.Warning("BetterBonk: Personal Leaderboard couldn't read the played character at run end (GameManager.Instance or PlayerMovement was null).");
                    return;
                }

                ECharacter character = movement.currentCharacter;
                _instance?._logger.Msg($"BetterBonk: Personal Leaderboard recording run end via DeathScreen.ShowStats (character={character}).");
                RecordCharacterAndMaybeSave(character);
            }
            catch (Exception ex)
            {
                _instance?._logger.Warning($"BetterBonk: Personal Leaderboard DeathStatsPostfix failed ({ex.Message}).");
            }
        }

        private static bool _lbDiagGoToMenuLogged;
        private static void DiagGoToMenu()
        {
            if (_lbDiagGoToMenuLogged) return;
            _lbDiagGoToMenuLogged = true;
            _instance?._logger.Msg("BetterBonk: [diag] DeathScreen.GoToMenu fired.");
        }

        private static bool _lbDiagRestartLogged;
        private static void DiagRestart()
        {
            if (_lbDiagRestartLogged) return;
            _lbDiagRestartLogged = true;
            _instance?._logger.Msg("BetterBonk: [diag] DeathScreen.Restart fired.");
        }

        private static void SaveHighScores()
        {
            try
            {
                if (!_lbRecordedRuns.TryGetValue(_lbLastPlayedCharacter, out List<int> runs))
                {
                    runs = new List<int>();
                    _lbRecordedRuns[_lbLastPlayedCharacter] = runs;
                }

                runs.Add(_lbLastNumKills);
                runs.Sort((a, b) => b.CompareTo(a));
                if (runs.Count > MaxKeptRunsPerCharacter)
                    runs.RemoveRange(MaxKeptRunsPerCharacter, runs.Count - MaxKeptRunsPerCharacter);

                WriteRunsToDisk();
                _instance?._logger.Msg($"BetterBonk: Personal Leaderboard recorded {_lbLastPlayedCharacter}={_lbLastNumKills} ({runs.Count} run(s) now kept for that character).");
            }
            catch (Exception ex)
            {
                _instance?._logger.Warning($"BetterBonk: Personal Leaderboard failed to save high scores ({ex.Message}).");
            }
        }

        private static void WriteRunsToDisk()
        {
            string json = JsonSerializer.Serialize(_lbRecordedRuns);
            string dir = _instance?.LeaderboardSaveDirectory;
            if (string.IsNullOrEmpty(dir))
                return;

            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "highscores.json"), json);
        }

        private static void LoadHighScores()
        {
            try
            {
                string path = _instance?.LeaderboardSaveFilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    _instance?._logger.Msg($"BetterBonk: Personal Leaderboard found no save file yet at '{path}' — nothing to load.");
                    return;
                }

                string json = File.ReadAllText(path);
                try
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<ECharacter, List<int>>>(json);
                    _lbRecordedRuns = loaded ?? new Dictionary<ECharacter, List<int>>();
                }
                catch (JsonException)
                {
                    // Older save file: one best-score-only entry per character, from before this
                    // tracked a run history. Migrate each into a single-run list and immediately
                    // rewrite the file so this fallback only ever runs once.
                    var legacy = JsonSerializer.Deserialize<Dictionary<ECharacter, int>>(json);
                    _lbRecordedRuns = new Dictionary<ECharacter, List<int>>();
                    if (legacy != null)
                        foreach (KeyValuePair<ECharacter, int> pair in legacy)
                            _lbRecordedRuns[pair.Key] = new List<int> { pair.Value };

                    WriteRunsToDisk();
                    _instance?._logger.Msg("BetterBonk: Personal Leaderboard migrated an older best-score-only save file to the new per-run format.");
                }

                int totalRuns = _lbRecordedRuns.Values.Sum(runList => runList.Count);
                _instance?._logger.Msg($"BetterBonk: Personal Leaderboard loaded {totalRuns} run(s) across {_lbRecordedRuns.Count} character(s).");
            }
            catch (Exception ex)
            {
                _instance?._logger.Warning($"BetterBonk: Personal Leaderboard failed to load high scores ({ex.Message}).");
            }
        }

        // Finds the leaderboard's existing "B_Effects" tab (the same button visibly labeled
        // "Effects"), relabels it "Personal", and wires it to OnPersonalLeaderboardClick.
        //
        // Confirmed live (via logging) that the one-time-only version of this originally shipped
        // here — do the GameObject search, relabel and listener wiring exactly once, guarded by
        // _lbPersonalLeaderboardButton — successfully relabels the tab the first time, but the
        // label reverts to "Effects" the next time Refresh() runs: the game's own Refresh() body
        // re-populates every tab's label from its own per-type name table on every single call,
        // and our one-time SetText only ever wins the very first race. The original mod handled
        // this by unconditionally repeating the ENTIRE find-and-relabel process on every Refresh()
        // call; that's excessive (a full scene-wide GameObject scan every refresh, and it would
        // re-register a duplicate click listener each time), so instead this keeps the expensive
        // one-time setup (find the button once, wire the listener once, resize the buttons array
        // once) but re-asserts just the label text and active state — the two things Refresh()
        // keeps clobbering — on every call.
        private static void LeaderboardPostfix(LeaderboardUiNew __instance)
        {
            _lbLeaderboardDisplay = __instance;

            try
            {
                if (_lbPersonalLeaderboardButton == null)
                    FindAndWirePersonalTab(__instance);

                if (_lbPersonalLeaderboardButton != null)
                {
                    TMP_Text label = _lbPersonalLeaderboardButton.text.GetComponent<TMP_Text>();
                    label.SetText("Personal");
                    _lbPersonalLeaderboardButton.gameObject.SetActive(true);
                }
            }
            catch (Exception ex)
            {
                _instance?._logger.Warning($"BetterBonk: Personal Leaderboard tab refresh failed ({ex.Message}).");
            }
        }

        // One-time setup: locates "B_Effects", wires our click handler onto its Button, and
        // splices it into the leaderboard's navigation array. Never repeats once
        // _lbPersonalLeaderboardButton is set, to avoid piling up duplicate click listeners.
        private static void FindAndWirePersonalTab(LeaderboardUiNew instance)
        {
            // var, not GameObject[] — see the identical note in ModCore.cs's CheckSpawns.
            var all = UnityEngine.Object.FindObjectsOfType<UnityEngine.GameObject>(true);
            foreach (UnityEngine.GameObject go in all)
            {
                if (go.name != "B_Effects")
                    continue;
                if (!go.transform.IsChildOf(instance.leaderboardTypeButtons.transform))
                    continue;

                MyButtonTabs tabs = go.GetComponent<MyButtonTabs>();
                Button button = tabs.GetComponent<Button>();
                // This UnityAction is an Il2Cpp-interop-projected delegate type, not a plain
                // .NET delegate — its constructors marshal a native function pointer, so
                // `new UnityAction(methodGroup)` resolves to an (IntPtr) overload and fails
                // (confirmed by an earlier build error). Converting the method group to a real
                // System.Action first, then letting Il2CppInterop's generated implicit/explicit
                // operator convert that Action to UnityAction, is the supported way to bridge a
                // plain C# method into one of these.
                button.onClick.AddListener((UnityAction)(Action)OnPersonalLeaderboardClick);

                _lbPersonalLeaderboardButton = tabs;

                // Indexed manually rather than via Array.Copy: "existing" comes back as
                // whatever array-like type Il2CppInterop projects this property as, which
                // isn't guaranteed to be a System.Array Array.Copy will accept directly, but
                // every such type supports plain indexing and Length.
                var existing = instance.leaderboardTypeButtons.buttons;
                MyButtonTabs[] buttons = new MyButtonTabs[3];
                for (int idx = 0; idx < Math.Min(2, existing.Length); idx++)
                    buttons[idx] = existing[idx];
                buttons[2] = _lbPersonalLeaderboardButton;
                instance.leaderboardTypeButtons.buttons = buttons;

                _instance?._logger.Msg("BetterBonk: Personal Leaderboard tab wired up.");
                return;
            }

            _instance?._logger.Warning("BetterBonk: Personal Leaderboard couldn't find the leaderboard's \"B_Effects\" tab template to reuse.");
        }

        // Click handler for the "Personal" tab: builds a top-10 list of individual runs — across
        // every character with no filter set, or just one character's own top 10 if
        // Config.PersonalLeaderboardCharacterFilter is set — and writes it into the leaderboard's
        // existing entry rows, exactly the same rows the game's own tabs populate.
        private static void OnPersonalLeaderboardClick()
        {
            try
            {
                if (_lbLeaderboardDisplay == null)
                {
                    _instance?._logger.Warning("BetterBonk: Personal Leaderboard click handler ran before the leaderboard screen was ever seen — nothing to populate.");
                    return;
                }

                string filter = _instance?.Config.PersonalLeaderboardCharacterFilter;
                List<KeyValuePair<ECharacter, int>> candidateList = new List<KeyValuePair<ECharacter, int>>();
                foreach (KeyValuePair<ECharacter, List<int>> characterRuns in _lbRecordedRuns)
                {
                    if (!string.IsNullOrEmpty(filter) && characterRuns.Key.ToString() != filter)
                        continue;

                    foreach (int kills in characterRuns.Value)
                        candidateList.Add(new KeyValuePair<ECharacter, int>(characterRuns.Key, kills));
                }

                List<KeyValuePair<ECharacter, int>> topTen = candidateList
                    .OrderByDescending(pair => pair.Value)
                    .Take(10)
                    .ToList();

                _instance?._logger.Msg($"BetterBonk: Personal Leaderboard click handler: {candidateList.Count} run(s) {(string.IsNullOrEmpty(filter) ? "across all characters" : $"for '{filter}'")}, {topTen.Count} row(s) to display.");

                DataManager dataManager = UnityEngine.Object.FindFirstObjectByType<DataManager>();
                // Unlike the array-like "buttons"/"existing" properties elsewhere in this file,
                // leaderboardEntries is an actual List<LeaderboardEntryUi> — confirmed by this
                // build error, not assumed — so it's Count, not Length.
                var entries = _lbLeaderboardDisplay.leaderboardEntries;

                int row = 0;
                foreach (KeyValuePair<ECharacter, int> entry in topTen)
                {
                    if (row >= entries.Count)
                        break;

                    LeaderboardEntryUi ui = entries[row];
                    ui.playerName.SetText(entry.Key.ToString());
                    ui.score.SetText(entry.Value.ToString());
                    if (dataManager != null)
                    {
                        UnityEngine.Texture icon = dataManager.GetCharacterData(entry.Key).icon;
                        ui.characterIcon.texture = icon;
                        ui.playerIcon.texture = icon;
                    }

                    row++;
                }

                for (; row < entries.Count; row++)
                    entries[row].Clear();
            }
            catch (Exception ex)
            {
                _instance?._logger.Warning($"BetterBonk: Personal Leaderboard click handler failed ({ex.Message}).");
            }
        }
    }
}
