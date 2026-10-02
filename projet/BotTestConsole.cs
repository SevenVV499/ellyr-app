using System;
using System.Collections.Generic;
using System.Globalization;
using EtatJoueurMod;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

/*
 * Console de test (provisoire) : thème sombre, onglets, cartes par thème.
 * F8 : afficher / masquer. L'en-tête se déplace à la souris.
 */
public sealed class BotTestConsoleBehaviour : MonoBehaviour
{
    private const int TabControl = 0;
    private const int TabSurvival = 1;
    private const int TabTargets = 2;
    private const int TabCollect = 3;
    private const int TabStatus = 4;
    private static readonly string[] TabLabels =
        { "Control", "Survival", "Targets", "Collect", "Status" };

    private const float HeaderHeight = 44f;
    private const float TabBarHeight = 34f;
    private const float FooterHeight = 22f;

    // Palette
    private static readonly Color ColBg = new Color(0.078f, 0.086f, 0.106f, 0.97f);
    private static readonly Color ColHeader = new Color(0.106f, 0.118f, 0.145f, 1f);
    private static readonly Color ColCard = new Color(0.125f, 0.137f, 0.165f, 1f);
    private static readonly Color ColField = new Color(0.176f, 0.192f, 0.227f, 1f);
    private static readonly Color ColFieldHover = new Color(0.22f, 0.24f, 0.285f, 1f);
    private static readonly Color ColAccent = new Color(0.24f, 0.65f, 0.96f, 1f);
    private static readonly Color ColAccentDim = new Color(0.14f, 0.30f, 0.45f, 1f);
    private static readonly Color ColOk = new Color(0.24f, 0.86f, 0.59f, 1f);
    private static readonly Color ColWarn = new Color(0.96f, 0.65f, 0.14f, 1f);
    private static readonly Color ColDanger = new Color(0.95f, 0.37f, 0.36f, 1f);
    private static readonly Color ColText = new Color(0.90f, 0.91f, 0.94f, 1f);
    private static readonly Color ColMuted = new Color(0.54f, 0.58f, 0.65f, 1f);

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

    private Rect _panel = new Rect(16f, 16f, 620f, 680f);
    private Vector2 _scrollPosition;
    private bool _visible = true;
    private bool _dragging;
    private Vector2 _dragOffset;
    private int _activeTab;
    private bool _collectEnabled;
    private bool _combatEnabled;
    private IReadOnlyList<Type> _cachedCollectibleCatalog;
    private List<string> _displayCollectibleTypes = new List<string>();
    private CombatCollectPriority _priority = CombatCollectPriority.Collect;
    private string _editingAmmoTarget;
    private TargetCategory _editingAmmoCategory;
    private string _editingAmmoName;

    // Styles (construits dans OnGUI : GUI.skin n'est accessible que là)
    private bool _stylesReady;
    private Texture2D _texBg;
    private GUIStyle _sWindow;
    private GUIStyle _sHeader;
    private GUIStyle _sTitle;
    private GUIStyle _sCard;
    private GUIStyle _sCardTitle;
    private GUIStyle _sLabel;
    private GUIStyle _sMuted;
    private GUIStyle _sValue;
    private GUIStyle _sTab;
    private GUIStyle _sTabOn;
    private GUIStyle _sBtn;
    private GUIStyle _sBtnPrimary;
    private GUIStyle _sBtnDanger;
    private GUIStyle _sSwitchOn;
    private GUIStyle _sSwitchOff;
    private GUIStyle _sSeg;
    private GUIStyle _sSegOn;
    private GUIStyle _sRow;
    private GUIStyle _sRowOn;
    private GUIStyle _sSlider;
    private GUIStyle _sThumb;
    private GUIStyle _sPill;

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
    }

    // ------------------------------------------------------------------ OnGUI

    private void OnGUI()
    {
        if (!_visible)
            return;

        EnsureStyles();

        _panel.width = Mathf.Min(_panel.width, Mathf.Max(380f, Screen.width - 16f));
        _panel.height = Mathf.Min(_panel.height, Mathf.Max(320f, Screen.height - 16f));
        _panel.x = Mathf.Clamp(_panel.x, 0f, Mathf.Max(0f, Screen.width - _panel.width));
        _panel.y = Mathf.Clamp(_panel.y, 0f, Mathf.Max(0f, Screen.height - _panel.height));

        GUI.Box(_panel, GUIContent.none, _sWindow);
        HandleDrag();
        DrawHeader();
        DrawTabBar();

        float bodyTop = _panel.y + HeaderHeight + TabBarHeight + 10f;
        float bodyHeight = _panel.height - HeaderHeight - TabBarHeight - FooterHeight - 14f;
        GUILayout.BeginArea(new Rect(_panel.x + 14f, bodyTop, _panel.width - 28f, bodyHeight));
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
        switch (_activeTab)
        {
            case TabControl: DrawControlTab(); break;
            case TabSurvival: DrawSurvivalTab(); break;
            case TabTargets: DrawTargetsTab(); break;
            case TabCollect: DrawCollectTab(); break;
            default: DrawStatusTab(); break;
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        GUI.Label(
            new Rect(_panel.x + 16f, _panel.yMax - FooterHeight - 2f, _panel.width - 32f, FooterHeight),
            "F8 hide / show   |   drag the header to move",
            _sMuted);
    }

    private void HandleDrag()
    {
        Event e = Event.current;
        // Zone de déplacement : l'en-tête sans les boutons de droite.
        Rect grip = new Rect(_panel.x, _panel.y, _panel.width - 240f, HeaderHeight);
        if (e.type == EventType.MouseDown && grip.Contains(e.mousePosition))
        {
            _dragging = true;
            _dragOffset = e.mousePosition - new Vector2(_panel.x, _panel.y);
            e.Use();
        }
        else if (_dragging && e.type == EventType.MouseDrag)
        {
            _panel.x = e.mousePosition.x - _dragOffset.x;
            _panel.y = e.mousePosition.y - _dragOffset.y;
            e.Use();
        }
        else if (e.type == EventType.MouseUp)
        {
            _dragging = false;
        }
    }

    private void DrawHeader()
    {
        GUI.Box(new Rect(_panel.x, _panel.y, _panel.width, HeaderHeight), GUIContent.none, _sHeader);
        GUI.Label(new Rect(_panel.x + 16f, _panel.y + 10f, 220f, 24f), "ELLYR BOT CONSOLE", _sTitle);

        bool running = CopperWire.AutomationEnabled;
        Rect play = new Rect(_panel.xMax - 108f, _panel.y + 8f, 92f, 28f);
        if (GUI.Button(play, running ? "STOP" : "PLAY", running ? _sBtnDanger : _sBtnPrimary))
            CopperWire.SetAutomationEnabled(!running);

        Rect pill = new Rect(play.x - 138f, _panel.y + 10f, 128f, 24f);
        Color previous = GUI.color;
        GUI.color = StateColor();
        GUI.Box(pill, GUIContent.none, _sPill);
        GUI.color = previous;
        GUI.Label(pill, StateText(), PillTextStyle());
    }

    // Style du texte de la pastille : centré, sombre sur fond coloré.
    private GUIStyle _pillText;
    private GUIStyle PillTextStyle()
    {
        if (_pillText == null)
        {
            _pillText = CloneStyle(_sLabel);
            _pillText.alignment = TextAnchor.MiddleCenter;
            _pillText.fontStyle = FontStyle.Bold;
            _pillText.fontSize = 11;
            _pillText.normal.textColor = new Color(0.06f, 0.07f, 0.09f, 1f);
        }
        return _pillText;
    }

    private string StateText()
    {
        if (!CopperWire.AutomationEnabled)
            return "STOPPED";
        if (RespawnWire.IsActive)
            return "RESPAWN";
        if (SurvivalWire.IsFleeing)
            return "FLEEING";
        if (SurvivalWire.IsRepairPaused)
            return "REPAIRING";
        BehaviorAction action = CopperWire.CurrentAction;
        return action == null ? "IDLE" : action.Type.ToString().ToUpperInvariant();
    }

    private Color StateColor()
    {
        if (!CopperWire.AutomationEnabled)
            return ColMuted;
        if (SurvivalWire.IsFleeing)
            return ColDanger;
        if (RespawnWire.IsActive || SurvivalWire.IsRepairPaused)
            return ColWarn;
        return ColOk;
    }

    private void DrawTabBar()
    {
        float y = _panel.y + HeaderHeight;
        float tabWidth = _panel.width / TabLabels.Length;
        for (int i = 0; i < TabLabels.Length; i++)
        {
            Rect rect = new Rect(_panel.x + i * tabWidth, y, tabWidth, TabBarHeight);
            bool selected = _activeTab == i;
            if (GUI.Button(rect, TabLabels[i], selected ? _sTabOn : _sTab) && !selected)
            {
                _activeTab = i;
                _scrollPosition = Vector2.zero;
            }
            if (selected)
            {
                Color previous = GUI.color;
                GUI.color = ColAccent;
                GUI.DrawTexture(
                    new Rect(rect.x + 12f, rect.yMax - 3f, rect.width - 24f, 3f),
                    Texture2D.whiteTexture);
                GUI.color = previous;
            }
        }
    }

    // ------------------------------------------------------------------ Onglets

    private void DrawControlTab()
    {
        BeginCard("Activities", "What the bot is allowed to do while automation is PLAY.");
        bool collect = Switch(_collectEnabled, "Collect");
        if (collect != _collectEnabled)
        {
            _collectEnabled = collect;
            Plugin.SetCollectEnabled(collect);
            ApplyConfiguration();
        }

        bool combat = Switch(_combatEnabled, "Combat");
        if (combat != _combatEnabled)
        {
            _combatEnabled = combat;
            ApplyConfiguration();
        }
        Hint("Navigation is the default activity when nothing else applies.");
        EndCard();

        BeginCard("Priority", "Used when a collectible and a combat target are both available.");
        int priority = Segmented(
            _priority == CombatCollectPriority.Collect ? 0 : 1,
            "Collect first",
            "Combat first");
        SetPriority(priority == 0 ? CombatCollectPriority.Collect : CombatCollectPriority.Combat);
        EndCard();

        BeginCard("Combat");
        bool longRange = Switch(CopperWire.LongRange, "Long-range combat spacing");
        if (longRange != CopperWire.LongRange)
            CopperWire.SetLongRange(longRange);
        EndCard();
    }

    private void DrawSurvivalTab()
    {
        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        FicheJoueur player = snapshot == null ? null : snapshot.Joueur;

        BeginCard("Hull");
        if (player == null || player.VieMax <= 0)
        {
            Hint("Waiting for a GameState player snapshot.");
        }
        else
        {
            Row("HP", player.Vie + " / " + player.VieMax
                + "   (" + player.PourcentageVie.ToString("0", CultureInfo.InvariantCulture) + " %)");
            DrawHpBar(player.PourcentageVie);
            Row("State", StateText());
        }
        EndCard();

        BeginCard("Repair", "Sends the repair command while HP is below the threshold.");
        bool repair = Switch(Plugin.RepairEnabled, "Repair enabled");
        if (repair != Plugin.RepairEnabled)
            Plugin.SetRepairEnabled(repair);

        int repairMode = Segmented(
            Plugin.RepairPausesActivity ? 1 : 0,
            "In activity",
            "Stopped");
        Plugin.SetRepairPausesActivity(repairMode == 1);
        Hint(Plugin.RepairPausesActivity
            ? "Stopped: all activity is paused and the ship stays still. Activity resumes at full HP."
            : "In activity: repair runs during Navigation, Collect and Combat; the activity continues.");

        int repairPercent = PercentSlider("Repair at HP <=", Plugin.RepairPercent);
        if (repairPercent != Plugin.RepairPercent)
            Plugin.SetRepairPercent(repairPercent);
        EndCard();

        BeginCard("Low HP", "Abandons Combat and keeps navigating until HP recovers.");
        bool flee = Switch(Plugin.FleeEnabled, "Flee enabled");
        if (flee != Plugin.FleeEnabled)
            Plugin.SetFleeEnabled(flee);

        int fleePercent = PercentSlider("Flee at HP <=", Plugin.FleePercent);
        if (fleePercent != Plugin.FleePercent)
            Plugin.SetFleePercent(fleePercent);

        bool fleeCollect = Switch(Plugin.FleeCollectEnabled, "Collect while fleeing");
        if (fleeCollect != Plugin.FleeCollectEnabled)
            Plugin.SetFleeCollectEnabled(fleeCollect);
        Hint("Flee ends once HP is above both the flee and repair thresholds.");
        EndCard();
    }

    private void DrawTargetsTab()
    {
        IReadOnlyList<string> npcCatalog = TargetCatalog.Npcs;
        IReadOnlyList<string> monsterCatalog = TargetCatalog.Monsters;
        RefreshDisplayCatalogs();

        BeginCard("Catalogs", "Select a target type; its ammo applies to every matching instance.");
        Row("Targets", TargetCatalog.IsInitialized
            ? npcCatalog.Count + " NPC types, " + monsterCatalog.Count + " monster types"
            : "waiting for network-ready prefab scan");
        Row("Ammo", AmmoCatalog.Bullets.Count + " bullet types, "
            + AmmoCatalog.Harpoons.Count + " harpoon types");
        Row("Selected", _selectedNpcs.Count + " NPC, " + _selectedMonsters.Count + " monster");
        EndCard();

        DrawTargetGroup(TargetCategory.Npc, "NPCs");
        DrawTargetGroup(TargetCategory.Monster, "Monsters");
    }

    private void DrawCollectTab()
    {
        RefreshCollectibleCatalog();

        BeginCard("Status");
        Row("Collect", _collectEnabled ? "ON (Control tab)" : "OFF (Control tab)");
        Row("Catalog", CollectibleCatalog.IsInitialized
            ? _displayCollectibleTypes.Count + " types"
            : "waiting for local player");
        Hint(CollectionStatusMessage());
        EndCard();

        BeginCard("Collectible types", _enabledCollectibleTypes.Count + " enabled");
        for (int i = 0; i < _displayCollectibleTypes.Count; i++)
        {
            string typeName = _displayCollectibleTypes[i];
            bool wasEnabled = _enabledCollectibleTypes.Contains(typeName);
            bool isEnabled = RowToggle(wasEnabled, typeName);
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
        if (_displayCollectibleTypes.Count == 0)
            Hint("No collectible type yet.");
        EndCard();
    }

    private void DrawStatusTab()
    {
        BehaviorAction action = CopperWire.CurrentAction;

        BeginCard("Bot");
        Row("System", CopperWire.SystemState.ToString());
        Row("Action", action == null ? "None" : action.Type + " / " + action.State);
        Row("Respawn", RespawnWire.IsActive
            ? RespawnWire.IsAbandoned ? "active (attempts abandoned)" : "active"
            : "inactive");

        CombatTarget target = CopperWire.CurrentCombatTarget;
        if (target != null)
        {
            string category = target.WeaponCategory.HasValue
                ? target.WeaponCategory.Value.ToString()
                : "unclassified";
            Row("Target", target.Name + " (" + category + ")"
                + (string.IsNullOrEmpty(target.Category) ? string.Empty : " / " + target.Category)
                + (string.IsNullOrEmpty(target.Type) ? string.Empty : " / " + target.Type));
            if (target.WeaponCategory.HasValue)
            {
                TargetCategory weapon = target.WeaponCategory.Value;
                Row("Ammo", "desired " + DescribeAmmo(weapon, target.AmmoId)
                    + " | selected " + DescribeAmmo(weapon, CopperWire.GetSelectedAmmoId(weapon)));
            }
        }
        else if (action != null && action.Type == BehaviorActionType.Collect)
        {
            CollectActionContext context = action.Context as CollectActionContext;
            Row("Collectible", context == null ? "unknown" : "NetId " + context.NetId);
        }
        EndCard();

        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        FicheJoueur player = snapshot == null ? null : snapshot.Joueur;
        BeginCard("Player");
        if (player == null)
        {
            Hint("Waiting for a GameState snapshot.");
        }
        else
        {
            Row("HP", player.Vie + " / " + player.VieMax);
            Row("Range", "cannon " + FormatNumber(player.Portee)
                + " | harpoon " + FormatNumber(player.PorteeHarpon));
            Row("Map", player.Harita
                + (string.IsNullOrEmpty(player.NomHarita) ? string.Empty : " / " + player.NomHarita));
            Row("Position", (player.CoordonneeSayi ?? "?") + " " + (player.CoordonneeHarf ?? "?")
                + " | world " + FormatNumber(player.X) + ", " + FormatNumber(player.Y));
        }
        EndCard();
    }

    // ------------------------------------------------------------------ Composants

    private void BeginCard(string title, string subtitle = null)
    {
        GUILayout.BeginVertical(_sCard);
        GUILayout.Label(title.ToUpperInvariant(), _sCardTitle);
        if (!string.IsNullOrEmpty(subtitle))
            GUILayout.Label(subtitle, _sMuted);
        GUILayout.Space(4f);
    }

    private void EndCard()
    {
        GUILayout.EndVertical();
        GUILayout.Space(10f);
    }

    private void Hint(string text)
    {
        GUILayout.Label(text, _sMuted);
    }

    private void Row(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _sMuted, GUILayout.Width(110f));
        GUILayout.Label(value, _sLabel);
        GUILayout.EndHorizontal();
    }

    private bool Switch(bool value, string label)
    {
        GUILayout.BeginHorizontal();
        bool clicked = GUILayout.Button(
            value ? "ON" : "OFF",
            value ? _sSwitchOn : _sSwitchOff,
            GUILayout.Width(54f),
            GUILayout.Height(24f));
        GUILayout.Label(label, _sLabel);
        GUILayout.EndHorizontal();
        return clicked ? !value : value;
    }

    private int Segmented(int index, string first, string second)
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(first, index == 0 ? _sSegOn : _sSeg, GUILayout.Height(26f)))
            index = 0;
        if (GUILayout.Button(second, index == 1 ? _sSegOn : _sSeg, GUILayout.Height(26f)))
            index = 1;
        GUILayout.EndHorizontal();
        return index;
    }

    private int PercentSlider(string label, int value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _sLabel, GUILayout.Width(130f));
        float slid = GUILayout.HorizontalSlider(value, 0f, 100f, _sSlider, _sThumb);
        GUILayout.Label(Mathf.RoundToInt(slid) + " %", _sValue, GUILayout.Width(52f));
        GUILayout.EndHorizontal();
        return Mathf.RoundToInt(slid);
    }

    private bool RowToggle(bool value, string label)
    {
        return GUILayout.Button(label, value ? _sRowOn : _sRow) ? !value : value;
    }

    private void DrawHpBar(float percent)
    {
        Rect r = GUILayoutUtility.GetRect(10f, 16f, GUILayout.ExpandWidth(true));
        float clamped = Mathf.Clamp(percent, 0f, 100f);
        Color fill = ColOk;
        if (Plugin.RepairEnabled && clamped <= Plugin.RepairPercent)
            fill = ColDanger;
        else if (Plugin.FleeEnabled && clamped <= Plugin.FleePercent)
            fill = ColWarn;

        Color previous = GUI.color;
        GUI.color = ColField;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = fill;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * clamped / 100f, r.height), Texture2D.whiteTexture);
        if (Plugin.RepairEnabled)
        {
            GUI.color = ColAccent;
            GUI.DrawTexture(
                new Rect(r.x + r.width * Plugin.RepairPercent / 100f - 1f, r.y - 2f, 2f, r.height + 4f),
                Texture2D.whiteTexture);
        }
        if (Plugin.FleeEnabled)
        {
            GUI.color = ColWarn;
            GUI.DrawTexture(
                new Rect(r.x + r.width * Plugin.FleePercent / 100f - 1f, r.y - 2f, 2f, r.height + 4f),
                Texture2D.whiteTexture);
        }
        GUI.color = previous;
        GUILayout.Space(2f);
        Hint("Markers: blue = repair threshold, orange = flee threshold.");
    }

    // ------------------------------------------------------------------ Styles

    private static Texture2D MakeTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        return texture;
    }

    // Le constructeur de copie GUIStyle(GUIStyle) n'existe pas dans les assemblies
    // IL2CPP : copie manuelle des propriétés utilisées.
    private static GUIStyle CloneStyle(GUIStyle source)
    {
        GUIStyle style = new GUIStyle();
        style.font = source.font;
        style.fontSize = source.fontSize;
        style.fontStyle = source.fontStyle;
        style.alignment = source.alignment;
        style.wordWrap = source.wordWrap;
        style.richText = source.richText;
        style.clipping = source.clipping;
        style.imagePosition = source.imagePosition;
        style.stretchWidth = source.stretchWidth;
        style.stretchHeight = source.stretchHeight;
        style.fixedWidth = source.fixedWidth;
        style.fixedHeight = source.fixedHeight;
        style.padding = CloneOffset(source.padding);
        style.margin = CloneOffset(source.margin);
        style.border = CloneOffset(source.border);
        CopyState(style.normal, source.normal);
        CopyState(style.hover, source.hover);
        CopyState(style.active, source.active);
        CopyState(style.focused, source.focused);
        return style;
    }

    private static RectOffset CloneOffset(RectOffset source)
    {
        return new RectOffset(source.left, source.right, source.top, source.bottom);
    }

    private static void CopyState(GUIStyleState target, GUIStyleState source)
    {
        target.background = source.background;
        target.textColor = source.textColor;
    }

    private static void Paint(GUIStyle style, Color normal, Color hover, Color text)
    {
        style.normal.background = MakeTexture(normal);
        style.hover.background = MakeTexture(hover);
        style.active.background = MakeTexture(hover);
        style.focused.background = MakeTexture(normal);
        style.normal.textColor = text;
        style.hover.textColor = text;
        style.active.textColor = text;
        style.focused.textColor = text;
    }

    private void EnsureStyles()
    {
        if (_stylesReady && _texBg != null)
            return;

        _texBg = MakeTexture(ColBg);
        GUISkin skin = GUI.skin;

        _sWindow = CloneStyle(skin.box);
        _sWindow.normal.background = _texBg;
        _sWindow.border = new RectOffset(0, 0, 0, 0);

        _sHeader = CloneStyle(skin.box);
        _sHeader.normal.background = MakeTexture(ColHeader);
        _sHeader.border = new RectOffset(0, 0, 0, 0);

        _sPill = CloneStyle(skin.box);
        _sPill.normal.background = Texture2D.whiteTexture;
        _sPill.border = new RectOffset(0, 0, 0, 0);

        _sCard = CloneStyle(skin.box);
        _sCard.normal.background = MakeTexture(ColCard);
        _sCard.border = new RectOffset(0, 0, 0, 0);
        _sCard.padding = new RectOffset(14, 14, 12, 12);
        _sCard.margin = new RectOffset(0, 0, 0, 0);

        _sLabel = CloneStyle(skin.label);
        _sLabel.fontSize = 13;
        _sLabel.alignment = TextAnchor.MiddleLeft;
        _sLabel.wordWrap = true;
        _sLabel.normal.textColor = ColText;

        _sTitle = CloneStyle(_sLabel);
        _sTitle.fontSize = 14;
        _sTitle.fontStyle = FontStyle.Bold;
        _sTitle.wordWrap = false;

        _sCardTitle = CloneStyle(_sLabel);
        _sCardTitle.fontSize = 11;
        _sCardTitle.fontStyle = FontStyle.Bold;
        _sCardTitle.normal.textColor = ColAccent;

        _sMuted = CloneStyle(_sLabel);
        _sMuted.fontSize = 12;
        _sMuted.normal.textColor = ColMuted;

        _sValue = CloneStyle(_sLabel);
        _sValue.alignment = TextAnchor.MiddleRight;
        _sValue.fontStyle = FontStyle.Bold;

        _sTab = CloneStyle(skin.button);
        Paint(_sTab, ColHeader, ColCard, ColMuted);
        _sTab.border = new RectOffset(0, 0, 0, 0);
        _sTab.fontSize = 13;

        _sTabOn = CloneStyle(_sTab);
        Paint(_sTabOn, ColHeader, ColHeader, ColText);
        _sTabOn.fontStyle = FontStyle.Bold;

        _sBtn = CloneStyle(skin.button);
        Paint(_sBtn, ColField, ColFieldHover, ColText);
        _sBtn.border = new RectOffset(0, 0, 0, 0);
        _sBtn.fontSize = 12;

        _sBtnPrimary = CloneStyle(_sBtn);
        Paint(_sBtnPrimary, ColAccent, new Color(0.35f, 0.72f, 1f, 1f), new Color(0.04f, 0.07f, 0.1f, 1f));
        _sBtnPrimary.fontStyle = FontStyle.Bold;

        _sBtnDanger = CloneStyle(_sBtn);
        Paint(_sBtnDanger, ColDanger, new Color(1f, 0.48f, 0.46f, 1f), new Color(0.1f, 0.03f, 0.03f, 1f));
        _sBtnDanger.fontStyle = FontStyle.Bold;

        _sSwitchOn = CloneStyle(_sBtn);
        Paint(_sSwitchOn, ColOk, new Color(0.4f, 0.93f, 0.7f, 1f), new Color(0.03f, 0.1f, 0.07f, 1f));
        _sSwitchOn.fontStyle = FontStyle.Bold;

        _sSwitchOff = CloneStyle(_sBtn);
        Paint(_sSwitchOff, ColField, ColFieldHover, ColMuted);
        _sSwitchOff.fontStyle = FontStyle.Bold;

        _sSeg = CloneStyle(_sBtn);
        _sSegOn = CloneStyle(_sBtn);
        Paint(_sSegOn, ColAccentDim, ColAccentDim, ColText);
        _sSegOn.fontStyle = FontStyle.Bold;

        _sRow = CloneStyle(_sBtn);
        _sRow.alignment = TextAnchor.MiddleLeft;
        _sRow.padding = new RectOffset(12, 8, 4, 4);
        Paint(_sRow, ColCard, ColFieldHover, ColMuted);

        _sRowOn = CloneStyle(_sRow);
        Paint(_sRowOn, ColAccentDim, ColAccentDim, ColText);

        _sSlider = CloneStyle(skin.horizontalSlider);
        _sSlider.normal.background = MakeTexture(ColField);
        _sSlider.fixedHeight = 6f;
        _sSlider.border = new RectOffset(0, 0, 0, 0);

        _sThumb = CloneStyle(skin.horizontalSliderThumb);
        _sThumb.normal.background = MakeTexture(ColAccent);
        _sThumb.hover.background = MakeTexture(new Color(0.35f, 0.72f, 1f, 1f));
        _sThumb.active.background = _sThumb.hover.background;
        _sThumb.fixedWidth = 14f;
        _sThumb.fixedHeight = 14f;
        _sThumb.border = new RectOffset(0, 0, 0, 0);

        _pillText = null;
        _stylesReady = true;
    }

    // ------------------------------------------------------------------ Collecte

    [HideFromIl2Cpp]
    private string CollectionStatusMessage()
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

        string counts = "Live collectibles: " + observedCount
            + " | matching enabled types: " + enabledObservedCount
            + " | enabled types: " + _enabledCollectibleTypes.Count + ". ";

        if (!CopperWire.AutomationEnabled)
            return counts + "Collection is blocked: press PLAY (automation starts STOP).";
        if (!_collectEnabled)
            return counts + "Collection is blocked: turn on Collect in the Control tab.";
        if (_enabledCollectibleTypes.Count == 0)
            return counts + "Collection is blocked: enable at least one collectible type below.";
        if (snapshot == null || snapshot.Joueur == null)
            return counts + "Waiting for a GameState player snapshot.";
        if (observedCount == 0)
            return counts + "No collectible is currently visible in the GameState snapshot.";
        if (enabledObservedCount == 0)
            return counts + "Collectibles are visible, but none match an enabled type.";
        if (action != null && action.Type == BehaviorActionType.Collect)
        {
            CollectActionContext context = action.Context as CollectActionContext;
            return counts + "Collect action: " + action.State
                + (context == null ? string.Empty : " | NetId " + context.NetId + " | " + context.Type);
        }
        if (action != null)
            return counts + "Current action: " + action.Type + " / " + action.State;
        return counts + "An eligible collectible is visible; waiting for the next planner tick.";
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

    // ------------------------------------------------------------------ Cibles

    [HideFromIl2Cpp]
    private void DrawTargetGroup(
        TargetCategory category,
        string label)
    {
        List<string> catalog = category == TargetCategory.Monster
            ? _displayMonsters
            : _displayNpcs;
        BeginCard(label, catalog.Count + " types in catalog");
        if (!TargetCatalog.IsInitialized)
        {
            Hint("Waiting for the runtime target catalog.");
            EndCard();
            return;
        }

        for (int i = 0; i < catalog.Count; i++)
        {
            string name = catalog[i];
            string targetKey = GetTargetKey(category, name);

            GUILayout.BeginHorizontal();
            bool wasSelected = IsTargetSelected(category, name);
            bool isSelected = RowToggle(wasSelected, name);
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
                    ? (category == TargetCategory.Npc ? "Cannonball unavailable" : "Ammo unavailable")
                    : (category == TargetCategory.Npc ? "Cannonball: " : "Harpoon: ")
                        + DescribeAmmo(category, id);
                if (GUILayout.Button(caption, _sBtn, GUILayout.Width(260f), GUILayout.Height(26f)))
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
            Hint("No types in this catalog.");
        EndCard();
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

        GUILayout.BeginVertical(_sCard);
        GUILayout.Label(
            (_editingAmmoCategory == TargetCategory.Npc ? "Cannonballs for NPC / " : "Harpoons for Monster / ")
            + _editingAmmoName
            + " (applies to every runtime instance)",
            _sMuted);
        if (GUILayout.Button("Close ammo list", _sBtn, GUILayout.Height(24f)))
        {
            CloseAmmoEditor();
            GUILayout.EndVertical();
            return;
        }

        IReadOnlyList<AmmoDefinition> catalog = GetAmmoCatalog(_editingAmmoCategory);
        if (catalog.Count == 0)
        {
            Hint("Waiting for the matching live ammo catalog.");
        }
        else
        {
            int? currentId = ResolveAmmo(_editingAmmoCategory, _editingAmmoName);
            for (int i = 0; i < catalog.Count; i++)
            {
                AmmoDefinition ammo = catalog[i];
                bool isSelected = currentId.HasValue && currentId.Value == ammo.Id;
                bool choose = RowToggle(isSelected, FormatAmmo(ammo));
                if (choose && !isSelected)
                {
                    _ammoByTarget[_editingAmmoTarget] = ammo.Id;
                    ApplyConfiguration();
                }
            }
        }
        GUILayout.EndVertical();
        GUILayout.Space(6f);
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
