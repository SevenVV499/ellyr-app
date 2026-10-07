using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;

namespace EtatJoueurMod
{
    public static class CollectibleCatalog
    {
        private static readonly object Sync = new object();
        // volatile : lu à chaque image sans verrou ; les écritures restent sous verrou.
        private static volatile IReadOnlyList<Type> _collectibleTypes = Array.AsReadOnly(Array.Empty<Type>());
        private static HashSet<Type> _collectibleTypeSet = new HashSet<Type>();
        private static volatile bool _initialized;

        public static bool IsInitialized
        {
            get { return _initialized; }
        }

        public static IReadOnlyList<Type> CollectibleTypes
        {
            get { return _collectibleTypes; }
        }

        public static void Initialize()
        {
            if (_initialized)
                return;

            lock (Sync)
            {
                if (_initialized)
                    return;

                Type[] runtimeTypes;
                try
                {
                    runtimeTypes = typeof(Player).Assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    runtimeTypes = e.Types.Where(type => type != null).ToArray();
                    Plugin.Logger.LogWarning(
                        "[CollectCatalog] Partial runtime type scan: "
                        + e.LoaderExceptions.Length + " loader errors.");
                }

                Type[] found = runtimeTypes
                    .Where(IsCollectibleType)
                    .GroupBy(type => type.Name, StringComparer.Ordinal)
                    .Where(group => group.Count() == 1)
                    .Select(group => group.First())
                    .OrderBy(type => type.Name, StringComparer.Ordinal)
                    .ToArray();

                _collectibleTypes = Array.AsReadOnly(found);
                _collectibleTypeSet = new HashSet<Type>(found);
                _initialized = true;
            }
        }

        public static void Reset()
        {
            lock (Sync)
            {
                _collectibleTypes = Array.AsReadOnly(Array.Empty<Type>());
                _collectibleTypeSet = new HashSet<Type>();
                _initialized = false;
            }
        }

        public static bool Contains(Type type)
        {
            if (type == null)
                return false;
            lock (Sync)
                return _collectibleTypeSet.Contains(type);
        }

        private static bool IsCollectibleType(Type type)
        {
            if (type == null
                || type.IsAbstract
                || !typeof(NetworkBehaviour).IsAssignableFrom(type))
                return false;

            foreach (MethodInfo callback in type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (callback.Name != "TargetSandikDonus" || callback.ReturnType != typeof(void))
                    continue;

                ParameterInfo[] parameters = callback.GetParameters();
                if (parameters.Length == 5
                    && parameters[0].ParameterType == typeof(NetworkConnection)
                    && parameters[1].ParameterType == typeof(int)
                    && parameters[2].ParameterType == typeof(int)
                    && parameters[3].ParameterType == typeof(bool)
                    && parameters[4].ParameterType == typeof(int))
                    return true;
            }

            return false;
        }
    }
}
