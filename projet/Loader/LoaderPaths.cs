using System;
using System.Diagnostics;
using System.IO;

namespace EllyrLoader
{
    // Tous les dossiers utilisés par le chargeur. Le dossier Ellyr se trouve dans le dossier du jeu.
    internal sealed class LoaderPaths
    {
        public string Root;          // dossier d'Ellyr (celui de Ellyr.Loader.dll)
        public string Lib;           // bibliothèques tierces (Il2CppInterop, Harmony, MonoMod, dobby…)
        public string Interop;       // types du jeu générés (Assembly-CSharp, UnityEngine…)
        public string Config;
        public string Logs;
        public string GameRoot;      // dossier de l'exécutable du jeu
        public string GameExe;
        public string GameDataDir;   // <jeu>_Data
        public string GameAssembly;

        public static LoaderPaths Create()
        {
            string loaderDll = null;
            string invoke = Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH");
            if (!string.IsNullOrEmpty(invoke))
            {
                string full = Path.GetFullPath(invoke);
                if (File.Exists(full))
                    loaderDll = full;
            }
            if (loaderDll == null)
                loaderDll = typeof(LoaderPaths).Assembly.Location;

            string exe = Environment.GetEnvironmentVariable("DOORSTOP_PROCESS_PATH");
            if (string.IsNullOrEmpty(exe))
                exe = Process.GetCurrentProcess().MainModule.FileName;
            exe = Path.GetFullPath(exe);

            var paths = new LoaderPaths();
            paths.Root = Path.GetDirectoryName(loaderDll);
            paths.Lib = Path.Combine(paths.Root, "lib");
            paths.Interop = Path.Combine(paths.Root, "interop");
            paths.Config = Path.Combine(paths.Root, "config");
            paths.Logs = Path.Combine(paths.Root, "logs");
            paths.GameExe = exe;
            paths.GameRoot = Path.GetDirectoryName(exe);
            paths.GameDataDir = Path.Combine(paths.GameRoot, Path.GetFileNameWithoutExtension(exe) + "_Data");
            paths.GameAssembly = Path.Combine(paths.GameRoot, "GameAssembly.dll");
            return paths;
        }
    }
}
