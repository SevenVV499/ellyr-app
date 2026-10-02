using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mirror;
using UnityEngine;

namespace EtatJoueurMod
{

    public class FicheJoueur
    {
        public int IdGlobal;
        public string Nom;
        public string GuildTag;
        public int Vie;
        public int VieMax;
        public float PourcentageVie;
        public int Harita;
        public string NomHarita;
        public float X;
        public float Y;
        public string CoordonneeSayi;
        public string CoordonneeHarf;
        public bool EnModeAttaque;
        public float DernierTirHarpon;
        public long DernierTirCanonSequence;
        public uint DernierTirCanonCibleNetId;
        public uint CibleAttaqueNetId;
        public bool PanneauMortVisible;
        public bool BoutonReapparitionDisponible;
        public bool Reparation;
        public float Portee;
        public float PorteeHarpon;
        public long Or;
        public long Perle;
        public long Exp;
        public int ClesCoffre;
        public int ClesCoffreIcePearl;
        public float TargetX;
        public float TargetY;
        public float Vitesse;

    }

    public class CollectibleInfo
    {
        public uint Id;
        public string Categorie;
        public string Type;
        public float X;
        public float Y;
        public float Z;
        public float Distance;
    }

    public class NavireInfo
    {
        public int IdGlobal;
        public string Nom;
        public string GuildTag;
        public float Portee;
        public float X;
        public float Y;
        public float Distance;
    }

    public class PnjInfo
    {
        public uint Id;
        public string Categorie;
        public string Type;
        public string Nom;
        public int Vie;
        public int VieMax;
        public float Portee;
        public int Harita;
        public string NomHarita;
        public string CoordonneeSayi;
        public string CoordonneeHarf;
        public float X;
        public float Y;
        public float Distance;
    }

    public enum RewardSourceType
    {
        Inconnu,
        Collectible,
        Monstre,
        Pnj
    }

    public sealed class RewardEvent
    {
        public uint SourceNetId { get; }
        public RewardSourceType SourceType { get; }
        public ushort FunctionHash { get; }
        public DateTime TimestampUtc { get; }
        public long Sequence { get; }

        public RewardEvent(
            uint sourceNetId,
            RewardSourceType sourceType,
            ushort functionHash,
            DateTime timestampUtc,
            long sequence = 0)
        {
            SourceNetId = sourceNetId;
            SourceType = sourceType;
            FunctionHash = functionHash;
            TimestampUtc = timestampUtc;
            Sequence = sequence;
        }
    }

    public sealed class CollectibleCallbackEvent
    {
        public uint SourceNetId { get; }
        public string CollectibleType { get; }
        public ushort FunctionHash { get; }
        public DateTime TimestampUtc { get; }
        public long Sequence { get; }

        public CollectibleCallbackEvent(
            uint sourceNetId,
            string collectibleType,
            ushort functionHash,
            DateTime timestampUtc,
            long sequence)
        {
            SourceNetId = sourceNetId;
            CollectibleType = collectibleType;
            FunctionHash = functionHash;
            TimestampUtc = timestampUtc;
            Sequence = sequence;
        }
    }

    public enum CollectConfirmationState
    {
        None,
        CallbackExecuted,
        RewardDetected,
        Confirmed
    }

    internal sealed class TamponEtat
    {
        public readonly FicheJoueur Joueur = new FicheJoueur();
        public readonly List<CollectibleInfo> Collectibles = new List<CollectibleInfo>();
        public readonly List<NavireInfo> Navires = new List<NavireInfo>();
        public readonly List<PnjInfo> Pnjs = new List<PnjInfo>();
        public DateTime Timestamp;
    }

    public sealed class EtatJeuSnapshot
    {
        public FicheJoueur Joueur { get; }
        public IReadOnlyList<CollectibleInfo> Collectibles { get; }
        public IReadOnlyList<NavireInfo> Navires { get; }
        public IReadOnlyList<PnjInfo> Pnjs { get; }
        public IReadOnlyList<RewardEvent> Rewards { get; }
        public IReadOnlyList<CollectibleCallbackEvent> CollectibleCallbacks { get; }
        public DateTime Timestamp { get; }

        public EtatJeuSnapshot(
            FicheJoueur joueur,
            IReadOnlyList<CollectibleInfo> collectibles,
            IReadOnlyList<NavireInfo> navires,
            IReadOnlyList<PnjInfo> pnjs,
            IReadOnlyList<RewardEvent> rewards,
            IReadOnlyList<CollectibleCallbackEvent> collectibleCallbacks,
            DateTime timestamp)
        {
            Joueur = joueur;
            Collectibles = collectibles;
            Navires = navires;
            Pnjs = pnjs;
            Rewards = rewards;
            CollectibleCallbacks = collectibleCallbacks;
            Timestamp = timestamp;
        }
    }

    public static class GameState
    {
        private static readonly MethodInfo Il2CppTypeOf = TrouverIl2CppTypeOf();
        public static Player JoueurLocal;

        private static readonly List<Player> NaviresProches = new List<Player>();
        private static readonly List<NetworkIdentity> _identitesSpawned =
            new List<NetworkIdentity>();
        private static readonly List<SuiviCoffres.Coffre> CoffresProches = new List<SuiviCoffres.Coffre>();
        private static List<PnjSuivi> PnjsProches = new List<PnjSuivi>();
        private static List<PnjSuivi> _pnjsEnCours = new List<PnjSuivi>();
        private static readonly List<SuiviCoffres.Coffre> _coffrePool = new List<SuiviCoffres.Coffre>();
        private static int _coffrePoolIndex;
        private static readonly List<CollectibleScanResult> _collectibleScanResults =
            new List<CollectibleScanResult>();
        private static readonly Dictionary<Type, Il2CppSystem.Type> _collectibleIl2CppTypes =
            new Dictionary<Type, Il2CppSystem.Type>();
        private static bool _collectibleScanFailed;
        private static bool _spawnedIdentityScanFailed;
        private static List<PnjSuivi> _suiviPool = new List<PnjSuivi>();
        private static List<PnjSuivi> _suiviPoolPublie = new List<PnjSuivi>();
        private static int _suiviPoolIndex;
        private static bool _pnjScanFailed;
        private static readonly Queue<Action> _fileScan = new Queue<Action>();
        private static float _dernierRemplissage;
        private static int _derniereCarteScannee;
        private static bool _carteScanneeConnue;
        private const float IntervalleRemplissage = 2.0f;
        private const int ScansParTick = 1;

        private class PnjSuivi
        {
            public string Categorie;
            public string Type;
            public NetworkBehaviour Instance;
            public IPnjLecteur Lecteur;
        }

        private struct CollectibleScanResult
        {
            public string Categorie;
            public string Type;
            public NetworkBehaviour Instance;
        }

        private interface IPnjLecteur
        {
            string Nom(NetworkBehaviour nb);
            int Vie(NetworkBehaviour nb);
            int VieMax(NetworkBehaviour nb);
            float Portee(NetworkBehaviour nb);
        }

        private sealed class PnjLecteur<T> : IPnjLecteur where T : NetworkBehaviour
        {
            private readonly Func<T, string> _nom;
            private readonly Func<T, int> _vie;
            private readonly Func<T, int> _vieMax;
            private readonly Func<T, float> _portee;

            public PnjLecteur(Func<T, string> nom, Func<T, int> vie, Func<T, int> vieMax, Func<T, float> portee)
            {
                _nom = nom; _vie = vie; _vieMax = vieMax; _portee = portee;
            }

            public string Nom(NetworkBehaviour nb) => _nom(nb.TryCast<T>());
            public int Vie(NetworkBehaviour nb) => _vie(nb.TryCast<T>());
            public int VieMax(NetworkBehaviour nb) => _vieMax(nb.TryCast<T>());
            public float Portee(NetworkBehaviour nb) => _portee(nb.TryCast<T>());
        }

        private static class LecteurCache<T> where T : NetworkBehaviour
        {
            public static IPnjLecteur Valeur;
        }

        private static readonly TamponEtat _tamponCourant = new TamponEtat();
        private static EtatJeuSnapshot _snapshotPublie;

        private struct CacheEntree
        {
            public int Harita;
            public string Sayi, Harf;
            public float RefX, RefY;
        }

        private const float SeuilDeplacement = 0.5f;

        private static long _dernierTirCanonSequence;
        private static uint _dernierTirCanonCibleNetId;

        private static readonly Dictionary<uint, CacheEntree> _cachePnj = new Dictionary<uint, CacheEntree>();
        private static readonly HashSet<uint> _idsPnjVus = new HashSet<uint>();
        private static readonly List<uint> _aSupprimerPnj = new List<uint>();

        private static readonly ConcurrentQueue<RewardEvent> _fileRecompenses = new ConcurrentQueue<RewardEvent>();
        private static readonly List<RewardEvent> _historiqueRecompenses = new List<RewardEvent>();
        private static readonly ConcurrentQueue<CollectibleCallbackEvent> _fileCallbacksCollectibles =
            new ConcurrentQueue<CollectibleCallbackEvent>();
        private static readonly List<CollectibleCallbackEvent> _historiqueCallbacksCollectibles =
            new List<CollectibleCallbackEvent>();
        private const int TailleMaxHistoriqueRecompenses = 20;
        private static readonly TimeSpan DureeRetentionRecompenses = TimeSpan.FromSeconds(30);
        private static long _sequenceRecompense;
        private static long _sequenceCallbackCollectible;
        private static long _revisionSignauxCollecte;

        public static long RevisionSignauxCollecte
        {
            get { return Interlocked.Read(ref _revisionSignauxCollecte); }
        }

        public static void EnfilerEvenementRecompense(RewardEvent evenement)
        {
            if (evenement == null) return;

            long sequence = Interlocked.Increment(ref _sequenceRecompense);
            var enregistrement = new RewardEvent(
                evenement.SourceNetId,
                evenement.SourceType,
                evenement.FunctionHash,
                evenement.TimestampUtc,
                sequence);
            _fileRecompenses.Enqueue(enregistrement);
            if (enregistrement.SourceType == RewardSourceType.Collectible)
                Interlocked.Increment(ref _revisionSignauxCollecte);
        }

        public static void EnfilerCallbackCollectible(
            uint sourceNetId,
            string collectibleType,
            ushort functionHash,
            DateTime timestampUtc)
        {
            if (sourceNetId == 0 || string.IsNullOrEmpty(collectibleType))
                return;

            long sequence = Interlocked.Increment(ref _sequenceCallbackCollectible);
            var evenement = new CollectibleCallbackEvent(
                sourceNetId,
                collectibleType,
                functionHash,
                timestampUtc,
                sequence);
            _fileCallbacksCollectibles.Enqueue(evenement);
            Interlocked.Increment(ref _revisionSignauxCollecte);
        }

        public static void ObtenirSequencesConfirmationCollecte(out long callbackSequence, out long rewardSequence)
        {
            callbackSequence = Interlocked.Read(ref _sequenceCallbackCollectible);
            rewardSequence = Interlocked.Read(ref _sequenceRecompense);
        }

        public static CollectConfirmationState ObtenirConfirmationCollecte(
            EtatJeuSnapshot snapshot,
            uint netId,
            string collectibleType,
            DateTime startedAtUtc,
            long callbackSequenceBaseline,
            long rewardSequenceBaseline)
        {
            if (snapshot == null || netId == 0 || string.IsNullOrEmpty(collectibleType))
                return CollectConfirmationState.None;

            ushort callbackHash = 0;
            bool callbackFound = FindCollectibleCallback(
                snapshot.CollectibleCallbacks,
                netId,
                collectibleType,
                startedAtUtc,
                callbackSequenceBaseline,
                out callbackHash);
            if (!callbackFound)
            {
                foreach (CollectibleCallbackEvent callback in _fileCallbacksCollectibles)
                {
                    if (MatchesCollectibleCallback(
                        callback,
                        netId,
                        collectibleType,
                        startedAtUtc,
                        callbackSequenceBaseline,
                        out callbackHash))
                    {
                        callbackFound = true;
                        break;
                    }
                }
            }

            bool rewardFound = FindCollectibleReward(
                snapshot.Rewards,
                netId,
                startedAtUtc,
                rewardSequenceBaseline,
                callbackHash,
                callbackFound);
            if (!rewardFound)
            {
                foreach (RewardEvent reward in _fileRecompenses)
                {
                    if (MatchesCollectibleReward(
                        reward,
                        netId,
                        startedAtUtc,
                        rewardSequenceBaseline,
                        callbackHash,
                        callbackFound))
                    {
                        rewardFound = true;
                        break;
                    }
                }
            }

            if (callbackFound && rewardFound)
                return CollectConfirmationState.Confirmed;
            if (callbackFound)
                return CollectConfirmationState.CallbackExecuted;
            if (rewardFound)
                return CollectConfirmationState.RewardDetected;
            return CollectConfirmationState.None;
        }

        private static bool FindCollectibleCallback(
            IReadOnlyList<CollectibleCallbackEvent> callbacks,
            uint netId,
            string collectibleType,
            DateTime startedAtUtc,
            long sequenceBaseline,
            out ushort functionHash)
        {
            functionHash = 0;
            if (callbacks == null)
                return false;

            for (int i = 0; i < callbacks.Count; i++)
            {
                if (MatchesCollectibleCallback(
                    callbacks[i],
                    netId,
                    collectibleType,
                    startedAtUtc,
                    sequenceBaseline,
                    out functionHash))
                    return true;
            }

            return false;
        }

        private static bool MatchesCollectibleCallback(
            CollectibleCallbackEvent callback,
            uint netId,
            string collectibleType,
            DateTime startedAtUtc,
            long sequenceBaseline,
            out ushort functionHash)
        {
            functionHash = 0;
            if (callback == null
                || callback.SourceNetId != netId
                || !string.Equals(callback.CollectibleType, collectibleType, StringComparison.Ordinal)
                || callback.TimestampUtc < startedAtUtc
                || callback.Sequence <= sequenceBaseline)
                return false;

            functionHash = callback.FunctionHash;
            return true;
        }

        private static bool FindCollectibleReward(
            IReadOnlyList<RewardEvent> rewards,
            uint netId,
            DateTime startedAtUtc,
            long sequenceBaseline,
            ushort callbackHash,
            bool callbackFound)
        {
            if (rewards == null)
                return false;

            for (int i = 0; i < rewards.Count; i++)
            {
                if (MatchesCollectibleReward(
                    rewards[i],
                    netId,
                    startedAtUtc,
                    sequenceBaseline,
                    callbackHash,
                    callbackFound))
                    return true;
            }

            return false;
        }

        private static bool MatchesCollectibleReward(
            RewardEvent reward,
            uint netId,
            DateTime startedAtUtc,
            long sequenceBaseline,
            ushort callbackHash,
            bool callbackFound)
        {
            return reward != null
                && reward.SourceNetId == netId
                && reward.SourceType == RewardSourceType.Collectible
                && reward.TimestampUtc >= startedAtUtc
                && reward.Sequence > sequenceBaseline
                && (!callbackFound || reward.FunctionHash == callbackHash);
        }

        public static void Tick(Player player)
        {
            if (player == null || !player.isLocalPlayer)
                return;

            int currentMap = player.harita;
            bool mapChanged = _carteScanneeConnue && currentMap != _derniereCarteScannee;
            _derniereCarteScannee = currentMap;
            _carteScanneeConnue = true;

            if (mapChanged)
            {
                _fileScan.Clear();
                _identitesSpawned.Clear();
                _collectibleScanResults.Clear();
                _pnjsEnCours.Clear();
                _suiviPoolIndex = 0;
                _collectibleScanFailed = false;
                _spawnedIdentityScanFailed = false;
                _pnjScanFailed = false;
            }

            if (_fileScan.Count == 0
                && (mapChanged || Time.time - _dernierRemplissage >= IntervalleRemplissage))
            {
                _dernierRemplissage = Time.time;
                RemplirFileDeScan();
            }

            for (int i = 0; i < ScansParTick && _fileScan.Count > 0; i++)
            {
                _fileScan.Dequeue().Invoke();
            }
        }

        private static void RemplirFileDeScan()
        {
            _fileScan.Enqueue(RafraichirJoueurEtNavires);

            _fileScan.Enqueue(DebuterCycleCoffres);
            IReadOnlyList<Type> collectibleTypes = CollectibleCatalog.CollectibleTypes;
            for (int i = 0; i < collectibleTypes.Count; i++)
            {
                Type collectibleType = collectibleTypes[i];
                _fileScan.Enqueue(() => ScannerCollectibleType(collectibleType));
            }
            _fileScan.Enqueue(FinaliserCycleCoffres);

            _fileScan.Enqueue(DebuterCyclePnj);

            _fileScan.Enqueue(() => ScannerPnj<AllMonsters>("monstre", p => p.geminame, p => p.Can, p => p.MaksCan, p => 0f));
            _fileScan.Enqueue(() => ScannerPnj<CalypsoAllMonsters>("monstre", p => p.geminame, p => p.Can, p => p.MaksCan, p => 0f));
            _fileScan.Enqueue(() => ScannerPnj<IceAllMonsters>("monstre", p => p.geminame, p => p.Can, p => p.MaksCan, p => 0f));
            _fileScan.Enqueue(() => ScannerPnj<SampiyonAllMonsters>("monstre", p => p.geminame, p => p.Can, p => p.MaksCan, p => 0f));
            _fileScan.Enqueue(() => ScannerPnj<MonsterAdmiral>("boss", p => p.geminame, p => p.Can, p => p.MaksCan, p => 0f));

            _fileScan.Enqueue(() => ScannerPnj<AllNpcs>("npc_navire", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<BonusMapAllNpcs>("npc_navire", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<BaronAdmiral>("boss", p => p.NpcName, p => p.Health, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<DragonAdmiral>("boss", p => p.NpcName, p => p.Health, p => p.MaxCan, p => p.menzil));

            _fileScan.Enqueue(() => ScannerPnj<EventShip>("npc_navire_event", p => p.NpcName, p => p.Health, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<BaronShip>("npc_navire_event", p => p.NpcName, p => p.Health, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<BaronEtkinlikShip>("npc_navire_event", p => p.NpcName, p => p.Health, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikAnaGemileri>("npc_navire_event", p => p.NpcName, p => p.Health, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikAnaGemileriBonus>("npc_navire_event", p => p.NpcName, p => p.Health, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikAnaGemileriKorsan>("npc_navire_event", p => p.NpcName, p => p.Health, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikAnaGemileriMagellan>("npc_navire_event", p => p.NpcName, p => p.Health, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikAnaGemileriPaskalya>("npc_navire_event", p => p.NpcName, p => p.Health, p => p.MaxCan, p => p.menzil));

            _fileScan.Enqueue(() => ScannerPnj<MiniEventShip>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<RaidProKucuk>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemileri>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemileriBonus>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemileriKorsan>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemileriMagellan>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemiCalypso>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemiHel>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemiIce>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemiIcePearl>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemiKaplumbaga>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemiMagellan>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemiSampiyon>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemiValentin>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));
            _fileScan.Enqueue(() => ScannerPnj<EtkinlikKucukGemiOzgurluk>("npc_navire_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => p.menzil));

            _fileScan.Enqueue(() =>
            {
                ScannerPnj<KuleKontrol>("tour_event", p => p.geminame, p => p.Can, p => p.MaxCan, p => 0f);
                FinaliserCyclePnj();
            });
        }

        private static void RafraichirJoueurEtNavires()
        {
            try
            {
                _identitesSpawned.Clear();
                NaviresProches.Clear();
                JoueurLocal = null;

                if (!NetworkClient.active || !NetworkClient.ready || NetworkClient.spawned == null)
                {
                    _spawnedIdentityScanFailed = true;
                    return;
                }

                foreach (var pair in NetworkClient.spawned)
                {
                    NetworkIdentity identity = pair.Value;
                    if (identity == null || identity.netId == 0)
                        continue;

                    _identitesSpawned.Add(identity);
                    Player player = identity.GetComponent<Player>();
                    if (player == null)
                        continue;

                    if (player.isLocalPlayer)
                        JoueurLocal = player;
                    else
                        NaviresProches.Add(player);
                }

                _spawnedIdentityScanFailed = false;
            }
            catch (Exception e)
            {
                _identitesSpawned.Clear();
                NaviresProches.Clear();
                _spawnedIdentityScanFailed = true;
                Plugin.Logger.LogError($"[EtatJoueur] Erreur lecture NetworkClient.spawned : {e}");
            }
        }

        private static void DebuterCyclePnj()
        {
            _pnjsEnCours.Clear();
            _suiviPoolIndex = 0;
            _pnjScanFailed = false;
        }

        private static void FinaliserCyclePnj()
        {
            if (_pnjScanFailed)
            {
                Plugin.Logger.LogWarning(
                    "[EtatJoueur] Cycle PNJ incomplet; conservation de la génération publiée précédente.");
                return;
            }

            List<PnjSuivi> anciensPnjs = PnjsProches;
            PnjsProches = _pnjsEnCours;
            _pnjsEnCours = anciensPnjs;

            List<PnjSuivi> ancienPoolPublie = _suiviPoolPublie;
            _suiviPoolPublie = _suiviPool;
            _suiviPool = ancienPoolPublie;
        }

        private static PnjSuivi ObtenirOuCreerPnjSuivi()
        {
            if (_suiviPoolIndex < _suiviPool.Count)
                return _suiviPool[_suiviPoolIndex++];

            var nouveau = new PnjSuivi();
            _suiviPool.Add(nouveau);
            _suiviPoolIndex++;
            return nouveau;
        }

        private static void ScannerPnj<T>(string categorie, Func<T, string> nom, Func<T, int> vie, Func<T, int> vieMax, Func<T, float> portee)
            where T : NetworkBehaviour
        {
            try
            {
                if (_spawnedIdentityScanFailed)
                {
                    _pnjScanFailed = true;
                    return;
                }

                if (LecteurCache<T>.Valeur == null)
                    LecteurCache<T>.Valeur = new PnjLecteur<T>(nom, vie, vieMax, portee);
                var lecteur = LecteurCache<T>.Valeur;

                Il2CppSystem.Type componentType = (Il2CppSystem.Type)Il2CppType.Of<T>();
                foreach (NetworkIdentity identity in _identitesSpawned)
                {
                    var componentObject = identity.gameObject.GetComponent(componentType);
                    T typed = componentObject == null ? null : componentObject.TryCast<T>();
                    if (typed == null)
                        continue;

                    var suivi = ObtenirOuCreerPnjSuivi();
                    suivi.Categorie = categorie;
                    suivi.Type = typeof(T).Name;
                    suivi.Instance = typed;
                    suivi.Lecteur = lecteur;
                    _pnjsEnCours.Add(suivi);
                }
            }
            catch (Exception e)
            {
                _pnjScanFailed = true;
                Plugin.Logger.LogError($"[EtatJoueur] Erreur ScannerPnj<{typeof(T).Name}> : {e}");
            }

        }

        private static void DebuterCycleCoffres()
        {
            _collectibleScanResults.Clear();
            _collectibleScanFailed = !CollectibleCatalog.IsInitialized || _spawnedIdentityScanFailed;
        }

        private static void FinaliserCycleCoffres()
        {
            if (_collectibleScanFailed)
            {
                Plugin.Logger.LogWarning(
                    "[EtatJoueur] Cycle collectibles incomplet; conservation de la génération publiée précédente.");
                return;
            }

            CoffresProches.Clear();
            _coffrePoolIndex = 0;
            for (int i = 0; i < _collectibleScanResults.Count; i++)
            {
                CollectibleScanResult result = _collectibleScanResults[i];
                SuiviCoffres.Coffre collectible = ObtenirOuCreerCoffre();
                collectible.Categorie = result.Categorie;
                collectible.Type = result.Type;
                collectible.Instance = result.Instance;
                CoffresProches.Add(collectible);
            }
        }

        private static SuiviCoffres.Coffre ObtenirOuCreerCoffre()
        {
            if (_coffrePoolIndex < _coffrePool.Count)
                return _coffrePool[_coffrePoolIndex++];

            var nouveau = new SuiviCoffres.Coffre();
            _coffrePool.Add(nouveau);
            _coffrePoolIndex++;
            return nouveau;
        }

        private static void ScannerCollectibleType(Type collectibleType)
        {

            try
            {
                if (!CollectibleCatalog.IsInitialized)
                {
                    _collectibleScanFailed = true;
                    return;
                }

                Il2CppSystem.Type il2CppType;
                if (!_collectibleIl2CppTypes.TryGetValue(collectibleType, out il2CppType))
                {
                    il2CppType = (Il2CppSystem.Type)Il2CppTypeOf
                        .MakeGenericMethod(collectibleType)
                        .Invoke(null, null);
                    _collectibleIl2CppTypes.Add(collectibleType, il2CppType);
                }

                foreach (NetworkIdentity identity in _identitesSpawned)
                {
                    var componentObject = identity.gameObject.GetComponent(il2CppType);
                    NetworkBehaviour behaviour = componentObject == null
                        ? null
                        : componentObject.TryCast<NetworkBehaviour>();
                    if (behaviour == null)
                        continue;

                    _collectibleScanResults.Add(new CollectibleScanResult
                    {
                        Categorie = collectibleType == typeof(Chest) ? "coffre" : "scintille",
                        Type = collectibleType.Name,
                        Instance = behaviour
                    });
                }
            }
            catch (Exception e)
            {
                _collectibleScanFailed = true;
                Plugin.Logger.LogError(
                    "[EtatJoueur] Erreur ScannerCollectibleType<"
                    + (collectibleType == null ? "null" : collectibleType.Name) + "> : " + e);
            }

        }

        private static MethodInfo TrouverIl2CppTypeOf()
        {
            foreach (MethodInfo method in typeof(Il2CppType).GetMethods(
                BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name == "Of"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 0)
                    return method;
            }

            throw new MissingMethodException(typeof(Il2CppType).FullName, "Of<T>()");
        }

        public static void PreparerEtEnvoyerInstantane()
        {

            if (JoueurLocal == null) return;

            try
            {
                TamponEtat tampon = _tamponCourant;
                RemplirFicheJoueur(tampon.Joueur);
                RemplirCollectibles(tampon.Collectibles);
                RemplirNavires(tampon.Navires);
                RemplirPnjs(tampon.Pnjs);

                DrainerFileRecompenses();
                DrainerCallbacksCollectibles();

                tampon.Timestamp = DateTime.UtcNow;

                var snapshot = new EtatJeuSnapshot(
                    ClonerFicheJoueur(tampon.Joueur),
                    ClonerCollectibles(tampon.Collectibles),
                    ClonerNavires(tampon.Navires),
                    ClonerPnjs(tampon.Pnjs),
                    new List<RewardEvent>(_historiqueRecompenses),
                    new List<CollectibleCallbackEvent>(_historiqueCallbacksCollectibles),
                    tampon.Timestamp);
                Interlocked.Exchange(ref _snapshotPublie, snapshot);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"[EtatJoueur] Erreur PreparerEtEnvoyerInstantane : {e}");
            }

        }

        public static EtatJeuSnapshot ObtenirSnapshot()
        {
            return _snapshotPublie;
        }

        public static void EnregistrerTirCanonConfirme(Player tireur, uint cibleNetId)
        {
            if (tireur == null || !tireur.isLocalPlayer)
                return;

            unchecked
            {
                _dernierTirCanonSequence++;
            }

            _dernierTirCanonCibleNetId = cibleNetId;
        }

        public static void PurgerRecompenses()
        {
            while (_fileRecompenses.TryDequeue(out var _)) { }
            _historiqueRecompenses.Clear();
            while (_fileCallbacksCollectibles.TryDequeue(out var _)) { }
            _historiqueCallbacksCollectibles.Clear();
        }

        private static void DrainerFileRecompenses()
        {
            while (_fileRecompenses.TryDequeue(out var evenement))
                _historiqueRecompenses.Add(evenement);

            if (_historiqueRecompenses.Count == 0)
                return;

            DateTime seuil = DateTime.UtcNow - DureeRetentionRecompenses;
            _historiqueRecompenses.RemoveAll(e => e.TimestampUtc < seuil);

            int exces = _historiqueRecompenses.Count - TailleMaxHistoriqueRecompenses;
            if (exces > 0)
                _historiqueRecompenses.RemoveRange(0, exces);

        }

        private static void DrainerCallbacksCollectibles()
        {
            while (_fileCallbacksCollectibles.TryDequeue(out var evenement))
                _historiqueCallbacksCollectibles.Add(evenement);

            if (_historiqueCallbacksCollectibles.Count == 0)
                return;

            DateTime seuil = DateTime.UtcNow - DureeRetentionRecompenses;
            _historiqueCallbacksCollectibles.RemoveAll(e => e.TimestampUtc < seuil);

            int exces = _historiqueCallbacksCollectibles.Count - TailleMaxHistoriqueRecompenses;
            if (exces > 0)
                _historiqueCallbacksCollectibles.RemoveRange(0, exces);
        }

        public static bool ObtenirLimitesCarte(int harita, out float minX, out float maxX, out float minY, out float maxY)
        {
            return PositionReelle.ObtenirLimites(harita, out minX, out maxX, out minY, out maxY);
        }

        private static FicheJoueur ClonerFicheJoueur(FicheJoueur source)
        {

            return new FicheJoueur
            {
                IdGlobal = source.IdGlobal,
                Nom = source.Nom,
                GuildTag = source.GuildTag,
                Vie = source.Vie,
                VieMax = source.VieMax,
                PourcentageVie = source.PourcentageVie,
                Harita = source.Harita,
                NomHarita = source.NomHarita,
                CoordonneeSayi = source.CoordonneeSayi,
                CoordonneeHarf = source.CoordonneeHarf,
                X = source.X,
                Y = source.Y,
                EnModeAttaque = source.EnModeAttaque,
                DernierTirHarpon = source.DernierTirHarpon,
                DernierTirCanonSequence = source.DernierTirCanonSequence,
                DernierTirCanonCibleNetId = source.DernierTirCanonCibleNetId,
                CibleAttaqueNetId = source.CibleAttaqueNetId,
                PanneauMortVisible = source.PanneauMortVisible,
                BoutonReapparitionDisponible = source.BoutonReapparitionDisponible,
                Reparation = source.Reparation,
                Portee = source.Portee,
                PorteeHarpon = source.PorteeHarpon,
                Or = source.Or,
                Perle = source.Perle,
                Exp = source.Exp,
                ClesCoffre = source.ClesCoffre,
                ClesCoffreIcePearl = source.ClesCoffreIcePearl,
                TargetX = source.TargetX,
                TargetY = source.TargetY,
                Vitesse = source.Vitesse,
            };

        }

        private static List<CollectibleInfo> ClonerCollectibles(List<CollectibleInfo> source)
        {

            var liste = new List<CollectibleInfo>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                var c = source[i];
                liste.Add(new CollectibleInfo
                {
                    Id = c.Id,
                    Categorie = c.Categorie,
                    Type = c.Type,
                    X = c.X,
                    Y = c.Y,
                    Z = c.Z,
                    Distance = c.Distance,
                });
            }
            return liste;

        }

        private static List<NavireInfo> ClonerNavires(List<NavireInfo> source)
        {

            var liste = new List<NavireInfo>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                var n = source[i];
                liste.Add(new NavireInfo
                {
                    IdGlobal = n.IdGlobal,
                    Nom = n.Nom,
                    GuildTag = n.GuildTag,
                    Portee = n.Portee,
                    X = n.X,
                    Y = n.Y,
                    Distance = n.Distance,
                });
            }
            return liste;

        }

        private static List<PnjInfo> ClonerPnjs(List<PnjInfo> source)
        {

            var liste = new List<PnjInfo>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                var p = source[i];
                liste.Add(new PnjInfo
                {
                    Id = p.Id,
                    Categorie = p.Categorie,
                    Type = p.Type,
                    Nom = p.Nom,
                    Vie = p.Vie,
                    VieMax = p.VieMax,
                    Portee = p.Portee,
                    Harita = p.Harita,
                    NomHarita = p.NomHarita,
                    CoordonneeSayi = p.CoordonneeSayi,
                    CoordonneeHarf = p.CoordonneeHarf,
                    X = p.X,
                    Y = p.Y,
                    Distance = p.Distance,
                });
            }
            return liste;

        }

        private delegate IntPtr DelegueHaritaAdiniGetir(IntPtr self, int harita, IntPtr methodInfo);

        private static DelegueHaritaAdiniGetir _haritaAdiniGetir;
        private static IntPtr _haritaAdiniGetirMethodInfo;
        private static bool _haritaAdiniGetirTente;

        private static readonly Dictionary<int, string> _nomsHarita = new Dictionary<int, string>();

        private static string ObtenirNomHarita(int harita)
        {
            if (_nomsHarita.TryGetValue(harita, out string nomMemorise)) return nomMemorise;

            string nom = AppelerHaritaAdiniGetirNatif(harita) ?? CalculerNomHaritaOrdinaire(harita);
            _nomsHarita[harita] = nom;
            return nom;
        }

        private delegate IntPtr DelegueKoordinatGetir(IntPtr self, int harita, float x, float y, IntPtr methodInfo);

        private static DelegueKoordinatGetir _koordinatGetir;
        private static IntPtr _koordinatGetirMethodInfo;
        private static bool _koordinatGetirTente;

        private static string ObtenirPositionNative(int harita, float x, float y)
        {
            try
            {
                if (!_koordinatGetirTente)
                {
                    _koordinatGetirTente = true;
                    IntPtr methodInfo = IL2CPP.GetIl2CppMethod(
                        Il2CppClassPointerStore<GrupUyesiUI>.NativeClassPtr,
                        false, "KoordinatGetir", "System.String", "System.Int32", "System.Single", "System.Single");
                    if (methodInfo != IntPtr.Zero)
                    {
                        IntPtr fonction = System.Runtime.InteropServices.Marshal.ReadIntPtr(methodInfo);
                        if (fonction != IntPtr.Zero)
                        {
                            _koordinatGetirMethodInfo = methodInfo;
                            _koordinatGetir = System.Runtime.InteropServices.Marshal
                                .GetDelegateForFunctionPointer<DelegueKoordinatGetir>(fonction);
                        }
                    }
                    if (_koordinatGetir == null)
                        Plugin.Logger.LogWarning("[EtatJoueur] GrupUyesiUI.KoordinatGetir introuvable : positions affichées des entités indisponibles.");
                }

                if (_koordinatGetir == null) return null;

                IntPtr resultat = _koordinatGetir(IntPtr.Zero, harita, x, y, _koordinatGetirMethodInfo);
                return resultat == IntPtr.Zero ? null : IL2CPP.Il2CppStringToManaged(resultat);
            }
            catch (Exception e)
            {
                _koordinatGetir = null;
                Plugin.Logger.LogError($"[EtatJoueur] Erreur appel natif KoordinatGetir : {e}");
                return null;
            }
        }

        private static void SeparerPositionNative(string natif, out string sayi, out string harf)
        {
            sayi = null;
            harf = null;
            if (string.IsNullOrEmpty(natif)) return;

            int separateur = natif.IndexOf('/');
            if (separateur < 0)
            {
                sayi = natif;
                return;
            }

            sayi = natif.Substring(0, separateur);
            harf = natif.Substring(separateur + 1);
        }

        private static string AppelerHaritaAdiniGetirNatif(int harita)
        {
            try
            {
                if (!_haritaAdiniGetirTente)
                {
                    _haritaAdiniGetirTente = true;
                    IntPtr methodInfo = IL2CPP.GetIl2CppMethod(
                        Il2CppClassPointerStore<GrupUyesiUI>.NativeClassPtr,
                        false, "HaritaAdiniGetir", "System.String", "System.Int32");
                    if (methodInfo != IntPtr.Zero)
                    {

                        IntPtr fonction = System.Runtime.InteropServices.Marshal.ReadIntPtr(methodInfo);
                        if (fonction != IntPtr.Zero)
                        {
                            _haritaAdiniGetirMethodInfo = methodInfo;
                            _haritaAdiniGetir = System.Runtime.InteropServices.Marshal
                                .GetDelegateForFunctionPointer<DelegueHaritaAdiniGetir>(fonction);
                        }
                    }
                    if (_haritaAdiniGetir == null)
                        Plugin.Logger.LogWarning("[EtatJoueur] GrupUyesiUI.HaritaAdiniGetir introuvable : repli sur la formule ordinaire.");
                }

                if (_haritaAdiniGetir == null) return null;

                IntPtr resultat = _haritaAdiniGetir(IntPtr.Zero, harita, _haritaAdiniGetirMethodInfo);
                return resultat == IntPtr.Zero ? null : IL2CPP.Il2CppStringToManaged(resultat);
            }
            catch (Exception e)
            {
                _haritaAdiniGetir = null;
                Plugin.Logger.LogError($"[EtatJoueur] Erreur appel natif HaritaAdiniGetir : {e}");
                return null;
            }
        }

        private static string CalculerNomHaritaOrdinaire(int harita)
        {
            if (harita <= 0 || (harita >= 40 && harita <= 46)) return string.Empty;
            return (harita % 2 != 0)
                ? ((harita + 1) / 2).ToString(CultureInfo.InvariantCulture) + "/1"
                : (harita / 2).ToString(CultureInfo.InvariantCulture) + "/2";
        }

        private static Kordinat _kordinat;
        private static float _prochaineRechercheKordinat;

        private static void LireCoordonneeAffichee(out string sayi, out string harf)
        {
            sayi = string.Empty;
            harf = string.Empty;
            try
            {
                if (_kordinat == null)
                {
                    if (Time.realtimeSinceStartup < _prochaineRechercheKordinat) return;
                    _prochaineRechercheKordinat = Time.realtimeSinceStartup + 2f;

                    Camera cam = Camera.main;
                    _kordinat = cam != null ? cam.GetComponent<Kordinat>() : null;
                    if (_kordinat == null)
                        _kordinat = UnityEngine.Object.FindObjectOfType<Kordinat>();
                    if (_kordinat == null) return;
                }

                var champSayi = _kordinat.kordinatSayi;
                if (champSayi != null) sayi = champSayi.text ?? string.Empty;

                var champHarf = _kordinat.kordinatHarf;
                if (champHarf != null) harf = champHarf.text ?? string.Empty;
            }
            catch
            {
                sayi = string.Empty;
                harf = string.Empty;
                _kordinat = null;
            }
        }

        private static bool ObtenirEtatPanneauMort()
        {
            try
            {
                var gm = GameManager.gm;
                if (gm == null) return false;

                GameObject panneau = gm.oyuncuOlduPanel;
                return panneau != null && panneau.activeSelf;
            }
            catch
            {
                return false;
            }
        }

        private static bool ObtenirEtatBoutonReapparition()
        {
            try
            {
                var gm = GameManager.gm;
                if (gm == null) return false;

                GameObject bouton = gm.YenidenisinlanBTN;
                return bouton != null && bouton.activeSelf;
            }
            catch
            {
                return false;
            }
        }

        private static uint ObtenirNetIdCibleAttaque()
        {
            try
            {
                var gm = GameManager.gm;
                if (gm == null) return 0;

                GameObject cible = gm.hedefgemi;
                if (cible == null) return 0;

                var identity = cible.GetComponent<NetworkIdentity>();
                if (identity == null) return 0;

                return identity.netId;
            }
            catch
            {
                return 0;
            }
        }

        private static void RemplirFicheJoueur(FicheJoueur f)
        {
            var j = JoueurLocal;
            Vector3 pos = j.transform.position;

            f.IdGlobal = j.oyuncuId;
            f.Nom = j.oyuncuadi;
            f.GuildTag = j.OyuncuFiloKisaltma;
            f.Vie = j.Can;
            f.VieMax = j.MaksCan;
            f.PourcentageVie = f.VieMax > 0
                ? (float)f.Vie / f.VieMax * 100f
                : 0f;
            f.Harita = j.harita;
            f.X = pos.x;
            f.Y = pos.y;
            f.EnModeAttaque = j.saldiridurumu;
            f.DernierTirHarpon = j.sonsaldirilanzaman;
            f.DernierTirCanonSequence = _dernierTirCanonSequence;
            f.DernierTirCanonCibleNetId = _dernierTirCanonCibleNetId;
            f.Reparation = j.oyuncuTamirDurumu;
            f.Portee = j.menzil;
            f.PorteeHarpon = j.zipkinMenzil;
            f.Or = j.oyuncuAltin;
            f.Perle = j.playerPearl;
            f.Exp = j.oyuncuTecrubePuan;
            f.ClesCoffre = j.oyuncuSandikAnahtari;
            f.ClesCoffreIcePearl = j.oyuncuIcePearlSandikAnahtari;
            f.TargetX = j.target.x;
            f.TargetY = j.target.y;
            f.Vitesse = j.hiz;

            f.CibleAttaqueNetId = f.EnModeAttaque
                                ? ObtenirNetIdCibleAttaque()
                                : 0;
            f.PanneauMortVisible = ObtenirEtatPanneauMort();
            f.BoutonReapparitionDisponible = ObtenirEtatBoutonReapparition();

            f.NomHarita = ObtenirNomHarita(f.Harita);
            string sayi;
            string harf;
            LireCoordonneeAffichee(out sayi, out harf);
            f.CoordonneeSayi = sayi;
            f.CoordonneeHarf = harf;
        }

        private static void RemplirCollectibles(List<CollectibleInfo> destination)
        {
            if (JoueurLocal == null)
            {
                destination.Clear();
                return;
            }
            Vector3 posJoueur = JoueurLocal.transform.position;

            CoffresProches.RemoveAll(c => c.Instance == null);

            int index = 0;

            foreach (var c in CoffresProches)
            {
                Vector3 cp;
                uint id;
                try
                {
                    cp = c.Instance.transform.position;
                    id = c.Instance.netId;
                }
                catch { continue; }

                CollectibleInfo info = index < destination.Count ? destination[index] : NouvelElement(destination);
                info.Id = id;
                info.Categorie = c.Categorie;
                info.Type = c.Type;
                info.X = cp.x;
                info.Y = cp.y;
                info.Z = cp.z;
                info.Distance = Vector3.Distance(posJoueur, cp);
                index++;
            }

            if (destination.Count > index)
                destination.RemoveRange(index, destination.Count - index);
        }

        private static void RemplirNavires(List<NavireInfo> destination)
        {
            if (JoueurLocal == null)
            {
                destination.Clear();
                return;
            }
            Vector3 posJoueur = JoueurLocal.transform.position;

            NaviresProches.RemoveAll(n => n == null);

            int index = 0;

            foreach (var navire in NaviresProches)
            {
                Vector3 np;
                int id;
                try
                {
                    np = navire.transform.position;
                    id = navire.oyuncuId;
                }
                catch { continue; }

                string nom = navire.oyuncuadi;
                string guildTag = navire.OyuncuFiloKisaltma;
                float portee = navire.menzil;

                NavireInfo info = index < destination.Count ? destination[index] : NouvelElement(destination);
                info.IdGlobal = id;
                info.Nom = nom;
                info.GuildTag = guildTag;
                info.Portee = portee;
                info.X = np.x;
                info.Y = np.y;
                info.Distance = Vector3.Distance(posJoueur, np);
                index++;
            }

            if (destination.Count > index)
                destination.RemoveRange(index, destination.Count - index);
        }

        private static void RemplirPnjs(List<PnjInfo> destination)
        {
            if (JoueurLocal == null)
            {
                destination.Clear();
                return;
            }
            Vector3 posJoueur = JoueurLocal.transform.position;

            PnjsProches.RemoveAll(p => p.Instance == null);

            _idsPnjVus.Clear();
            int index = 0;

            foreach (var p in PnjsProches)
            {
                Vector3 pp;
                uint id;
                string nom;
                int vie, vieMax;
                float portee;
                try
                {
                    pp = p.Instance.transform.position;
                    id = p.Instance.netId;
                    nom = p.Lecteur.Nom(p.Instance);
                    vie = p.Lecteur.Vie(p.Instance);
                    vieMax = p.Lecteur.VieMax(p.Instance);
                    portee = p.Lecteur.Portee(p.Instance);
                }
                catch { continue; }

                _idsPnjVus.Add(id);

                bool connu = _cachePnj.TryGetValue(id, out var cache);

                int harita = cache.Harita;
                string sayi = cache.Sayi, harf = cache.Harf;
                float refX = cache.RefX, refY = cache.RefY;
                if (!connu
                    || Math.Abs(pp.x - cache.RefX) > SeuilDeplacement
                    || Math.Abs(pp.y - cache.RefY) > SeuilDeplacement)
                {
                    harita = PositionReelle.TrouverHarita(pp.x, pp.y);
                    SeparerPositionNative(harita > 0 ? ObtenirPositionNative(harita, pp.x, pp.y) : null, out sayi, out harf);
                    refX = pp.x;
                    refY = pp.y;
                }

                _cachePnj[id] = new CacheEntree
                {
                    Harita = harita,
                    Sayi = sayi,
                    Harf = harf,
                    RefX = refX,
                    RefY = refY
                };

                PnjInfo info = index < destination.Count ? destination[index] : NouvelElement(destination);
                info.Id = id;
                info.Categorie = p.Categorie;
                info.Type = p.Type;
                info.Nom = nom;
                info.Vie = vie;
                info.VieMax = vieMax;
                info.Portee = portee;
                info.Harita = harita;
                info.NomHarita = harita > 0 ? ObtenirNomHarita(harita) : null;
                info.CoordonneeSayi = sayi;
                info.CoordonneeHarf = harf;
                info.X = pp.x;
                info.Y = pp.y;
                info.Distance = Vector3.Distance(posJoueur, pp);
                index++;
            }

            if (destination.Count > index)
                destination.RemoveRange(index, destination.Count - index);

            if (_cachePnj.Count > _idsPnjVus.Count)
            {
                _aSupprimerPnj.Clear();
                foreach (var idConnu in _cachePnj.Keys)
                    if (!_idsPnjVus.Contains(idConnu)) _aSupprimerPnj.Add(idConnu);
                foreach (var idASupprimer in _aSupprimerPnj)
                    _cachePnj.Remove(idASupprimer);
            }
        }

        private static T NouvelElement<T>(List<T> liste) where T : new()
        {
            var element = new T();
            liste.Add(element);
            return element;
        }

    }

    public static class SuiviCoffres
    {
        public class Coffre
        {
            public string Categorie;
            public string Type;
            public NetworkBehaviour Instance;
        }
    }

    public static class PositionReelle
    {
        private const float MultiplicateurColonnes = 60.0f;

        private static Il2CppStructArray<float> _minX, _maxX, _minY, _maxY;
        private static Il2CppStringArray _lettres;
        private static bool _chargementTente;
        private static bool _chargementReussi;

        private static bool ChargerTableauxDuJeu()
        {
            if (_chargementReussi) return true;
            if (_chargementTente) return false;

            try
            {
                _minX = GrupUyesiUI.haritaMinX;
                _maxX = GrupUyesiUI.haritaMaxX;
                _minY = GrupUyesiUI.haritaMinY;
                _maxY = GrupUyesiUI.haritaMaxY;
                _lettres = GrupUyesiUI.dikeyKoordinatHarfleri;

                if (_minX == null || _maxX == null || _minY == null || _maxY == null)
                {
                    Plugin.Logger.LogError("[EtatJoueur] PositionReelle : un des tableaux GrupUyesiUI renvoie null.");
                    _chargementTente = true;
                    return false;
                }

                Plugin.Logger.LogInfo(
                    $"[EtatJoueur] PositionReelle : {_minX.Length} cartes chargées.");

                _chargementReussi = true;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"[EtatJoueur] Erreur PositionReelle.ChargerTableauxDuJeu : {e}");
                _chargementTente = true;
                return false;
            }
        }

        public static string Convertir(int harita, float x, float y)
        {
            if (!ChargerTableauxDuJeu())
                return null;

            int index = harita - 1;
            if (index < 0
                || index >= _minX.Length
                || index >= _maxX.Length
                || index >= _minY.Length
                || index >= _maxY.Length
                || _lettres == null
                || _lettres.Length == 0)
                return null;

            float minX = _minX[index];
            float maxX = _maxX[index];
            float minY = _minY[index];
            float maxY = _maxY[index];
            if (maxX <= minX || maxY <= minY)
                return null;

            float ratioX = Mathf.Clamp01((x - minX) / (maxX - minX));
            float ratioY = Mathf.Clamp01((y - minY) / (maxY - minY));
            int colonne = Mathf.Clamp(
                Mathf.RoundToInt(ratioX * MultiplicateurColonnes),
                0,
                (int)MultiplicateurColonnes);
            int ligne = Mathf.Clamp(
                Mathf.RoundToInt(ratioY * (_lettres.Length - 1)),
                0,
                _lettres.Length - 1);
            return colonne + " " + _lettres[ligne];
        }

        public static bool ObtenirLimites(int harita, out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = maxX = minY = maxY = 0f;

            if (!ChargerTableauxDuJeu()) return false;

            int index = harita - 1;
            if (index < 0
                || index >= _minX.Length
                || index >= _maxX.Length
                || index >= _minY.Length
                || index >= _maxY.Length)
                return false;

            minX = _minX[index];
            maxX = _maxX[index];
            minY = _minY[index];
            maxY = _maxY[index];
            return true;
        }

        /*
         * Grille utilisée par Convertir et par Player.HedefeGit : colonnes 0 à
         * derniereColonne, une ligne par lettre (0 à derniereLigne). Chaque case
         * correspond au point minX + colonne x pasX, minY + ligne x pasY.
         */
        public static bool ObtenirGrille(
            int harita,
            out float minX, out float maxX, out float minY, out float maxY,
            out int derniereColonne, out int derniereLigne)
        {
            derniereColonne = (int)MultiplicateurColonnes;
            derniereLigne = 0;
            if (!ObtenirLimites(harita, out minX, out maxX, out minY, out maxY))
                return false;
            if (_lettres == null || _lettres.Length < 2 || maxX <= minX || maxY <= minY)
                return false;

            derniereLigne = _lettres.Length - 1;
            return true;
        }

        // Lettre de la ligne donnée de la grille (null si hors limites).
        public static string LettreLigne(int ligne)
        {
            if (!ChargerTableauxDuJeu() || _lettres == null || ligne < 0 || ligne >= _lettres.Length)
                return null;
            return _lettres[ligne];
        }

        public static int TrouverHarita(float x, float y)
        {
            if (!ChargerTableauxDuJeu()) return 0;

            int n = Math.Min(Math.Min(_minX.Length, _maxX.Length), Math.Min(_minY.Length, _maxY.Length));
            for (int i = 0; i < n; i++)
            {
                if (x >= _minX[i] && x <= _maxX[i] && y >= _minY[i] && y <= _maxY[i])
                    return i + 1;
            }

            return 0;
        }
    }
}
