using System;
using EtatJoueurMod;
using UnityEngine;

/*
 * Mécanique centrale de survie (PV) : Réparation + Fuite.
 *
 * - Réparation : parallèle à l'activité. Ne touche jamais au Brain ; elle envoie
 *   seulement Player.tamirOlBaslat() tant que les PV sont sous le seuil.
 * - Fuite : prioritaire. Quand elle démarre, CopperWire abandonne l'action
 *   Navigation / Collect / Combat et se déplace à l'opposé de la menace.
 *
 * Les deux seuils sont indépendants (Plugin.RepairPercent / Plugin.FleePercent).
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

    // Rayon dans lequel une entité visible est considérée comme menace
    // quand aucune cible de combat n'est connue.
    private const float FallbackThreatRadius = 80f;

    private static bool _fleeing;
    private static bool _fleeSuppressed;
    private static float _fleeStartedAt;
    private static float _nextRepairAt;

    private static bool _hasThreat;
    private static int _threatMap;
    private static Vector2 _threatPosition;

    public static bool IsFleeing { get { return _fleeing; } }

    public static void Reset()
    {
        _fleeing = false;
        _fleeSuppressed = false;
        _nextRepairAt = 0f;
        _hasThreat = false;
    }

    public static void NoteThreat(int mapId, float x, float y)
    {
        if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y))
            return;

        _hasThreat = true;
        _threatMap = mapId;
        _threatPosition = new Vector2(x, y);
    }

    public static void NoteThreatFromSnapshot(EtatJeuSnapshot snapshot)
    {
        if (snapshot == null || snapshot.Joueur == null)
            return;

        int mapId = snapshot.Joueur.Harita;
        float bestDistance = FallbackThreatRadius;
        bool found = false;
        float bestX = 0f;
        float bestY = 0f;

        for (int i = 0; i < snapshot.Pnjs.Count; i++)
        {
            PnjInfo pnj = snapshot.Pnjs[i];
            if (pnj == null || pnj.Harita != mapId || pnj.Distance >= bestDistance)
                continue;
            bestDistance = pnj.Distance;
            bestX = pnj.X;
            bestY = pnj.Y;
            found = true;
        }

        for (int i = 0; i < snapshot.Navires.Count; i++)
        {
            NavireInfo navire = snapshot.Navires[i];
            if (navire == null || navire.Distance >= bestDistance)
                continue;
            bestDistance = navire.Distance;
            bestX = navire.X;
            bestY = navire.Y;
            found = true;
        }

        if (found)
            NoteThreat(mapId, bestX, bestY);
    }

    public static bool TryGetThreat(int mapId, out Vector2 position)
    {
        position = _threatPosition;
        return _hasThreat && _threatMap == mapId;
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
                _fleeing = false;
            ended = wasFleeing && !_fleeing;
            return _fleeing;
        }

        float percent = joueur.PourcentageVie;
        UpdateFlee(percent);
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

        if (percent > Plugin.RepairPercent && !_fleeing)
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
