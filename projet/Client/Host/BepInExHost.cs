using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

/*
 * Hôte BepInEx : seul fichier du bot qui connaît BepInEx. Il fait le point d'entrée du plugin et branche le journal,
 * les réglages (fichier de config BepInEx, inchangé) et la création du composant Unity sur le contrat IHost.
 */
namespace EtatJoueurMod
{
    [BepInPlugin(Plugin.PluginGuid, Plugin.PluginName, Plugin.PluginVersion)]
    public class BepInExEntry : BasePlugin, IHost
    {
        private IHostLog _log;
        private ISettingsStore _settings;

        IHostLog IHost.Log { get { return _log; } }
        ISettingsStore IHost.Settings { get { return _settings; } }

        public override void Load()
        {
            _log = new BepInExLog(BepInEx.Logging.Logger.CreateLogSource(Plugin.PluginName));
            _settings = new BepInExSettings(Config);
            Plugin.Start(this);
        }

        T IHost.AddComponent<T>()
        {
            return AddComponent<T>();
        }
    }

    internal sealed class BepInExLog : IHostLog
    {
        private readonly ManualLogSource _source;

        public BepInExLog(ManualLogSource source)
        {
            _source = source;
        }

        public void Warning(string message) { _source.LogWarning(message); }
        public void Error(string message) { _source.LogError(message); }
    }

    internal sealed class BepInExSettings : ISettingsStore
    {
        private readonly ConfigFile _file;

        public BepInExSettings(ConfigFile file)
        {
            _file = file;
        }

        public ISetting<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return new Entry<T>(_file.Bind(section, key, defaultValue, description));
        }

        public ISetting<int> BindRange(string section, string key, int defaultValue, string description, int minimum, int maximum)
        {
            return new Entry<int>(_file.Bind(
                section, key, defaultValue,
                new ConfigDescription(description, new AcceptableValueRange<int>(minimum, maximum))));
        }

        public void Save()
        {
            _file.Save();
        }

        private sealed class Entry<T> : ISetting<T>
        {
            private readonly ConfigEntry<T> _entry;

            public Entry(ConfigEntry<T> entry)
            {
                _entry = entry;
            }

            public T Value
            {
                get { return _entry.Value; }
                set { _entry.Value = value; }
            }
        }
    }
}
