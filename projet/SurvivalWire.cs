using System;
using EtatJoueurMod;
using UnityEngine;

/*
 * Mécanique centrale de survie (PV) : Réparation + Fuite.
 *
 * - Réparation : parallèle à l'activité. Ne touche jamais au Brain ; elle envoie
 *   seulement Player.tamirOlBaslat() tant que les PV sont sous le seuil.
 * - Fuite : prioritaire. Quand elle démarre, CopperWire abandonne Combat (et Collect sauf option)
 *   et n'autorise que la Navigation (déplacements aléatoires habituels) jusqu'à
 *   la sortie de fuite.
 *
 * Les deux seuils sont indépendants (Plugin.RepairPercent / Plugin.FleePercent).
 *
 * Mode « arrêt d'activité » (Plugin.HaltInsteadOfFlee) : au même seuil, au lieu de
 * fuir, toute activité est arrêtée (pas de Navigation, pas de déplacement) ; seule
 * la réparation travaille. Même règle de sortie que la fuite.
 *
 * Sortie de fuite : PV >= seuil réparation ET PV > seuil fuite, c'est-à-dire
 * au-dessus des deux seuils. Pendant la fuite, la réparation est tentée à
 * chaque PV < PV max (si elle est activée) pour que les PV puissent remonter
 * même quand le seuil de réparation est plus bas que le seuil de fuite.
 */
public static class SurvivalWire
{
    // Délai minimal entre deux Commands Repair() : une réparation interrompue
    // (tir reçu...) est retentée tant que la condition de PV reste vraie.
    private const float RepairRetrySeconds = 2f;

    // Un snapshot plus vieux que ça ne prouve rien sur les PV actuels.
    private const float MaxSnapshotAgeSeconds = 3f;

    // Garde-fou : une fuite qui ne se termine pas (réparation désactivée,
    // PV qui ne remontent pas) est abandonnée après ce délai.
    private const float FleeMaxSeconds = 180f;

    private static bool _fleeing;
    private static bool _fleeSuppressed;
    private static bool _repairPaused;
    private static bool _pauseSuppressed;
    private static float _pauseStartedAt;
    private static float _fleeStartedAt;
    private static float _nextRepairAt;

    public static bool IsFleeing { get { return _fleeing && !Plugin.HaltInsteadOfFlee; } }
    // Mode « réparer puis reprendre » : activité arrêtée jusqu'aux PV pleins.
    public static bool IsRepairPaused { get { return _repairPaused; } }
    public static bool IsHalting { get { return _fleeing && Plugin.HaltInsteadOfFlee; } }

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
     * started / ended signalent la transition survenue pendant cet appel.
     */
    public static bool Tick(Player player, EtatJeuSnapshot snapshot, out bool started, out bool ended)
    {
        started = false;
        ended = false;

        bool wasFleeing = _fleeing;
        FicheJoueur joueur = snapshot == null ? null : snapshot.Joueur;
        if (player == null
            || joueur == null
            || IsSnapshotStale(snapshot)
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
        TryRepair(player, joueur, percent);

        started = !wasFleeing && _fleeing;
        ended = wasFleeing && !_fleeing;
        return _fleeing;
    }

    private static void UpdateFlee(float percent)
    {
        if (!Plugin.FleeEnabled)
        {
            if (_fleeing)
                EndFlee("fuite désactivée");
            _fleeSuppressed = false;
            return;
        }

        float fleePercent = Plugin.FleePercent;
        if (!_fleeing)
        {
            if (percent > fleePercent)
                _fleeSuppressed = false;

            if (!_fleeSuppressed && percent <= fleePercent)
            {
                _fleeing = true;
                _fleeStartedAt = Time.time;
                Plugin.Logger.LogInfo(
                    "[SurvivalWire] Fuite démarrée à " + percent.ToString("F0") + " % (seuil "
                    + fleePercent + " %).");
            }
            return;
        }

        if (percent >= Plugin.RepairPercent && percent > fleePercent)
        {
            EndFlee("PV remontés à " + percent.ToString("F0") + " %");
            return;
        }

        if (Time.time - _fleeStartedAt >= FleeMaxSeconds)
        {
            // Pas de nouvelle fuite tant que les PV ne sont pas repassés au-dessus du seuil.
            _fleeSuppressed = true;
            EndFlee("délai maximal de fuite atteint");
        }
    }

    /*
     * Mode « réparer puis reprendre » (Plugin.RepairPausesActivity) : dès que les PV
     * passent sous le seuil de réparation, toute activité est mise en pause (navire
     * immobile, géré par CopperWire) tandis que la réparation travaille ; l'activité
     * ne reprend qu'aux PV pleins. La fuite reste prioritaire si elle est active.
     */
    private static void UpdateRepairPause(FicheJoueur joueur, float percent)
    {
        if (!Plugin.RepairPausesActivity || !Plugin.RepairEnabled)
        {
            if (_repairPaused)
                Plugin.Logger.LogInfo("[SurvivalWire] Fin de pause de réparation : option désactivée.");
            _repairPaused = false;
            _pauseSuppressed = false;
            return;
        }

        if (!_repairPaused)
        {
            if (percent > Plugin.RepairPercent)
                _pauseSuppressed = false;

            if (!_pauseSuppressed && percent <= Plugin.RepairPercent && joueur.Vie < joueur.VieMax)
            {
                _repairPaused = true;
                _pauseStartedAt = Time.time;
                Plugin.Logger.LogInfo(
                    "[SurvivalWire] Pause de réparation à " + percent.ToString("F0") + " % (seuil "
                    + Plugin.RepairPercent + " %).");
            }
            return;
        }

        if (joueur.Vie >= joueur.VieMax)
        {
            _repairPaused = false;
            Plugin.Logger.LogInfo("[SurvivalWire] Fin de pause de réparation : PV pleins.");
        }
        else if (Time.time - _pauseStartedAt >= FleeMaxSeconds)
        {
            _pauseSuppressed = true;
            _repairPaused = false;
            Plugin.Logger.LogInfo("[SurvivalWire] Fin de pause de réparation : délai maximal atteint.");
        }
    }

    private static void EndFlee(string reason)
    {
        _fleeing = false;
        Plugin.Logger.LogInfo("[SurvivalWire] Fin de fuite : " + reason + ".");
    }

    /*
     * Repair() : déclenché tant que les PV sont sous le seuil de réparation
     * (ou pendant une fuite), sans jamais modifier l'activité courante.
     * Réparation en cours (état rapporté par le jeu) : rien à renvoyer.
     */
    private static void TryRepair(Player player, FicheJoueur joueur, float percent)
    {
        if (!Plugin.RepairEnabled
            || joueur.Reparation
            || joueur.Vie >= joueur.VieMax
            || Time.time < _nextRepairAt)
            return;

        if (percent > Plugin.RepairPercent && !_fleeing && !_repairPaused)
            return;

        _nextRepairAt = Time.time + RepairRetrySeconds;
        try
        {
            player.tamirOlBaslat();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[SurvivalWire] Player.tamirOlBaslat a échoué : " + e);
        }
    }

    private static bool IsSnapshotStale(EtatJeuSnapshot snapshot)
    {
        if (snapshot.Timestamp == default(DateTime))
            return true;

        return (DateTime.UtcNow - snapshot.Timestamp).TotalSeconds > MaxSnapshotAgeSeconds;
    }
}
