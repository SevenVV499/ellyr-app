/*
 * Contrat : les ordres que le cerveau peut demander au client. Le cerveau décide ; le client touche au jeu.
 */
namespace EtatJoueurMod
{
    public interface IRaidActions
    {
        // Lance l'entrée en Raid (petite ou grande). Faux si l'appel n'a pas pu être fait.
        bool TryEnter(bool petiteRaid);
    }
}
