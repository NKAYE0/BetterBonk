using System;
using System.Collections.Generic;
using Il2CppAssets.Scripts.Inventory__Items__Pickups.Interactables;
using UnityEngine;

namespace BetterBonk.Shared
{
    // Automatic pot breaking. The player supplied a reference mod (Potomatic) whose author
    // asked that direct copies of their code not be made, so nothing here is copied from it —
    // but after live testing broke unrelated things (see below), the player asked that its
    // decompiled IL specifically be examined for the correct technique, which is what the fix
    // below is grounded in.
    //
    // The first version of this file called pot.Interact() directly, once per pot per frame,
    // whenever the pot was within a range read from DetectInteractables.interactableRange. Live
    // testing showed two problems: pot breaking was still slower/less reliable than the reference
    // mod, and — much worse — it broke OTHER interactions entirely: chests got stuck mid-open
    // animation, and Shady Guys stopped responding to the interact key at all.
    //
    // Decompiling Potomatic's own IL (via the same dnfile/dncil tooling used earlier for
    // PersonalLeaderboard/MegabonkToggleEverything) explains both:
    //   - DetectInteractables keeps its own currentInteractable state and does bookkeeping in
    //     TryInteract() that a bare BaseInteractable.Interact() call never goes through. Calling
    //     Interact() directly leaves that shared state inconsistent for whatever the player
    //     genuinely tries to interact with next — exactly the chest/Shady-Guy symptoms reported.
    //     The reference mod always calls, in this exact order: pot.StartHover(detector), then
    //     detector.TryInteract(), then pot.Interact() — confirmed against Assembly-CSharp.dll's
    //     real signatures (BaseInteractable.StartHover(DetectInteractables),
    //     DetectInteractables.TryInteract(), BaseInteractable.Interact()). This file now
    //     reproduces that same three-call sequence instead of a bare Interact().
    //   - The reference mod also never repeats this sequence on a pot once it has actually
    //     broken it — tracked by GetInstanceID() in a set that's only added to ON SUCCESS. An
    //     earlier version of this file added a pot to that set unconditionally, the instant it
    //     was attempted, regardless of whether the interact sequence actually succeeded. That is
    //     the real explanation for pots that sat in full view with the native "E Break pot" prompt
    //     showing and never broke: one attempt happened to fail (whatever the transient reason —
    //     a hover/interact race the very first frame a pot came into range, for instance), and
    //     that pot was then permanently blacklisted and never tried again. Fixed to match the
    //     reference exactly: retry every frame while in range until the interact sequence reports
    //     success, and only then stop trying that pot.
    //   - The reference mod checks a fixed 6-unit (36 squared) range from the detector's own
    //     transform. Matching that exact value still left pots outside it unbroken, since this
    //     game's native interact prompt (DetectInteractables.interactableRange) isn't necessarily
    //     that same 6 units — so the range here is now whichever is larger of that native value
    //     and a further-widened baseline, per continued player feedback that the radius still
    //     needed to be bigger even after the interaction-sequence fix above.
    // pot.CanInteract() is still checked first, same as the reference mod — restored from an
    // intermediate version of this file that had removed it based on a misreading of an earlier,
    // inconclusive test; the reference mod's own IL confirms it's meant to gate this.
    //
    // Per player feedback this still checks every frame rather than on a throttled poll, so a
    // newly-in-range pot is attempted as soon as possible — the once-broken-only tracking above is
    // what keeps that safe without needing a per-pot cooldown.
    //
    // A pot breaking right next to a curse shrine (or presumably any other interactable) was also
    // separately activating that other interactable. detector.TryInteract() doesn't act on
    // whatever pot.StartHover(detector) was just called with — it acts on the detector's own
    // currentInteractable, which the game's native per-frame scan may have already pointed at
    // that nearby shrine before our code ever ran this frame. So TryInteract() was interacting
    // with the shrine (whatever the detector already considered "current"), while our own direct
    // pot.Interact() call separately broke the pot — two real interactions from one call
    // sequence. Fixed by explicitly setting detector.currentInteractable to the pot first
    // (confirmed real, publicly settable property via signature lookup), so TryInteract() acts on
    // the pot specifically, then restoring whatever it was before once done.
    public sealed partial class ModCore
    {
        // 8 units, widened past the reference mod's own 6-unit (36 squared) constant per player
        // feedback that pots were still being missed just outside whatever radius was in effect.
        private const float BaselineRangeSqr = 64f;
        private static readonly HashSet<int> _brokenPotIds = new HashSet<int>();

        private void UpdatePotBreaking()
        {
            if (!Config.AutoBreakPots)
                return;

            TryBreakNearbyPots();
        }

        private void TryBreakNearbyPots()
        {
            try
            {
                DetectInteractables detector = UnityEngine.Object.FindObjectOfType<DetectInteractables>();
                if (detector == null)
                    return;

                Vector3 origin = detector.transform.position;
                float nativeRange = detector.interactableRange;
                float rangeSqr = Mathf.Max(BaselineRangeSqr, nativeRange * nativeRange);

                // var, not InteractablePot[] — FindObjectsOfType<T>() returns an IL2CPP interop
                // array wrapper, not a plain C# T[] (see the identical note in ModCore.cs's
                // CheckSpawns) — foreach works on it regardless of the exact wrapper type.
                var pots = UnityEngine.Object.FindObjectsOfType<InteractablePot>();
                foreach (InteractablePot pot in pots)
                {
                    if (pot == null || pot.broken)
                        continue;

                    int id = pot.GetInstanceID();
                    if (_brokenPotIds.Contains(id))
                        continue;

                    Vector3 delta = pot.transform.position - origin;
                    float distSqr = (delta.x * delta.x) + (delta.y * delta.y) + (delta.z * delta.z);
                    if (distSqr > rangeSqr)
                        continue;

                    if (!pot.CanInteract())
                        continue;

                    // Only marked once the interact sequence actually reports success — matching
                    // the reference mod exactly. Retries every frame on failure, since a pot still
                    // sitting there unbroken should keep getting tried, not get permanently
                    // skipped over one bad attempt.
                    if (HoverThenInteract(pot, detector))
                        _brokenPotIds.Add(id);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: pot breaking check failed ({ex.Message}).");
            }
        }

        // Mirrors the reference mod's exact interaction sequence — see the file header for why
        // pot.Interact() alone isn't enough, and for why currentInteractable is pinned to the
        // pot around the TryInteract() call specifically.
        private static bool HoverThenInteract(InteractablePot pot, DetectInteractables detector)
        {
            var previousInteractable = detector.currentInteractable;
            try
            {
                pot.StartHover(detector);
                detector.currentInteractable = pot;
                detector.TryInteract();
                return pot.Interact();
            }
            catch (Exception ex)
            {
                _instance?._logger.Warning($"BetterBonk: pot breaking interact sequence failed ({ex.Message}).");
                return false;
            }
            finally
            {
                detector.currentInteractable = previousInteractable;
            }
        }
    }
}
