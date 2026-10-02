using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using HarmonyLib;
using Mirror;
using UnityEngine;
using System.Collections.Generic;

namespace EtatJoueurMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BasePlugin
    {
        // GUID arbitraire mais unique — à changer si tu as un autre mod avec
        // le même GUID, sinon BepInEx pourrait les confondre.
        public const string PluginGuid = "com.etatjoueur.mod";
        public const string PluginName = "EtatJoueurPlugin";
        public const string PluginVersion = "3.0.0";

        public static ManualLogSource Logger;

        private BotTestConsoleBehaviour _botTestConsole;

        private static ConfigFile _config;
        private static ConfigEntry<bool> _collectEnabled;
        private static ConfigEntry<bool> _repairEnabled;
        private static ConfigEntry<int> _repairPercent;
        private static ConfigEntry<bool> _fleeEnabled;
        private static ConfigEntry<int> _fleePercent;
        private static ConfigEntry<bool> _fleeCollectEnabled;
        private static ConfigEntry<bool> _repairPausesActivity;
        private static ConfigEntry<bool> _onlyFullHealthTargets;
        private static readonly Dictionary<string, ConfigEntry<bool>> CollectibleTypeSettings =
            new Dictionary<string, ConfigEntry<bool>>(StringComparer.Ordinal);
        private static Il2CppSystem.Action _targetCatalogDisconnectHandler;
        private const int BackgroundTargetFrameRate = 60;
        private static Action<bool> _focusChangedHandler;
        private static int _foregroundTargetFrameRate = -1;
        private static int _foregroundVSyncCount;
        private static bool _backgroundFrameRateApplied;

        public override void Load()
        {
            Logger = BepInEx.Logging.Logger.CreateLogSource(PluginName);

            try
            {
            _config = Config;
            Application.runInBackground = true;
                _foregroundTargetFrameRate = Application.targetFrameRate;
                _foregroundVSyncCount = QualitySettings.vSyncCount;
                _focusChangedHandler = OnApplicationFocusChanged;
                Application.focusChanged += _focusChangedHandler;
                OnApplicationFocusChanged(Application.isFocused);

                ResetSessionCatalogs();
                CollectibleCatalog.Initialize();
                InitializeCollectibleConfiguration();
                InitializeSurvivalConfiguration();
                _targetCatalogDisconnectHandler =
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(ResetSessionCatalogs));
                NetworkClient.OnDisconnectedEvent -= _targetCatalogDisconnectHandler;
                NetworkClient.OnDisconnectedEvent += _targetCatalogDisconnectHandler;

                var harmony = new Harmony(PluginGuid);
                Hooks.Appliquer(harmony);

                _botTestConsole = AddComponent<BotTestConsoleBehaviour>();
                Logger.LogInfo("[EtatJoueur] Hooks appliqués.");
            }
            catch (Exception e)
            {
                Logger.LogError($"[EtatJoueur] Erreur application des hooks : {e}");
            }
        }

        private static void ResetSessionCatalogs()
        {
            TargetCatalog.Reset();
            AmmoCatalog.Reset();
            CollectibleCatalog.Reset();
        }

        public static bool CollectEnabled
        {
            get { return _collectEnabled != null && _collectEnabled.Value; }
        }

        public static bool RepairEnabled
        {
            get { return _repairEnabled != null && _repairEnabled.Value; }
        }

        public static int RepairPercent
        {
            get { return _repairPercent == null ? 30 : _repairPercent.Value; }
        }

        public static bool FleeEnabled
        {
            get { return _fleeEnabled != null && _fleeEnabled.Value; }
        }

        public static int FleePercent
        {
            get { return _fleePercent == null ? 60 : _fleePercent.Value; }
        }

        public static bool FleeCollectEnabled
        {
            get { return _fleeCollectEnabled != null && _fleeCollectEnabled.Value; }
        }

        public static bool OnlyFullHealthTargets
        {
            get { return _onlyFullHealthTargets != null && _onlyFullHealthTargets.Value; }
        }

        public static void SetOnlyFullHealthTargets(bool enabled)
        {
            SetSetting(_onlyFullHealthTargets, enabled);
        }

        public static bool RepairPausesActivity
        {
            get { return _repairPausesActivity != null && _repairPausesActivity.Value; }
        }

        public static void SetRepairPausesActivity(bool enabled)
        {
            SetSetting(_repairPausesActivity, enabled);
        }

        public static void SetFleeCollectEnabled(bool enabled)
        {
            SetSetting(_fleeCollectEnabled, enabled);
        }

        public static void SetRepairEnabled(bool enabled)
        {
            SetSetting(_repairEnabled, enabled);
        }

        public static void SetRepairPercent(int percent)
        {
            SetSetting(_repairPercent, Mathf.Clamp(percent, 0, 100));
        }

        public static void SetFleeEnabled(bool enabled)
        {
            SetSetting(_fleeEnabled, enabled);
        }

        public static void SetFleePercent(int percent)
        {
            SetSetting(_fleePercent, Mathf.Clamp(percent, 0, 100));
        }

        private static void SetSetting<T>(ConfigEntry<T> setting, T value)
        {
            if (setting == null || EqualityComparer<T>.Default.Equals(setting.Value, value))
                return;
            setting.Value = value;
            _config.Save();
        }

        internal static void InitializeSurvivalConfiguration()
        {
            var percentRange = new AcceptableValueRange<int>(0, 100);
            _repairEnabled = _config.Bind("Survival", "RepairEnabled", false,
                "Trigger Repair() while HP <= RepairPercent (runs alongside the current activity).");
            _repairPercent = _config.Bind("Survival", "RepairPercent", 30,
                new ConfigDescription("Repair trigger threshold, in percent of max HP.", percentRange));
            _fleeEnabled = _config.Bind("Survival", "FleeEnabled", false,
                "While HP <= FleePercent, abandon Combat (and Collect unless FleeCollectEnabled) and navigate until HP recovers.");
            _fleeCollectEnabled = _config.Bind("Survival", "FleeCollectEnabled", false,
                "Allow collecting (never combat) while fleeing.");
            _onlyFullHealthTargets = _config.Bind("Combat", "OnlyFullHealthTargets", false,
                "Only engage NPCs / monsters that are at full HP. When false, damaged targets can be engaged too.");
            _repairPausesActivity = _config.Bind("Survival", "RepairPausesActivity", false,
                "Repair mode: false = repair during activity, true = stop all activity (ship still) and repair, resume at full HP.");
            _fleePercent = _config.Bind("Survival", "FleePercent", 60,
                new ConfigDescription("Flee trigger threshold, in percent of max HP.", percentRange));
        }

        public static bool IsCollectibleTypeEnabled(string typeName)
        {
            ConfigEntry<bool> setting;
            return !string.IsNullOrEmpty(typeName)
                && CollectibleTypeSettings.TryGetValue(typeName, out setting)
                && setting.Value;
        }

        public static void SetCollectEnabled(bool enabled)
        {
            if (_collectEnabled == null || _collectEnabled.Value == enabled)
                return;
            _collectEnabled.Value = enabled;
            _config.Save();
        }

        public static void SetCollectibleTypeEnabled(string typeName, bool enabled)
        {
            ConfigEntry<bool> setting;
            if (string.IsNullOrEmpty(typeName)
                || !CollectibleTypeSettings.TryGetValue(typeName, out setting)
                || setting.Value == enabled)
                return;

            setting.Value = enabled;
            _config.Save();
            Logger.LogInfo("[CollectConfig] " + typeName + " = " + (enabled ? "Enabled" : "Disabled"));
        }

        internal static void InitializeCollectibleConfiguration()
        {
            if (_collectEnabled == null)
                _collectEnabled = _config.Bind("Collect", "CollectEnabled", false, "Allow collectible collection.");

            foreach (Type type in CollectibleCatalog.CollectibleTypes)
            {
                if (CollectibleTypeSettings.ContainsKey(type.Name))
                    continue;

                CollectibleTypeSettings.Add(
                    type.Name,
                    _config.Bind("Collectible types", type.Name, false,
                        "Allow collecting runtime collectible type " + type.FullName + "."));
            }
        }

        private static void OnApplicationFocusChanged(bool focused)
        {
            if (!focused)
            {
                if (_backgroundFrameRateApplied)
                    return;

                _foregroundTargetFrameRate = Application.targetFrameRate;
                _foregroundVSyncCount = QualitySettings.vSyncCount;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = BackgroundTargetFrameRate;
                _backgroundFrameRateApplied = true;
                return;
            }

            if (!_backgroundFrameRateApplied)
                return;

            QualitySettings.vSyncCount = _foregroundVSyncCount;
            Application.targetFrameRate = _foregroundTargetFrameRate;
            _backgroundFrameRateApplied = false;
        }
    }
}
