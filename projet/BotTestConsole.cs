using System;
using System.Collections.Generic;
using System.Globalization;
using EtatJoueurMod;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

public sealed class BotTestConsoleBehaviour : MonoBehaviour
{
    private readonly HashSet<string> _selectedNpcs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedMonsters =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _enabledCollectibleTypes =
        new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _ammoByTarget =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private Func<PnjInfo, bool> _allowPnj;
    private IReadOnlyList<string> _cachedNpcCatalog;
    private IReadOnlyList<string> _cachedMonsterCatalog;
    private List<string> _displayNpcs = new List<string>();
    private List<string> _displayMonsters = new List<string>();

    private Rect _panel = new Rect(16f, 16f, 680f, 680f);
    private Rect _npcPanel = new Rect(0f, 16f, 400f, 410f);
    private bool _npcPanelPlaced;
    private bool _npcDebugVisible = true;
    private Vector2 _scrollPosition;
    private bool _visible = true;
    private int _activeTab;
    private bool _collectEnabled;
    private bool _combatEnabled;
    private IReadOnlyList<Type> _cachedCollectibleCatalog;
    private List<string> _displayCollectibleTypes = new List<string>();
    private CombatCollectPriority _priority = CombatCollectPriority.Collect;
    private string _editingAmmoTarget;
    private TargetCategory _editingAmmoCategory;
    private string _editingAmmoName;

    public BotTestConsoleBehaviour(IntPtr ptr) : base(ptr)
    {
    }

    private void Awake()
    {
        _allowPnj = BotTestConsoleCatalog.CreatePnjFilter(IsPnjAllowed);
        _collectEnabled = Plugin.CollectEnabled;
        RefreshCollectibleSettings();
        RefreshCollectibleCatalog();
        ApplyConfiguration();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8))
            _visible = !_visible;
        if (Input.GetKeyDown(KeyCode.F9))
            _npcDebugVisible = !_npcDebugVisible;
    }

    private void OnGUI()
    {
        if (_npcDebugVisible)
            DrawNpcCombatPanel();

        if (!_visible)
            return;

        _panel.width = Mathf.Min(_panel.width, Mathf.Max(320f, Screen.width - 16f));
        _panel.height = Mathf.Min(_panel.height, Mathf.Max(240f, Screen.height - 16f));
        _panel.x = Mathf.Clamp(_panel.x, 0f, Mathf.Max(0f, Screen.width - _panel.width));
        _panel.y = Mathf.Clamp(_panel.y, 0f, Mathf.Max(0f, Screen.height - _panel.height));
        GUI.Box(_panel, "BOT TEST CONSOLE  |  F8 hide/show");
        GUILayout.BeginArea(new Rect(_panel.x + 8f, _panel.y + 24f, _panel.width - 16f, _panel.height - 32f));
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        DrawTabButton(0, "Automation");
        DrawTabButton(1, "Target selection + ammo");
        DrawTabButton(2, "Live status");
        DrawTabButton(3, "Collect");
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        if (_activeTab == 0)
        {
            DrawControls();
        }
        else if (_activeTab == 1)
        {
            DrawTargetConfiguration();
        }
        else if (_activeTab == 2)
        {
            DrawStatus();
        }
        else
            DrawCollectConfiguration();
        GUILayout.EndVertical();
        GUILayout.EndArea();

    }

    private void DrawTabButton(int tab, string label)
    {
        bool selected = GUILayout.Toggle(_activeTab == tab, label, GUI.skin.button);
        if (selected)
            _activeTab = tab;
    }

    private void DrawTargetConfiguration()
    {
        IReadOnlyList<string> npcCatalog = TargetCatalog.Npcs;
        IReadOnlyList<string> monsterCatalog = TargetCatalog.Monsters;
        RefreshDisplayCatalogs();

        GUILayout.Label(
            TargetCatalog.IsInitialized
                ? "Runtime target catalog: " + npcCatalog.Count + " NPC types, "
                    + monsterCatalog.Count + " Monster types"
                : "Runtime target catalog: waiting for network-ready prefab scan");
        GUILayout.Label(
            "Live ammo catalog: " + AmmoCatalog.Bullets.Count + " bullet types, "
            + AmmoCatalog.Harpoons.Count + " harpoon types");
        GUILayout.Label(
            "Select a target type below; its ammo mapping applies to every matching runtime instance.");

        _scrollPosition = GUILayout.BeginScrollView(
            _scrollPosition,
            GUILayout.Height(Mathf.Max(140f, _panel.height - 190f)));
        DrawTargetGroup(TargetCategory.Npc, "NPCs");
        DrawTargetGroup(TargetCategory.Monster, "Monsters");
        GUILayout.EndScrollView();

        GUILayout.Label(
            "Selected target types: " + _selectedNpcs.Count + " NPC, "
            + _selectedMonsters.Count + " Monster.");
    }

    private void DrawControls()
    {
        GUILayout.BeginHorizontal();
        string button = CopperWire.AutomationEnabled ? "STOP" : "PLAY";
        if (GUILayout.Button(button, GUILayout.Width(100f), GUILayout.Height(30f)))
            CopperWire.SetAutomationEnabled(!CopperWire.AutomationEnabled);
        GUILayout.Label("Automation: " + (CopperWire.AutomationEnabled ? "ON" : "OFF"));
        GUILayout.EndHorizontal();

        bool collect = GUILayout.Toggle(_collectEnabled, "Collect");
        if (collect != _collectEnabled)
        {
            _collectEnabled = collect;
            Plugin.SetCollectEnabled(collect);
            ApplyConfiguration();
        }

        bool combat = GUILayout.Toggle(_combatEnabled, "Combat");
        if (combat != _combatEnabled)
        {
            _combatEnabled = combat;
            ApplyConfiguration();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(
            _priority == CombatCollectPriority.Collect
                ? "[Collect priority]"
                : "Collect priority"))
        {
            SetPriority(CombatCollectPriority.Collect);
        }
        if (GUILayout.Button(
            _priority == CombatCollectPriority.Combat
                ? "[Combat priority]"
                : "Combat priority"))
        {
            SetPriority(CombatCollectPriority.Combat);
        }
        GUILayout.EndHorizontal();

        bool longRange = GUILayout.Toggle(CopperWire.LongRange, "Long-range combat spacing");
        if (longRange != CopperWire.LongRange)
            CopperWire.SetLongRange(longRange);

        DrawSurvivalControls();
    }

    private void DrawSurvivalControls()
    {
        bool repair = GUILayout.Toggle(Plugin.RepairEnabled, "Repair (parallel to activity)");
        if (repair != Plugin.RepairEnabled)
            Plugin.SetRepairEnabled(repair);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Repair at HP <= " + Plugin.RepairPercent + " %", GUILayout.Width(160f));
        int repairPercent = Mathf.RoundToInt(
            GUILayout.HorizontalSlider(Plugin.RepairPercent, 0f, 100f));
        GUILayout.EndHorizontal();
        if (repairPercent != Plugin.RepairPercent)
            Plugin.SetRepairPercent(repairPercent);

        bool pause = GUILayout.Toggle(
            Plugin.RepairPausesActivity, "Repair pauses all activity until HP is full");
        if (pause != Plugin.RepairPausesActivity)
            Plugin.SetRepairPausesActivity(pause);

        bool flee = GUILayout.Toggle(Plugin.FleeEnabled, "Low-HP response (abandons current activity)");
        if (flee != Plugin.FleeEnabled)
            Plugin.SetFleeEnabled(flee);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Low-HP response at HP <= " + Plugin.FleePercent + " %", GUILayout.Width(160f));
        int fleePercent = Mathf.RoundToInt(
            GUILayout.HorizontalSlider(Plugin.FleePercent, 0f, 100f));
        GUILayout.EndHorizontal();
        if (fleePercent != Plugin.FleePercent)
            Plugin.SetFleePercent(fleePercent);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(Plugin.HaltInsteadOfFlee ? "Flee" : "[Flee]"))
            Plugin.SetHaltInsteadOfFlee(false);
        if (GUILayout.Button(Plugin.HaltInsteadOfFlee ? "[Halt activity]" : "Halt activity"))
            Plugin.SetHaltInsteadOfFlee(true);
        GUILayout.EndHorizontal();

        bool fleeCollect = GUILayout.Toggle(Plugin.FleeCollectEnabled, "Collect while fleeing");
        if (fleeCollect != Plugin.FleeCollectEnabled)
            Plugin.SetFleeCollectEnabled(fleeCollect);

        if (SurvivalWire.IsFleeing)
            GUILayout.Label("State: FLEEING");
        else if (SurvivalWire.IsRepairPaused)
            GUILayout.Label("State: PAUSED (repairing)");
        else if (SurvivalWire.IsHalting)
            GUILayout.Label("State: HALTED (repairing)");
    }

    [HideFromIl2Cpp]
    private void DrawCollectConfiguration()
    {
        RefreshCollectibleCatalog();

        GUILayout.BeginHorizontal();
        string automationButton = CopperWire.AutomationEnabled ? "STOP" : "PLAY";
        if (GUILayout.Button(automationButton, GUILayout.Width(100f), GUILayout.Height(28f)))
            CopperWire.SetAutomationEnabled(!CopperWire.AutomationEnabled);
        GUILayout.Label("Automation: " + (CopperWire.AutomationEnabled ? "ON" : "OFF"));
        GUILayout.EndHorizontal();

        GUILayout.Label(
            CollectibleCatalog.IsInitialized
                ? "Runtime collectible catalog: " + _displayCollectibleTypes.Count + " types"
                : "Runtime collectible catalog: waiting for local player");

        GUILayout.Label(
            "Global Collect toggle: " + (_collectEnabled ? "ON" : "OFF")
            + " (change it in the Automation tab)");

        DrawCollectionRuntimeStatus();

        _scrollPosition = GUILayout.BeginScrollView(
            _scrollPosition,
            GUILayout.Height(Mathf.Max(120f, _panel.height - 180f)));
        for (int i = 0; i < _displayCollectibleTypes.Count; i++)
        {
            string typeName = _displayCollectibleTypes[i];
            bool wasEnabled = _enabledCollectibleTypes.Contains(typeName);
            bool isEnabled = GUILayout.Toggle(wasEnabled, typeName);
            if (isEnabled != wasEnabled)
            {
                Plugin.SetCollectibleTypeEnabled(typeName, isEnabled);
                if (isEnabled)
                    _enabledCollectibleTypes.Add(typeName);
                else
                    _enabledCollectibleTypes.Remove(typeName);
                ApplyConfiguration();
            }
        }
        GUILayout.EndScrollView();
    }

    [HideFromIl2Cpp]
    private void DrawCollectionRuntimeStatus()
    {
        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        BehaviorAction action = CopperWire.CurrentAction;
        int observedCount = snapshot == null || snapshot.Collectibles == null
            ? 0
            : snapshot.Collectibles.Count;
        int enabledObservedCount = 0;
        if (snapshot != null && snapshot.Collectibles != null)
        {
            for (int i = 0; i < snapshot.Collectibles.Count; i++)
            {
                CollectibleInfo item = snapshot.Collectibles[i];
                if (item != null
                    && item.Id != 0
                    && _enabledCollectibleTypes.Contains(item.Type)
                    && Plugin.IsCollectibleTypeEnabled(item.Type))
                    enabledObservedCount++;
            }
        }

        GUILayout.Label(
            "Live collectibles: " + observedCount
            + " | matching enabled types: " + enabledObservedCount
            + " | enabled types: " + _enabledCollectibleTypes.Count);

        if (!CopperWire.AutomationEnabled)
        {
            GUILayout.Label("Collection is blocked: press PLAY (automation starts STOP).");
        }
        else if (!_collectEnabled)
        {
            GUILayout.Label("Collection is blocked: turn on the global Collect toggle.");
        }
        else if (_enabledCollectibleTypes.Count == 0)
        {
            GUILayout.Label("Collection is blocked: enable at least one collectible type below.");
        }
        else if (snapshot == null || snapshot.Joueur == null)
        {
            GUILayout.Label("Collection is waiting for a GameState player snapshot.");
        }
        else if (observedCount == 0)
        {
            GUILayout.Label("No collectible is currently visible in the GameState snapshot.");
        }
        else if (enabledObservedCount == 0)
        {
            GUILayout.Label("Collectibles are visible, but none match an enabled type.");
        }
        else if (action != null && action.Type == BehaviorActionType.Collect)
        {
            CollectActionContext context = action.Context as CollectActionContext;
            GUILayout.Label(
                "Collect action: " + action.State
                + (context == null ? string.Empty : " | NetId " + context.NetId + " | " + context.Type));
        }
        else if (action != null)
        {
            GUILayout.Label("Current action: " + action.Type + " / " + action.State);
        }
        else
        {
            GUILayout.Label("An eligible collectible is visible; waiting for the next planner tick.");
        }
    }

    [HideFromIl2Cpp]
    private void RefreshCollectibleCatalog()
    {
        IReadOnlyList<Type> catalog = CollectibleCatalog.CollectibleTypes;
        if (ReferenceEquals(_cachedCollectibleCatalog, catalog))
            return;

        _cachedCollectibleCatalog = catalog;
        _displayCollectibleTypes = new List<string>(catalog.Count);
        for (int i = 0; i < catalog.Count; i++)
            _displayCollectibleTypes.Add(catalog[i].Name);
        _displayCollectibleTypes.Sort(StringComparer.Ordinal);
        RefreshCollectibleSettings();
    }

    [HideFromIl2Cpp]
    private void RefreshCollectibleSettings()
    {
        _enabledCollectibleTypes.Clear();
        IReadOnlyList<Type> catalog = CollectibleCatalog.CollectibleTypes;
        for (int i = 0; i < catalog.Count; i++)
        {
            string typeName = catalog[i].Name;
            if (Plugin.IsCollectibleTypeEnabled(typeName))
                _enabledCollectibleTypes.Add(typeName);
        }
    }

    [HideFromIl2Cpp]
    private void SetPriority(CombatCollectPriority priority)
    {
        if (_priority == priority)
            return;

        _priority = priority;
        ApplyConfiguration();
    }

    private void DrawStatus()
    {
        BehaviorAction action = CopperWire.CurrentAction;
        GUILayout.Label(
            "System: " + CopperWire.SystemState
            + " | Action: " + (action == null ? "None" : action.Type + " / " + action.State));
        GUILayout.Label(
            "Respawn: " + (RespawnWire.IsActive
                ? RespawnWire.IsAbandoned ? "active (attempts abandoned)" : "active"
                : "inactive"));

        CombatTarget target = CopperWire.CurrentCombatTarget;
        if (target != null)
        {
            string category = target.WeaponCategory.HasValue
                ? target.WeaponCategory.Value.ToString()
                : "unclassified";
            GUILayout.Label(
                "Target: " + target.Name + " (" + category + ")"
                + (string.IsNullOrEmpty(target.Category) ? string.Empty : " / " + target.Category)
                + (string.IsNullOrEmpty(target.Type) ? string.Empty : " / " + target.Type));
            if (target.WeaponCategory.HasValue)
            {
                TargetCategory weapon = target.WeaponCategory.Value;
                GUILayout.Label(
                    "Ammo: desired " + DescribeAmmo(weapon, target.AmmoId)
                    + " | selected " + DescribeAmmo(weapon, CopperWire.GetSelectedAmmoId(weapon)));
            }
        }
        else if (action != null && action.Type == BehaviorActionType.Collect)
        {
            CollectActionContext context = action.Context as CollectActionContext;
            GUILayout.Label("Collectible: " + (context == null ? "unknown" : "NetId " + context.NetId));
        }

        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        FicheJoueur player = snapshot == null ? null : snapshot.Joueur;
        if (player == null)
        {
            GUILayout.Label("Player status: waiting for GameState snapshot");
            return;
        }

        GUILayout.Label("HP: " + player.Vie + " / " + player.VieMax);
        GUILayout.Label(
            "Range: cannon " + FormatNumber(player.Portee)
            + " | harpoon " + FormatNumber(player.PorteeHarpon));
        GUILayout.Label(
            "Map: " + player.Harita
            + (string.IsNullOrEmpty(player.NomHarita) ? string.Empty : " / " + player.NomHarita));
        GUILayout.Label(
            "Coordinates: " + (player.CoordonneeSayi ?? "?") + " " + (player.CoordonneeHarf ?? "?")
            + " | world " + FormatNumber(player.X) + ", " + FormatNumber(player.Y));
    }

    [HideFromIl2Cpp]
    private void DrawTargetGroup(
        TargetCategory category,
        string label)
    {
        List<string> catalog = category == TargetCategory.Monster
            ? _displayMonsters
            : _displayNpcs;
        GUILayout.Label(label + " (catalog types)");
        if (!TargetCatalog.IsInitialized)
        {
            GUILayout.Label("Waiting for the runtime target catalog.");
            return;
        }

        for (int i = 0; i < catalog.Count; i++)
        {
            string name = catalog[i];
            string targetKey = GetTargetKey(category, name);

            GUILayout.BeginHorizontal();
            bool wasSelected = IsTargetSelected(category, name);
            bool isSelected = GUILayout.Toggle(wasSelected, name);
            if (isSelected != wasSelected)
            {
                SetTargetSelected(category, name, isSelected);
                if (isSelected)
                {
                    _editingAmmoTarget = targetKey;
                    _editingAmmoCategory = category;
                    _editingAmmoName = name;
                }
                else
                {
                    if (string.Equals(_editingAmmoTarget, targetKey, StringComparison.OrdinalIgnoreCase))
                        CloseAmmoEditor();
                }
                ApplyConfiguration();
            }

            if (isSelected || category == TargetCategory.Npc)
            {
                IReadOnlyList<AmmoDefinition> ammo = GetAmmoCatalog(category);
                int? id = ResolveAmmo(category, name);
                string caption = ammo.Count == 0
                    ? (category == TargetCategory.Npc ? "Boulet indisponible" : "Ammo unavailable")
                    : (category == TargetCategory.Npc ? "Boulet: " : "Harpoon: ")
                        + DescribeAmmo(category, id);
                if (GUILayout.Button(caption, GUILayout.Width(260f)))
                {
                    if (string.Equals(_editingAmmoTarget, targetKey, StringComparison.OrdinalIgnoreCase))
                    {
                        CloseAmmoEditor();
                    }
                    else
                    {
                        _editingAmmoTarget = targetKey;
                        _editingAmmoCategory = category;
                        _editingAmmoName = name;
                    }
                }
            }
            GUILayout.EndHorizontal();

            if (string.Equals(
                _editingAmmoTarget,
                targetKey,
                StringComparison.OrdinalIgnoreCase))
            {
                DrawAmmoEditor();
            }
        }

        if (catalog.Count == 0)
            GUILayout.Label("No types in this catalog.");
    }

    private void RefreshDisplayCatalogs()
    {
        IReadOnlyList<string> npcCatalog = TargetCatalog.Npcs;
        IReadOnlyList<string> monsterCatalog = TargetCatalog.Monsters;

        if (!ReferenceEquals(_cachedNpcCatalog, npcCatalog))
        {
            _cachedNpcCatalog = npcCatalog;
            _displayNpcs = BotTestConsoleCatalog.BuildDisplayCatalog(
                npcCatalog,
                TargetCategory.Npc);
        }

        if (!ReferenceEquals(_cachedMonsterCatalog, monsterCatalog))
        {
            _cachedMonsterCatalog = monsterCatalog;
            _displayMonsters = BotTestConsoleCatalog.BuildDisplayCatalog(
                monsterCatalog,
                TargetCategory.Monster);
        }
    }

    [HideFromIl2Cpp]
    private bool IsTargetSelected(TargetCategory category, string name)
    {
        return (category == TargetCategory.Monster ? _selectedMonsters : _selectedNpcs)
            .Contains(name);
    }

    [HideFromIl2Cpp]
    private void SetTargetSelected(TargetCategory category, string name, bool selected)
    {
        HashSet<string> targets = category == TargetCategory.Monster
            ? _selectedMonsters
            : _selectedNpcs;
        if (selected)
            targets.Add(name);
        else
            targets.Remove(name);
    }

    private void DrawAmmoEditor()
    {
        if (string.IsNullOrEmpty(_editingAmmoTarget))
            return;

        if (_editingAmmoCategory == TargetCategory.Monster
            && !IsTargetSelected(_editingAmmoCategory, _editingAmmoName))
        {
            CloseAmmoEditor();
            return;
        }

        GUILayout.BeginVertical("box");
        GUILayout.Label(
            (_editingAmmoCategory == TargetCategory.Npc ? "Boulets pour NPC / " : "Harpons pour Monster / ")
            + _editingAmmoName
            + " (applies to every runtime instance)");
        if (GUILayout.Button("Close ammo list"))
        {
            CloseAmmoEditor();
            GUILayout.EndVertical();
            return;
        }

        IReadOnlyList<AmmoDefinition> catalog = GetAmmoCatalog(_editingAmmoCategory);
        if (catalog.Count == 0)
        {
            GUILayout.Label("Waiting for the matching live ammo catalog.");
        }
        else
        {
            int? currentId = ResolveAmmo(_editingAmmoCategory, _editingAmmoName);
            for (int i = 0; i < catalog.Count; i++)
            {
                AmmoDefinition ammo = catalog[i];
                bool isSelected = currentId.HasValue && currentId.Value == ammo.Id;
                bool choose = GUILayout.Toggle(isSelected, FormatAmmo(ammo), GUI.skin.button);
                if (choose && !isSelected)
                {
                    _ammoByTarget[_editingAmmoTarget] = ammo.Id;
                    ApplyConfiguration();
                }
            }
        }
        GUILayout.EndVertical();
    }

    private void ApplyConfiguration()
    {
        CopperWire.Configurer(
            _collectEnabled,
            _combatEnabled,
            _priority,
            CopperWire.LongRange,
            _allowPnj,
            null,
            ResolveAmmo,
            IsCollectibleTypeAllowed);
    }

    [HideFromIl2Cpp]
    private bool IsCollectibleTypeAllowed(CollectibleInfo collectible)
    {
        return collectible != null
            && _enabledCollectibleTypes.Contains(collectible.Type)
            && Plugin.IsCollectibleTypeEnabled(collectible.Type);
    }

    private bool IsPnjAllowed(string name)
    {
        TargetCategory category;
        if (!TargetCatalog.TryGetCategory(name, out category))
            return false;

        return IsTargetSelected(category, TargetCatalog.NormalizeName(name));
    }

    [HideFromIl2Cpp]
    private int? ResolveAmmo(TargetCategory category, string targetName)
    {
        IReadOnlyList<AmmoDefinition> catalog = GetAmmoCatalog(category);
        if (catalog.Count == 0)
            return null;

        int selectedId;
        string key = GetTargetKey(category, targetName);
        if (_ammoByTarget.TryGetValue(key, out selectedId))
        {
            for (int i = 0; i < catalog.Count; i++)
            {
                AmmoDefinition ammo = catalog[i];
                if (ammo.Id == selectedId)
                    return selectedId;
            }
        }

        return catalog[0].Id;
    }

    [HideFromIl2Cpp]
    private string DescribeAmmo(TargetCategory category, int? id)
    {
        if (!id.HasValue)
            return "unavailable";

        IReadOnlyList<AmmoDefinition> catalog = GetAmmoCatalog(category);
        for (int i = 0; i < catalog.Count; i++)
        {
            AmmoDefinition ammo = catalog[i];
            if (ammo.Id == id.Value)
                return FormatAmmo(ammo);
        }
        return "ID " + id.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatAmmo(AmmoDefinition ammo)
    {
        string name = string.IsNullOrWhiteSpace(ammo.LocalizationKey)
            ? ammo.InternalName
            : ammo.LocalizationKey;
        return name + " (ID " + ammo.Id.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static IReadOnlyList<AmmoDefinition> GetAmmoCatalog(TargetCategory category)
    {
        return category == TargetCategory.Monster ? AmmoCatalog.Harpoons : AmmoCatalog.Bullets;
    }

    private static string GetTargetKey(TargetCategory category, string name)
    {
        return category + ":" + TargetCatalog.NormalizeName(name);
    }

    // Fenêtre de debug du combat NPC (F9) : affiche le calcul fait par CopperWire.
    private void DrawNpcCombatPanel()
    {
        if (!_npcPanelPlaced)
        {
            _npcPanel.x = Screen.width - _npcPanel.width - 16f;
            _npcPanelPlaced = true;
        }
        _npcPanel.x = Mathf.Clamp(_npcPanel.x, 0f, Mathf.Max(0f, Screen.width - _npcPanel.width));
        _npcPanel.y = Mathf.Clamp(_npcPanel.y, 0f, Mathf.Max(0f, Screen.height - _npcPanel.height));

        GUI.Box(_npcPanel, "COMBAT NPC (debug)  |  F9 hide/show");
        GUILayout.BeginArea(new Rect(_npcPanel.x + 8f, _npcPanel.y + 24f, _npcPanel.width - 16f, _npcPanel.height - 100f));

        NpcCombatDebugInfo info = CopperWire.LastNpcCombat;
        if (info == null)
        {
            GUILayout.Label("Aucun combat contre un NPC pour l'instant.");
            GUILayout.EndArea();
            return;
        }

        float age = Time.time - info.UpdatedAt;
        GUILayout.Label("Cible : " + info.NpcName + "  (NetId " + info.NetId + ")"
            + (age > 1.5f ? "  [terminé il y a " + age.ToString("0", CultureInfo.InvariantCulture) + " s]" : string.Empty));
        GUILayout.Label("Portée du NPC : " + FormatRange(info.NpcRange)
            + "   |   Ma portée : " + FormatNumber(info.OwnRange)
            + "   |   Écart : " + (IsFiniteNumber(info.NpcRange) ? FormatNumber(info.OwnRange - info.NpcRange) : "?"));
        GUILayout.Label("LongRange : " + (info.LongRange ? "coché" : "décoché"));
        GUILayout.Label("Décision : " + info.Decision);
        if (info.OutOfReachMode)
            GUILayout.Label("Zone : de " + FormatNumber(info.ZoneMinimum) + " à " + FormatNumber(info.OwnRange)
                + "   |   Point visé : " + FormatNumber(info.AimDistance));
        GUILayout.Label("Distance actuelle : " + FormatNumber(info.Distance) + "  →  " + info.Status);
        GUILayout.Label("Déplacement : " + info.Movement);
        GUILayout.Label("Tir de canon confirmé : " + (info.ShotConfirmed ? "oui" : "pas encore"));
        GUILayout.Label("Vitesse mesurée : moi " + FormatSpeed(info.PlayerSpeed)
            + " | NPC " + FormatSpeed(info.NpcSpeed)
            + "   (stat jeu : " + FormatNumber(info.PlayerSpeedStat) + ")");

        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        FicheJoueur player = snapshot == null ? null : snapshot.Joueur;
        string gameCoordinates = player == null
            ? string.Empty
            : "  (" + (player.CoordonneeSayi ?? "?") + " " + (player.CoordonneeHarf ?? "?") + ")";
        GUILayout.Label("Ma position : " + FormatNumber(info.PlayerX) + ", " + FormatNumber(info.PlayerY) + gameCoordinates);
        GUILayout.Label("Position du NPC : " + FormatNumber(info.NpcX) + ", " + FormatNumber(info.NpcY));
        GUILayout.EndArea();

        DrawNpcRangeRuler(new Rect(_npcPanel.x + 12f, _npcPanel.y + _npcPanel.height - 72f, _npcPanel.width - 24f, 64f), info);
    }

    // Règle graduée depuis le NPC (à gauche) : vert = ma portée, rouge = sa portée,
    // jaune = point visé, blanc = ma distance actuelle.
    [HideFromIl2Cpp]
    private static void DrawNpcRangeRuler(Rect area, NpcCombatDebugInfo info)
    {
        float npcRange = IsFiniteNumber(info.NpcRange) && info.NpcRange > 0f ? info.NpcRange : 0f;
        float max = Mathf.Max(info.OwnRange, Mathf.Max(npcRange, info.Distance)) * 1.1f + 0.5f;
        if (!IsFiniteNumber(max) || max <= 0f)
            return;

        float left = area.x;
        float width = area.width;
        Color previous = GUI.color;

        GUI.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        GUI.DrawTexture(new Rect(left, area.y + 6f, width, 20f), Texture2D.whiteTexture);

        GUI.color = new Color(0.25f, 0.8f, 0.6f, 0.95f);
        GUI.DrawTexture(new Rect(left, area.y + 6f, RulerX(info.OwnRange, left, width, max) - left, 10f), Texture2D.whiteTexture);

        if (npcRange > 0f)
        {
            GUI.color = new Color(0.95f, 0.45f, 0.35f, 0.95f);
            GUI.DrawTexture(new Rect(left, area.y + 16f, RulerX(npcRange, left, width, max) - left, 10f), Texture2D.whiteTexture);
        }

        if (info.OutOfReachMode)
        {
            GUI.color = new Color(1f, 0.85f, 0.2f, 1f);
            GUI.DrawTexture(new Rect(RulerX(info.AimDistance, left, width, max) - 1f, area.y + 2f, 2f, 28f), Texture2D.whiteTexture);
        }

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(RulerX(info.Distance, left, width, max) - 1.5f, area.y, 3f, 32f), Texture2D.whiteTexture);

        GUI.color = previous;
        GUI.Label(new Rect(left, area.y + 32f, 120f, 20f), "0 (NPC)");
        GUI.Label(new Rect(left + width - 60f, area.y + 32f, 60f, 20f), FormatNumber(max));
        GUI.Label(new Rect(left, area.y + 46f, width, 20f),
            "vert : ma portée | rouge : sa portée | jaune : visé | blanc : moi");
    }

    private static float RulerX(float distance, float left, float width, float max)
    {
        return left + Mathf.Clamp01(distance / max) * width;
    }

    private static bool IsFiniteNumber(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string FormatSpeed(float value)
    {
        return IsFiniteNumber(value) ? FormatNumber(value) : "?";
    }

    private static string FormatRange(float value)
    {
        return IsFiniteNumber(value) && value > 0f ? FormatNumber(value) : "illisible";
    }

    private static string FormatNumber(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void CloseAmmoEditor()
    {
        _editingAmmoTarget = null;
        _editingAmmoName = null;
    }
}
