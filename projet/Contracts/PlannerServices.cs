using System;

/*
 * Contrat : les trois services du jeu dont le planificateur a besoin. Le planificateur ne connaît ni
 * GameState ni TargetCatalog : il appelle BrainContext.Services, renseigné par le client au démarrage.
 */
namespace EtatJoueurMod
{
    public enum TargetCategory
    {
        Monster,
        Npc
    }

    public interface IPlannerServices
    {
        // État de confirmation d'une collecte (callback du jeu, récompense reçue).
        CollectConfirmationState GetCollectConfirmation(
            EtatJeuSnapshot snapshot,
            uint netId,
            string collectibleType,
            DateTime startedAtUtc,
            long callbackSequenceBaseline,
            long rewardSequenceBaseline);

        // Compteurs de séquence au moment où une collecte démarre.
        void GetCollectSequences(out long callbackSequence, out long rewardSequence);

        // Catégorie d'arme (canon ou harpon) d'une cible d'après le catalogue du jeu.
        bool TryGetWeaponCategory(string name, out TargetCategory category);
    }
}
