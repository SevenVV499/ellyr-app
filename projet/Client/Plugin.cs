using System;
using Il2CppInterop.Runtime;
using HarmonyLib;
using Mirror;
using UnityEngine;
using System.Collections.Generic;

namespace EtatJoueurMod
{
    // Cœur du bot côté client : réglages, démarrage, hooks. Aucune dépendance à BepInEx : l'hôte qui charge le bot
    // (voir Client/Host) appelle Start et fournit le journal, les réglages et la création du composant Unity.
    public static class Plugin
    {
        // GUID arbitraire mais unique, utilisé aussi comme nom du fichier de config par l'hôte BepInEx.
        public const string PluginGuid = "com.etatjoueur.mod";
        public const string PluginName = "EtatJoueurPlugin";
        public const string PluginVersion = "3.0.0";

        public static readonly HostLog Logger = new HostLog();

        private static BotTestConsoleBehaviour _botTestConsole;

        private static ISettingsStore _config;
        private static ISetting<bool> _collectEnabled;
        private static ISetting<bool> _repairEnabled;
        private static ISetting<int> _repairPercent;
        private static ISetting<bool> _fleeEnabled;
        private static ISetting<int> _fleePercent;
        private static ISetting<bool> _fleeCollectEnabled;
        private static ISetting<bool> _repairPausesActivity;
        private static ISetting<bool> _raidEnabled;
        private static ISetting<bool> _raidBossPriority;
        private static ISetting<bool> _respawnEnabled;
        private static ISetting<string> _themeId;
        private static ISetting<string> _language;
        private static ISetting<bool> _onlyFullHealthTargets;
        private static readonly Dictionary<string, ISetting<bool>> CollectibleTypeSettings =
            new Dictionary<string, ISetting<bool>>(StringComparer.Ordinal);
        private static Il2CppSystem.Action _targetCatalogDisconnectHandler;
        private const int DefaultBackgroundFps = 20;
        private const int MinimumBackgroundFps = 20;
        private const int MaximumBackgroundFps = 60;
        private static ISetting<int> _backgroundFps;
        private static Action<bool> _focusChangedHandler;
        private static int _foregroundTargetFrameRate = -1;
        private static int _foregroundVSyncCount;
        private static bool _backgroundFrameRateApplied;

        public static void Start(IHost host)
        {
            Logger.Attach(host.Log);

            try
            {
            _config = host.Settings;
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
                BrainContext.Settings = new PluginBrainSettings();
                BrainContext.Log = new PluginBrainLog();
                BrainContext.Services = new ClientPlannerServices();
                BrainContext.RaidActions = new ClientRaidActions();
                BrainContext.SurvivalActions = new ClientSurvivalActions();
                BrainContext.RespawnActions = new ClientRespawnActions();
                _targetCatalogDisconnectHandler =
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(ResetSessionCatalogs));
                NetworkClient.OnDisconnectedEvent -= _targetCatalogDisconnectHandler;
                NetworkClient.OnDisconnectedEvent += _targetCatalogDisconnectHandler;

                var harmony = new Harmony(PluginGuid);
                Hooks.Appliquer(harmony);

                _botTestConsole = host.AddComponent<BotTestConsoleBehaviour>();
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

        public static bool RaidBossPriority
        {
            get { return _raidBossPriority == null || _raidBossPriority.Value; }
        }

        public static void SetRaidBossPriority(bool enabled)
        {
            SetSetting(_raidBossPriority, enabled);
        }

        // Images par seconde quand le jeu est réduit ou n'a plus le focus (20 à 60, 20 par défaut).
        public static int BackgroundFps
        {
            get
            {
                return _backgroundFps == null
                    ? DefaultBackgroundFps
                    : Mathf.Clamp(_backgroundFps.Value, MinimumBackgroundFps, MaximumBackgroundFps);
            }
        }

        public static int MinimumBackgroundFpsValue { get { return MinimumBackgroundFps; } }
        public static int MaximumBackgroundFpsValue { get { return MaximumBackgroundFps; } }

        public static void SetBackgroundFps(int fps)
        {
            SetSetting(_backgroundFps, Mathf.Clamp(fps, MinimumBackgroundFps, MaximumBackgroundFps));
        }

        // Apparence de la console : identifiant du thème et code de langue ("auto" = langue du système).
        public static string ThemeId
        {
            get { return _themeId == null ? "logo" : _themeId.Value; }
        }

        public static void SetThemeId(string id)
        {
            SetSetting(_themeId, id);
        }

        public static string Language
        {
            get { return _language == null ? "auto" : _language.Value; }
        }

        public static void SetLanguage(string code)
        {
            SetSetting(_language, code);
        }

        public static bool RespawnEnabled
        {
            get { return _respawnEnabled == null || _respawnEnabled.Value; }
        }

        public static void SetRespawnEnabled(bool enabled)
        {
            SetSetting(_respawnEnabled, enabled);
        }

        public static bool RaidEnabled
        {
            get { return _raidEnabled != null && _raidEnabled.Value; }
        }

        public static void SetRaidEnabled(bool enabled)
        {
            SetSetting(_raidEnabled, enabled);
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

        private static void SetSetting<T>(ISetting<T> setting, T value)
        {
            if (setting == null || EqualityComparer<T>.Default.Equals(setting.Value, value))
                return;
            setting.Value = value;
            _config.Save();
        }

        internal static void InitializeSurvivalConfiguration()
        {
            _repairEnabled = _config.Bind("Survival", "RepairEnabled", false,
                "Trigger Repair() while HP <= RepairPercent (runs alongside the current activity).");
            _repairPercent = _config.BindRange("Survival", "RepairPercent", 30,
                "Repair trigger threshold, in percent of max HP.", 0, 100);
            _fleeEnabled = _config.Bind("Survival", "FleeEnabled", false,
                "While HP <= FleePercent, abandon Combat (and Collect unless FleeCollectEnabled) and navigate until HP recovers.");
            _fleeCollectEnabled = _config.Bind("Survival", "FleeCollectEnabled", false,
                "Allow collecting (never combat) while fleeing.");
            _onlyFullHealthTargets = _config.Bind("Combat", "OnlyFullHealthTargets", false,
                "Only engage NPCs / monsters that are at full HP. When false, damaged targets can be engaged too.");
            _repairPausesActivity = _config.Bind("Survival", "RepairPausesActivity", false,
                "Repair mode: false = repair during activity, true = stop all activity (ship still) and repair, resume at full HP.");
            _raidEnabled = _config.Bind("Raid", "RaidEnabled", false,
                "Enable the Raid map module: enter the Raid matching the player level when the medallion is available.");
            _respawnEnabled = _config.Bind("Respawn", "RespawnEnabled", true,
                "Respawn module: when true, the bot presses the respawn button after a death; when false it does nothing on death.");
            _backgroundFps = _config.BindRange("Console", "BackgroundFps", DefaultBackgroundFps,
                "Frame rate used while the game is minimized or unfocused (20 to 60). The foreground frame rate is never changed.",
                MinimumBackgroundFps, MaximumBackgroundFps);
            _themeId = _config.Bind("Console", "Theme", "logo",
                "Console theme: logo, rouge, rgb or blanc.");
            _language = _config.Bind("Console", "Language", "auto",
                "Console language: auto (system language), en, tr, fr, de, es, pl, ru or it.");
            _raidBossPriority = _config.Bind("Raid", "RaidBossPriority", true,
                "Raid maps: true = attack the boss first when it is visible; false = ignore the boss, only the mobs.");
            _fleePercent = _config.BindRange("Survival", "FleePercent", 60,
                "Flee trigger threshold, in percent of max HP.", 0, 100);
        }

        public static bool IsCollectibleTypeEnabled(string typeName)
        {
            ISetting<bool> setting;
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
            ISetting<bool> setting;
            if (string.IsNullOrEmpty(typeName)
                || !CollectibleTypeSettings.TryGetValue(typeName, out setting)
                || setting.Value == enabled)
                return;

            setting.Value = enabled;
            _config.Save();
        }

        // Liste de types pour laquelle les réglages ont déjà été créés. Le catalogue
        // produit une nouvelle liste à chaque (re)chargement : comparer la référence
        // suffit pour ne refaire le parcours qu'une fois par chargement du catalogue.
        private static IReadOnlyList<Type> _configuredCollectibleTypes;

        internal static void InitializeCollectibleConfiguration()
        {
            if (_collectEnabled == null)
                _collectEnabled = _config.Bind("Collect", "CollectEnabled", false, "Allow collectible collection.");

            IReadOnlyList<Type> types = CollectibleCatalog.CollectibleTypes;
            if (types.Count == 0 || ReferenceEquals(types, _configuredCollectibleTypes))
                return;

            foreach (Type type in types)
            {
                if (CollectibleTypeSettings.ContainsKey(type.Name))
                    continue;

                CollectibleTypeSettings.Add(
                    type.Name,
                    _config.Bind("Collectible types", type.Name, false,
                        "Allow collecting runtime collectible type " + type.FullName + "."));
            }

            _configuredCollectibleTypes = types;
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
                Application.targetFrameRate = BackgroundFps;
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

    // Réglages du cerveau : lus dans les réglages du bot (côté client).
    internal sealed class PluginBrainSettings : IBrainSettings
    {
        public bool RepairEnabled { get { return Plugin.RepairEnabled; } }
        public int RepairPercent { get { return Plugin.RepairPercent; } }
        public bool RepairPausesActivity { get { return Plugin.RepairPausesActivity; } }
        public bool FleeEnabled { get { return Plugin.FleeEnabled; } }
        public int FleePercent { get { return Plugin.FleePercent; } }
        public bool FleeCollectEnabled { get { return Plugin.FleeCollectEnabled; } }
        public bool OnlyFullHealthTargets { get { return Plugin.OnlyFullHealthTargets; } }
        public bool RaidEnabled { get { return Plugin.RaidEnabled; } }
        public bool RaidBossPriority { get { return Plugin.RaidBossPriority; } }
        public bool RespawnEnabled { get { return Plugin.RespawnEnabled; } }
    }

    internal sealed class PluginBrainLog : IBrainLog
    {
        public void Warning(string message) { Plugin.Logger.LogWarning(message); }
        public void Error(string message) { Plugin.Logger.LogError(message); }
    }

    // Services du jeu fournis au planificateur : lecture de l'état de collecte et du catalogue de cibles.
    internal sealed class ClientPlannerServices : IPlannerServices
    {
        public CollectConfirmationState GetCollectConfirmation(
            EtatJeuSnapshot snapshot, uint netId, string collectibleType,
            DateTime startedAtUtc, long callbackSequenceBaseline, long rewardSequenceBaseline)
        {
            return GameState.ObtenirConfirmationCollecte(
                snapshot, netId, collectibleType, startedAtUtc,
                callbackSequenceBaseline, rewardSequenceBaseline);
        }

        public void GetCollectSequences(out long callbackSequence, out long rewardSequence)
        {
            GameState.ObtenirSequencesConfirmationCollecte(out callbackSequence, out rewardSequence);
        }

        public bool TryGetWeaponCategory(string name, out TargetCategory category)
        {
            return TargetCatalog.TryGetCategory(name, out category);
        }

        public IReadOnlyList<string> MonsterNames { get { return TargetCatalog.Monsters; } }
        public IReadOnlyList<string> NpcNames { get { return TargetCatalog.Npcs; } }
    }
}
