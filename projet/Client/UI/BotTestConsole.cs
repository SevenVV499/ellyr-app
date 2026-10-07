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
    private const int TabRaid = 4;
    private const int TabResources = 5;
    private const int TabEvents = 6;
    private static readonly string[] TabLabels =
        { "Paramètres", "Santé", "Cibles", "Collecte", "Raid", "Ressources", "Annonces" };

    private const float HeaderHeight = 30f;
    private const float NavHeight = 30f;
    private const float TitleHeight = 22f;
    private const float BodyTop = HeaderHeight + NavHeight + TitleHeight + 4f;
    private const float DropItemHeight = 20f;
    private const int DropMaxRows = 7;

    // Palette
    private static readonly Color ColBg = new Color(0.063f, 0.082f, 0.110f, 0.98f);
    private static readonly Color ColHeader = new Color(0.063f, 0.082f, 0.110f, 1f);
    private static readonly Color ColSidebar = new Color(0.047f, 0.063f, 0.086f, 1f);
    private static readonly Color ColLine = new Color(0.114f, 0.145f, 0.192f, 1f);
    private static readonly Color ColCard = new Color(0.078f, 0.102f, 0.137f, 1f);
    private static readonly Color ColField = new Color(0.098f, 0.129f, 0.173f, 1f);
    private static readonly Color ColFieldHover = new Color(0.141f, 0.176f, 0.231f, 1f);
    private static readonly Color ColBox = new Color(0.043f, 0.055f, 0.075f, 1f);
    private static readonly Color ColBoxEdge = new Color(0.30f, 0.35f, 0.42f, 1f);
    private static readonly Color ColAccent = new Color(0.239f, 0.608f, 1f, 1f);
    private static readonly Color ColAccentDim = new Color(0.090f, 0.227f, 0.388f, 1f);
    private static readonly Color ColEdge = new Color(0.157f, 0.196f, 0.263f, 1f);
    private static readonly Color ColFaint = new Color(0.30f, 0.37f, 0.46f, 1f);
    private static readonly Color ColSel = new Color(0.239f, 0.608f, 1f, 0.16f);
    private static readonly Color ColSelEdge = new Color(0.239f, 0.608f, 1f, 0.45f);
    private static readonly Color ColClear = new Color(0f, 0f, 0f, 0f);
    private static readonly Color ColNavOn = new Color(0.60f, 0.80f, 1f, 1f);
    private static readonly Color ColOk = new Color(0.24f, 0.86f, 0.59f, 1f);
    private static readonly Color ColWarn = new Color(0.96f, 0.65f, 0.14f, 1f);
    private static readonly Color ColDanger = new Color(0.95f, 0.37f, 0.36f, 1f);
    private static readonly Color ColText = new Color(0.90f, 0.91f, 0.94f, 1f);
    private static readonly Color ColMuted = new Color(0.486f, 0.533f, 0.600f, 1f);

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

    private Rect _panel = new Rect(16f, 16f, 300f, 300f);
    private Vector2 _scrollPosition;
    private bool _visible = true;
    private bool _dragging;

    // Vrai quand le pointeur est sur la console (ou qu'on la déplace) : lu par le patch du
    // clic sur la mer pour que le clic ne traverse pas la console jusqu'au jeu.
    private static bool _pointerOverConsole;
    public static bool PointerOverConsole { get { return _pointerOverConsole; } }
    private Vector2 _dragOffset;
    private int _activeTab;
    private bool _collectEnabled;
    private bool _combatEnabled;
    private IReadOnlyList<Type> _cachedCollectibleCatalog;
    private List<string> _displayCollectibleTypes = new List<string>();
    private CombatCollectPriority _priority = CombatCollectPriority.Collect;
    private string _editingAmmoTarget;
    private Vector2 _ammoScroll;
    private bool _openNpcs;
    private bool _openMonsters;

    // Styles (construits dans OnGUI : GUI.skin n'est accessible que là)
    private bool _stylesReady;
    private GUISkin _skin;
    private Font _font;
    private Texture2D _texBg;
    private Texture2D _texDot;
    private Texture2D _texCheck;
    private Texture2D _texBoxOff;
    private Texture2D _texBoxHover;
    private Texture2D _texBoxOn;
    private GUIStyle _sWindow;
    private GUIStyle _sTitle;
    private GUIStyle _sCardTitle;
    private GUIStyle _sLabel;
    private GUIStyle _sMuted;
    private GUIStyle _sValue;
    private GUIStyle _sTab;
    private GUIStyle _sTabOn;
    private GUIStyle _sBtn;
    private GUIStyle _sBtnPrimary;
    private GUIStyle _sBtnDanger;
    private GUIStyle _sSeg;
    private GUIStyle _sSegOn;
    private GUIStyle _sRow;
    private GUIStyle _sRowOn;
    private GUIStyle _sSlider;
    private GUIStyle _sThumb;
    private GUIStyle _sTrack;
    private GUIStyle _sTrackFill;
    private GUIStyle _sFold;
    private GUIStyle _sLabelClip;
    private GUIStyle _sLabelAccent;
    private GUIStyle _sSelect;
    private GUIStyle _sSelectOpen;
    private GUIStyle _sPopup;
    private GUIStyle _sItemHover;
    private GUIStyle _sTip;
    private GUIStyle _sTgOff;
    private GUIStyle _sTgOn;
    private GUIStyle _sRight;
    private Texture2D _texPlay;
    private Texture2D _texStop;
    private readonly List<Texture2D> _icons = new List<Texture2D>();
    private TargetCategory _ddCategory;
    private string _ddName;
    private Rect _ddAnchor;
    private float _ddScroll;
    private int _hoverTab = -1;

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
        {
            _pointerOverConsole = false;
            return;
        }

        EnsureStyles();

        // La console utilise sa propre copie du skin : les autres interfaces du jeu ne sont pas touchées.
        GUISkin previousSkin = GUI.skin;
        if (_skin != null)
            GUI.skin = _skin;
        try
        {
            DrawConsole();
        }
        finally
        {
            GUI.skin = previousSkin;
        }
    }

    private void DrawConsole()
    {
        _panel.width = Mathf.Min(_panel.width, Mathf.Max(260f, Screen.width - 16f));
        _panel.height = Mathf.Min(_panel.height, Mathf.Max(220f, Screen.height - 16f));
        _panel.x = Mathf.Clamp(_panel.x, 0f, Mathf.Max(0f, Screen.width - _panel.width));
        _panel.y = Mathf.Clamp(_panel.y, 0f, Mathf.Max(0f, Screen.height - _panel.height));

        Event current = Event.current;
        _pointerOverConsole = _dragging
            || (current != null && _panel.Contains(current.mousePosition));

        Rect body = new Rect(_panel.x + 10f, _panel.y + BodyTop, _panel.width - 20f, _panel.height - BodyTop - 8f);
        HandleDropdownInput(body);

        GUI.Box(_panel, GUIContent.none, _sWindow);
        HandleDrag();
        DrawHeader();
        DrawNav();
        GUI.Label(
            new Rect(_panel.x + 12f, _panel.y + HeaderHeight + NavHeight + 4f, _panel.width - 24f, TitleHeight),
            TabLabels[_activeTab],
            _sTitle);

        GUILayout.BeginArea(body);
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
        switch (_activeTab)
        {
            case TabControl: DrawControlTab(); break;
            case TabSurvival: DrawSurvivalTab(); break;
            case TabTargets: DrawTargetsTab(); break;
            case TabCollect: DrawCollectTab(); break;
            case TabRaid: DrawRaidTab(); break;
            case TabResources: DrawResourcesTab(); break;
            default: DrawEventsTab(); break;
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        DrawDropdownOverlay(body);
        DrawNavTooltip();
    }

    private void HandleDrag()
    {
        Event e = Event.current;
        // Zone de déplacement : l'en-tête sans les boutons de droite.
        Rect grip = new Rect(_panel.x, _panel.y, _panel.width - 80f, HeaderHeight);
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
        GUI.Label(new Rect(_panel.x + 12f, _panel.y + 5f, 50f, 20f), "ELLYR", _sTitle);
        DrawDot(new Rect(_panel.x + 60f, _panel.y + 11f, 8f, 8f), StateColor());

        bool running = CopperWire.AutomationEnabled;
        Rect play = new Rect(_panel.xMax - 46f, _panel.y + 5f, 34f, 20f);
        if (GUI.Button(play, GUIContent.none, running ? _sBtnDanger : _sBtnPrimary))
            CopperWire.SetAutomationEnabled(!running);
        if (Event.current.type == EventType.Repaint)
            DrawIcon(
                running ? _texStop : _texPlay,
                new Rect(play.center.x - 5f, play.center.y - 5f, 10f, 10f),
                running ? ColDanger : new Color(0.02f, 0.06f, 0.12f, 1f));
    }

    private void DrawDot(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, _texDot);
        GUI.color = previous;
    }

    // Texte de l'état : discret, aligné à gauche.
    private GUIStyle _pillText;
    private GUIStyle PillTextStyle()
    {
        if (_pillText == null)
        {
            _pillText = CloneStyle(_sLabel);
            _pillText.alignment = TextAnchor.MiddleLeft;
            _pillText.fontSize = 10;
            _pillText.wordWrap = false;
            _pillText.normal.textColor = ColMuted;
        }
        return _pillText;
    }

    private static string ActionLabel(BehaviorActionType type)
    {
        switch (type)
        {
            case BehaviorActionType.Navigation: return "Navigation";
            case BehaviorActionType.Collect: return "Collecte";
            case BehaviorActionType.Combat: return "Combat";
            default: return "Aucune";
        }
    }

    private static string ActionStateLabel(BehaviorActionState state)
    {
        switch (state)
        {
            case BehaviorActionState.Running: return "en cours";
            case BehaviorActionState.Completed: return "terminée";
            case BehaviorActionState.Failed: return "échec";
            case BehaviorActionState.Cancelled: return "annulée";
            default: return state.ToString();
        }
    }

    private string StateText()
    {
        if (!CopperWire.AutomationEnabled)
            return "ARRÊTÉ";
        if (RespawnRules.IsActive)
            return "RÉAPPARITION";
        if (SurvivalRules.IsFleeing)
            return "FUITE";
        if (SurvivalRules.IsRepairPaused)
            return "RÉPARATION";
        BehaviorAction action = CopperWire.CurrentAction;
        return action == null ? "INACTIF" : ActionLabel(action.Type).ToUpperInvariant();
    }

    private Color StateColor()
    {
        if (!CopperWire.AutomationEnabled)
            return ColMuted;
        if (SurvivalRules.IsFleeing)
            return ColDanger;
        if (RespawnRules.IsActive || SurvivalRules.IsRepairPaused)
            return ColWarn;
        return ColOk;
    }

    private void DrawNav()
    {
        float top = _panel.y + HeaderHeight;
        FillRect(new Rect(_panel.x + 10f, top + NavHeight, _panel.width - 20f, 1f), ColLine);

        float cell = (_panel.width - 16f) / TabLabels.Length;
        Vector2 mouse = Event.current.mousePosition;
        bool repaint = Event.current.type == EventType.Repaint;
        _hoverTab = -1;
        for (int i = 0; i < TabLabels.Length; i++)
        {
            Rect rect = new Rect(_panel.x + 8f + i * cell + 1f, top + 3f, cell - 2f, NavHeight - 6f);
            bool selected = _activeTab == i;
            bool hover = rect.Contains(mouse);
            if (hover)
                _hoverTab = i;
            if (GUI.Button(rect, GUIContent.none, selected ? _sTabOn : _sTab) && !selected)
            {
                _activeTab = i;
                _scrollPosition = Vector2.zero;
                CloseAmmoEditor();
            }
            if (repaint)
                DrawIcon(_icons[i], new Rect(rect.center.x - 7f, rect.center.y - 7f, 14f, 14f),
                    selected ? ColNavOn : (hover ? ColText : ColMuted));
        }
    }

    private void DrawNavTooltip()
    {
        if (_hoverTab < 0 || Event.current.type != EventType.Repaint)
            return;

        string text = TabLabels[_hoverTab];
        float width = text.Length * 6.4f + 16f;
        float cell = (_panel.width - 16f) / TabLabels.Length;
        float centre = _panel.x + 8f + (_hoverTab + 0.5f) * cell;
        Rect tip = new Rect(
            Mathf.Clamp(centre - width * 0.5f, _panel.x + 4f, _panel.xMax - width - 4f),
            _panel.y + HeaderHeight + NavHeight + 4f,
            width,
            20f);
        GUI.Box(tip, text, _sTip);
    }

    private static void DrawIcon(Texture2D icon, Rect rect, Color color)
    {
        if (icon == null)
            return;
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, icon);
        GUI.color = previous;
    }

    private static void FillRect(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    // ------------------------------------------------------------------ Onglets

    private void DrawControlTab()
    {
        BeginCard("Activités");
        bool collect = Switch(_collectEnabled, "Collecte");
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

        // La priorité n'a de sens que si les deux activités sont cochées.
        if (_collectEnabled && _combatEnabled)
        {
            bool combatFirst = _priority == CombatCollectPriority.Combat;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Priorité", _sMuted, GUILayout.Width(50f), GUILayout.Height(22f));
            GUILayout.Label("Collecte", combatFirst ? _sMuted : _sLabelClip, GUILayout.Width(52f), GUILayout.Height(22f));
            bool combatSide = TwoWaySwitch(combatFirst);
            GUILayout.Label("Combat", combatSide ? _sLabelClip : _sMuted, GUILayout.Height(22f));
            GUILayout.EndHorizontal();
            SetPriority(combatSide ? CombatCollectPriority.Combat : CombatCollectPriority.Collect);
        }
        EndCard();

        BeginCard("Modules");
        bool repair = Switch(Plugin.RepairEnabled, "Réparation");
        if (repair != Plugin.RepairEnabled)
            Plugin.SetRepairEnabled(repair);

        bool respawn = Switch(Plugin.RespawnEnabled, "Réapparition");
        if (respawn != Plugin.RespawnEnabled)
            Plugin.SetRespawnEnabled(respawn);

        bool raid = Switch(Plugin.RaidEnabled, "Carte Raid");
        if (raid != Plugin.RaidEnabled)
            Plugin.SetRaidEnabled(raid);
        EndCard();
    }

    private void DrawResourcesTab()
    {
        float elapsed = ResourceTracker.ElapsedSeconds;

        GUILayout.BeginHorizontal();
        GUILayout.Label(FormatDuration(elapsed), _sLabel);
        if (GUILayout.Button("Remise à zéro", _sBtn, GUILayout.Width(100f), GUILayout.Height(20f)))
            ResourceTracker.Reset();
        GUILayout.EndHorizontal();
        Divider();

        IReadOnlyList<ResourceTracker.Counter> counters = ResourceTracker.Counters;
        int shown = 0;
        for (int i = 0; i < counters.Count; i++)
        {
            ResourceTracker.Counter counter = counters[i];
            if (counter.Total == 0)
                continue;

            shown++;
            GUILayout.BeginHorizontal();
            GUILayout.Label(counter.Name, _sLabelClip, GUILayout.Height(18f));
            GUILayout.Label(counter.TotalText, _sValue, GUILayout.Width(74f));
            GUILayout.Label(counter.PerHourText(elapsed), _sMuted, GUILayout.Width(70f));
            GUILayout.EndHorizontal();
        }

        if (shown == 0)
            Hint(counters.Count == 0 ? "En attente des compteurs." : "Aucun compteur n'a bougé.");
    }

    private static string FormatDuration(float seconds)
    {
        int total = (int)seconds;
        return (total / 3600).ToString(CultureInfo.InvariantCulture) + " h "
            + ((total % 3600) / 60).ToString("00", CultureInfo.InvariantCulture) + " min "
            + (total % 60).ToString("00", CultureInfo.InvariantCulture) + " s";
    }

    private void DrawRaidTab()
    {
        EtatJeuSnapshot raidSnapshot = GameState.ObtenirSnapshot();
        FicheJoueur player = raidSnapshot == null ? null : raidSnapshot.Joueur;

        BeginCard("Raid", player == null ? null : "Niveau " + player.Niveau);
        bool bossPriority = Switch(Plugin.RaidBossPriority, "Boss en priorité");
        if (bossPriority != Plugin.RaidBossPriority)
            Plugin.SetRaidBossPriority(bossPriority);
        DrawFullHealthSwitch();
        DrawLongRangeSwitch();

        if (player != null)
        {
            int damage = player.RaidHasar;
            int cap = RulesData.Raid.BossDamageCap;
            GUILayout.Space(4f);
            Row("Dégâts boss", damage.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.')
                + " / " + cap.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.'));
            DrawBar(cap <= 0 ? 0f : damage / (float)cap, damage >= cap ? ColWarn : ColAccent, 4f);
        }
        EndCard();

        BeginCard("Petite Raid", player == null ? null : "Talisman " + player.Talisman);
        DrawRaidTargetRow("Sunburst", "mob");
        DrawRaidTargetRow("Ameterasu", "boss");
        EndCard();

        BeginCard("Grande Raid", player == null ? null : "Talisman " + player.TalismanAcemi);
        DrawRaidTargetRow("Léviathan", "mob");
        DrawRaidTargetRow("Behemoth", "boss");
        EndCard();
    }

    [HideFromIl2Cpp]
    private void DrawRaidTargetRow(string name, string role)
    {
        TargetCategory category = RaidRules.WeaponCategoryFor(name);
        GUILayout.BeginHorizontal();
        GUILayout.Label(name + " (" + role + ")", _sLabelClip, GUILayout.Height(20f));
        AmmoField(category, name, 138f);
        GUILayout.EndHorizontal();
    }

    private void DrawSurvivalTab()
    {
        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        FicheJoueur player = snapshot == null ? null : snapshot.Joueur;

        BeginCard("Coque");
        if (player == null || player.VieMax <= 0)
        {
            Hint("En attente du joueur.");
        }
        else
        {
            Row("PV", player.Vie + " / " + player.VieMax
                + "  (" + player.PourcentageVie.ToString("0", CultureInfo.InvariantCulture) + " %)");
            DrawHpBar(player.PourcentageVie);
        }
        EndCard();

        BeginCard("Réparation");
        int repairMode = Segmented(
            Plugin.RepairPausesActivity ? 1 : 0,
            "En activité",
            "À l'arrêt");
        Plugin.SetRepairPausesActivity(repairMode == 1);

        int repairPercent = PercentSlider("Réparer si PV <=", Plugin.RepairPercent);
        if (repairPercent != Plugin.RepairPercent)
            Plugin.SetRepairPercent(repairPercent);
        EndCard();

        BeginCard("PV bas");
        bool flee = Switch(Plugin.FleeEnabled, "Fuite activée");
        if (flee != Plugin.FleeEnabled)
            Plugin.SetFleeEnabled(flee);

        int fleePercent = PercentSlider("Fuir si PV <=", Plugin.FleePercent);
        if (fleePercent != Plugin.FleePercent)
            Plugin.SetFleePercent(fleePercent);

        bool fleeCollect = Switch(Plugin.FleeCollectEnabled, "Collecter pendant la fuite");
        if (fleeCollect != Plugin.FleeCollectEnabled)
            Plugin.SetFleeCollectEnabled(fleeCollect);
        EndCard();
    }

    private void DrawTargetsTab()
    {
        RefreshDisplayCatalogs();

        DrawFullHealthSwitch();
        DrawLongRangeSwitch();
        Divider();

        DrawTargetGroup(TargetCategory.Npc, "NPC");
        DrawTargetGroup(TargetCategory.Monster, "Monstres");
    }

    // Même réglage que dans Contrôle : l'option est accessible depuis Cibles et Carte Raid.
    private void DrawFullHealthSwitch()
    {
        bool fullHealth = Switch(Plugin.OnlyFullHealthTargets, "Cibles à PV max seulement");
        if (fullHealth != Plugin.OnlyFullHealthTargets)
            Plugin.SetOnlyFullHealthTargets(fullHealth);
    }

    // Même réglage que l'ancienne case de Contrôle : accessible depuis Cibles et Raid.
    private void DrawLongRangeSwitch()
    {
        bool longRange = Switch(CopperWire.LongRange, "Longue portée");
        if (longRange != CopperWire.LongRange)
            CopperWire.SetLongRange(longRange);
    }

    private void DrawCollectTab()
    {
        RefreshCollectibleCatalog();

        BeginCard("Types   " + _enabledCollectibleTypes.Count + " / " + _displayCollectibleTypes.Count);
        for (int i = 0; i < _displayCollectibleTypes.Count; i++)
        {
            string typeName = _displayCollectibleTypes[i];
            bool wasEnabled = _enabledCollectibleTypes.Contains(typeName);
            bool isEnabled = Switch(wasEnabled, typeName);
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
            Hint("Aucun type pour l'instant.");
        EndCard();
    }

    // ------------------------------------------------------------------ Événements

    // Navires d'événement présents côté client, alimentés par le scan réseau : un navire
    // qui n'est plus présent ou qui tombe à 0 PV sort de la liste.
    private const string EventShipCategory = "npc_navire_event";
    private List<PnjInfo> _eventShips = new List<PnjInfo>();
    private DateTime _eventShipsStamp;

    [HideFromIl2Cpp]
    private void RefreshEventShips(EtatJeuSnapshot snapshot)
    {
        if (snapshot == null || snapshot.Pnjs == null)
        {
            _eventShips.Clear();
            _eventShipsStamp = default(DateTime);
            return;
        }

        // OnGUI s'exécute plusieurs fois par image : on ne reconstruit la liste que
        // lorsqu'un nouvel instantané est publié.
        if (snapshot.Timestamp == _eventShipsStamp)
            return;

        _eventShipsStamp = snapshot.Timestamp;
        _eventShips.Clear();
        for (int i = 0; i < snapshot.Pnjs.Count; i++)
        {
            PnjInfo pnj = snapshot.Pnjs[i];
            if (pnj != null
                && pnj.Vie > 0
                && string.Equals(pnj.Categorie, EventShipCategory, StringComparison.Ordinal))
                _eventShips.Add(pnj);
        }
        _eventShips.Sort(CompareEventShips);
    }

    [HideFromIl2Cpp]
    private static int CompareEventShips(PnjInfo a, PnjInfo b)
    {
        int carte = a.Harita.CompareTo(b.Harita);
        if (carte != 0)
            return carte;
        return string.Compare(a.Nom, b.Nom, StringComparison.OrdinalIgnoreCase);
    }

    [HideFromIl2Cpp]
    private void DrawEventsTab()
    {
        RefreshEventShips(GameState.ObtenirSnapshot());

        BeginCard("Navires d'événement   " + _eventShips.Count);
        if (_eventShips.Count == 0)
            Hint("Aucun navire pour l'instant.");
        for (int i = 0; i < _eventShips.Count; i++)
            DrawEventShipRow(_eventShips[i]);
        EndCard();
    }

    [HideFromIl2Cpp]
    private void DrawEventShipRow(PnjInfo pnj)
    {
        GUILayout.BeginVertical(_sSeg);
        GUILayout.BeginHorizontal();
        GUILayout.Label(string.IsNullOrEmpty(pnj.Nom) ? "?" : pnj.Nom, _sLabel);
        GUILayout.Label("PV " + pnj.Vie + " / " + pnj.VieMax, _sValue, GUILayout.Width(150f));
        GUILayout.EndHorizontal();

        string carte = !string.IsNullOrEmpty(pnj.NomHarita)
            ? pnj.NomHarita
            : pnj.Harita > 0 ? "carte " + pnj.Harita : "carte inconnue";
        GUILayout.Label(
            carte + "  |  " + (pnj.CoordonneeSayi ?? "?") + " " + (pnj.CoordonneeHarf ?? "?"),
            _sMuted);
        GUILayout.EndVertical();
        GUILayout.Space(4f);
    }

    // ------------------------------------------------------------------ Composants

    // Section sans cadre : un petit titre, le contenu, puis un filet fin.
    private void BeginCard(string title, string right = null)
    {
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.Label(title.ToUpperInvariant(), _sCardTitle);
        if (!string.IsNullOrEmpty(right))
            GUILayout.Label(right, _sRight, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
        GUILayout.Space(3f);
    }

    // Interrupteur à deux positions : false = gauche, true = droite.
    private bool TwoWaySwitch(bool right)
    {
        Rect r = GUILayoutUtility.GetRect(30f, 22f, GUILayout.Width(30f), GUILayout.Height(22f));
        Rect track = new Rect(r.x, r.y + 3f, 30f, 16f);
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
        {
            right = !right;
            e.Use();
        }

        if (e.type == EventType.Repaint)
        {
            GUI.Box(track, GUIContent.none, right ? _sTgOn : _sTgOff);
            DrawDot(new Rect(right ? track.xMax - 13f : track.x + 3f, track.y + 3f, 10f, 10f), ColText);
        }
        return right;
    }

    private void EndCard()
    {
        GUILayout.EndVertical();
        Divider();
    }

    private void Divider()
    {
        GUILayout.Space(7f);
        Rect line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
            FillRect(line, ColLine);
        GUILayout.Space(8f);
    }

    private void Hint(string text)
    {
        GUILayout.Label(text, _sMuted);
    }

    private void Row(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _sMuted, GUILayout.Width(78f));
        GUILayout.Label(value, _sLabel);
        GUILayout.EndHorizontal();
    }

    // Case à cocher : petit carré arrondi, rempli en accent avec une coche quand c'est actif.
    private bool Switch(bool value, string label)
    {
        Rect row = GUILayoutUtility.GetRect(10f, 20f, GUILayout.ExpandWidth(true));
        Event e = Event.current;
        bool hover = row.Contains(e.mousePosition);
        if (e.type == EventType.MouseDown && e.button == 0 && hover)
        {
            value = !value;
            e.Use();
        }

        if (e.type == EventType.Repaint)
        {
            Rect box = new Rect(row.x + 1f, row.y + (row.height - 13f) * 0.5f, 13f, 13f);
            GUI.DrawTexture(box, value ? _texBoxOn : (hover ? _texBoxHover : _texBoxOff));
            if (value)
                GUI.DrawTexture(box, _texCheck);
        }

        GUI.Label(new Rect(row.x + 21f, row.y, row.width - 21f, row.height), label, _sLabelClip);
        return value;
    }

    private int Segmented(int index, string first, string second)
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(first, index == 0 ? _sSegOn : _sSeg, GUILayout.Height(22f)))
            index = 0;
        if (GUILayout.Button(second, index == 1 ? _sSegOn : _sSeg, GUILayout.Height(22f)))
            index = 1;
        GUILayout.EndHorizontal();
        return index;
    }

    private int PercentSlider(string label, int value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _sLabelClip, GUILayout.Width(104f), GUILayout.Height(20f));
        Rect r = GUILayoutUtility.GetRect(40f, 14f, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
        {
            Rect track = new Rect(r.x + 7f, r.center.y - 2f, r.width - 14f, 4f);
            GUI.Box(track, GUIContent.none, _sTrack);
            float fraction = Mathf.Clamp01(value / 100f);
            if (fraction > 0f)
            {
                Color previous = GUI.color;
                GUI.color = ColAccent;
                GUI.Box(
                    new Rect(track.x, track.y, Mathf.Max(4f, track.width * fraction), 4f),
                    GUIContent.none,
                    _sTrackFill);
                GUI.color = previous;
            }
        }
        float slid = GUI.HorizontalSlider(r, value, 0f, 100f, _sSlider, _sThumb);
        GUILayout.Label(Mathf.RoundToInt(slid) + " %", _sValue, GUILayout.Width(40f));
        GUILayout.EndHorizontal();
        return Mathf.RoundToInt(slid);
    }

    private bool RowToggle(bool value, string label)
    {
        return GUILayout.Button(label, value ? _sRowOn : _sRow) ? !value : value;
    }

    // Barre fine arrondie ; renvoie son rectangle pour y poser des repères.
    private Rect DrawBar(float fraction, Color fill, float height)
    {
        Rect r = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
        {
            GUI.Box(r, GUIContent.none, _sTrack);
            float clamped = Mathf.Clamp01(fraction);
            if (clamped > 0f)
            {
                Color previous = GUI.color;
                GUI.color = fill;
                GUI.Box(new Rect(r.x, r.y, Mathf.Max(4f, r.width * clamped), r.height), GUIContent.none, _sTrackFill);
                GUI.color = previous;
            }
        }
        return r;
    }

    private void DrawHpBar(float percent)
    {
        float clamped = Mathf.Clamp(percent, 0f, 100f);
        Color fill = ColOk;
        if (Plugin.RepairEnabled && clamped <= Plugin.RepairPercent)
            fill = ColDanger;
        else if (Plugin.FleeEnabled && clamped <= Plugin.FleePercent)
            fill = ColWarn;

        GUILayout.Space(2f);
        Rect r = DrawBar(clamped / 100f, fill, 5f);
        if (Event.current.type == EventType.Repaint)
        {
            if (Plugin.RepairEnabled)
                FillRect(new Rect(r.x + r.width * Plugin.RepairPercent / 100f - 1f, r.y - 2f, 2f, r.height + 4f), ColAccent);
            if (Plugin.FleeEnabled)
                FillRect(new Rect(r.x + r.width * Plugin.FleePercent / 100f - 1f, r.y - 2f, 2f, r.height + 4f), ColWarn);
        }
        GUILayout.Space(2f);
    }

    // ------------------------------------------------------------------ Sélecteur de munitions

    // Champ fermé : nom de la munition + chevron. Un clic ouvre la liste complète, dessinée
    // par-dessus la fenêtre (voir DrawDropdownOverlay).
    [HideFromIl2Cpp]
    private void AmmoField(TargetCategory category, string name, float width)
    {
        string key = GetTargetKey(category, name);
        bool open = string.Equals(_editingAmmoTarget, key, StringComparison.OrdinalIgnoreCase);
        Rect r = GUILayoutUtility.GetRect(width, 20f, GUILayout.Width(width));

        IReadOnlyList<AmmoDefinition> catalog = GetAmmoCatalog(category);
        string caption = catalog.Count == 0 ? "indisponible" : ShortAmmo(category, ResolveAmmo(category, name));

        if (GUI.Button(r, caption, open ? _sSelectOpen : _sSelect) && catalog.Count > 0)
        {
            if (open)
            {
                CloseAmmoEditor();
            }
            else
            {
                _editingAmmoTarget = key;
                _ddCategory = category;
                _ddName = name;
                _ddScroll = 0f;
                _ddAnchor = GUIUtility.GUIToScreenRect(r);
            }
        }

        if (Event.current.type == EventType.Repaint)
        {
            if (open)
                _ddAnchor = GUIUtility.GUIToScreenRect(r);
            DrawIcon(_icons[7], new Rect(r.xMax - 17f, r.center.y - 7f, 14f, 14f), ColMuted);
        }
    }

    [HideFromIl2Cpp]
    private bool TryGetDropdownLayout(Rect body, out Rect anchor, out Rect list, out IReadOnlyList<AmmoDefinition> items)
    {
        anchor = default(Rect);
        list = default(Rect);
        items = null;
        if (_editingAmmoTarget == null)
            return false;

        items = GetAmmoCatalog(_ddCategory);
        if (items.Count == 0)
            return false;

        anchor = GUIUtility.ScreenToGUIRect(_ddAnchor);
        if (!anchor.Overlaps(body))
            return false;

        float height = Mathf.Min(DropMaxRows, items.Count) * DropItemHeight + 8f;
        float width = Mathf.Min(Mathf.Max(anchor.width, 190f), _panel.width - 12f);
        float x = Mathf.Clamp(anchor.xMax - width, _panel.x + 6f, _panel.xMax - width - 6f);
        float y = anchor.yMax + 2f;
        if (y + height > _panel.yMax - 6f)
            y = Mathf.Max(_panel.y + 6f, anchor.y - 2f - height);
        list = new Rect(x, y, width, height);
        return true;
    }

    // Traité AVANT le reste de l'interface : la liste ouverte capte ses propres clics
    // et les contrôles situés dessous ne les voient pas.
    [HideFromIl2Cpp]
    private void HandleDropdownInput(Rect body)
    {
        if (_editingAmmoTarget == null)
            return;

        Rect anchor;
        Rect list;
        IReadOnlyList<AmmoDefinition> items;
        if (!TryGetDropdownLayout(body, out anchor, out list, out items))
        {
            CloseAmmoEditor();
            return;
        }

        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            CloseAmmoEditor();
            e.Use();
            return;
        }

        if (e.type == EventType.ScrollWheel && list.Contains(e.mousePosition))
        {
            float max = Mathf.Max(0f, items.Count * DropItemHeight + 4f - (list.height - 4f));
            _ddScroll = Mathf.Clamp(_ddScroll + e.delta.y * DropItemHeight, 0f, max);
            e.Use();
            return;
        }

        if (e.type != EventType.MouseDown || e.button != 0)
            return;

        if (list.Contains(e.mousePosition))
        {
            int index = Mathf.FloorToInt((e.mousePosition.y - list.y - 4f + _ddScroll) / DropItemHeight);
            if (index >= 0 && index < items.Count)
            {
                _ammoByTarget[GetTargetKey(_ddCategory, _ddName)] = items[index].Id;
                ApplyConfiguration();
                CloseAmmoEditor();
            }
            e.Use();
        }
        else if (anchor.Contains(e.mousePosition))
        {
            CloseAmmoEditor();
            e.Use();
        }
        else
        {
            CloseAmmoEditor();
        }
    }

    [HideFromIl2Cpp]
    private void DrawDropdownOverlay(Rect body)
    {
        if (Event.current.type != EventType.Repaint || _editingAmmoTarget == null)
            return;

        Rect anchor;
        Rect list;
        IReadOnlyList<AmmoDefinition> items;
        if (!TryGetDropdownLayout(body, out anchor, out list, out items))
            return;

        GUI.Box(list, GUIContent.none, _sPopup);
        int? current = ResolveAmmo(_ddCategory, _ddName);
        Vector2 mouse = Event.current.mousePosition;
        Vector2 origin = new Vector2(list.x + 2f, list.y + 2f);

        GUI.BeginGroup(new Rect(origin.x, origin.y, list.width - 4f, list.height - 4f));
        for (int i = 0; i < items.Count; i++)
        {
            Rect item = new Rect(2f, 2f + i * DropItemHeight - _ddScroll, list.width - 12f, DropItemHeight);
            if (item.yMax < 0f || item.y > list.height)
                continue;

            bool isCurrent = current.HasValue && current.Value == items[i].Id;
            bool hover = list.Contains(mouse) && item.Contains(mouse - origin);
            if (isCurrent)
                GUI.Box(item, GUIContent.none, _sRowOn);
            else if (hover)
                GUI.Box(item, GUIContent.none, _sItemHover);
            GUI.Label(
                new Rect(item.x + 8f, item.y, item.width - 12f, item.height),
                FormatAmmo(items[i]),
                isCurrent ? _sLabelAccent : _sLabelClip);
        }
        GUI.EndGroup();

        float content = items.Count * DropItemHeight + 4f;
        float viewport = list.height - 4f;
        if (content > viewport)
        {
            float thumbHeight = Mathf.Max(14f, viewport * viewport / content);
            float travel = viewport - thumbHeight - 4f;
            float t = _ddScroll / (content - viewport);
            FillRect(new Rect(list.xMax - 6f, list.y + 4f + travel * t, 3f, thumbHeight), ColBoxEdge);
        }
    }

    [HideFromIl2Cpp]
    private string ShortAmmo(TargetCategory category, int? id)
    {
        if (!id.HasValue)
            return "indisponible";

        IReadOnlyList<AmmoDefinition> catalog = GetAmmoCatalog(category);
        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog[i].Id == id.Value)
                return string.IsNullOrWhiteSpace(catalog[i].LocalizationKey)
                    ? catalog[i].InternalName
                    : catalog[i].LocalizationKey;
        }
        return "ID " + id.Value.ToString(CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------ Styles

    // Une texture 1x1 par couleur, partagée entre tous les styles.
    private static readonly Dictionary<Color, Texture2D> TextureCache =
        new Dictionary<Color, Texture2D>();

    private static Texture2D MakeTexture(Color color)
    {
        Texture2D texture;
        if (TextureCache.TryGetValue(color, out texture) && texture != null)
            return texture;

        texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        TextureCache[color] = texture;
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

    // Rectangle arrondi anti-aliasé généré en code. Avec un cadre (border) il sert de
    // fond « 9 tranches » aux GUIStyle ; sans cadre il sert d'image de taille exacte.
    private static readonly Dictionary<string, Texture2D> RoundCache =
        new Dictionary<string, Texture2D>();

    private static Texture2D RoundTexture(int size, int radius, Color fill, Color edge, float edgeWidth)
    {
        string key = size + "|" + radius + "|" + fill + "|" + edge + "|" + edgeWidth;
        Texture2D cached;
        if (RoundCache.TryGetValue(key, out cached) && cached != null)
            return cached;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        float half = size * 0.5f;
        float inner = half - radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - inner;
                float qy = Mathf.Abs(y + 0.5f - half) - inner;
                float ox = Mathf.Max(qx, 0f);
                float oy = Mathf.Max(qy, 0f);
                float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                float cover = Mathf.Clamp01(0.5f - d);
                float body = edgeWidth <= 0f ? 1f : Mathf.Clamp01(0.5f - (d + edgeWidth));
                Color c = Color.Lerp(edge, fill, body);
                c.a *= cover;
                texture.SetPixel(x, y, c);
            }
        }
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        RoundCache[key] = texture;
        return texture;
    }

    // Coche dessinée pixel par pixel (deux segments), sans dépendre d'une police.
    private static Texture2D CheckTexture(int size, Color ink)
    {
        string key = "check|" + size + "|" + ink;
        Texture2D cached;
        if (RoundCache.TryGetValue(key, out cached) && cached != null)
            return cached;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        float k = size / 16f;
        Vector2 a = new Vector2(3.6f, 8.2f) * k;
        Vector2 b = new Vector2(6.8f, 5.0f) * k;
        Vector2 c = new Vector2(12.6f, 11.4f) * k;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(DistanceToSegment(p, a, b), DistanceToSegment(p, b, c));
                Color pixel = ink;
                pixel.a = Mathf.Clamp01(k + 0.5f - d);
                texture.SetPixel(x, y, pixel);
            }
        }
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        RoundCache[key] = texture;
        return texture;
    }

    // Triangle plein (icône « lancer »), anti-aliasé par échantillonnage 4 x 4.
    private static Texture2D PlayTexture(int size)
    {
        string key = "play|" + size;
        Texture2D cached;
        if (RoundCache.TryGetValue(key, out cached) && cached != null)
            return cached;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        Vector2 a = new Vector2(0.24f, 0.08f) * size;
        Vector2 b = new Vector2(0.24f, 0.92f) * size;
        Vector2 c = new Vector2(0.92f, 0.5f) * size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 4; sy++)
                {
                    for (int sx = 0; sx < 4; sx++)
                    {
                        Vector2 p = new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f);
                        float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
                        float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
                        float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
                        bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
                        bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
                        if (!(negative && positive))
                            hits++;
                    }
                }
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, hits / 16f));
            }
        }
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        RoundCache[key] = texture;
        return texture;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
        return (p - (a + ab * t)).magnitude;
    }

    // Applique un fond arrondi aux 4 états d'un style (normal / survol / actif / focus).
    private static void RoundPaint(
        GUIStyle style, Color normal, Color hover, Color text, Color edge, Color edgeHover, int radius)
    {
        int size = radius * 2 + 2;
        Texture2D n = RoundTexture(size, radius, normal, edge, 1f);
        Texture2D h = RoundTexture(size, radius, hover, edgeHover, 1f);
        style.normal.background = n;
        style.hover.background = h;
        style.active.background = h;
        style.focused.background = n;
        style.normal.textColor = text;
        style.hover.textColor = text;
        style.active.textColor = text;
        style.focused.textColor = text;
        style.border = new RectOffset(radius, radius, radius, radius);
    }

    private static Font TryCreateFont()
    {
        try
        {
            return Font.CreateDynamicFontFromOSFont("Segoe UI", 12);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Vector2[] Poly(params float[] c)
    {
        Vector2[] points = new Vector2[c.Length / 2];
        for (int i = 0; i < points.Length; i++)
            points[i] = new Vector2(c[i * 2], c[i * 2 + 1]);
        return points;
    }

    // Icônes au trait (16 px, blanches : la couleur vient de GUI.color), dessinées en code.
    private static Texture2D BuildIcon(int index)
    {
        string key = "icon|" + index;
        Texture2D cached;
        if (RoundCache.TryGetValue(key, out cached) && cached != null)
            return cached;

        List<Vector2[]> lines = new List<Vector2[]>();
        List<Vector3> rings = new List<Vector3>();
        switch (index)
        {
            case 0: // réglages
                lines.Add(Poly(2, 4, 8, 4)); lines.Add(Poly(12, 4, 14, 4));
                lines.Add(Poly(2, 8, 3, 8)); lines.Add(Poly(7, 8, 14, 8));
                lines.Add(Poly(2, 12, 9, 12)); lines.Add(Poly(13, 12, 14, 12));
                rings.Add(new Vector3(10, 4, 1.8f)); rings.Add(new Vector3(5, 8, 1.8f)); rings.Add(new Vector3(11, 12, 1.8f));
                break;
            case 1: // bouclier
                lines.Add(Poly(8, 2, 13, 4, 13, 8, 8, 14, 3, 8, 3, 4, 8, 2));
                break;
            case 2: // cible
                rings.Add(new Vector3(8, 8, 5.5f)); rings.Add(new Vector3(8, 8, 2f));
                lines.Add(Poly(8, 1, 8, 3.5f)); lines.Add(Poly(8, 12.5f, 8, 15));
                lines.Add(Poly(1, 8, 3.5f, 8)); lines.Add(Poly(12.5f, 8, 15, 8));
                break;
            case 3: // sac
                lines.Add(Poly(3, 6, 13, 6, 12, 14, 4, 14, 3, 6));
                lines.Add(Poly(6, 6, 6, 4.2f, 7.2f, 3, 8.8f, 3, 10, 4.2f, 10, 6));
                break;
            case 4: // carte
                lines.Add(Poly(2, 4, 6, 3, 10, 5, 14, 4, 14, 12, 10, 13, 6, 11, 2, 12, 2, 4));
                lines.Add(Poly(6, 3, 6, 11)); lines.Add(Poly(10, 5, 10, 13));
                break;
            case 5: // barres
                lines.Add(Poly(4, 13, 4, 8)); lines.Add(Poly(8, 13, 8, 3)); lines.Add(Poly(12, 13, 12, 6));
                break;
            case 6: // horloge
                rings.Add(new Vector3(8, 8, 6f));
                lines.Add(Poly(8, 4.5f, 8, 8, 10.5f, 9.5f));
                break;
            default: // chevron vers le bas
                lines.Add(Poly(4.5f, 6, 8, 9.5f, 11.5f, 6));
                break;
        }

        const int size = 16;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, size - (y + 0.5f));
                float d = 99f;
                for (int l = 0; l < lines.Count; l++)
                {
                    Vector2[] line = lines[l];
                    for (int k = 0; k + 1 < line.Length; k++)
                        d = Mathf.Min(d, DistanceToSegment(p, line[k], line[k + 1]));
                }
                for (int r = 0; r < rings.Count; r++)
                {
                    Vector3 ring = rings[r];
                    d = Mathf.Min(d, Mathf.Abs((p - new Vector2(ring.x, ring.y)).magnitude - ring.z));
                }
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1.25f - d)));
            }
        }
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        RoundCache[key] = texture;
        return texture;
    }

    private void EnsureStyles()
    {
        if (_stylesReady && _texBg != null)
            return;

        _texBg = MakeTexture(ColBg);

        GUISkin skin = GUI.skin;
        _skin = null;
        try
        {
            GUISkin copy = UnityEngine.Object.Instantiate(skin);
            if (copy != null)
            {
                copy.hideFlags = HideFlags.HideAndDontSave;
                skin = copy;
                _skin = copy;
            }
        }
        catch (Exception)
        {
            _skin = null;
        }

        _font = TryCreateFont();
        if (_skin != null && _font != null)
        {
            skin.label.font = _font;
            skin.button.font = _font;
            skin.box.font = _font;
        }

        _texDot = RoundTexture(10, 5, Color.white, Color.white, 0f);
        _texBoxOff = RoundTexture(13, 3, ColBox, ColBoxEdge, 1f);
        _texBoxHover = RoundTexture(13, 3, ColBox, ColMuted, 1f);
        _texBoxOn = RoundTexture(13, 3, ColAccent, ColAccent, 1f);
        _texCheck = CheckTexture(13, new Color(0.02f, 0.06f, 0.12f, 1f));
        _texPlay = PlayTexture(10);
        _texStop = RoundTexture(10, 2, Color.white, Color.white, 0f);
        _icons.Clear();
        for (int i = 0; i < 8; i++)
            _icons.Add(BuildIcon(i));

        _sWindow = CloneStyle(skin.box);
        RoundPaint(_sWindow, ColBg, ColBg, ColText, ColEdge, ColEdge, 8);
        _sWindow.padding = new RectOffset(0, 0, 0, 0);
        _sWindow.margin = new RectOffset(0, 0, 0, 0);

        _sLabel = CloneStyle(skin.label);
        _sLabel.fontSize = 11;
        _sLabel.alignment = TextAnchor.MiddleLeft;
        _sLabel.wordWrap = true;
        _sLabel.margin = new RectOffset(0, 0, 1, 1);
        _sLabel.padding = new RectOffset(0, 0, 0, 0);
        _sLabel.normal.textColor = ColText;

        _sLabelClip = CloneStyle(_sLabel);
        _sLabelClip.wordWrap = false;
        _sLabelClip.clipping = TextClipping.Clip;

        _sLabelAccent = CloneStyle(_sLabelClip);
        _sLabelAccent.normal.textColor = ColAccent;

        _sTitle = CloneStyle(_sLabel);
        _sTitle.fontSize = 12;
        _sTitle.fontStyle = FontStyle.Bold;
        _sTitle.wordWrap = false;

        _sCardTitle = CloneStyle(_sLabel);
        _sCardTitle.fontSize = 9;
        _sCardTitle.fontStyle = FontStyle.Bold;
        _sCardTitle.normal.textColor = ColFaint;

        _sMuted = CloneStyle(_sLabel);
        _sMuted.normal.textColor = ColMuted;

        _sValue = CloneStyle(_sLabel);
        _sValue.alignment = TextAnchor.MiddleRight;
        _sValue.fontStyle = FontStyle.Bold;

        _sTab = CloneStyle(skin.button);
        RoundPaint(_sTab, ColClear, new Color(1f, 1f, 1f, 0.06f), ColMuted, ColClear, ColClear, 5);
        _sTab.margin = new RectOffset(0, 0, 0, 0);

        _sTabOn = CloneStyle(_sTab);
        RoundPaint(_sTabOn, ColSel, ColSel, ColText, ColSelEdge, ColSelEdge, 5);

        _sBtn = CloneStyle(skin.button);
        RoundPaint(_sBtn, ColField, ColFieldHover, ColText, ColLine, ColBoxEdge, 5);
        _sBtn.fontSize = 11;
        _sBtn.margin = new RectOffset(2, 2, 2, 2);
        _sBtn.padding = new RectOffset(8, 8, 2, 2);

        _sBtnPrimary = CloneStyle(_sBtn);
        RoundPaint(_sBtnPrimary, ColAccent, new Color(0.35f, 0.69f, 1f, 1f), new Color(0.02f, 0.06f, 0.12f, 1f), ColAccent, ColAccent, 5);
        _sBtnPrimary.fontStyle = FontStyle.Bold;

        _sBtnDanger = CloneStyle(_sBtn);
        RoundPaint(_sBtnDanger, ColClear, new Color(0.94f, 0.36f, 0.36f, 0.14f), ColDanger, ColDanger, ColDanger, 5);
        _sBtnDanger.fontStyle = FontStyle.Bold;

        _sSeg = CloneStyle(_sBtn);
        RoundPaint(_sSeg, ColBox, ColFieldHover, ColMuted, ColLine, ColBoxEdge, 5);

        _sSegOn = CloneStyle(_sBtn);
        RoundPaint(_sSegOn, ColSel, ColSel, ColText, ColSelEdge, ColSelEdge, 5);

        _sFold = CloneStyle(_sBtn);
        RoundPaint(_sFold, ColClear, ColField, ColText, ColClear, ColClear, 5);
        _sFold.alignment = TextAnchor.MiddleLeft;
        _sFold.fontStyle = FontStyle.Bold;
        _sFold.padding = new RectOffset(6, 6, 2, 2);

        _sRow = CloneStyle(_sBtn);
        RoundPaint(_sRow, ColClear, ColField, ColMuted, ColClear, ColClear, 5);
        _sRow.alignment = TextAnchor.MiddleLeft;
        _sRow.padding = new RectOffset(8, 6, 2, 2);

        _sRowOn = CloneStyle(_sRow);
        RoundPaint(_sRowOn, ColSel, ColSel, ColText, ColSelEdge, ColSelEdge, 5);

        _sItemHover = CloneStyle(_sRow);
        RoundPaint(_sItemHover, ColFieldHover, ColFieldHover, ColText, ColFieldHover, ColFieldHover, 5);

        _sSelect = CloneStyle(_sBtn);
        RoundPaint(_sSelect, ColField, ColFieldHover, ColText, ColLine, ColBoxEdge, 5);
        _sSelect.alignment = TextAnchor.MiddleLeft;
        _sSelect.clipping = TextClipping.Clip;
        _sSelect.wordWrap = false;
        _sSelect.padding = new RectOffset(8, 20, 2, 2);

        _sSelectOpen = CloneStyle(_sSelect);
        RoundPaint(_sSelectOpen, ColField, ColField, ColText, ColAccent, ColAccent, 5);

        _sPopup = CloneStyle(skin.box);
        RoundPaint(_sPopup, new Color(0.075f, 0.098f, 0.133f, 1f), new Color(0.075f, 0.098f, 0.133f, 1f), ColText, ColBoxEdge, ColBoxEdge, 6);
        _sPopup.padding = new RectOffset(0, 0, 0, 0);
        _sPopup.margin = new RectOffset(0, 0, 0, 0);

        _sTgOff = CloneStyle(_sBtn);
        RoundPaint(_sTgOff, ColField, ColField, ColText, ColBoxEdge, ColBoxEdge, 8);
        _sTgOn = CloneStyle(_sBtn);
        RoundPaint(_sTgOn, ColSel, ColSel, ColText, ColAccent, ColAccent, 8);

        _sRight = CloneStyle(_sMuted);
        _sRight.alignment = TextAnchor.MiddleRight;
        _sRight.fontSize = 10;
        _sRight.wordWrap = false;

        _sTip = CloneStyle(_sPopup);
        _sTip.alignment = TextAnchor.MiddleCenter;
        _sTip.fontSize = 10;
        _sTip.wordWrap = false;

        // Curseur : la piste est dessinée à la main (voir PercentSlider), le style est transparent.
        _sSlider = CloneStyle(skin.horizontalSlider);
        _sSlider.normal.background = MakeTexture(ColClear);
        _sSlider.hover.background = _sSlider.normal.background;
        _sSlider.active.background = _sSlider.normal.background;
        _sSlider.focused.background = _sSlider.normal.background;
        _sSlider.fixedHeight = 14f;
        _sSlider.border = new RectOffset(0, 0, 0, 0);
        _sSlider.margin = new RectOffset(0, 0, 0, 0);

        _sThumb = CloneStyle(skin.horizontalSliderThumb);
        _sThumb.normal.background = RoundTexture(12, 6, ColText, ColText, 0f);
        _sThumb.hover.background = RoundTexture(12, 6, new Color(0.62f, 0.80f, 1f, 1f), new Color(0.62f, 0.80f, 1f, 1f), 0f);
        _sThumb.active.background = _sThumb.hover.background;
        _sThumb.focused.background = _sThumb.normal.background;
        _sThumb.fixedWidth = 12f;
        _sThumb.fixedHeight = 12f;
        _sThumb.border = new RectOffset(0, 0, 0, 0);

        _sTrack = CloneStyle(skin.box);
        RoundPaint(_sTrack, ColField, ColField, ColText, ColField, ColField, 2);
        _sTrack.padding = new RectOffset(0, 0, 0, 0);
        _sTrack.margin = new RectOffset(0, 0, 0, 0);

        _sTrackFill = CloneStyle(_sTrack);
        RoundPaint(_sTrackFill, Color.white, Color.white, ColText, Color.white, Color.white, 2);

        if (_skin != null)
            StyleScrollbar(_skin);

        _pillText = null;
        _stylesReady = true;
    }

    // Barre de défilement fine et discrète, sans flèches.
    private static void StyleScrollbar(GUISkin skin)
    {
        skin.verticalScrollbar.normal.background = MakeTexture(ColClear);
        skin.verticalScrollbar.fixedWidth = 8f;
        skin.verticalScrollbar.border = new RectOffset(0, 0, 0, 0);

        Texture2D thumb = RoundTexture(8, 4, ColBoxEdge, ColBoxEdge, 0f);
        Texture2D thumbHover = RoundTexture(8, 4, ColMuted, ColMuted, 0f);
        skin.verticalScrollbarThumb.normal.background = thumb;
        skin.verticalScrollbarThumb.hover.background = thumbHover;
        skin.verticalScrollbarThumb.active.background = thumbHover;
        skin.verticalScrollbarThumb.fixedWidth = 8f;
        skin.verticalScrollbarThumb.border = new RectOffset(4, 4, 4, 4);

        skin.verticalScrollbarUpButton.fixedHeight = 0f;
        skin.verticalScrollbarUpButton.fixedWidth = 0f;
        skin.verticalScrollbarDownButton.fixedHeight = 0f;
        skin.verticalScrollbarDownButton.fixedWidth = 0f;
    }

    // ------------------------------------------------------------------ Collecte

    // OnGUI s'exécute plusieurs fois par image : ce message parcourt tous les
    // collectibles, il n'est donc recalculé que 4 fois par seconde.
    private string _collectStatusCache = string.Empty;
    private float _collectStatusAt = -1f;

    private string CollectionStatusCached()
    {
        float now = Time.unscaledTime;
        if (now - _collectStatusAt >= 0.25f || _collectStatusAt < 0f)
        {
            _collectStatusCache = CollectionStatusMessage();
            _collectStatusAt = now;
        }
        return _collectStatusCache;
    }

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

        string counts = "Collectibles visibles : " + observedCount
            + " | types activés correspondants : " + enabledObservedCount
            + " | types activés : " + _enabledCollectibleTypes.Count + ". ";

        if (!CopperWire.AutomationEnabled)
            return counts + "Collecte bloquée : appuie sur LANCER (l'automatisation démarre à l'arrêt).";
        if (!_collectEnabled)
            return counts + "Collecte bloquée : active la collecte dans l'onglet Contrôle.";
        if (_enabledCollectibleTypes.Count == 0)
            return counts + "Collecte bloquée : active au moins un type de collectible ci-dessous.";
        if (snapshot == null || snapshot.Joueur == null)
            return counts + "En attente d'un instantané du joueur (GameState).";
        if (observedCount == 0)
            return counts + "Aucun collectible n'est visible dans l'instantané GameState.";
        if (enabledObservedCount == 0)
            return counts + "Des collectibles sont visibles, mais aucun ne correspond à un type activé.";
        if (action != null && action.Type == BehaviorActionType.Collect)
        {
            CollectActionContext context = action.Context as CollectActionContext;
            return counts + "Action de collecte : " + ActionStateLabel(action.State)
                + (context == null ? string.Empty : " | NetId " + context.NetId + " | " + context.Type);
        }
        if (action != null)
            return counts + "Action en cours : " + ActionLabel(action.Type) + " / " + ActionStateLabel(action.State);
        return counts + "Un collectible éligible est visible ; en attente du prochain cycle du planificateur.";
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
        bool isMonster = category == TargetCategory.Monster;
        List<string> catalog = isMonster ? _displayMonsters : _displayNpcs;
        bool open = isMonster ? _openMonsters : _openNpcs;
        int selectedCount = (isMonster ? _selectedMonsters : _selectedNpcs).Count;

        GUILayout.BeginVertical();
        string caption = (open ? "-   " : "+   ") + label + "   " + selectedCount + " / " + catalog.Count;
        if (GUILayout.Button(caption, _sFold, GUILayout.Height(22f)))
        {
            open = !open;
            if (isMonster)
                _openMonsters = open;
            else
                _openNpcs = open;
            CloseAmmoEditor();
        }

        if (open)
            DrawTargetRows(category, catalog);

        GUILayout.EndVertical();
        Divider();
    }

    [HideFromIl2Cpp]
    private void DrawTargetRows(TargetCategory category, List<string> catalog)
    {
        GUILayout.Space(3f);
        if (!TargetCatalog.IsInitialized)
        {
            Hint("En attente du catalogue.");
            return;
        }

        for (int i = 0; i < catalog.Count; i++)
        {
            string name = catalog[i];
            string targetKey = GetTargetKey(category, name);

            GUILayout.BeginHorizontal();
            bool wasSelected = IsTargetSelected(category, name);
            bool isSelected = Switch(wasSelected, name);
            if (isSelected != wasSelected)
            {
                SetTargetSelected(category, name, isSelected);
                if (!isSelected
                    && string.Equals(_editingAmmoTarget, targetKey, StringComparison.OrdinalIgnoreCase))
                    CloseAmmoEditor();
                ApplyConfiguration();
            }

            if (isSelected || category == TargetCategory.Npc)
                AmmoField(category, name, 124f);
            GUILayout.EndHorizontal();
        }

        if (catalog.Count == 0)
            Hint("Aucun type.");
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
        // Cibles de Raid : réservées au module Raid, jamais ciblées via l'onglet Cibles.
        if (RaidRules.TargetKey(name) != null)
            return false;

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
            return "indisponible";

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
        // Cibles de Raid : clé indépendante de l'orthographe et de la catégorie du jeu.
        string raidKey = RaidRules.TargetKey(name);
        if (raidKey != null)
            return "raid:" + raidKey;

        return category + ":" + TargetCatalog.NormalizeName(name);
    }

    private static string FormatNumber(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void CloseAmmoEditor()
    {
        _editingAmmoTarget = null;
    }
}
