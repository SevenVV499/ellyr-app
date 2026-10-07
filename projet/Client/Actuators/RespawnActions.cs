using System;
using EtatJoueurMod;

/*
 * Actionneur client de la réapparition : les trois appels au jeu dont les règles ont besoin.
 * - SendRespawn : GameManager.YenidenDog(1) (chemin natif du bouton de réapparition) ;
 * - StopCombat  : arrête l'attaque en cours (GameManager.SaldiriDurdur) ;
 * - PurgeRewards : efface les récompenses d'avant la mort.
 */
internal sealed class ClientRespawnActions : IRespawnActions
{
    public RespawnSendResult SendRespawn()
    {
        try
        {
            GameManager manager = GameManager.gm;
            if (manager == null)
                return RespawnSendResult.Unavailable;

            manager.YenidenDog(1);
            return RespawnSendResult.Sent;
        }
        catch (Exception e)
        {
            BrainContext.Log.Error("[RespawnActions] YenidenDog(1) a échoué : " + e);
            return RespawnSendResult.Failed;
        }
    }

    public void StopCombat()
    {
        try
        {
            Player player = GameState.JoueurLocal;
            GameManager manager = GameManager.gm;
            if (manager != null && player != null && player.saldiridurumu)
                manager.SaldiriDurdur();
        }
        catch (Exception e)
        {
            BrainContext.Log.Error("[RespawnActions] Erreur arrêt combat : " + e);
        }
    }

    public void PurgeRewards()
    {
        try
        {
            GameState.PurgerRecompenses();
        }
        catch (Exception e)
        {
            BrainContext.Log.Error("[RespawnActions] Erreur purge des récompenses : " + e);
        }
    }
}
