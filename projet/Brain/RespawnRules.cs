using System;
using EtatJoueurMod;

/*
 * Règles de réapparition : détection du décès, envoi de la commande (par IRespawnActions, côté client),
 * tentatives, délais, confirmation du retour à la vie. Aucun appel direct au jeu.
 */
public static class RespawnRules
{
    // Horloge du jeu (secondes), fournie par le client à chaque Tick : les règles ne lisent pas le temps elles-mêmes.
    private static float _now;

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
            BrainContext.Log.Error(
                "[RespawnRules] Configurer appelé sans BehaviorBrain : " +
                "le SystemState Respawn ne sera pas synchronisé.");
    }

    public static void Reset()
    {
        ClearRunState();
        _confirmedSnapshotUtc = default(DateTime);

        /*
         * Un reset garantit RespawnRules inactif ET Brain en Normal,
         * même si _active était déjà faux alors que le Brain est resté
         * accidentellement en Respawn.
         */
        if (_brain != null && _brain.IsRespawning)
            _brain.ExitRespawn();
    }

    public static void Tick(EtatJeuSnapshot snapshot, float now)
    {
        if (snapshot == null || snapshot.Joueur == null)
            return;

        _now = now;

        if (_brain == null && !_brainWarned)
        {
            _brainWarned = true;
            BrainContext.Log.Error(
                "[RespawnRules] Aucun BehaviorBrain configuré : " +
                "le respawn fonctionne sans synchroniser le SystemState.");
        }

        /*
         * Un snapshot trop ancien (JoueurLocal indisponible, changement de
         * scène...) ne prouve rien : il ne peut ni déclencher un respawn,
         * ni le confirmer, ni provoquer l'envoi d'une commande.
         */
        if (RulesData.IsSnapshotStale(snapshot))
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
             * alors que RespawnRules est inactif.
             */
            if (_brain != null && _brain.IsRespawning)
            {
                BrainContext.Log.Warning(
                    "[RespawnRules] Brain en Respawn alors que RespawnRules est inactif : sortie forcée.");
                _brain.ExitRespawn();
            }

            if (snapshot.Joueur.Vie > 0 || snapshot.Joueur.VieMax <= 0)
                return;

            StartRespawn();
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
        if (_now < _nextCheckAt)
            return;

        _nextCheckAt = _now + RulesData.Respawn.CheckIntervalSeconds;

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

            if (_confirmCount >= RulesData.Respawn.ConfirmationSnapshotsRequired)
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
                if (_now - _startedAt >= RulesData.Respawn.AttemptTimeoutSeconds)
                {
                    BrainContext.Log.Warning(
                        "[RespawnRules] Timeout avant disponibilité du bouton de respawn.");

                    RestartRespawnAttempt();
                }

                return;
            }

            SendRespawnCommand();
            return;
        }

        /*
         * La commande a été envoyée.
         *
         * Si le retour n'est jamais confirmé, on considère
         * la tentative comme échouée après timeout.
         */
        if (_now - _startedAt >= RulesData.Respawn.AttemptTimeoutSeconds)
        {
            if (_attempts >= RulesData.Respawn.MaxCommandAttempts)
            {
                Abandon();
                return;
            }

            BrainContext.Log.Warning(
                "[RespawnRules] Timeout après YenidenDog(1) (tentative " +
                _attempts + "/" + RulesData.Respawn.MaxCommandAttempts + "). Nouvelle tentative.");

            RestartRespawnAttempt();
        }
    }

    private static void StartRespawn()
    {
        ClearRunState();
        _active = true;
        _startedAt = _now;

        /*
         * Passage explicite du BehaviorBrain dans le SystemState Respawn.
         *
         * Cela annule l'action Navigation / Collect / Combat
         * actuellement en cours.
         */
        if (_brain != null)
            _brain.EnterRespawn();

        BrainContext.RespawnActions.StopCombat();
    }

    private static void SendRespawnCommand()
    {
        if (_commandSent)
            return;

        /*
         * La tentative est comptée même si l'envoi échoue : en cas d'erreur, on ne renvoie pas
         * la commande à chaque contrôle, c'est le timeout qui décidera d'une nouvelle tentative.
         * Seul « jeu indisponible » ne consomme pas de tentative (GameManager absent).
         */
        RespawnSendResult result = BrainContext.RespawnActions.SendRespawn();
        if (result == RespawnSendResult.Unavailable)
        {
            if (!_gameManagerWarned)
            {
                _gameManagerWarned = true;
                BrainContext.Log.Warning(
                    "[RespawnRules] GameManager.gm indisponible.");
            }
            return;
        }

        _attempts++;
        _commandSent = true;
        _startedAt = _now;

        if (result == RespawnSendResult.Failed)
            BrainContext.Log.Error(
                "[RespawnRules] Erreur YenidenDog(1) (tentative " +
                _attempts + "/" + RulesData.Respawn.MaxCommandAttempts + ").");
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
        BrainContext.RespawnActions.PurgeRewards();

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

        BrainContext.Log.Error(
            "[RespawnRules] " + RulesData.Respawn.MaxCommandAttempts +
            " tentatives de YenidenDog(1) sans confirmation : abandon des envois. " +
            "Le système reste en Respawn et observe l'état du joueur.");
    }

    private static void RestartRespawnAttempt()
    {
        /*
         * Une nouvelle tentative ne réactive pas artificiellement
         * le bouton et ne considère pas l'échec comme un succès.
         */
        _commandSent = false;
        _gameManagerWarned = false;
        _startedAt = _now;
        _nextCheckAt = 0f;

        BrainContext.RespawnActions.StopCombat();
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
}
