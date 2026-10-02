using System;
using System.Collections.Generic;
using System.Reflection;
using EtatJoueurMod;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;

namespace EtatJoueurMod
{
    internal static class CollectibleRuntime
    {
        private static readonly MethodInfo Il2CppTypeOf = FindIl2CppTypeOf();
        private static readonly Dictionary<Type, Il2CppSystem.Type> Il2CppTypes =
            new Dictionary<Type, Il2CppSystem.Type>();
        private static readonly Dictionary<Type, PropertyInfo> PickupPointProperties =
            new Dictionary<Type, PropertyInfo>();

        internal static bool TryGetPickupPoint(
            NetworkIdentity identity,
            string collectibleType,
            out Vector3 pickupPoint)
        {
            pickupPoint = Vector3.zero;
            NetworkBehaviour behaviour = ResolveBehaviour(identity, collectibleType);
            if (behaviour == null)
                return false;

            Type behaviourType = behaviour.GetType();
            PropertyInfo positionProperty;
            if (!PickupPointProperties.TryGetValue(behaviourType, out positionProperty))
            {
                positionProperty = behaviourType.GetProperty(
                    "sandikPozisyon",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PickupPointProperties.Add(behaviourType, positionProperty);
            }

            if (positionProperty == null || !positionProperty.CanRead)
                return false;

            try
            {
                object positionValue = positionProperty.GetValue(behaviour, null);
                if (!(positionValue is Vector2 position))
                    return false;

                pickupPoint = new Vector3(
                    position.x,
                    position.y,
                    identity.transform.position.z);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(
                    "[Collect] Impossible de lire sandikPozisyon pour "
                    + collectibleType + " : " + e);
                return false;
            }
        }

        private static NetworkBehaviour ResolveBehaviour(
            NetworkIdentity identity,
            string collectibleType)
        {
            if (identity == null || string.IsNullOrEmpty(collectibleType))
                return null;

            Type managedType = typeof(Player).Assembly.GetType(collectibleType, false);
            if (managedType == null || !CollectibleCatalog.Contains(managedType))
                return null;

            try
            {
                Il2CppSystem.Type il2CppType;
                if (!Il2CppTypes.TryGetValue(managedType, out il2CppType))
                {
                    il2CppType = (Il2CppSystem.Type)Il2CppTypeOf
                        .MakeGenericMethod(managedType)
                        .Invoke(null, null);
                    Il2CppTypes.Add(managedType, il2CppType);
                }

                var componentObject = identity.gameObject.GetComponent(il2CppType);
                return componentObject == null
                    ? null
                    : componentObject.TryCast<NetworkBehaviour>();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(
                    "[Collect] Impossible de résoudre le composant "
                    + collectibleType + " du collectible : " + e);
                return null;
            }
        }

        private static MethodInfo FindIl2CppTypeOf()
        {
            foreach (MethodInfo method in typeof(Il2CppType).GetMethods(
                BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name == "Of"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 0)
                    return method;
            }

            throw new MissingMethodException(typeof(Il2CppType).FullName, "Of<T>()");
        }
    }
}
