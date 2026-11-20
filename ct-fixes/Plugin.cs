// CT Fixes - small fixes for Casualties: Together, applied at runtime to the official build.
// Same fixes as upstream PRs #60, #69 and #70. The high-ping fix (PR #61) is built into the mod since 5.5.4.
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using CasualtiesTogetherUtils;
using HarmonyLib;
using Together;
using UnityEngine;

namespace CTFixes {

    [BepInPlugin(GUID, "CT Fixes", VERSION)]
    [BepInDependency("CasualtiesMP")]
    public class Plugin : BaseUnityPlugin {
        public const string GUID = "stefan7573.ctfixes";
        public const string VERSION = "1.2.0";
        internal static ManualLogSource Log;

        void Awake() {
            Log = Logger;
            var harmony = new Harmony(GUID);
            Try("prosthesis repair for clients", () => ProsthesisFix.Apply(harmony));
            Try("scatter rule player count", () => ScatterFix.Apply(harmony));
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
            // Always look the count up again, even if there is an entry already: sync ids are reused,
            // so an old entry can belong to a different item that had this id before (PR #70).
            ushort id = item_syncid.id;
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

    // ---------------------------------------------------------------------------------------
    // Fix 2: the "don't scatter" rule counted the wrong players (PR #69).
    // The required group size comes from a filtered list (same team, past the 20 s spawn grace,
    // loaded in), but the players nearby were counted from everyone alive. With teams on,
    // enemies standing next to you counted as your group. Players without a body could also
    // throw a NullReferenceException there. Same method, counting from the filtered list.
    // Runs wherever the plugin is installed: the check happens on each player's own machine.
    // ---------------------------------------------------------------------------------------
    static class ScatterFix {
        public static void Apply(Harmony h) {
            h.Patch(AccessTools.Method(typeof(SharedMain), nameof(SharedMain.CheckIfShouldActivateDispersionPunishmentProtocol)),
                prefix: new HarmonyMethod(typeof(ScatterFix), nameof(Check)));
        }

        static bool Check(ScavPlayer plr, out float max_recorded_dist, out int count_plrs_in_range, out int required_count_plrs_in_range, ref bool __result) {
            count_plrs_in_range = ScavPlayer.AllLivingPlayers.Count;
            required_count_plrs_in_range = 0;
            max_recorded_dist = 0;
            __result = false;

            if (plr == null || plr.body == null || !plr.IsAlive() || plr.levelPlayTime < 20) {
                return false;
            }
            if (count_plrs_in_range <= 1) {
                return false;
            }

            List<ScavPlayer> plrs = new List<ScavPlayer>(ScavPlayer.AllLivingPlayers);
            plrs.RemoveAll(x => x == null || x.body == null || x.levelPlayTime < 20);
            if (Net.IsServer) {
                plrs.RemoveAll(x => x.ServerPlayerState == null || !x.ServerPlayerState.is_loaded_in);
            }
            if (Multiplayer.rules.Teams) {
                plrs.RemoveAll(x => !x.IsInSameTeamAs(plr));
            }
            if (plrs.Count <= 1) {
                return false;
            }

            required_count_plrs_in_range = Math.Max(1, (int)Mathf.Ceil(plrs.Count * (float)Multiplayer.rules.ScatterMinGroupSize * 0.01f));

            float presqr = Util.MetersToTiles(Multiplayer.rules.ScatterPunishDistance);
            presqr *= presqr;
            Vector3 pos = plr.body.transform.position;
            count_plrs_in_range = 0;
            foreach (ScavPlayer x in plrs) {
                float d = ((Vector2)(x.body.transform.position - pos)).sqrMagnitude;
                if (d < presqr) {
                    count_plrs_in_range++;
                    if (d > max_recorded_dist) {
                        max_recorded_dist = d;
                    }
                }
            }
            max_recorded_dist = Mathf.Sqrt(max_recorded_dist);

            __result = count_plrs_in_range < required_count_plrs_in_range;
            return false;                                  // skip the original
        }
    }
}
