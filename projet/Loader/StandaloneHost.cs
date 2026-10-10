using System;
using System.IO;
using EtatJoueurMod;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace EllyrLoader
{
    /*
     * L'hôte d'Ellyr sans BepInEx : il fournit au bot son journal, ses réglages et la création de sa console.
     * C'est l'équivalent de Client/Host/BepInExHost.cs.
     */
    internal sealed class StandaloneHost : IHost, IHostLog
    {
        private readonly ISettingsStore _settings;
        private GameObject _manager;

        public StandaloneHost(LoaderPaths paths)
        {
            // Reprise des réglages de l'ancienne version BepInEx s'ils existent.
            string oldFile = Path.Combine(paths.GameRoot, "BepInEx", "config", Plugin.PluginGuid + ".cfg");
            _settings = new IniSettingsStore(Path.Combine(paths.Config, "ellyr.cfg"), oldFile);
        }

        public IHostLog Log { get { return this; } }
        public ISettingsStore Settings { get { return _settings; } }

        public void Warning(string message) { LoaderLog.Warning(message); }
        public void Error(string message) { LoaderLog.Error(message); }

        // Même méthode que BepInEx : le type est déclaré à IL2CPP puis ajouté à un objet Unity qui survit aux scènes.
        public T AddComponent<T>() where T : MonoBehaviour
        {
            if (_manager == null)
                _manager = new GameObject { hideFlags = HideFlags.HideAndDontSave, name = "Ellyr_Manager" };

            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(T)))
                ClassInjector.RegisterTypeInIl2Cpp(typeof(T));

            return _manager.AddComponent(Il2CppType.From(typeof(T))).Cast<T>();
        }
    }
}
