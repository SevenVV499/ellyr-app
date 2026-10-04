using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace EtatJoueurMod
{
    public static class Hooks
    {
        public static void Appliquer(Harmony harmony)
        {
            RpcRecompenses.InitialiserHashesCollectibles();
            int actifs = 0;
            int ignores = 0;
            int echecs = 0;

            Type[] types;
            try
            {
                types = typeof(Hooks).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
            }

            foreach (Type type in types)
            {
                if (type == null || !type.IsDefined(typeof(HarmonyPatch), false))
                    continue;

                try
                {
                    var methodes = harmony.CreateClassProcessor(type).Patch();
                    if (methodes == null || methodes.Count == 0)
                    {
                        ignores++;
                        Plugin.Logger.LogWarning($"[EtatJoueur] Patch {type.Name} ignoré (aucune méthode patchée).");
                    }
                    else
                    {
                        actifs++;
                    }
                }
                catch (Exception e)
                {
                    echecs++;
                    Plugin.Logger.LogError($"[EtatJoueur] Patch {type.Name} en échec : {e}");
                }
            }

            Patch_CallbacksCollectibles.Appliquer(harmony);
            RpcRecompenses.VerifierAuDemarrage();
        }

        internal static MethodInfo TrouverMethodeUnique(Type type, string nom)
        {
            MethodInfo trouvee = null;
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
            {
                if (m.Name != nom)
                    continue;
                if (trouvee != null)
                {
                    Plugin.Logger.LogError($"[EtatJoueur] {type.Name}.{nom} : plusieurs surcharges trouvées, patch impossible.");
                    return null;
                }
                trouvee = m;
            }

            if (trouvee == null)
                Plugin.Logger.LogError($"[EtatJoueur] {type.Name}.{nom} : méthode introuvable, patch ignoré.");
            return trouvee;
        }
    }

    internal static class RpcRecompenses
    {
        internal const string SigChest =
            "System.Void Chest::TargetSandikDonus(Mirror.NetworkConnection,System.Int32,System.Int32,System.Boolean,System.Int32)";
        internal const string SigSandikKontrol =
            "System.Void SandikKontrol::TargetSandikDonus(Mirror.NetworkConnection,System.Int32,System.Int32,System.Boolean,System.Int32)";
        internal const string SigAllMonsters =
            "System.Void AllMonsters::TargetOdulDonusu(Mirror.NetworkConnection,System.Int32,System.Int32,System.Boolean,System.Int32)";
        internal const string SigAllNpcs =
            "System.Void AllNpcs::TargetOdulDonusu(Mirror.NetworkConnection,System.Int32,System.Int32,System.Int32,System.Boolean,System.Int32)";

        private const ushort RefChest = 7243;
        private const ushort RefSandikKontrol = 7853;
        private const ushort RefAllMonsters = 42021;
        private const ushort RefAllNpcs = 12685;

        internal static readonly ushort HashChest = CalculerHash(SigChest);
        internal static readonly ushort HashSandikKontrol = CalculerHash(SigSandikKontrol);
        internal static readonly ushort HashAllMonsters = CalculerHash(SigAllMonsters);
        internal static readonly ushort HashAllNpcs = CalculerHash(SigAllNpcs);

        private static readonly Dictionary<Type, ushort> _hashesCollectibles =
            new Dictionary<Type, ushort>();

        // Tous les hashes de RPC de récompense attendus. Permet au hook réseau, appelé
        // pour chaque message du jeu, d'écarter les autres sans résoudre de type.
        private static readonly HashSet<ushort> _hashesRecompense = new HashSet<ushort>();

        internal static bool EstHashRecompense(ushort hash)
        {
            return _hashesRecompense.Contains(hash);
        }

        internal static ushort CalculerHash(string signature)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in signature)
                    hash = hash * 31 + c;
                return (ushort)(hash & 0xFFFF);
            }
        }

        internal static Type[] ObtenirTypesCollectibles()
        {
            return CollectibleCatalog.CollectibleTypes.ToArray();
        }

        internal static void InitialiserHashesCollectibles()
        {
            _hashesCollectibles.Clear();
            foreach (Type type in ObtenirTypesCollectibles())
            {
                MethodInfo methode = Hooks.TrouverMethodeUnique(type, "TargetSandikDonus");
                if (methode == null)
                    continue;

                ParameterInfo[] parametres = methode.GetParameters();
                string[] typesParametres = new string[parametres.Length];
                for (int i = 0; i < parametres.Length; i++)
                    typesParametres[i] = parametres[i].ParameterType.FullName;

                string signature = methode.ReturnType.FullName + " "
                    + type.Name + "::TargetSandikDonus("
                    + string.Join(",", typesParametres) + ")";
                ushort hash = CalculerHash(signature);

                if ((type == typeof(Chest) && hash != RefChest)
                    || (type == typeof(SandikKontrol) && hash != RefSandikKontrol))
                {
                    Plugin.Logger.LogError(
                        "[EtatJoueur] Hash RPC collectible incohérent pour " + type.Name
                        + " : " + hash + " (signature " + signature + ").");
                    continue;
                }

                _hashesCollectibles[type] = hash;
            }

            _hashesRecompense.Clear();
            _hashesRecompense.Add(HashAllMonsters);
            _hashesRecompense.Add(HashAllNpcs);
            foreach (ushort hash in _hashesCollectibles.Values)
                _hashesRecompense.Add(hash);
        }

        internal static bool TryGetHashCollectible(Type type, out ushort hash)
        {
            return _hashesCollectibles.TryGetValue(type, out hash);
        }

        internal static void VerifierAuDemarrage()
        {
            try
            {
                Controler("Chest", HashChest, RefChest);
                Controler("SandikKontrol", HashSandikKontrol, RefSandikKontrol);
                Controler("AllMonsters", HashAllMonsters, RefAllMonsters);
                Controler("AllNpcs", HashAllNpcs, RefAllNpcs);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"[EtatJoueur] Erreur VerifierAuDemarrage : {e}");
            }
        }

        private static void Controler(string nom, ushort calcule, ushort reference)
        {
            if (calcule == reference)
                return;
            else
                Plugin.Logger.LogError(
                    $"[EtatJoueur] Hash RPC {nom} incohérent : calculé {calcule} != référence {reference}. " +
                    "Signature erronée ou référence périmée : les récompenses de ce type risquent de ne pas être détectées.");
        }
    }

    internal static class Patch_CallbacksCollectibles
    {
        private static readonly MethodInfo PostfixMethod =
            AccessTools.Method(typeof(Patch_CallbacksCollectibles), nameof(Postfix));

        internal static void Appliquer(Harmony harmony)
        {
            foreach (Type type in RpcRecompenses.ObtenirTypesCollectibles())
            {
                ushort functionHash;
                if (!RpcRecompenses.TryGetHashCollectible(type, out functionHash))
                {
                    Plugin.Logger.LogError(
                        "[Collect] Hash RPC non vérifié pour " + type.Name
                        + " : callback non patché.");
                    continue;
                }

                MethodInfo callback = null;
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
                {
                    if (!method.Name.StartsWith("UserCode_TargetSandikDonus", StringComparison.Ordinal))
                        continue;

                    if (callback != null)
                    {
                        Plugin.Logger.LogError(
                            "[EtatJoueur] Callback collectible ambigu pour " + type.Name
                            + " : patch ignoré.");
                        callback = null;
                        break;
                    }
                    callback = method;
                }

                if (callback == null)
                {
                    Plugin.Logger.LogError(
                        "[EtatJoueur] Callback UserCode_TargetSandikDonus introuvable pour "
                        + type.Name + " : confirmation stricte indisponible.");
                    continue;
                }

                try
                {
                    harmony.Patch(callback, postfix: new HarmonyMethod(PostfixMethod));
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError(
                        "[Collect] Hook callback impossible pour " + type.Name + " : " + e);
                }
            }
        }

        private static void Postfix(
            NetworkBehaviour __instance,
            MethodBase __originalMethod)
        {

            try
            {
                if (__instance == null || __originalMethod == null)
                    return;

                Type type = __originalMethod.DeclaringType;
                ushort functionHash;
                if (type == null || !RpcRecompenses.TryGetHashCollectible(type, out functionHash))
                    return;

                NetworkIdentity identity = __instance.netIdentity;
                if (identity == null || identity.netId == 0)
                    return;

                GameState.EnfilerCallbackCollectible(
                    identity.netId,
                    type.Name,
                    functionHash,
                    DateTime.UtcNow);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[Collect] Échec d'observation du callback : " + e);
            }

        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    public static class Patch_Player_Update
    {
        private static float _dernierTickDecision;
        private const float IntervalleDecision = 0.5f;

        // Écart minimal entre deux instantanés quand des dégâts en demandent un
        // immédiat : évite de reconstruire l'instantané à chaque coup reçu.
        private const float IntervalleUrgent = 0.1f;

        public static void Postfix(Player __instance)
        {
            try
            {
                if (__instance == null || !__instance.isLocalPlayer) return;

                TargetCatalog.Initialize();
                AmmoCatalog.Initialize(__instance);
                CollectibleCatalog.Initialize();
                Plugin.InitializeCollectibleConfiguration();

                GameState.Tick(__instance);

                float ecoule = Time.time - _dernierTickDecision;
                if (ecoule >= IntervalleDecision
                    || (ecoule >= IntervalleUrgent && GameState.InstantaneUrgentDemande))
                {
                    _dernierTickDecision = Time.time;
                    GameState.EffacerInstantaneUrgent();
                    if (GameState.JoueurLocal != null)
                    {
                        GameState.PreparerEtEnvoyerInstantane();
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"[EtatJoueur] Erreur Patch_Player_Update : {e}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), "CanKontrol")]
    public static class Patch_Player_CanKontrol
    {
        public static void Postfix(Player __instance, int oldvalue, int newvalue)
        {
            try
            {
                if (__instance == null || !__instance.isLocalPlayer) return;

                if (newvalue > 0 && newvalue < oldvalue)
                    GameState.SignalerDegats(newvalue, __instance.MaksCan);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"[EtatJoueur] Erreur Patch_Player_CanKontrol : {e}");
            }
        }
    }

    [HarmonyPatch(typeof(NetworkIdentity), "HandleRemoteCall")]
    public static class Patch_NetworkIdentity_HandleRemoteCall
    {

        private const int MaxHashInconnusLogues = 30;
        private const int SeuilAlerteSansSucces = 50;

        private sealed class Stat
        {
            public int Ok;
            public int Inconnu;
            public bool AlerteEmise;
        }

        private static readonly object _verrou = new object();
        private static readonly System.Collections.Generic.Dictionary<string, Stat> _stats =
            new System.Collections.Generic.Dictionary<string, Stat>();
        private static readonly System.Collections.Generic.HashSet<(string, ushort)> _inconnusLogues =
            new System.Collections.Generic.HashSet<(string, ushort)>();
        private static int _premierAppel;

        // Les hashes hors récompense ne sont examinés qu'une fois sur 32 : cela garde
        // les statistiques de diagnostic (hash périmé) sans payer le coût de résolution
        // du type à chaque message réseau du jeu.
        private const int EchantillonnageInconnus = 32;
        private static int _compteurInconnus;

        [HarmonyPrepare]
        public static bool Prepare()
        {
            MethodInfo methode = Hooks.TrouverMethodeUnique(typeof(NetworkIdentity), "HandleRemoteCall");
            if (methode == null)
                return false;

            ParameterInfo[] p = methode.GetParameters();
            if (p.Length < 2 || p[0].ParameterType != typeof(byte) || p[1].ParameterType != typeof(ushort))
            {
                Plugin.Logger.LogError(
                    "[EtatJoueur] HandleRemoteCall : signature inattendue " +
                    $"({p.Length} paramètres, premiers types : " +
                    $"{(p.Length > 0 ? p[0].ParameterType.Name : "-")}, " +
                    $"{(p.Length > 1 ? p[1].ParameterType.Name : "-")}). " +
                    "Attendu (byte, ushort, ...). Patch désactivé : les récompenses ne seront pas détectées.");
                return false;
            }
            return true;
        }

        public static void Prefix(NetworkIdentity __instance, byte __0, ushort __1)
        {
            try
            {

                if (!RpcRecompenses.EstHashRecompense(__1)
                    && (++_compteurInconnus % EchantillonnageInconnus) != 0)
                    return;

                if (__instance == null) return;

                NetworkBehaviour[] composants = __instance.NetworkBehaviours;
                byte componentIndex = __0;
                if (composants == null || componentIndex >= composants.Length) return;

                NetworkBehaviour composant = composants[componentIndex];
                if (composant == null) return;

                ushort functionHash = __1;

                RewardSourceType type = IdentifierSourceRecompense(composant, functionHash);
                if (type == RewardSourceType.Inconnu) return;

                uint sourceNetId = __instance.netId;

                GameState.EnfilerEvenementRecompense(
                    new RewardEvent(sourceNetId, type, functionHash, DateTime.UtcNow));
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"[EtatJoueur] Erreur Patch_NetworkIdentity_HandleRemoteCall : {e}");
            }

        }

        private static RewardSourceType IdentifierSourceRecompense(NetworkBehaviour composant, ushort functionHash)
        {
            ushort hashCollectible;
            if (composant != null
                && RpcRecompenses.TryGetHashCollectible(composant.GetType(), out hashCollectible))
                return Evaluer(
                    composant.GetType().Name,
                    functionHash,
                    hashCollectible,
                    RewardSourceType.Collectible);

            if (composant.TryCast<AllMonsters>() != null)
                return Evaluer("AllMonsters", functionHash, RpcRecompenses.HashAllMonsters, RewardSourceType.Monstre);

            if (composant.TryCast<AllNpcs>() != null)
                return Evaluer("AllNpcs", functionHash, RpcRecompenses.HashAllNpcs, RewardSourceType.Pnj);

            return RewardSourceType.Inconnu;
        }

        private static RewardSourceType Evaluer(string nomType, ushort recu, ushort attendu, RewardSourceType type)
        {
            lock (_verrou)
            {
                Stat stat;
                if (!_stats.TryGetValue(nomType, out stat))
                {
                    stat = new Stat();
                    _stats[nomType] = stat;
                }

                if (recu == attendu)
                {
                    stat.Ok++;
                    return type;
                }

                stat.Inconnu++;

                if (stat.Ok == 0 && stat.Inconnu >= SeuilAlerteSansSucces && !stat.AlerteEmise)
                {
                    stat.AlerteEmise = true;
                    Plugin.Logger.LogWarning(
                        $"[EtatJoueur] {stat.Inconnu} RPC reçus sur {nomType} sans aucun hash de récompense reconnu " +
                        $"(attendu {attendu}). Le hash est peut-être périmé après une mise à jour du jeu : " +
                        "vérifier la signature dans RpcRecompenses.");
                }

                return RewardSourceType.Inconnu;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "guverteleraktifMojo")]
    public static class Patch_Player_GuverteleraktifMojo
    {
        private static int _nbBloques;

        [HarmonyPrepare]
        public static bool Prepare()
        {
            MethodInfo methode = Hooks.TrouverMethodeUnique(typeof(Player), "guverteleraktifMojo");
            if (methode == null)
                return false;

            if (methode.ReturnType != typeof(void))
            {
                Plugin.Logger.LogError(
                    $"[EtatJoueur] guverteleraktifMojo retourne désormais {methode.ReturnType.Name} : " +
                    "bloquer l'appel d'origine n'est plus sûr, patch désactivé.");
                return false;
            }
            return true;
        }

        public static bool Prefix(Player __instance)
        {
            if (!CopperWire.AutomationEnabled)
                return true;

            if (__instance == null) return true;
            if (__instance.isLocalPlayer) return true;

            return false;
        }
    }

    // Clic sur la mer (déplacement manuel) : la console est dessinée en IMGUI, qui ne
    // bloque pas les clics lus par le jeu. Tant que le pointeur est sur la console, le
    // gestionnaire de clic du jeu est donc sauté.
    [HarmonyPatch(typeof(Player), "OyuncuGitmekIstenilenYereTikla")]
    public static class Patch_Player_ClicSurMer
    {
        [HarmonyPrepare]
        public static bool Prepare()
        {
            MethodInfo methode = Hooks.TrouverMethodeUnique(typeof(Player), "OyuncuGitmekIstenilenYereTikla");
            if (methode == null)
                return false;

            if (methode.ReturnType != typeof(void) || methode.GetParameters().Length != 0)
            {
                Plugin.Logger.LogError(
                    "[EtatJoueur] OyuncuGitmekIstenilenYereTikla a une signature inattendue : "
                    + "la console ne bloquera pas les clics sur la mer.");
                return false;
            }
            return true;
        }

        public static bool Prefix()
        {
            return !BotTestConsoleBehaviour.PointerOverConsole;
        }
    }
}
