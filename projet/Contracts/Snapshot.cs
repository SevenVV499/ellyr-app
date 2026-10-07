using System;
using System.Collections.Generic;

/*
 * Contrats : les fiches de l'instantané du jeu, partagées entre l'observation (client) et la décision.
 * Ce fichier ne contient que des données : aucune règle, aucun appel au jeu.
 */
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
        public int Niveau;
        public int TalismanAcemi;
        public int Talisman;
        public int RaidHasar;

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
}
