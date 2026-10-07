using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;

namespace EtatJoueurMod
{
    public static class TargetCatalog
    {
        private static readonly object Gate = new object();
        private static readonly MethodInfo Il2CppTypeOf = FindIl2CppTypeOf();
        private static readonly MethodInfo Il2CppTryCast = FindIl2CppTryCast();
        private static ReadOnlyCollection<string> _monsters = Array.AsReadOnly(Array.Empty<string>());
        private static ReadOnlyCollection<string> _npcs = Array.AsReadOnly(Array.Empty<string>());
        private static List<TargetComponentDescriptor> _targetComponents;
        private static volatile bool _isInitialized;
        private static float _nextAttemptAt;

        private sealed class TargetComponentDescriptor
        {
            public Type ComponentType;
            public Il2CppSystem.Type Il2CppType;
            public TargetCategory Category;
            public FieldInfo NameField;
            public PropertyInfo NameProperty;
            public PropertyInfo NetworkNameProperty;
            public int PrefabMatches;
            public int NamedMatches;
        }

        public static IReadOnlyList<string> Monsters
        {
            get { lock (Gate) return _monsters; }
        }

        public static IReadOnlyList<string> Npcs
        {
            get { lock (Gate) return _npcs; }
        }

        public static bool IsInitialized
        {
            get { return _isInitialized; }
        }

        public static void Initialize()
        {
            if (_isInitialized)
                return;

            lock (Gate)
            {
                if (_isInitialized || !NetworkClient.active || !NetworkClient.ready)
                    return;

                float now = Time.realtimeSinceStartup;
                if (now < _nextAttemptAt)
                    return;

                _nextAttemptAt = now + 2f;

                var rawMonsters = new List<string>();
                var rawNpcs = new List<string>();
                List<TargetComponentDescriptor> targetComponents = GetTargetComponents();
                foreach (TargetComponentDescriptor descriptor in targetComponents)
                {
                    descriptor.PrefabMatches = 0;
                    descriptor.NamedMatches = 0;
                }
                var monsterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var npcKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int unnamedTargetCount = 0;
                int inspectionErrorCount = 0;
                int clientPrefabCount = 0;
                int managerPrefabCount = 0;

                var clientPrefabs = NetworkClient.prefabs;
                if (clientPrefabs != null)
                {
                    clientPrefabCount = clientPrefabs.Count;
                    try
                    {
                        foreach (var pair in clientPrefabs)
                        {
                            InspectPrefab(
                                pair.Value,
                                rawMonsters,
                                monsterKeys,
                                rawNpcs,
                                npcKeys,
                                targetComponents,
                                ref unnamedTargetCount,
                                ref inspectionErrorCount);
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.Logger.LogError(
                            $"[TargetCatalog] Échec de l'énumération de NetworkClient.prefabs : {e}");
                        inspectionErrorCount++;
                    }
                }

                try
                {
                    NetworkManager manager = NetworkManager.singleton;
                    var spawnPrefabs = manager == null ? null : manager.spawnPrefabs;
                    if (spawnPrefabs != null)
                    {
                        managerPrefabCount = spawnPrefabs.Count;
                        for (int i = 0; i < spawnPrefabs.Count; i++)
                        {
                            InspectPrefab(
                                spawnPrefabs[i],
                                rawMonsters,
                                monsterKeys,
                                rawNpcs,
                                npcKeys,
                                targetComponents,
                                ref unnamedTargetCount,
                                ref inspectionErrorCount);
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError(
                        $"[TargetCatalog] Échec de l'énumération de NetworkManager.spawnPrefabs : {e}");
                    inspectionErrorCount++;
                }

                if (rawMonsters.Count == 0 && rawNpcs.Count == 0)
                {
                    Plugin.Logger.LogWarning(
                        $"[TargetCatalog] Catalogue non validé : aucun nom de cible lisible " +
                        $"(prefabs client={clientPrefabCount}, spawnPrefabs={managerPrefabCount}). " +
                        "Nouvel essai différé.");
                    return;
                }

                rawMonsters.Sort(StringComparer.OrdinalIgnoreCase);
                rawNpcs.Sort(StringComparer.OrdinalIgnoreCase);

                var monsters = Canonicalize(rawMonsters);
                var npcs = Canonicalize(rawNpcs);

                _monsters = monsters.AsReadOnly();
                _npcs = npcs.AsReadOnly();
                _isInitialized = true;

                foreach (TargetComponentDescriptor descriptor in targetComponents)
                {
                    if (descriptor.Category == TargetCategory.Monster)
                    {
                    }
                }
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                _monsters = Array.AsReadOnly(Array.Empty<string>());
                _npcs = Array.AsReadOnly(Array.Empty<string>());
                _nextAttemptAt = 0f;
                _isInitialized = false;
            }
        }

        public static bool ContainsMonster(string name)
        {
            return Contains(_monsters, name);
        }

        public static bool ContainsNpc(string name)
        {
            return Contains(_npcs, name);
        }

        public static bool TryGetCategory(string name, out TargetCategory category)
        {
            bool isMonster = ContainsMonster(name);
            bool isNpc = ContainsNpc(name);
            if (isMonster == isNpc)
            {
                category = default(TargetCategory);
                return false;
            }

            category = isMonster ? TargetCategory.Monster : TargetCategory.Npc;
            return true;
        }

        public static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string normalized = name.Trim().Normalize(NormalizationForm.FormC);
            if (string.Equals(normalized, "Valacto", StringComparison.OrdinalIgnoreCase))
                return "Valocto";

            return normalized;
        }

        private static bool Contains(IReadOnlyList<string> names, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            string normalizedCandidate = NormalizeName(candidate);
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(
                    NormalizeName(names[i]),
                    normalizedCandidate,
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static List<string> Canonicalize(List<string> rawNames)
        {
            var names = new List<string>();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawName in rawNames)
            {
                string canonicalName = NormalizeName(rawName);
                string key = canonicalName.Normalize(NormalizationForm.FormC);
                if (keys.Add(key))
                    names.Add(canonicalName);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static void InspectPrefab(
            GameObject prefab,
            List<string> monsters,
            HashSet<string> monsterKeys,
            List<string> npcs,
            HashSet<string> npcKeys,
            List<TargetComponentDescriptor> targetComponents,
            ref int unnamedTargetCount,
            ref int inspectionErrorCount)
        {
            if (prefab == null)
                return;

            foreach (TargetComponentDescriptor descriptor in targetComponents)
            {
                try
                {
                    var componentObject = prefab.GetComponent(descriptor.Il2CppType);
                    if (componentObject == null)
                        continue;

                    descriptor.PrefabMatches++;
                    object typedComponent = Il2CppTryCast
                        .MakeGenericMethod(descriptor.ComponentType)
                        .Invoke(componentObject, null);
                    if (typedComponent == null)
                        continue;

                    string name = ReadName(descriptor, typedComponent);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        unnamedTargetCount++;
                        continue;
                    }

                    descriptor.NamedMatches++;
                    if (descriptor.Category == TargetCategory.Monster)
                        AddName(name, monsters, monsterKeys);
                    else
                        AddName(name, npcs, npcKeys);
                }
                catch (Exception e)
                {
                    inspectionErrorCount++;
                    Plugin.Logger.LogWarning(
                        $"[TargetCatalog] Prefab '{prefab.name}', composant " +
                        $"{descriptor.ComponentType.Name} ignoré : {e.GetBaseException().Message}");
                }
            }
        }

        private static MethodInfo FindIl2CppTypeOf()
        {
            foreach (MethodInfo method in typeof(Il2CppType).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name == "Of"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 0)
                    return method;
            }

            throw new MissingMethodException(typeof(Il2CppType).FullName, "Of<T>()");
        }

        private static MethodInfo FindIl2CppTryCast()
        {
            foreach (MethodInfo method in typeof(NetworkBehaviour).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name == "TryCast"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 0)
                    return method;
            }

            throw new MissingMethodException(typeof(NetworkBehaviour).FullName, "TryCast<T>()");
        }

        private static List<TargetComponentDescriptor> GetTargetComponents()
        {
            if (_targetComponents != null)
                return _targetComponents;

            var targetComponents = new List<TargetComponentDescriptor>();
            Type[] types;
            try
            {
                types = typeof(AllMonsters).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
                Plugin.Logger.LogWarning(
                    $"[TargetCatalog] Certains types du jeu sont inaccessibles pendant la découverte : {e.Message}");
            }

            foreach (Type type in types)
            {
                if (type == null || type.IsAbstract || !typeof(NetworkBehaviour).IsAssignableFrom(type))
                    continue;

                FieldInfo geminame = FindInstanceField(type, "geminame");
                FieldInfo npcName = FindInstanceField(type, "NpcName");
                PropertyInfo geminameProperty = FindInstanceProperty(type, "geminame");
                PropertyInfo npcNameProperty = FindInstanceProperty(type, "NpcName");
                PropertyInfo networkName = FindInstanceProperty(type, "Networkgeminame");

                bool hasGeminame = (geminame != null && geminame.FieldType == typeof(string))
                    || (geminameProperty != null && geminameProperty.PropertyType == typeof(string));
                bool hasNpcName = (npcName != null && npcName.FieldType == typeof(string))
                    || (npcNameProperty != null && npcNameProperty.PropertyType == typeof(string));
                bool hasNetworkName = networkName != null
                    && networkName.PropertyType == typeof(string)
                    && networkName.GetIndexParameters().Length == 0;

                if (!hasGeminame && !hasNpcName && !hasNetworkName)
                    continue;

                bool isMonster = typeof(AllMonsters).IsAssignableFrom(type)
                    || type.Name.IndexOf("Monster", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isMonster && !hasGeminame && !hasNetworkName)
                    continue;

                try
                {
                    targetComponents.Add(new TargetComponentDescriptor
                    {
                        ComponentType = type,
                        Il2CppType = (Il2CppSystem.Type)Il2CppTypeOf.MakeGenericMethod(type).Invoke(null, null),
                        Category = isMonster ? TargetCategory.Monster : TargetCategory.Npc,
                        NameField = npcName != null ? npcName : geminame,
                        NameProperty = npcNameProperty != null ? npcNameProperty : geminameProperty,
                        NetworkNameProperty = hasNetworkName ? networkName : null
                    });
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning(
                        $"[TargetCatalog] Type cible {type.FullName} ignoré : {e.GetBaseException().Message}");
                }
            }

            _targetComponents = targetComponents;
            return _targetComponents;
        }

        private static FieldInfo FindInstanceField(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, flags);
                if (field != null)
                    return field;
            }

            return null;
        }

        private static PropertyInfo FindInstanceProperty(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, flags);
                if (property != null)
                    return property;
            }

            return null;
        }

        private static string ReadName(TargetComponentDescriptor descriptor, object component)
        {
            if (descriptor.NameField != null)
            {
                string name = descriptor.NameField.GetValue(component) as string;
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            if (descriptor.NameProperty != null)
            {
                string name = descriptor.NameProperty.GetValue(component, null) as string;
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            if (descriptor.NetworkNameProperty != null)
                return descriptor.NetworkNameProperty.GetValue(component, null) as string;

            return null;
        }

        private static void AddName(string name, List<string> names, HashSet<string> keys)
        {
            string key = name.Trim().Normalize(NormalizationForm.FormC);
            if (keys.Add(key))
                names.Add(name);
        }
    }
}
