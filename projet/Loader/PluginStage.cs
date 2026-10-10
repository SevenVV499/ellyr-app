using System;
using System.Runtime.CompilerServices;

namespace EllyrLoader
{
    // Étape 3 : démarre le bot avec notre hôte. Compilée par le runtime seulement à l'appel, quand le jeu est prêt.
    internal static class PluginStage
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Run(LoaderPaths paths)
        {
            LoaderLog.Info("Étape 3 : démarrage du bot");
            var host = new StandaloneHost(paths);
            EtatJoueurMod.Plugin.Start(host);
            LoaderLog.Info("Le bot est démarré");
        }
    }
}
