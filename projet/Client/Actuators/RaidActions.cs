using System;
using EtatJoueurMod;

/*
 * Actionneur client de la Raid : reproduit le clic du bouton d'entrée du jeu
 * (MenuManager.raidmapgir / raidmapAcemigir). Les contrôles natifs du jeu (délai, stock, carte)
 * restent actifs : l'appel équivaut à celui de l'interface.
 */
internal sealed class ClientRaidActions : IRaidActions
{
    private MenuManager _menu;

    public bool TryEnter(bool petiteRaid)
    {
        try
        {
            // Accès statique du jeu (assigné dans MenuManager.Start) ; repli sur une recherche
            // dans la scène, mémorisée, tant qu'il est nul.
            MenuManager menu = MenuManager.menuManager;
            if (menu == null)
            {
                if (_menu == null)
                    _menu = UnityEngine.Object.FindObjectOfType<MenuManager>();
                menu = _menu;
            }
            if (menu == null)
            {
                BrainContext.Log.Error("[RaidActions] MenuManager introuvable.");
                return false;
            }

            // Petite Raid (talisman du soleil) = raidmapgir / oyuncuTilsim ;
            // Grande Raid (talisman de Behemoth) = raidmapAcemigir / oyuncuAcemiTilsim.
            if (petiteRaid)
                menu.raidmapgir();
            else
                menu.raidmapAcemigir();
            return true;
        }
        catch (Exception e)
        {
            BrainContext.Log.Error("[RaidActions] Erreur d'entrée : " + e);
            return false;
        }
    }
}
