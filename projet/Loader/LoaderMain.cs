using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EllyrLoader
{
    /*
     * Démarrage du chargeur. Cette classe ne doit référencer AUCUN type du jeu ni d'Il2CppInterop :
     * ils ne sont pas encore disponibles. Les étapes qui en ont besoin sont dans d'autres classes,
     * compilées par le runtime seulement au moment où on les appelle.
     */
    internal static class LoaderMain
    {
        private static LoaderPaths _paths;

        public static void Run()
        {
            try
            {
                _paths = LoaderPaths.Create();
                LoaderLog.Init(_paths);
                AppDomain.CurrentDomain.AssemblyResolve += ResolveFromEllyrFolders;

                LoaderConfig config = LoaderConfig.Load(_paths);
                LoaderLog.Info("Étape 1 : le chargeur est démarré par Doorstop");
                LoaderLog.Info("Runtime : " + RuntimeInformation.FrameworkDescription);
                LoaderLog.Info("Jeu : " + _paths.GameExe);
                LoaderLog.Info("Dossier Ellyr : " + _paths.Root);
                LoaderLog.Info("Étape demandée : " + config.Stage);

                if (config.Stage >= 2)
                    RuntimeStage.Run(_paths, config);
            }
            catch (Exception e)
            {
                LoaderLog.Error("Le chargeur s'est arrêté : " + e);
            }
        }

        // Cherche les bibliothèques d'Ellyr (lib), les types du jeu (interop) et le dossier d'Ellyr.
        private static Assembly ResolveFromEllyrFolders(object sender, ResolveEventArgs args)
        {
            LoaderPaths paths = _paths;
            if (paths == null)
                return null;

            string name = new AssemblyName(args.Name).Name;
            foreach (string folder in new[] { paths.Root, paths.Lib, paths.Interop })
            {
                string candidate = Path.Combine(folder, name + ".dll");
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);
            }
            return null;
        }
    }
}
