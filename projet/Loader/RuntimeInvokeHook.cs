using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EllyrLoader
{
    /*
     * Attend que Unity ait fini de démarrer, comme BepInEx : on surveille les appels du jeu (il2cpp_runtime_invoke)
     * jusqu'au changement de scène active, puis on retire la surveillance et on démarre le bot.
     */
    internal static class RuntimeInvokeHook
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr RuntimeInvokeDelegate(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exception);

        private static RuntimeInvokeDelegate _hook;
        private static RuntimeInvokeDelegate _original;
        private static DobbyDetour _detour;
        private static LoaderPaths _paths;
        private static LoaderConfig _config;

        public static void Install(LoaderPaths paths, LoaderConfig config)
        {
            _paths = paths;
            _config = config;

            IntPtr gameAssembly = NativeLibrary.Load(paths.GameAssembly);
            IntPtr export = NativeLibrary.GetExport(gameAssembly, "il2cpp_runtime_invoke");

            _hook = OnInvoke;
            _detour = new DobbyDetour(export, _hook);
            _original = _detour.GenerateTrampoline<RuntimeInvokeDelegate>();
            _detour.Apply();
            LoaderLog.Info("En attente du démarrage de la scène par Unity");
        }

        private static IntPtr OnInvoke(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exception)
        {
            bool sceneChanged = false;
            try
            {
                string name = Marshal.PtrToStringAnsi(Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name(method));
                if (name == "Internal_ActiveSceneChanged")
                {
                    sceneChanged = true;
                    OnUnityReady();
                }
            }
            catch (Exception e)
            {
                LoaderLog.Error("Erreur au démarrage de la scène : " + e);
            }

            IntPtr result = _original(method, obj, parameters, exception);

            if (sceneChanged)
            {
                try { _detour.Dispose(); }
                catch (Exception e) { LoaderLog.Error("Retrait de la surveillance : " + e); }
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OnUnityReady()
        {
            LoaderLog.Info("Unity est prêt (scène active changée)");
            PreloadInteropAssemblies();

            if (_config.Stage >= 3)
                PluginStage.Run(_paths);
            else
                LoaderLog.Info("Étape 3 non demandée : le bot n'est pas démarré (loader.ini : stage=3)");
        }

        // Charge tous les types du jeu avant le bot, comme BepInEx : le bot parcourt certains assemblies par réflexion.
        private static void PreloadInteropAssemblies()
        {
            int loaded = 0;
            foreach (string file in Directory.GetFiles(_paths.Interop, "*.dll"))
            {
                try
                {
                    Assembly.LoadFrom(file);
                    loaded++;
                }
                catch (Exception)
                {
                    // Un assembly illisible est ignoré, comme dans BepInEx.
                }
            }
            LoaderLog.Info("Types du jeu chargés : " + loaded);
        }
    }
}
