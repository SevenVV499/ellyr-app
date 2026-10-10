using System;
using System.Collections.Generic;
using System.IO;

namespace EllyrLoader
{
    /*
     * Réglages du chargeur (fichier loader.ini, une ligne clé=valeur) :
     *   stage=1   le chargeur démarre et écrit son journal, rien d'autre
     *   stage=2   + initialise Il2CppInterop et attend que Unity soit prêt
     *   stage=3   + démarre le bot
     *   unityVersion=2022.3.10   (facultatif) force la version d'Unity si elle n'est pas détectée
     */
    internal sealed class LoaderConfig
    {
        public int Stage = 1;
        public string UnityVersion;

        public static LoaderConfig Load(LoaderPaths paths)
        {
            var config = new LoaderConfig();
            string file = Path.Combine(paths.Root, "loader.ini");
            if (!File.Exists(file))
                return config;

            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[')
                    continue;

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                if (string.Equals(key, "stage", StringComparison.OrdinalIgnoreCase))
                {
                    int stage;
                    if (int.TryParse(value, out stage))
                        config.Stage = Math.Max(1, Math.Min(3, stage));
                }
                else if (string.Equals(key, "unityVersion", StringComparison.OrdinalIgnoreCase))
                {
                    config.UnityVersion = value;
                }
            }
            return config;
        }
    }
}
