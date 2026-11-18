// CT Fixes - two sync fixes for Casualties: Together, applied at runtime to the official build.
// Same fixes as upstream PRs #60 (prosthesis repair for clients) and #61 (use ack on high ping).
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using Together;
using UnityEngine;

namespace CTFixes {

    [BepInPlugin(GUID, "CT Fixes", VERSION)]
    [BepInDependency("CasualtiesMP")]
    public class Plugin : BaseUnityPlugin {
        public const string GUID = "stefan7573.ctfixes";
        public const string VERSION = "1.0.0";
        internal static ManualLogSource Log;

        void Awake() {
            Log = Logger;
            var harmony = new Harmony(GUID);
            Try("prosthesis repair for clients", () => ProsthesisFix.Apply(harmony));
            Try("use ack on high ping", () => UseAck.Apply(harmony));
        }

        void Start() => Try("use ack client handler", UseAck.RegisterClientHandler);

        void Update() => UseAck.Tick();

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

    // ---------------------------------------------------------------------------------------
    // Fix 2: use ack on high ping.
    // A client runs an item/object use locally and ignores the host's snapshots of that object
    // for a guessed window (0.3 s + RTT margin). On high or jittery ping the host's answer comes
    // later, the stale snapshot wins and the use rolls back (or goes through twice).
    // Host: acks every handled use request. Client: holds its predicted copy until the ack.
    // Both sides need the plugin; with a host without it the client keeps the old behaviour.
    // ---------------------------------------------------------------------------------------
    static class UseAck {
        const string Command = "use_ack";
        // message ids of the use requests the host answers (Together.NetmsgId, 5.5.3)
        static readonly HashSet<byte> Requests = new HashSet<byte> { 150, 125, 130, 131, 128 };
        static readonly (string type, string method)[] Receivers = {
            ("Together.Body_UseItem_MultiplayerPatch", "Server_RequestUseItem"),
            ("Together.BandageMinigame_DoBandageAction_MultiplayerPatch", "Server_BandageMinigame_DoBandageAction"),
            ("Together.Openable_OnUse_MultiplayerPatch", "Server_OpenableOpenTypeShit"),
            ("Together.ScrapEaterScript_OnUse_MultiplayerPatch", "Server_ScrapEaterScriptUse"),
            ("Together.DrillPod_OnUse_MultiplayerPatch", "Server_DrillPodRepairTypeShit"),
        };

        class Pending { public int count; public double deadline; }
        static readonly ConditionalWeakTable<SyncInfo, Pending> pending = new ConditionalWeakTable<SyncInfo, Pending>();
        // set once the current host has acked something: an official host never does
        static bool hostAcks;

        public static void Apply(Harmony h) {
            foreach (var (type, method) in Receivers) {
                var m = AccessTools.Method(AccessTools.TypeByName(type), method);
                if (m == null) {
                    throw new MissingMethodException(type, method);
                }
                h.Patch(m, prefix: new HarmonyMethod(typeof(UseAck), nameof(PeekItem)),
                    finalizer: new HarmonyMethod(typeof(UseAck), nameof(SendAck)));
            }
            foreach (var args in new[] {
                new[] { typeof(byte).MakeByRefType(), typeof(ushort), typeof(bool) },
                new[] { typeof(byte).MakeByRefType(), typeof(ushort), typeof(ushort), typeof(bool) },
                new[] { typeof(byte).MakeByRefType(), typeof(ushort), typeof(ushort), typeof(byte), typeof(bool) },
            }) {
                var send = AccessTools.Method(typeof(Multiplayer), nameof(Multiplayer.Client_SendSimpleMessageToServer), args);
                h.Patch(send, prefix: new HarmonyMethod(typeof(UseAck), nameof(OnClientSend)));
            }
            h.Patch(AccessTools.Method(typeof(SyncInfo), nameof(SyncInfo.IsIgnored)),
                prefix: new HarmonyMethod(typeof(UseAck), nameof(IsIgnoredPrefix)));
        }

        public static void RegisterClientHandler() {
            var f = AccessTools.Field(typeof(ClientMain), "ClientCustomCommandsDict");
            var dict = (Dictionary<string, Action<string, NetDataReader>>)f.GetValue(null);
            dict[Command] = (cmd, reader) => {
                ushort id = reader.GetUShort();
                hostAcks = true;
                if (NetObjectRegistry.TryGetSyncInfo(id, out SyncInfo si) && si != null) {
                    OnAck(si);
                }
            };
        }

        public static void Tick() {
            if (!Net.IsRunning) {
                hostAcks = false;                          // a new connection may be to an official host
            }
        }

        // --- host ------------------------------------------------------------------------
        static void PeekItem(NetDataReader reader, out ushort __state) {
            __state = reader != null && reader.AvailableBytes >= 2 ? reader.PeekUShort() : (ushort)0;
        }

        static void SendAck(knetid clientId, ushort __state) {
            try {
                if (!Net.IsServer || __state == 0) {
                    return;
                }
                if (!ScavPlayer.TryGetPlayerFromClientId(clientId, out ScavPlayer plr) || plr == null || plr.IsLocal) {
                    return;
                }
                NetDataWriter w = Multiplayer.CreateNamedWriter("CustomCommand");
                w.Put(Command);
                w.Put(__state);
                knetid to = plr.playerId;
                Net.Server_SendToClients(DeliveryMethod.ReliableOrdered, w, to);
            } catch (Exception e) {
                Plugin.Log.LogWarning("use ack: " + e.Message);
            }
        }

        // --- client ----------------------------------------------------------------------
        static void OnClientSend(object[] __args) {
            if (!hostAcks || !Net.IsClient || !Requests.Contains((byte)__args[0])) {
                return;
            }
            if (NetObjectRegistry.TryGetSyncInfo((ushort)__args[1], out SyncInfo si) && si != null) {
                var p = pending.GetOrCreateValue(si);
                float rtt = Mathf.Max(0f, ClientMain.RTT_IN_SECONDS_with_margin);
                p.count++;
                p.deadline = Math.Max(p.deadline, Time.realtimeSinceStartupAsDouble + 1.5 + Math.Min(rtt * 2f, 6f));
            }
        }

        static bool IsIgnoredPrefix(SyncInfo __instance, ref bool __result) {
            if (!pending.TryGetValue(__instance, out Pending p) || p.count <= 0) {
                return true;
            }
            if (Time.realtimeSinceStartupAsDouble < p.deadline) {
                __result = true;                           // still waiting for the host
                return false;
            }
            p.count = 0;                                   // the host never answered: back to normal
            return true;
        }

        static void OnAck(SyncInfo si) {
            if (!pending.TryGetValue(si, out Pending p) || p.count <= 0) {
                return;
            }
            if (--p.count > 0) {
                return;
            }
            // the host state is final; the snapshot carrying it can trail the reliable ack a little
            int span = Math.Max(2, (int)(0.25f * Net.ServerEntitySendRate));
            ushort tick = (ushort)(Net.ServerTick + span);
            if ((short)(tick - si.lastUpdateTick) > 0) {
                si.lastUpdateTick = tick;
            }
        }
    }
}
