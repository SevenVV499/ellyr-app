/*
 * Point d'entrée appelé par Doorstop dans le processus du jeu.
 * Doorstop exige exactement cette classe et cette méthode : Doorstop.Entrypoint.Start().
 */
namespace Doorstop
{
    public static class Entrypoint
    {
        public static void Start()
        {
            EllyrLoader.LoaderMain.Run();
        }
    }
}
