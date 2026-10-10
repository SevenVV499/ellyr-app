using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using EtatJoueurMod;

namespace EllyrLoader
{
    /*
     * Réglages du bot dans un fichier texte (config/ellyr.cfg), au même format que les fichiers de BepInEx :
     *   [Section]
     *   ## description
     *   Clé = valeur
     * Au premier lancement, les valeurs de l'ancien fichier BepInEx (s'il existe) sont reprises.
     */
    internal sealed class IniSettingsStore : ISettingsStore
    {
        private abstract class EntryBase
        {
            public string Section;
            public string Key;
            public string Description;
            public string DefaultText;
            public abstract string ValueText { get; }
        }

        private sealed class Entry<T> : EntryBase, ISetting<T>
        {
            private T _value;
            private readonly int _minimum;
            private readonly int _maximum;
            private readonly bool _ranged;

            public Entry(T value, bool ranged, int minimum, int maximum)
            {
                _value = value;
                _ranged = ranged;
                _minimum = minimum;
                _maximum = maximum;
            }

            public T Value
            {
                get { return _value; }
                set { _value = Clamp(value); }
            }

            public T Clamp(T value)
            {
                if (_ranged && value is int)
                {
                    int clamped = Math.Max(_minimum, Math.Min(_maximum, (int)(object)value));
                    return (T)(object)clamped;
                }
                return value;
            }

            public override string ValueText
            {
                get { return ToText(_value); }
            }
        }

        private readonly string _path;
        private readonly Dictionary<string, Dictionary<string, string>> _loaded =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private readonly List<EntryBase> _entries = new List<EntryBase>();

        public IniSettingsStore(string path, string migrateFrom)
        {
            _path = path;
            if (File.Exists(path))
                Parse(path);
            else if (!string.IsNullOrEmpty(migrateFrom) && File.Exists(migrateFrom))
                Parse(migrateFrom);
        }

        public ISetting<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return Create(section, key, defaultValue, description, false, 0, 0);
        }

        public ISetting<int> BindRange(string section, string key, int defaultValue, string description, int minimum, int maximum)
        {
            return Create(section, key, defaultValue, description, true, minimum, maximum);
        }

        private Entry<T> Create<T>(string section, string key, T defaultValue, string description, bool ranged, int minimum, int maximum)
        {
            T value = defaultValue;
            string text;
            Dictionary<string, string> keys;
            if (_loaded.TryGetValue(section, out keys) && keys.TryGetValue(key, out text))
            {
                try
                {
                    value = (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(text);
                }
                catch (Exception)
                {
                    value = defaultValue;
                }
            }

            var entry = new Entry<T>(value, ranged, minimum, maximum);
            entry.Value = value;
            entry.Section = section;
            entry.Key = key;
            entry.Description = description;
            entry.DefaultText = ToText(defaultValue);
            _entries.Add(entry);
            return entry;
        }

        public void Save()
        {
            try
            {
                var builder = new StringBuilder();
                var written = new HashSet<string>(StringComparer.Ordinal);
                var sections = new List<string>();
                foreach (EntryBase entry in _entries)
                {
                    if (!sections.Contains(entry.Section))
                        sections.Add(entry.Section);
                }
                foreach (string section in _loaded.Keys)
                {
                    if (!sections.Contains(section))
                        sections.Add(section);
                }

                foreach (string section in sections)
                {
                    builder.Append('[').Append(section).AppendLine("]");
                    builder.AppendLine();
                    foreach (EntryBase entry in _entries)
                    {
                        if (entry.Section != section)
                            continue;
                        if (!string.IsNullOrEmpty(entry.Description))
                            builder.Append("## ").AppendLine(entry.Description);
                        builder.Append("# Default value: ").AppendLine(entry.DefaultText);
                        builder.Append(entry.Key).Append(" = ").AppendLine(entry.ValueText);
                        builder.AppendLine();
                        written.Add(section + "\u0001" + entry.Key);
                    }

                    // Valeurs lues dans le fichier mais pas (encore) déclarées : on les garde.
                    Dictionary<string, string> keys;
                    if (_loaded.TryGetValue(section, out keys))
                    {
                        foreach (KeyValuePair<string, string> pair in keys)
                        {
                            if (written.Contains(section + "\u0001" + pair.Key))
                                continue;
                            builder.Append(pair.Key).Append(" = ").AppendLine(pair.Value);
                            builder.AppendLine();
                        }
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, builder.ToString());
            }
            catch (Exception e)
            {
                LoaderLog.Error("Enregistrement des réglages impossible : " + e.Message);
            }
        }

        private void Parse(string path)
        {
            string section = string.Empty;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                Dictionary<string, string> keys;
                if (!_loaded.TryGetValue(section, out keys))
                {
                    keys = new Dictionary<string, string>(StringComparer.Ordinal);
                    _loaded[section] = keys;
                }
                keys[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }
        }

        private static string ToText<T>(T value)
        {
            if (value is bool)
                return ((bool)(object)value) ? "true" : "false";
            return TypeDescriptor.GetConverter(typeof(T)).ConvertToInvariantString(value) ?? string.Empty;
        }
    }
}
