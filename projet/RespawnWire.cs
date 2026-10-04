using System;
using EtatJoueurMod;
using UnityEngine;

public static class RespawnWire
{
    private const float RespawnAttemptTimeoutSeconds = 15f;
    private const float RespawnCheckIntervalSeconds = 0.25f;

    // Nombre maximal d'envois de YenidenDog(1) pour un même décès.
    private const int MaxRespawnCommandAttempts = 3;

    // Un snapshot plus vieux que ça n'est plus une information fiable
    // (le snapshot est publié toutes les 0,5 s).
    private const float MaxSnapshotAgeSeconds = 3f;

    // Nombre de snapshots distincts conformes exigés avant de sortir de Respawn.
    private const int ConfirmationSnapshotsRequired = 2;

    private static BehaviorBrain _brain;

    private static bool _active;
    private static bool _commandSent;
    private static bool _abandoned;
    private static bool _gameManagerWarned;
    private static bool _brainWarned;

    private static int _attempts;
    private static int _confirmCount;

    private static float _startedAt;
    private static float _nextCheckAt;

    private static DateTime _lastConfirmSnapshotUtc;
    private static DateTime _confirmedSnapshotUtc;

    public static bool IsActive
    {
        get { return _active; }
    }

    /*
     * Vrai lorsque le nombre maximal de tentatives a été atteint sans
     * confirmation : plus aucune commande n'est envoyée, mais le système reste
     * en Respawn et continue d'observer (un retour à la vie le fait sortir).
     */
    public static bool IsAbandoned
    {
        get { return _abandoned; }
    }

    /*
     * Timestamp du snapshot qui a confirmé le dernier respawn.
     * Le comportement normal ne doit repartir que sur un snapshot plus récent.
     */
    public static DateTime DernierSnapshotConfirme
    {
        get { return _confirmedSnapshotUtc; }
    }

    public static void Configurer(BehaviorBrain brain)
    {
        _brain = brain;
        _brainWarned = false;

        if (_brain == null)
            Plugin.Logger.LogError(
                "[RespawnWire] Configurer appelé sans BehaviorBrain : " +
                "le SystemState Respawn ne sera pas synchronisé.");
    }

    public static void Reset()
    {
        ClearRunState();
        _confirmedSnapshotUtc = default(DateTime);

        /*
         * Un reset garantit RespawnWire inactif ET Brain en Normal,
         * même si _active était déjà faux alors que le Brain est resté
         * accidentellement en Respawn.
         */
        if (_brain != null && _brain.IsRespawning)
            _brain.ExitRespawn();
    }

    public static void Tick(Player player, EtatJeuSnapshot snapshot)
    {
        if (player == null || snapshot == null || snapshot.Joueur == null)
            return;

        if (_brain == null && !_brainWarned)
        {
            _brainWarned = true;
            Plugin.Logger.LogError(
                "[RespawnWire] Aucun BehaviorBrain configuré : " +
                "le respawn fonctionne sans synchroniser le SystemState.");
        }

        /*
         * Un snapshot trop ancien (JoueurLocal indisponible, changement de
         * scène...) ne prouve rien : il ne peut ni déclencher un respawn,
         * ni le confirmer, ni provoquer l'envoi d'une commande.
         */
        if (IsSnapshotStale(snapshot))
            return;

        /*
         * Détection du décès.
         *
         * On utilise Vie <= 0 comme déclencheur d'entrée
         * dans le SystemState Respawn.
         */
        if (!_active)
        {
            /*
             * Filet de sécurité : le Brain ne doit jamais rester en Respawn
             * alors que RespawnWire est inactif.
             */
            if (_brain != null && _brain.IsRespawning)
            {
                Plugin.Logger.LogWarning(
                    "[RespawnWire] Brain en Respawn alors que RespawnWire est inactif : sortie forcée.");
                _brain.ExitRespawn();
            }

            if (snapshot.Joueur.Vie > 0 || snapshot.Joueur.VieMax <= 0)
                return;

            StartRespawn(player);
            return;
        }

        /*
         * Tant que le système est en Respawn,
         * aucune logique normale ne doit reprendre.
         */
        if (_brain != null && !_brain.IsRespawning)
            _brain.EnterRespawn();

        /*
         * Vérification périodique pour éviter de travailler
         * inutilement à chaque appel du Hook.
         */
        if (Time.time < _nextCheckAt)
            return;

        _nextCheckAt = Time.time + RespawnCheckIntervalSeconds;

        /*
         * Confirmation testée AVANT toute logique de commande :
         * si le joueur est revenu (commande tardive, relevé autrement...),
         * on sort proprement, quel que soit l'état de _commandSent.
         *
         * Deux snapshots réellement distincts (Timestamp différent)
         * doivent être conformes ; un même snapshot relu ne compte qu'une fois.
         */
        if (IsRespawnComplete(snapshot))
        {
            if (snapshot.Timestamp > _lastConfirmSnapshotUtc)
            {
                _lastConfirmSnapshotUtc = snapshot.Timestamp;
                _confirmCount++;
            }

            if (_confirmCount >= ConfirmationSnapshotsRequired)
                CompleteRespawn(snapshot);

            return;
        }

        _confirmCount = 0;
        _lastConfirmSnapshotUtc = default(DateTime);

        /*
         * Tentatives épuisées : plus aucune commande, on continue
         * seulement d'observer (confirmation ci-dessus).
         */
        if (_abandoned)
            return;

        /*
         * Le jeu doit d'abord rendre le bouton disponible.
         *
         * On NE force jamais activeSelf = true.
         */
        if (!_commandSent)
        {
            if (!snapshot.Joueur.BoutonReapparitionDisponible)
            {
                if (Time.time - _startedAt >= RespawnAttemptTimeoutSeconds)
                {
                    Plugin.Logger.LogWarning(
                        "[RespawnWire] Timeout avant disponibilité du bouton de respawn.");

                    RestartRespawnAttempt(player);
                }

                return;
            }

            SendRespawnCommand(player);
            return;
        }

        /*
         * La commande a été envoyée.
         *
         * Si le retour n'est jamais confirmé, on considère
         * la tentative comme échouée après timeout.
         */
        if (Time.time - _startedAt >= RespawnAttemptTimeoutSeconds)
        {
            if (_attempts >= MaxRespawnCommandAttempts)
            {
                Abandon();
                return;
            }

            Plugin.Logger.LogWarning(
                "[RespawnWire] Timeout après YenidenDog(1) (tentative " +
                _attempts + "/" + MaxRespawnCommandAttempts + "). Nouvelle tentative.");

            RestartRespawnAttempt(player);
        }
    }

    private static bool IsSnapshotStale(EtatJeuSnapshot snapshot)
    {
        if (snapshot.Timestamp == default(DateTime))
            return true;

        return (DateTime.UtcNow - snapshot.Timestamp).TotalSeconds > MaxSnapshotAgeSeconds;
    }

    private static void StartRespawn(Player player)
    {
        ClearRunState();
        _active = true;
        _startedAt = Time.time;

        /*
         * Passage explicite du BehaviorBrain dans le SystemState Respawn.
         *
         * Cela annule l'action Navigation / Collect / Combat
         * actuellement en cours.
         */
        if (_brain != null)
            _brain.EnterRespawn();

        StopNormalBehavior(player);

    }

    private static void SendRespawnCommand(Player player)
    {
        if (player == null)
            return;

        if (_commandSent)
            return;

        try
        {
            GameManager manager = GameManager.gm;

            if (manager == null)
            {
                if (!_gameManagerWarned)
                {
                    _gameManagerWarned = true;
                    Plugin.Logger.LogWarning(
                        "[RespawnWire] GameManager.gm indisponible.");
                }
                return;
            }

            /*
             * La tentative est comptée AVANT l'appel : si YenidenDog lève
             * une exception, on ne renvoie pas la commande à chaque check,
             * c'est le timeout qui décidera d'une éventuelle nouvelle tentative.
             */
            _attempts++;
            _commandSent = true;
            _startedAt = Time.time;

            /*
             * Chemin natif utilisé par le bouton :
             *
             * GameManager.YenidenDog(1)
             * -> Player.SunucuOyuncuyuYenidenDogur(1)
             */
            manager.YenidenDog(1);

        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(
                "[RespawnWire] Erreur YenidenDog(1) (tentative " +
                _attempts + "/" + MaxRespawnCommandAttempts + ") : " + e);
        }
    }

    private static bool IsRespawnComplete(EtatJeuSnapshot snapshot)
    {
        if (snapshot == null || snapshot.Joueur == null)
            return false;

        /*
         * Vie > 0
         * ET panneau de mort fermé
         * ET bouton de respawn désactivé.
         *
         * Les trois conditions sont nécessaires.
         */
        return snapshot.Joueur.Vie > 0
            && !snapshot.Joueur.PanneauMortVisible
            && !snapshot.Joueur.BoutonReapparitionDisponible;
    }

    private static void CompleteRespawn(EtatJeuSnapshot confirmingSnapshot)
    {
        ClearRunState();

        /*
         * Le comportement normal ne repartira que sur un snapshot
         * strictement plus récent que celui-ci.
         */
        _confirmedSnapshotUtc = confirmingSnapshot.Timestamp;

        /*
         * Les récompenses d'avant la mort ne doivent pas être attribuées
         * au nouveau cycle de comportement.
         */
        try
        {
            GameState.PurgerRecompenses();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(
                "[RespawnWire] Erreur purge des récompenses : " + e);
        }

        /*
         * On quitte uniquement le SystemState Respawn.
         *
         * On ne choisit aucune action ici.
         * Le prochain cycle normal repartira avec un nouveau
         * snapshot et BluePencil décidera quoi faire.
         */
        if (_brain != null)
            _brain.ExitRespawn();

    }

    private static void Abandon()
    {
        _abandoned = true;

        Plugin.Logger.LogError(
            "[RespawnWire] " + MaxRespawnCommandAttempts +
            " tentatives de YenidenDog(1) sans confirmation : abandon des envois. " +
            "Le système reste en Respawn et observe l'état du joueur.");
    }

    private static void RestartRespawnAttempt(Player player)
    {
        /*
         * Une nouvelle tentative ne réactive pas artificiellement
         * le bouton et ne considère pas l'échec comme un succès.
         */
        _commandSent = false;
        _gameManagerWarned = false;
        _startedAt = Time.time;
        _nextCheckAt = 0f;

        StopNormalBehavior(player);
    }

    private static void ClearRunState()
    {
        _active = false;
        _commandSent = false;
        _abandoned = false;
        _gameManagerWarned = false;
        _attempts = 0;
        _confirmCount = 0;
        _startedAt = 0f;
        _nextCheckAt = 0f;
        _lastConfirmSnapshotUtc = default(DateTime);
    }

    private static void StopNormalBehavior(Player player)
    {
        if (player == null)
            return;

        try
        {
            /*
             * Stoppe uniquement le combat actuellement engagé.
             */
            GameManager manager = GameManager.gm;

            if (manager != null && player.saldiridurumu)
                manager.SaldiriDurdur();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(
                "[RespawnWire] Erreur arrêt combat : " + e);
        }
    }
}
