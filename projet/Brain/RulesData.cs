using System;
using EtatJoueurMod;

/*
 * Données de règles du cerveau : tous les niveaux, numéros de carte, noms de cibles, plafonds et
 * délais que les règles utilisent, regroupés au même endroit. Les règles (RaidRules, SurvivalRules,
 * RespawnRules, BluePencil) ne contiennent plus aucune valeur de ce type : pour changer une règle de
 * jeu, on ne touche qu'à ce fichier. C'est aussi lui qui pourra être alimenté par le serveur.
 */
public static class RulesData
{
    // ------------------------------------------------------------------ Commun

    // Un instantané plus vieux que ça n'est plus une information fiable (il est publié toutes les 0,5 s).
    public static readonly float MaxSnapshotAgeSeconds = 3f;

    public static bool IsSnapshotStale(EtatJeuSnapshot snapshot)
    {
        if (snapshot == null || snapshot.Timestamp == default(DateTime))
            return true;

        return (DateTime.UtcNow - snapshot.Timestamp).TotalSeconds > MaxSnapshotAgeSeconds;
    }

    // ------------------------------------------------------------------ Planificateur

    public static readonly int NavigationPriority = 10;
    public static readonly int EngagedActionPriority = 200;

    // ------------------------------------------------------------------ Raid

    public static class Raid
    {
        // Petite Raid : niveaux 1 à 10, talisman du soleil.
        public static readonly string PetiteLabel = "Petite Raid";
        public static readonly int PetiteMinLevel = 1;
        public static readonly int PetiteMaxLevel = 10;
        public static readonly int PetiteMapId = 41;
        public static readonly string PetiteMobName = "sunburst";
        public static readonly string PetiteBossName = "ameterasu";

        // Grande Raid : niveaux 11 à 15, talisman de Behemoth.
        public static readonly string GrandeLabel = "Grande Raid";
        public static readonly int GrandeMinLevel = 11;
        public static readonly int GrandeMaxLevel = 15;
        public static readonly int GrandeMapId = 42;
        public static readonly string GrandeMobName = "leviathan";
        public static readonly string GrandeBossName = "behemoth";

        // Immobilité avant l'envoi de l'ordre d'entrée (laisse retomber un déplacement en cours).
        public static readonly float SettleSeconds = 1f;

        // Décompte du jeu ≈ 10 s : au-delà, l'entrée est considérée comme avortée.
        public static readonly float CountdownTimeoutSeconds = 25f;

        // Plafond journalier de dégâts aux boss de Raid (compteur commun aux deux Raids) : au-delà,
        // plus aucun gain, donc plus aucun intérêt à attaquer Ameterasu ou Behemoth.
        public static readonly int BossDamageCap = 200000000;

        public static readonly int MaxEntryAttempts = 3;
        public static readonly float CooldownSeconds = 120f;
        public static readonly float FleeCooldownSeconds = 10f;
    }

    // ------------------------------------------------------------------ Survie

    public static class Survival
    {
        // Délai minimal entre deux Commands Repair() : une réparation interrompue
        // (tir reçu...) est retentée tant que la condition de PV reste vraie.
        public static readonly float RepairRetrySeconds = 2f;

        // Garde-fou : une fuite qui ne se termine pas (réparation désactivée,
        // PV qui ne remontent pas) est abandonnée après ce délai.
        public static readonly float FleeMaxSeconds = 180f;
    }

    // ------------------------------------------------------------------ Réapparition

    public static class Respawn
    {
        public static readonly float AttemptTimeoutSeconds = 15f;
        public static readonly float CheckIntervalSeconds = 0.25f;

        // Nombre maximal d'envois de YenidenDog(1) pour un même décès.
        public static readonly int MaxCommandAttempts = 3;

        // Nombre d'instantanés distincts conformes exigés avant de sortir de Respawn.
        public static readonly int ConfirmationSnapshotsRequired = 2;
    }
}
