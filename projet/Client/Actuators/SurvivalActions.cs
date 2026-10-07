using System;
using EtatJoueurMod;

/*
 * Actionneur client de la survie : envoie la commande de réparation du jeu (Player.tamirOlBaslat).
 * Le cerveau décide quand réparer ; ce fichier est le seul à toucher au joueur.
 */
internal sealed class ClientSurvivalActions : ISurvivalActions
{
    public void Repair()
    {
        try
        {
            Player player = GameState.JoueurLocal;
            if (player != null)
                player.tamirOlBaslat();
        }
        catch (Exception e)
        {
            BrainContext.Log.Error("[SurvivalActions] Player.tamirOlBaslat a échoué : " + e);
        }
    }
}
