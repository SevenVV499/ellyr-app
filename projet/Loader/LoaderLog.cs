using System;
using System.IO;

namespace EllyrLoader
{
    // Journal du chargeur et du bot : un seul fichier, logs/ellyr.log, une ligne par message.
    internal static class LoaderLog
    {
        private static readonly object Gate = new object();
        private static string _file;

        public static void Init(LoaderPaths paths)
        {
            try
            {
                Directory.CreateDirectory(paths.Logs);
                _file = Path.Combine(paths.Logs, "ellyr.log");
                File.WriteAllText(_file, string.Empty);
            }
            catch (Exception)
            {
                _file = null;
            }
        }

        public static void Info(string message) { Write("Info", message); }
        public static void Warning(string message) { Write("Warning", message); }
        public static void Error(string message) { Write("Error", message); }

        private static void Write(string level, string message)
        {
            string file = _file;
            if (file == null)
                return;

            try
            {
                lock (Gate)
                {
                    File.AppendAllText(
                        file,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level + "] " + message + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // Le journal ne doit jamais faire planter le jeu.
            }
        }
    }
}
