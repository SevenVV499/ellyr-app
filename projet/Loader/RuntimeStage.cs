using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Il2CppInterop.HarmonySupport;
using Il2CppInterop.Runtime.Startup;

namespace EllyrLoader
{
    /*
     * Étape 2 : prépare Il2CppInterop (le pont entre .NET et le jeu IL2CPP), comme le fait BepInEx.
     * Cette classe référence Il2CppInterop : elle n'est compilée par le runtime que quand LoaderMain l'appelle,
     * après la mise en place de la recherche des bibliothèques.
     */
    internal static class RuntimeStage
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Run(LoaderPaths paths, LoaderConfig config)
        {
            LoaderLog.Info("Étape 2 : initialisation d'Il2CppInterop");

            if (!File.Exists(paths.GameAssembly))
                throw new FileNotFoundException("GameAssembly.dll introuvable", paths.GameAssembly);
            if (!Directory.Exists(paths.Interop))
                throw new DirectoryNotFoundException("Dossier interop introuvable : " + paths.Interop);

            DobbyLib.InstallResolver(paths.Lib);
            Environment.SetEnvironmentVariable("IL2CPP_INTEROP_DATABASES_LOCATION", paths.Interop);

            string gameAssemblyPath = paths.GameAssembly;
            NativeLibrary.SetDllImportResolver(
                typeof(Il2CppInterop.Runtime.IL2CPP).Assembly,
                delegate (string name, Assembly assembly, DllImportSearchPath? search)
                {
                    return name == "GameAssembly"
                        ? NativeLibrary.Load(gameAssemblyPath, assembly, search)
                        : IntPtr.Zero;
                });

            Version unity = UnityVersionReader.Read(paths, config);
            LoaderLog.Info("Version d'Unity : " + unity);

            Il2CppInteropRuntime.Create(new RuntimeConfiguration
                {
                    UnityVersion = unity,
                    DetourProvider = new DobbyDetourProvider()
                })
                .AddLogger(new MsLoggerAdapter("Il2CppInterop"))
                .AddHarmonySupport()
                .Start();
            LoaderLog.Info("Il2CppInterop est prêt");

            RuntimeInvokeHook.Install(paths, config);
        }
    }
}
