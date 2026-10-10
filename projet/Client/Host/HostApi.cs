using System;

/*
 * Contrat entre le bot et l'hôte qui le charge dans le jeu (aujourd'hui BepInEx, demain notre propre chargeur).
 * Le bot ne connaît que ces interfaces : tout ce qui est propre à un hôte (point d'entrée, fichier de config,
 * journal, création du composant Unity) vit dans le fichier de cet hôte, sous Client/Host/.
 */
namespace EtatJoueurMod
{
    // Un réglage persistant : la valeur courante, lue et écrite par le bot.
    public interface ISetting<T>
    {
        T Value { get; set; }
    }

    // Stockage des réglages fourni par l'hôte.
    public interface ISettingsStore
    {
        ISetting<T> Bind<T>(string section, string key, T defaultValue, string description);
        ISetting<int> BindRange(string section, string key, int defaultValue, string description, int minimum, int maximum);
        void Save();
    }

    // Journal fourni par l'hôte (avertissements et erreurs seulement).
    public interface IHostLog
    {
        void Warning(string message);
        void Error(string message);
    }

    // Ce que le bot demande à l'hôte qui l'a chargé.
    public interface IHost
    {
        IHostLog Log { get; }
        ISettingsStore Settings { get; }

        // Crée le composant Unity (injecté dans le jeu) qui porte la console.
        T AddComponent<T>() where T : UnityEngine.MonoBehaviour;
    }

    // Journal utilisé partout dans le bot (Plugin.Logger). Sans hôte branché, les messages sont ignorés.
    public sealed class HostLog
    {
        private IHostLog _sink;

        public void Attach(IHostLog sink)
        {
            _sink = sink;
        }

        public void LogWarning(object message)
        {
            IHostLog sink = _sink;
            if (sink != null)
                sink.Warning(message == null ? string.Empty : message.ToString());
        }

        public void LogError(object message)
        {
            IHostLog sink = _sink;
            if (sink != null)
                sink.Error(message == null ? string.Empty : message.ToString());
        }
    }
}
