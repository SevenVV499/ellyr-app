using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace EtatJoueurMod
{
    public enum AmmoType
    {
        Bullet,
        Harpoon
    }

    public sealed class AmmoDefinition
    {
        internal AmmoDefinition(
            AmmoType type,
            int id,
            string internalName,
            string localizationKey)
        {
            Type = type;
            Id = id;
            InternalName = internalName;
            LocalizationKey = localizationKey;
        }

        public AmmoType Type { get; }
        public int Id { get; }
        public string InternalName { get; }
        public string LocalizationKey { get; }
    }

    public static class AmmoCatalog
    {
        private static readonly object Gate = new object();
        private static ReadOnlyCollection<AmmoDefinition> _bullets =
            Array.AsReadOnly(Array.Empty<AmmoDefinition>());
        private static ReadOnlyCollection<AmmoDefinition> _harpoons =
            Array.AsReadOnly(Array.Empty<AmmoDefinition>());
        private static volatile bool _isInitialized;
        private static float _nextAttemptAt;
        private static bool _warnedLocalizationRead;

        public static IReadOnlyList<AmmoDefinition> Bullets
        {
            get { lock (Gate) return _bullets; }
        }

        public static IReadOnlyList<AmmoDefinition> Harpoons
        {
            get { lock (Gate) return _harpoons; }
        }

        public static void Initialize(Player player)
        {
            lock (Gate)
            {
                if (_isInitialized || player == null || !player.isLocalPlayer
                    || !NetworkClient.active || !NetworkClient.ready)
                    return;

                float now = Time.realtimeSinceStartup;
                if (now < _nextAttemptAt)
                    return;

                if (GetLocalPlayerIdentity() == null)
                    return;

                _nextAttemptAt = now + 10f;

                InspectAmmoSelectionButtons(
                    out List<AmmoDefinition> bullets,
                    out List<AmmoDefinition> harpoons);

                if (bullets.Count == 0 || harpoons.Count == 0)
                {
                    Plugin.Logger.LogWarning(
                        $"[AmmoCatalog] Scan incomplet : {bullets.Count} IDs de boulets, " +
                        $"{harpoons.Count} IDs de harpons; nouvelle tentative dans 10 secondes.");
                    return;
                }

                Dictionary<string, List<string>> localizationKeys = ReadLocalizationKeys();
                bullets = AttachLocalizationKeys(bullets, localizationKeys);
                harpoons = AttachLocalizationKeys(harpoons, localizationKeys);
                bullets.Sort(CompareDefinitions);
                harpoons.Sort(CompareDefinitions);

                _bullets = bullets.AsReadOnly();
                _harpoons = harpoons.AsReadOnly();
                _isInitialized = true;
                LogDefinitions(_bullets);
                LogDefinitions(_harpoons);
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                _bullets = Array.AsReadOnly(Array.Empty<AmmoDefinition>());
                _harpoons = Array.AsReadOnly(Array.Empty<AmmoDefinition>());
                _nextAttemptAt = 0f;
                _warnedLocalizationRead = false;
                _isInitialized = false;
            }
        }

        private static NetworkIdentity GetLocalPlayerIdentity()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo field = typeof(NetworkClient).GetField("localPlayer", flags);
            if (field != null)
                return field.GetValue(null) as NetworkIdentity;

            PropertyInfo property = typeof(NetworkClient).GetProperty("localPlayer", flags);
            return property == null ? null : property.GetValue(null, null) as NetworkIdentity;
        }

        private static PropertyInfo FindProperty(Type type, string name, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property;

                foreach (PropertyInfo candidate in current.GetProperties(flags | BindingFlags.DeclaredOnly))
                {
                    if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }

            return null;
        }

        private static bool TryGetArrayAccessor(
            object source,
            out int count,
            out PropertyInfo itemProperty)
        {
            count = 0;
            itemProperty = null;
            Type type = source.GetType();

            if (source is Array array)
            {
                count = array.Length;
                return true;
            }

            PropertyInfo lengthProperty = type.GetProperty(
                "Length",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (lengthProperty != null && lengthProperty.PropertyType == typeof(int))
                count = (int)lengthProperty.GetValue(source, null);
            else
            {
                PropertyInfo countProperty = type.GetProperty(
                    "Count",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (countProperty == null || countProperty.PropertyType != typeof(int))
                    return false;

                count = (int)countProperty.GetValue(source, null);
            }

            itemProperty = type.GetProperty(
                "Item",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return itemProperty != null && itemProperty.GetIndexParameters().Length == 1;
        }

        private static Dictionary<string, List<string>> ReadLocalizationKeys()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Type managerType = typeof(LanguagesManager);
                const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                PropertyInfo instanceProperty = managerType.GetProperty("Instance", staticFlags);
                object manager = instanceProperty == null ? null : instanceProperty.GetValue(null, null);
                if (manager == null)
                {
                    WarnLocalizationRead("LanguagesManager.Instance est nul ou inaccessible");
                    return result;
                }

                object texts = GetMemberValue(manager, "texts");
                if (texts == null)
                {
                    WarnLocalizationRead("dictionnaire LanguagesManager.texts introuvable");
                    return result;
                }

                List<object> entries = ReadCollectionEntries(texts);
                if (entries.Count == 0)
                {
                    WarnLocalizationRead(
                        $"dictionnaire de type {texts.GetType().FullName} vide ou non énumérable");
                    return result;
                }

                foreach (object entry in entries)
                {
                    if (entry == null)
                        continue;

                    string key = GetMemberValue(entry, "Key") as string;
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    string normalizedKey = NormalizeKey(key);
                    if (!result.TryGetValue(normalizedKey, out List<string> keys))
                    {
                        keys = new List<string>();
                        result.Add(normalizedKey, keys);
                    }

                    keys.Add(key);
                }

                if (result.Count == 0)
                    WarnLocalizationRead("aucune clé exploitable dans LanguagesManager.texts");
            }
            catch (Exception e)
            {
                WarnLocalizationRead(
                    $"échec de lecture des clés : {e.GetBaseException().Message}");
            }

            return result;
        }

        private static List<object> ReadCollectionEntries(object collection)
        {
            var entries = new List<object>();
            IEnumerable enumerable = collection as IEnumerable;
            if (enumerable != null)
            {
                foreach (object entry in enumerable)
                    entries.Add(entry);
                return entries;
            }

            MethodInfo getEnumerator = collection.GetType().GetMethod(
                "GetEnumerator",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            object enumerator = getEnumerator == null ? null : getEnumerator.Invoke(collection, null);
            if (enumerator == null)
                return entries;

            MethodInfo moveNext = enumerator.GetType().GetMethod("MoveNext");
            if (moveNext == null)
                return entries;

            while ((bool)moveNext.Invoke(enumerator, null))
                entries.Add(GetMemberValue(enumerator, "Current"));

            return entries;
        }

        private static void WarnLocalizationRead(string reason)
        {
            if (_warnedLocalizationRead)
                return;

            Plugin.Logger.LogWarning($"[AmmoCatalog] Clés de localisation indisponibles : {reason}.");
            _warnedLocalizationRead = true;
        }

        private static FieldInfo FindField(Type type, string name, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }

            return null;
        }

        private static List<AmmoDefinition> AttachLocalizationKeys(
            List<AmmoDefinition> definitions,
            Dictionary<string, List<string>> localizationKeys)
        {
            var resolved = new List<AmmoDefinition>(definitions.Count);
            foreach (AmmoDefinition definition in definitions)
            {
                string normalizedName = NormalizeKey(definition.InternalName);
                string localizationKey = null;
                var titleMatches = new HashSet<string>(StringComparer.Ordinal);
                var relatedKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, List<string>> pair in localizationKeys)
                {
                    if (!pair.Key.StartsWith(normalizedName, StringComparison.Ordinal))
                        continue;

                    foreach (string key in pair.Value)
                        relatedKeys.Add(key);

                    bool titleShaped = pair.Key.EndsWith("baslik", StringComparison.Ordinal)
                        || pair.Key.EndsWith("basll", StringComparison.Ordinal);
                    if (!titleShaped
                        || pair.Key.IndexOf("adet", StringComparison.Ordinal) >= 0
                        || pair.Key.IndexOf("hasar", StringComparison.Ordinal) >= 0)
                        continue;

                    foreach (string key in pair.Value)
                        titleMatches.Add(key);
                }

                var exactMatches = new HashSet<string>(StringComparer.Ordinal);
                string[] exactTitleKeys =
                {
                    normalizedName + "baslik",
                    normalizedName + "basll",
                    normalizedName + "gullebaslik",
                    normalizedName + "gullebasll"
                };
                foreach (string exactTitleKey in exactTitleKeys)
                {
                    if (localizationKeys.TryGetValue(exactTitleKey, out List<string> keys))
                    {
                        foreach (string key in keys)
                            exactMatches.Add(key);
                    }
                }

                HashSet<string> unambiguousMatches = exactMatches.Count > 0
                    ? exactMatches
                    : titleMatches;
                if (unambiguousMatches.Count == 1)
                {
                    foreach (string match in unambiguousMatches)
                    {
                        localizationKey = match;
                        break;
                    }
                }
                else if (relatedKeys.Count > 0)
                {
                    var candidates = new List<string>(relatedKeys);
                    candidates.Sort(StringComparer.Ordinal);
                }

                resolved.Add(new AmmoDefinition(
                    definition.Type,
                    definition.Id,
                    definition.InternalName,
                    localizationKey));
            }

            return resolved;
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string decomposed = value.Normalize(NormalizationForm.FormD);
            var characters = new char[decomposed.Length];
            int length = 0;
            foreach (char character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    continue;

                if (character == '\u0131')
                {
                    characters[length++] = 'i';
                    continue;
                }

                if (char.IsLetterOrDigit(character))
                    characters[length++] = char.ToLowerInvariant(character);
            }

            return new string(characters, 0, length);
        }

        private static int CompareDefinitions(AmmoDefinition left, AmmoDefinition right)
        {
            return left.Id.CompareTo(right.Id);
        }

        private static void LogDefinitions(IReadOnlyList<AmmoDefinition> definitions)
        {
            foreach (AmmoDefinition definition in definitions)
            {
            }
        }

        private static void InspectAmmoSelectionButtons(
            out List<AmmoDefinition> bullets,
            out List<AmmoDefinition> harpoons)
        {
            bullets = new List<AmmoDefinition>();
            harpoons = new List<AmmoDefinition>();
            try
            {
                MethodInfo findObjects = typeof(Resources).GetMethod(
                    "FindObjectsOfTypeAll",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Type) },
                    null);
                object buttons;
                if (findObjects != null)
                {
                    buttons = findObjects.Invoke(null, new object[] { typeof(Button) });
                }
                else
                {
                    findObjects = null;
                    foreach (MethodInfo candidate in typeof(Resources).GetMethods(
                        BindingFlags.Public | BindingFlags.Static))
                    {
                        if (candidate.Name == "FindObjectsOfTypeAll"
                            && candidate.IsGenericMethodDefinition
                            && candidate.GetParameters().Length == 0)
                        {
                            findObjects = candidate.MakeGenericMethod(typeof(Button));
                            break;
                        }
                    }

                    if (findObjects == null)
                    {
                        Plugin.Logger.LogWarning(
                            "[AmmoCatalog] Surcharge de Resources.FindObjectsOfTypeAll introuvable; scan UI ignoré.");
                        return;
                    }

                    buttons = findObjects.Invoke(null, null);
                }
                if (!TryGetArrayAccessor(buttons, out int count, out PropertyInfo itemProperty))
                {
                    Plugin.Logger.LogWarning(
                        "[AmmoCatalog] Liste des boutons Unity non énumérable; scan UI ignoré.");
                    return;
                }

                int matchingListeners = 0;
                var bulletNames = new Dictionary<int, Dictionary<string, HashSet<string>>>();
                var harpoonNames = new Dictionary<int, Dictionary<string, HashSet<string>>>();
                for (int index = 0; index < count; index++)
                {
                    try
                    {
                        object buttonObject = buttons is Array array
                            ? array.GetValue(index)
                            : itemProperty.GetValue(buttons, new object[] { index });
                        Button button = buttonObject as Button;
                        if (button == null)
                            continue;

                        PropertyInfo onClickProperty = button.GetType().GetProperty(
                            "onClick",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        object onClick = onClickProperty == null ? null : onClickProperty.GetValue(button, null);
                        if (onClick == null)
                            continue;

                        MethodInfo getCount = onClick.GetType().GetMethod("GetPersistentEventCount");
                        MethodInfo getName = onClick.GetType().GetMethod("GetPersistentMethodName");
                        if (getCount == null || getName == null)
                            continue;

                        int listenerCount = (int)getCount.Invoke(onClick, null);
                        for (int listener = 0; listener < listenerCount; listener++)
                        {
                            string methodName = getName.Invoke(onClick, new object[] { listener }) as string;
                            if (!IsAmmoSelectionMethod(methodName))
                                continue;

                            if (button.gameObject == null || !button.gameObject.scene.IsValid())
                                continue;

                            if (!TryGetPersistentIntArgument(onClick, listener, out int id))
                                continue;
                            if (id < 0)
                                continue;

                            if (!TryGetUiAmmoIdentity(button.transform, methodName, out AmmoType type, out string name))
                                continue;

                            matchingListeners++;
                            AddNameCandidate(
                                type == AmmoType.Bullet ? bulletNames : harpoonNames,
                                id,
                                name,
                                GetHierarchyPath(button.transform));
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.Logger.LogWarning(
                            $"[AmmoCatalog] Bouton UI {index} ignoré : {e.GetBaseException().Message}");
                    }
                }

                bullets = ResolveNameCandidates(AmmoType.Bullet, bulletNames);
                harpoons = ResolveNameCandidates(AmmoType.Harpoon, harpoonNames);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"[AmmoCatalog] Échec du scan des boutons UI : {e}");
            }
        }

        private static bool TryGetUiAmmoIdentity(
            Transform button,
            string methodName,
            out AmmoType type,
            out string internalName)
        {
            type = default(AmmoType);
            internalName = null;

            if (string.Equals(methodName, "GulleDegistir", StringComparison.Ordinal))
            {
                if (!HasAncestor(button, "GulleSecmeePanel")
                    || !TryGetChildAfterAncestor(button, "Gulleler", out internalName))
                    return false;

                type = AmmoType.Bullet;
                return !string.IsNullOrWhiteSpace(internalName);
            }

            if (!string.Equals(methodName, "ZipkinDegistir", StringComparison.Ordinal)
                || !HasAncestorContaining(button, "YanPanelMobil"))
                return false;

            for (Transform current = button; current != null; current = current.parent)
            {
                string parentName = current.gameObject == null ? current.name : current.gameObject.name;
                int suffix = parentName.IndexOf("ArkaPlan", StringComparison.OrdinalIgnoreCase);
                if (suffix <= 0)
                    continue;

                string harpoonName = StripUnityCloneSuffix(parentName.Substring(0, suffix).Trim());
                if (harpoonName.Length == 0)
                    continue;

                type = AmmoType.Harpoon;
                internalName = harpoonName + "Zipkin";
                return true;
            }

            return false;
        }

        private static bool HasAncestor(Transform transform, string ancestorName)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                string name = current.gameObject == null ? current.name : current.gameObject.name;
                if (string.Equals(
                    StripUnityCloneSuffix(name),
                    ancestorName,
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool HasAncestorContaining(Transform transform, string namePart)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                string name = current.gameObject == null ? current.name : current.gameObject.name;
                if (name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool TryGetChildAfterAncestor(
            Transform transform,
            string ancestorName,
            out string childName)
        {
            childName = null;
            Transform child = transform;
            for (Transform current = transform.parent; current != null; current = current.parent)
            {
                string name = current.gameObject == null ? current.name : current.gameObject.name;
                if (string.Equals(
                    StripUnityCloneSuffix(name),
                    ancestorName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    childName = StripUnityCloneSuffix(
                        child.gameObject == null ? child.name : child.gameObject.name);
                    return true;
                }

                child = current;
            }

            return false;
        }

        private static string StripUnityCloneSuffix(string value)
        {
            int open = value.LastIndexOf(" (", StringComparison.Ordinal);
            if (open < 0 || !value.EndsWith(")", StringComparison.Ordinal))
                return value;

            string suffix = value.Substring(open + 2, value.Length - open - 3);
            if (string.Equals(suffix, "Clone", StringComparison.Ordinal))
                return value.Substring(0, open);

            const string numberedClonePrefix = "Clone ";
            if (!suffix.StartsWith(numberedClonePrefix, StringComparison.Ordinal)
                || suffix.Length == numberedClonePrefix.Length)
                return value;

            for (int i = numberedClonePrefix.Length; i < suffix.Length; i++)
            {
                if (!char.IsDigit(suffix[i]))
                    return value;
            }

            return value.Substring(0, open);
        }

        private static void AddNameCandidate(
            Dictionary<int, Dictionary<string, HashSet<string>>> candidates,
            int id,
            string name,
            string hierarchyPath)
        {
            if (!candidates.TryGetValue(id, out Dictionary<string, HashSet<string>> names))
            {
                names = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                candidates.Add(id, names);
            }

            if (!names.TryGetValue(name, out HashSet<string> paths))
            {
                paths = new HashSet<string>(StringComparer.Ordinal);
                names.Add(name, paths);
            }

            paths.Add(hierarchyPath);
        }

        private static List<AmmoDefinition> ResolveNameCandidates(
            AmmoType type,
            Dictionary<int, Dictionary<string, HashSet<string>>> candidates)
        {
            var definitions = new List<AmmoDefinition>();
            foreach (KeyValuePair<int, Dictionary<string, HashSet<string>>> idCandidates in candidates)
            {
                string selectedName = null;
                foreach (KeyValuePair<string, HashSet<string>> candidate in idCandidates.Value)
                {
                    if (selectedName != null)
                        break;

                    selectedName = candidate.Key;
                }

                if (selectedName == null || idCandidates.Value.Count != 1)
                {
                    if (type == AmmoType.Bullet
                        && TryResolveNativeBulletIdentity(
                            idCandidates.Key,
                            out string nativeName,
                            out string playerAmmoField)
                        && idCandidates.Value.ContainsKey(nativeName))
                    {
                        definitions.Add(new AmmoDefinition(
                            type,
                            idCandidates.Key,
                            nativeName,
                            null));
                        continue;
                    }

                    Plugin.Logger.LogWarning(
                        $"[AmmoCatalog] ID {idCandidates.Key} ({type}) ignoré : " +
                        $"noms UI concurrents ({FormatNameCandidates(idCandidates.Value)}).");
                    continue;
                }

                definitions.Add(new AmmoDefinition(type, idCandidates.Key, selectedName, null));
            }

            definitions.Sort(CompareDefinitions);
            return definitions;
        }

        private static bool TryResolveNativeBulletIdentity(
            int id,
            out string internalName,
            out string playerAmmoField)
        {
            switch (id)
            {
                case 5:
                    internalName = "Sarapnel";
                    playerAmmoField = "oyuncuSarapnelGulle";
                    return true;
                case 7:
                    internalName = "Buzgulle";
                    playerAmmoField = "oyuncuBuzGulle";
                    return true;
                case 8:
                    internalName = "KalpKiriciGulle";
                    playerAmmoField = "oyuncuKalpKiriciGulle";
                    return true;
                default:
                    internalName = null;
                    playerAmmoField = null;
                    return false;
            }
        }

        private static string FormatNameCandidates(
            Dictionary<string, HashSet<string>> candidates)
        {
            var descriptions = new List<string>();
            foreach (KeyValuePair<string, HashSet<string>> candidate in candidates)
            {
                var paths = new List<string>(candidate.Value);
                paths.Sort(StringComparer.Ordinal);
                descriptions.Add(
                    $"\"{candidate.Key}\" ({candidate.Value.Count} chemins: {string.Join(" | ", paths)})");
            }

            return string.Join(", ", descriptions);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new List<string>();
            for (Transform current = transform; current != null; current = current.parent)
            {
                string name = current.gameObject == null ? current.name : current.gameObject.name;
                names.Add(StripUnityCloneSuffix(name));
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static bool IsAmmoSelectionMethod(string methodName)
        {
            return string.Equals(methodName, "GulleDegistir", StringComparison.Ordinal)
                || string.Equals(methodName, "ZipkinDegistir", StringComparison.Ordinal);
        }

        private static bool TryGetPersistentIntArgument(object unityEvent, int listenerIndex, out int value)
        {
            value = 0;
            object persistentCalls = GetMemberValue(unityEvent, "m_PersistentCalls");
            object calls = persistentCalls == null ? null : GetMemberValue(persistentCalls, "m_Calls");
            if (calls == null || !TryGetArrayAccessor(calls, out int count, out PropertyInfo itemProperty)
                || listenerIndex < 0 || listenerIndex >= count)
                return false;

            object call = calls is Array array
                ? array.GetValue(listenerIndex)
                : itemProperty.GetValue(calls, new object[] { listenerIndex });
            if (call == null)
                return false;

            object arguments = GetMemberValue(call, "m_Arguments");
            object intArgument = arguments == null ? null : GetMemberValue(arguments, "m_IntArgument");
            if (!(intArgument is int))
                return false;

            value = (int)intArgument;
            return true;
        }

        private static object GetMemberValue(object instance, string name)
        {
            if (instance == null)
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();
            PropertyInfo property = FindProperty(type, name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(instance, null);

            FieldInfo field = FindField(type, name, flags);
            return field == null ? null : field.GetValue(instance);
        }

    }
}
