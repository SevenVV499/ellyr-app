using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace EllyrLoader
{
    // Version d'Unity du jeu : Il2CppInterop en a besoin pour choisir ses structures internes.
    internal static class UnityVersionReader
    {
        private static readonly Regex InGameFiles = new Regex(@"\b(20\d\d)\.(\d+)\.(\d+)[abfp]\d+", RegexOptions.Compiled);
        private static readonly Regex Plain = new Regex(@"(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled);

        public static Version Read(LoaderPaths paths, LoaderConfig config)
        {
            Version version;
            if (!string.IsNullOrWhiteSpace(config.UnityVersion) && TryPlain(config.UnityVersion, out version))
                return version;

            string manager = Path.Combine(paths.GameDataDir, "globalgamemanagers");
            if (File.Exists(manager))
            {
                byte[] head = new byte[65536];
                int read;
                using (FileStream stream = File.OpenRead(manager))
                    read = stream.Read(head, 0, head.Length);
                Match match = InGameFiles.Match(Encoding.Latin1.GetString(head, 0, read));
                if (match.Success)
                    return new Version(
                        int.Parse(match.Groups[1].Value),
                        int.Parse(match.Groups[2].Value),
                        int.Parse(match.Groups[3].Value));
            }

            string player = Path.Combine(paths.GameRoot, "UnityPlayer.dll");
            if (File.Exists(player))
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(player);
                if (TryPlain(info.ProductVersion, out version) || TryPlain(info.FileVersion, out version))
                    return version;
            }

            throw new InvalidOperationException(
                "Version d'Unity introuvable. Ajoute une ligne unityVersion=2022.3.10 dans loader.ini.");
        }

        private static bool TryPlain(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(text))
                return false;

            Match match = Plain.Match(text);
            if (!match.Success)
                return false;

            version = new Version(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value));
            return true;
        }
    }
}
