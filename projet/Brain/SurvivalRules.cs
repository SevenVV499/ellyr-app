using System;
using EtatJoueurMod;

/*
 * Mécanique centrale de survie (PV) : Réparation + Fuite.
 *
 * - Réparation : parallèle à l'activité. Ne touche jamais au Brain ; elle demande seulement au client
 *   (ISurvivalActions.Repair) de réparer tant que les PV sont sous le seuil.
 * - Fuite : prioritaire. Quand elle démarre, le client abandonne Combat (et Collect sauf option)
 *   et n'autorise que la Navigation (déplacements aléatoires habituels) jusqu'à
 *   la sortie de fuite.
 *
 * Les deux seuils sont indépendants (BrainContext.Settings.RepairPercent / BrainContext.Settings.FleePercent).
 *
 * L'arrêt d'activité est porté par la réparation « Stopped »
 * (BrainContext.Settings.RepairPausesActivity, voir UpdateRepairPause) ; la fuite reste prioritaire.
 *
 * Sortie de fuite : PV >= seuil réparation ET PV > seuil fuite, c'est-à-dire
 * au-dessus des deux seuils. Pendant la fuite, la réparation est tentée à
 * chaque PV < PV max (si elle est activée) pour que les PV puissent remonter
 * même quand le seuil de réparation est plus bas que le seuil de fuite.
 */
public static class SurvivalRules
{
    private static bool _fleeing;
    private static bool _fleeSuppressed;
    private static bool _repairPaused;
    private static bool _pauseSuppressed;
    private static float _pauseStartedAt;
    private static float _fleeStartedAt;
    private static float _nextRepairAt;

    // Horloge du jeu (secondes), fournie par le client à chaque Tick : les règles ne lisent pas le temps elles-mêmes.
    private static float _now;

    public static bool IsFleeing { get { return _fleeing; } }
    // Mode « réparer puis reprendre » : activité arrêtée jusqu'aux PV pleins.
    public static bool IsRepairPaused { get { return _repairPaused; } }

    public static void Reset()
    {
        _fleeing = false;
        _fleeSuppressed = false;
        _repairPaused = false;
        _pauseSuppressed = false;
        _nextRepairAt = 0f;
    }

    /*
     * Évalue la survie sur un snapshot. Renvoie true tant que la fuite est active.
     * ended signale la fin de fuite survenue pendant cet appel.
     */
    public static bool Tick(EtatJeuSnapshot snapshot, float now, out bool ended)
    {
        ended = false;
        _now = now;

        bool wasFleeing = _fleeing;
        FicheJoueur joueur = snapshot == null ? null : snapshot.Joueur;
        if (joueur == null
            || RulesData.IsSnapshotStale(snapshot)
            || joueur.Vie <= 0
            || joueur.VieMax <= 0)
        {
            // Mort ou information non fiable : la fuite ne peut pas continuer sur cette base.
            if (joueur != null && (joueur.Vie <= 0 || joueur.VieMax <= 0))
            {
                _fleeing = false;
                _repairPaused = false;
            }
            ended = wasFleeing && !_fleeing;
            return _fleeing;
        }

        float percent = joueur.PourcentageVie;
        UpdateFlee(percent);
        UpdateRepairPause(joueur, percent);
        TryRepair(joueur, percent);

        ended = wasFleeing && !_fleeing;
        return _fleeing;
    }

    private static void UpdateFlee(float percent)
    {
        if (!BrainContext.Settings.FleeEnabled)
        {
            if (_fleeing)
                EndFlee("fuite désactivée");
            _fleeSuppressed = false;
            return;
        }

        float fleePercent = BrainContext.Settings.FleePercent;
        if (!_fleeing)
        {
            if (percent > fleePercent)
                _fleeSuppressed = false;

            if (!_fleeSuppressed && percent <= fleePercent)
            {
                _fleeing = true;
                _fleeStartedAt = _now;
            }
            return;
        }

        if (percent >= BrainContext.Settings.RepairPercent && percent > fleePercent)
        {
            EndFlee("PV remontés à " + percent.ToString("F0") + " %");
            return;
        }

        if (_now - _fleeStartedAt >= RulesData.Survival.FleeMaxSeconds)
        {
            // Pas de nouvelle fuite tant que les PV ne sont pas repassés au-dessus du seuil.
            _fleeSuppressed = true;
            EndFlee("délai maximal de fuite atteint");
        }
    }

    /*
     * Mode « réparer puis reprendre » (BrainContext.Settings.RepairPausesActivity) : dès que les PV
     * passent sous le seuil de réparation, toute activité est mise en pause (navire
     * immobile, géré par le client) tandis que la réparation travaille ; l'activité
     * ne reprend qu'aux PV pleins. La fuite reste prioritaire si elle est active.
     */
    private static void UpdateRepairPause(FicheJoueur joueur, float percent)
    {
        if (!BrainContext.Settings.RepairPausesActivity || !BrainContext.Settings.RepairEnabled)
        {
            _repairPaused = false;
            _pauseSuppressed = false;
            return;
        }

        if (!_repairPaused)
        {
            if (percent > BrainContext.Settings.RepairPercent)
                _pauseSuppressed = false;

            if (!_pauseSuppressed && percent <= BrainContext.Settings.RepairPercent && joueur.Vie < joueur.VieMax)
            {
                _repairPaused = true;
                _pauseStartedAt = _now;
            }
            return;
        }

        if (joueur.Vie >= joueur.VieMax)
        {
            _repairPaused = false;
        }
        else if (_now - _pauseStartedAt >= RulesData.Survival.FleeMaxSeconds)
        {
            _pauseSuppressed = true;
            _repairPaused = false;
        }
    }

    private static void EndFlee(string reason)
    {
        _fleeing = false;
    }

    /*
     * Repair() : déclenché tant que les PV sont sous le seuil de réparation
     * (ou pendant une fuite), sans jamais modifier l'activité courante.
     * Réparation en cours (état rapporté par le jeu) : rien à renvoyer.
     */
    private static void TryRepair(FicheJoueur joueur, float percent)
    {
        if (!BrainContext.Settings.RepairEnabled
            || joueur.Reparation
            || joueur.Vie >= joueur.VieMax
            || _now < _nextRepairAt)
            return;

        if (percent > BrainContext.Settings.RepairPercent && !_fleeing && !_repairPaused)
            return;

        _nextRepairAt = _now + RulesData.Survival.RepairRetrySeconds;
        BrainContext.SurvivalActions.Repair();
    }
}
