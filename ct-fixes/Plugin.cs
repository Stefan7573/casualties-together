// CT Fixes - prosthesis repair for clients in Casualties: Together, applied at runtime to the official build.
// Same fix as upstream PR #60. The high-ping fix (PR #61) is built into the mod since 5.5.4.
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Together;

namespace CTFixes {

    [BepInPlugin(GUID, "CT Fixes", VERSION)]
    [BepInDependency("CasualtiesMP")]
    public class Plugin : BaseUnityPlugin {
        public const string GUID = "stefan7573.ctfixes";
        public const string VERSION = "1.1.0";
        internal static ManualLogSource Log;

        void Awake() {
            Log = Logger;
            var harmony = new Harmony(GUID);
            Try("prosthesis repair for clients", () => ProsthesisFix.Apply(harmony));
        }

        static void Try(string what, Action a) {
            try {
                a();
                Log.LogInfo("enabled: " + what);
            } catch (Exception e) {
                // a newer mod version may have renamed something; the game keeps working without this fix
                Log.LogError("could not enable " + what + ": " + e);
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Fix 1: prosthesis repair for clients.
    // The host only accepts a "prosthesis_patch" if ProsthesisRepair_RememberPatchCount.PatchCount
    // knows the kit's patch count, and that is filled only on the machine that opened the minigame.
    // For a client's repair the host never had it, so every patch was dropped and rolled back.
    // Here the host fills it in itself (from the item) right when it validates the request.
    // Host-side only: works with clients running the plain official build.
    // ---------------------------------------------------------------------------------------
    static class ProsthesisFix {
        // patch counts from the game's own item definitions (Item.cs, game 1.0.2)
        static readonly Dictionary<string, int> Known = new Dictionary<string, int> {
            ["scrapmetal"] = 16, ["scrapcube"] = 16, ["scrappanel"] = 2, ["scraptube"] = 6,
            ["woodscraps"] = 10, ["woodcube"] = 10, ["woodpanel"] = 1, ["stick"] = 4,
            ["titaniumslab"] = 50, ["titaniumsheet"] = 10, ["titaniumrod"] = 25,
        };

        static Dictionary<ushort, int> patchCount;
        [ThreadStatic] static bool probing;
        [ThreadStatic] static int probed;

        public static void Apply(Harmony h) {
            var remember = AccessTools.TypeByName("Together.ProsthesisRepair_RememberPatchCount");
            patchCount = (Dictionary<ushort, int>)AccessTools.Field(remember, "PatchCount").GetValue(null);
            h.Patch(AccessTools.Method(AccessTools.TypeByName("Together.MedicalSync"), "Server_CheckCanHealerUseItemForLimb"),
                postfix: new HarmonyMethod(typeof(ProsthesisFix), nameof(AfterCheck)));
            // lets us ask an unknown (modded) repair material for its patch count without starting a minigame
            h.Patch(AccessTools.Method(typeof(Item), nameof(Item.TryProsthesisRepair)),
                prefix: new HarmonyMethod(typeof(ProsthesisFix), nameof(ProbePrefix)) { priority = Priority.First });
        }

        static void AfterCheck(bool __result, knetid item_syncid, byte limb_id, ref NetBody target_nb, ref SyncInfo item_si) {
            if (!__result || !Net.IsServer || item_si == null || item_si.item == null) {
                return;
            }
            ushort id = item_syncid.id;
            if (patchCount.TryGetValue(id, out int have) && have >= 1) {
                return;                                    // the host opened this minigame itself
            }
            Item item = item_si.item;
            if (Known.TryGetValue(item.id, out int n)) {
                patchCount[id] = n;
                return;
            }
            n = Probe(item, target_nb, limb_id);
            if (n >= 1 && n <= 64) {
                patchCount[id] = n;
            }
        }

        static int Probe(Item item, NetBody target, byte limbId) {
            // only for materials: their limb action is the prosthesis repair, nothing else runs
            var stats = item.Stats;
            if (stats == null || stats.useLimbAction == null || stats.tags == null || !stats.tags.Contains("material")) {
                return 0;
            }
            if (target == null || target.body == null || limbId >= target.body.limbs.Length) {
                return 0;
            }
            probing = true;
            probed = 0;
            try {
                stats.useLimbAction(target.body.limbs[limbId], item);
            } catch (Exception) {
            } finally {
                probing = false;
            }
            return probed;
        }

        static bool ProbePrefix(int holeCount) {
            if (!probing) {
                return true;
            }
            probed = holeCount;
            return false;                                  // no minigame on the host
        }
    }
}
