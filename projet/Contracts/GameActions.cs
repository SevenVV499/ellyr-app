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

    public enum RespawnSendResult
    {
        Sent,
        // Le jeu n'est pas prêt (GameManager absent) : aucune tentative n'est consommée.
        Unavailable,
        // La commande a levé une erreur : la tentative est comptée.
        Failed
    }

    public interface IRespawnActions
    {
        // Envoie la commande de réapparition du jeu (GameManager.YenidenDog(1)).
        RespawnSendResult SendRespawn();

        // Arrête l'attaque en cours, s'il y en a une.
        void StopCombat();

        // Efface les récompenses d'avant la mort.
        void PurgeRewards();
    }

    public interface ISurvivalActions
    {
        // Envoie la commande de réparation du navire (le client garde la référence au joueur).
        void Repair();
    }
}
