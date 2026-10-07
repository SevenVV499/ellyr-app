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
    private const int TabStatus = 6;
    private const int TabEvents = 7;
    private static readonly string[] TabLabels =
        { "Contrôle", "Survie", "Cibles", "Collecte", "Carte Raid", "Ressources", "État", "Événements" };

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
    private GUIStyle _sFold;

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

        _panel.width = Mathf.Min(_panel.width, Mathf.Max(380f, Screen.width - 16f));
        _panel.height = Mathf.Min(_panel.height, Mathf.Max(320f, Screen.height - 16f));
        _panel.x = Mathf.Clamp(_panel.x, 0f, Mathf.Max(0f, Screen.width - _panel.width));
        _panel.y = Mathf.Clamp(_panel.y, 0f, Mathf.Max(0f, Screen.height - _panel.height));

        Event current = Event.current;
        _pointerOverConsole = _dragging
            || (current != null && _panel.Contains(current.mousePosition));

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
            case TabRaid: DrawRaidTab(); break;
            case TabResources: DrawResourcesTab(); break;
            case TabEvents: DrawEventsTab(); break;
            default: DrawStatusTab(); break;
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        GUI.Label(
            new Rect(_panel.x + 16f, _panel.yMax - FooterHeight - 2f, _panel.width - 32f, FooterHeight),
            "F8 afficher / masquer   |   glisser l'en-tête pour déplacer",
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
        GUI.Label(new Rect(_panel.x + 16f, _panel.y + 10f, 220f, 24f), "CONSOLE BOT ELLYR", _sTitle);

        bool running = CopperWire.AutomationEnabled;
        Rect play = new Rect(_panel.xMax - 108f, _panel.y + 8f, 92f, 28f);
        if (GUI.Button(play, running ? "ARRÊTER" : "LANCER", running ? _sBtnDanger : _sBtnPrimary))
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
        if (RespawnWire.IsActive)
            return "RÉAPPARITION";
        if (SurvivalWire.IsFleeing)
            return "FUITE";
        if (SurvivalWire.IsRepairPaused)
            return "RÉPARATION";
        BehaviorAction action = CopperWire.CurrentAction;
        return action == null ? "INACTIF" : ActionLabel(action.Type).ToUpperInvariant();
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
        BeginCard("Activités", "Ce que le bot peut faire quand l'automatisation est lancée.");
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
        Hint("La navigation est l'activité par défaut quand rien d'autre ne s'applique.");
        EndCard();

        BeginCard("Priorité", "Utilisée quand un collectible et une cible de combat sont disponibles.");
        int priority = Segmented(
            _priority == CombatCollectPriority.Collect ? 0 : 1,
            "Collecte d'abord",
            "Combat d'abord");
        SetPriority(priority == 0 ? CombatCollectPriority.Collect : CombatCollectPriority.Combat);
        EndCard();

        BeginCard("Combat");
        bool longRange = Switch(CopperWire.LongRange, "Espacement de combat longue portée");
        if (longRange != CopperWire.LongRange)
            CopperWire.SetLongRange(longRange);

        bool fullHealth = Switch(Plugin.OnlyFullHealthTargets, "Attaquer uniquement les cibles à PV max");
        if (fullHealth != Plugin.OnlyFullHealthTargets)
            Plugin.SetOnlyFullHealthTargets(fullHealth);
        Hint(Plugin.OnlyFullHealthTargets
            ? "Seuls les NPC et monstres à PV max sont engagés ; les cibles déjà entamées sont ignorées."
            : "Toutes les cibles sélectionnées peuvent être engagées, même déjà entamées.");
        EndCard();
    }

    private void DrawResourcesTab()
    {
        float elapsed = ResourceTracker.ElapsedSeconds;

        BeginCard("Ressources", "Variation nette depuis la dernière remise à zéro (gains moins dépenses).");
        Row("Durée", FormatDuration(elapsed));
        if (GUILayout.Button("Remise à zéro", _sBtn, GUILayout.Height(26f)))
            ResourceTracker.Reset();
        EndCard();

        BeginCard("Compteurs");
        IReadOnlyList<ResourceTracker.Counter> counters = ResourceTracker.Counters;
        int shown = 0;
        for (int i = 0; i < counters.Count; i++)
        {
            ResourceTracker.Counter counter = counters[i];
            if (counter.Total == 0)
                continue;

            shown++;
            GUILayout.BeginHorizontal();
            GUILayout.Label(counter.Name, _sLabel, GUILayout.Width(260f));
            GUILayout.Label(counter.TotalText, _sValue, GUILayout.Width(130f));
            GUILayout.Label(counter.PerHourText(elapsed), _sMuted);
            GUILayout.EndHorizontal();
        }

        if (counters.Count == 0)
            Hint("En attente des compteurs du joueur.");
        else if (shown == 0)
            Hint("Aucun compteur n'a bougé depuis la remise à zéro.");
        EndCard();
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
        BeginCard("Carte Raid");
        bool raid = Switch(Plugin.RaidEnabled, "Entrer automatiquement en Raid");
        if (raid != Plugin.RaidEnabled)
            Plugin.SetRaidEnabled(raid);
        bool bossPriority = Switch(Plugin.RaidBossPriority, "Boss en priorité (Ameterasu / Behemoth)");
        if (bossPriority != Plugin.RaidBossPriority)
            Plugin.SetRaidBossPriority(bossPriority);
        Hint(Plugin.RaidBossPriority
            ? "Dès qu'un boss est visible, il est attaqué avant les mobs, même plus éloigné."
            : "Boss ignoré : seuls les mobs (Sunburst / Léviathan) sont attaqués.");
        EtatJeuSnapshot raidSnapshot = GameState.ObtenirSnapshot();
        if (raidSnapshot != null && raidSnapshot.Joueur != null)
        {
            int damage = raidSnapshot.Joueur.RaidHasar;
            Row("Dégâts boss", damage.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.')
                + " / " + RaidRules.BossDamageCap.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.')
                + (damage >= RaidRules.BossDamageCap ? "  (plafond atteint : boss ignorés)" : string.Empty));
        }
        Hint("Le type de Raid dépend du niveau (1-10 petite, 11-15 grande) ; un médaillon est requis.");
        Hint("Dans la Raid : navigation, combat et réparation uniquement, avec les cibles propres à la Raid.");
        EndCard();

        BeginCard("Petite Raid", "Niveaux 1 à 10 - talisman du soleil");
        DrawRaidTargetRow("Sunburst", "mob");
        DrawRaidTargetRow("Ameterasu", "boss");
        EndCard();

        BeginCard("Grande Raid", "Niveaux 11 à 15 - talisman de Behemoth");
        DrawRaidTargetRow("Léviathan", "mob");
        DrawRaidTargetRow("Behemoth", "boss");
        EndCard();
        Hint("Ces quatre cibles sont retirées de l'onglet Cibles : leurs munitions se règlent ici.");
    }

    [HideFromIl2Cpp]
    private void DrawRaidTargetRow(string name, string role)
    {
        TargetCategory category = RaidRules.WeaponCategoryFor(name);
        string targetKey = GetTargetKey(category, name);
        bool dropdownOpen = string.Equals(
            _editingAmmoTarget, targetKey, StringComparison.OrdinalIgnoreCase);

        GUILayout.BeginHorizontal();
        GUILayout.Label(name + " (" + role + ")", _sLabel, GUILayout.Width(170f));
        IReadOnlyList<AmmoDefinition> ammo = GetAmmoCatalog(category);
        string caption = ammo.Count == 0
            ? "Munitions indisponibles"
            : (category == TargetCategory.Npc ? "Boulet : " : "Harpon : ")
                + DescribeAmmo(category, ResolveAmmo(category, name)) + (dropdownOpen ? "  ^" : "  v");
        if (GUILayout.Button(caption, dropdownOpen ? _sSegOn : _sBtn,
                GUILayout.Width(270f), GUILayout.Height(26f)))
        {
            if (dropdownOpen)
            {
                CloseAmmoEditor();
            }
            else
            {
                _editingAmmoTarget = targetKey;
                _ammoScroll = Vector2.zero;
            }
        }
        GUILayout.EndHorizontal();

        if (dropdownOpen)
            DrawAmmoDropdown(category, name);
    }

    private void DrawSurvivalTab()
    {
        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        FicheJoueur player = snapshot == null ? null : snapshot.Joueur;

        BeginCard("Coque");
        if (player == null || player.VieMax <= 0)
        {
            Hint("En attente d'un instantané du joueur (GameState).");
        }
        else
        {
            Row("PV", player.Vie + " / " + player.VieMax
                + "   (" + player.PourcentageVie.ToString("0", CultureInfo.InvariantCulture) + " %)");
            DrawHpBar(player.PourcentageVie);
            Row("État", StateText());
        }
        EndCard();

        BeginCard("Réparation", "Envoie la commande de réparation tant que les PV sont sous le seuil.");
        bool repair = Switch(Plugin.RepairEnabled, "Réparation activée");
        if (repair != Plugin.RepairEnabled)
            Plugin.SetRepairEnabled(repair);

        int repairMode = Segmented(
            Plugin.RepairPausesActivity ? 1 : 0,
            "En activité",
            "À l'arrêt");
        Plugin.SetRepairPausesActivity(repairMode == 1);
        Hint(Plugin.RepairPausesActivity
            ? "À l'arrêt : toute activité est suspendue et le navire reste immobile. L'activité reprend à PV pleins."
            : "En activité : la réparation tourne pendant la navigation, la collecte et le combat ; l'activité continue.");

        int repairPercent = PercentSlider("Réparer si PV <=", Plugin.RepairPercent);
        if (repairPercent != Plugin.RepairPercent)
            Plugin.SetRepairPercent(repairPercent);
        EndCard();

        BeginCard("PV bas", "Abandonne le combat et continue de naviguer jusqu'à la remontée des PV.");
        bool flee = Switch(Plugin.FleeEnabled, "Fuite activée");
        if (flee != Plugin.FleeEnabled)
            Plugin.SetFleeEnabled(flee);

        int fleePercent = PercentSlider("Fuir si PV <=", Plugin.FleePercent);
        if (fleePercent != Plugin.FleePercent)
            Plugin.SetFleePercent(fleePercent);

        bool fleeCollect = Switch(Plugin.FleeCollectEnabled, "Collecter pendant la fuite");
        if (fleeCollect != Plugin.FleeCollectEnabled)
            Plugin.SetFleeCollectEnabled(fleeCollect);
        Hint("La fuite se termine quand les PV dépassent à la fois le seuil de fuite et celui de réparation.");
        EndCard();
    }

    private void DrawTargetsTab()
    {
        IReadOnlyList<string> npcCatalog = TargetCatalog.Npcs;
        IReadOnlyList<string> monsterCatalog = TargetCatalog.Monsters;
        RefreshDisplayCatalogs();

        BeginCard("Catalogues", "Sélectionne un type de cible ; ses munitions s'appliquent à toutes les instances correspondantes.");
        Row("Cibles", TargetCatalog.IsInitialized
            ? npcCatalog.Count + " types de NPC, " + monsterCatalog.Count + " types de monstres"
            : "en attente du scan réseau des prefabs");
        Row("Munitions", AmmoCatalog.Bullets.Count + " types de boulets, "
            + AmmoCatalog.Harpoons.Count + " types de harpons");
        Row("Sélection", _selectedNpcs.Count + " NPC, " + _selectedMonsters.Count + " monstre(s)");
        EndCard();

        DrawTargetGroup(TargetCategory.Npc, "NPC");
        DrawTargetGroup(TargetCategory.Monster, "Monstres");
    }

    private void DrawCollectTab()
    {
        RefreshCollectibleCatalog();

        BeginCard("Statut");
        Row("Collecte", _collectEnabled ? "ACTIVÉE (onglet Contrôle)" : "DÉSACTIVÉE (onglet Contrôle)");
        Row("Catalogue", CollectibleCatalog.IsInitialized
            ? _displayCollectibleTypes.Count + " types"
            : "en attente du joueur local");
        Hint(CollectionStatusCached());
        EndCard();

        BeginCard("Types de collectibles", _enabledCollectibleTypes.Count + " activé(s)");
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
            Hint("Aucun type de collectible pour l'instant.");
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

        BeginCard(
            "Navires d'événement",
            _eventShips.Count + " présent(s), triés par carte puis par nom.");
        Hint("Seuls les navires que le serveur envoie à ton client sont listés. Un navire "
            + "sort de la liste quand il n'est plus présent ou à 0 PV.");
        EndCard();

        BeginCard("Liste");
        if (_eventShips.Count == 0)
            Hint("Aucun navire d'événement pour l'instant.");
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

    private void DrawStatusTab()
    {
        BehaviorAction action = CopperWire.CurrentAction;

        BeginCard("Bot");
        Row("Système", CopperWire.SystemState == BehaviorSystemState.Respawn ? "Réapparition" : "Normal");
        Row("Action", action == null ? "Aucune" : ActionLabel(action.Type) + " / " + ActionStateLabel(action.State));
        Row("Raid", RaidRules.Description);
        Row("Réapparition", RespawnWire.IsActive
            ? RespawnWire.IsAbandoned ? "active (tentatives abandonnées)" : "active"
            : "inactive");

        CombatTarget target = CopperWire.CurrentCombatTarget;
        if (target != null)
        {
            string category = target.WeaponCategory.HasValue
                ? target.WeaponCategory.Value.ToString()
                : "non classée";
            Row("Cible", target.Name + " (" + category + ")"
                + (string.IsNullOrEmpty(target.Category) ? string.Empty : " / " + target.Category)
                + (string.IsNullOrEmpty(target.Type) ? string.Empty : " / " + target.Type));
            if (target.WeaponCategory.HasValue)
            {
                TargetCategory weapon = target.WeaponCategory.Value;
                Row("Munitions", "voulues " + DescribeAmmo(weapon, target.AmmoId)
                    + " | sélectionnées " + DescribeAmmo(weapon, CopperWire.GetSelectedAmmoId(weapon)));
            }
        }
        else if (action != null && action.Type == BehaviorActionType.Collect)
        {
            CollectActionContext context = action.Context as CollectActionContext;
            Row("Collectible", context == null ? "inconnu" : "NetId " + context.NetId);
        }
        EndCard();

        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        FicheJoueur player = snapshot == null ? null : snapshot.Joueur;
        BeginCard("Joueur");
        if (player == null)
        {
            Hint("En attente d'un instantané GameState.");
        }
        else
        {
            Row("PV", player.Vie + " / " + player.VieMax);
            Row("Portée", "canon " + FormatNumber(player.Portee)
                + " | harpon " + FormatNumber(player.PorteeHarpon));
            Row("Carte", player.Harita
                + (string.IsNullOrEmpty(player.NomHarita) ? string.Empty : " / " + player.NomHarita));
            Row("Position", (player.CoordonneeSayi ?? "?") + " " + (player.CoordonneeHarf ?? "?")
                + " | monde " + FormatNumber(player.X) + ", " + FormatNumber(player.Y));
            Row("Niveau", player.Niveau.ToString());
            Row("Talismans", "acemi " + player.TalismanAcemi + " | tilsim " + player.Talisman);
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
            value ? "OUI" : "NON",
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
        Hint("Repères : bleu = seuil de réparation, orange = seuil de fuite.");
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

        _sFold = CloneStyle(_sBtn);
        _sFold.alignment = TextAnchor.MiddleLeft;
        _sFold.fontStyle = FontStyle.Bold;
        _sFold.padding = new RectOffset(12, 8, 4, 4);

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

        GUILayout.BeginVertical(_sCard);
        string caption = label.ToUpperInvariant() + "   -   " + selectedCount + " sélectionné(s) sur "
            + catalog.Count + (open ? "   [replier]" : "   [déplier]");
        if (GUILayout.Button(caption, _sFold, GUILayout.Height(28f)))
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
        GUILayout.Space(10f);
    }

    [HideFromIl2Cpp]
    private void DrawTargetRows(TargetCategory category, List<string> catalog)
    {
        GUILayout.Space(4f);
        if (!TargetCatalog.IsInitialized)
        {
            Hint("En attente du catalogue de cibles du jeu.");
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
                if (!isSelected
                    && string.Equals(_editingAmmoTarget, targetKey, StringComparison.OrdinalIgnoreCase))
                    CloseAmmoEditor();
                ApplyConfiguration();
            }

            if (isSelected || category == TargetCategory.Npc)
            {
                IReadOnlyList<AmmoDefinition> ammo = GetAmmoCatalog(category);
                int? id = ResolveAmmo(category, name);
                bool dropdownOpen = string.Equals(
                    _editingAmmoTarget, targetKey, StringComparison.OrdinalIgnoreCase);
                string caption = ammo.Count == 0
                    ? (category == TargetCategory.Npc ? "Boulet indisponible" : "Munitions indisponibles")
                    : (category == TargetCategory.Npc ? "Boulet : " : "Harpon : ")
                        + DescribeAmmo(category, id) + (dropdownOpen ? "  ^" : "  v");
                if (GUILayout.Button(caption, dropdownOpen ? _sSegOn : _sBtn,
                        GUILayout.Width(270f), GUILayout.Height(26f)))
                {
                    if (dropdownOpen)
                    {
                        CloseAmmoEditor();
                    }
                    else
                    {
                        _editingAmmoTarget = targetKey;
                        _ammoScroll = Vector2.zero;
                    }
                }
            }
            GUILayout.EndHorizontal();

            if (string.Equals(_editingAmmoTarget, targetKey, StringComparison.OrdinalIgnoreCase))
                DrawAmmoDropdown(category, name);
        }

        if (catalog.Count == 0)
            Hint("Aucun type dans ce catalogue.");
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

    [HideFromIl2Cpp]
    private void DrawAmmoDropdown(TargetCategory category, string name)
    {
        IReadOnlyList<AmmoDefinition> catalog = GetAmmoCatalog(category);
        if (catalog.Count == 0)
        {
            Hint("En attente du catalogue de munitions du jeu.");
            return;
        }

        GUILayout.Label(
            (category == TargetCategory.Npc ? "Boulets pour " : "Harpons pour ") + name
            + " (appliqué à toutes les instances)",
            _sMuted);
        float height = Mathf.Min(180f, catalog.Count * 30f + 4f);
        _ammoScroll = GUILayout.BeginScrollView(_ammoScroll, GUILayout.Height(height));
        int? currentId = ResolveAmmo(category, name);
        for (int i = 0; i < catalog.Count; i++)
        {
            AmmoDefinition ammo = catalog[i];
            bool isCurrent = currentId.HasValue && currentId.Value == ammo.Id;
            if (RowToggle(isCurrent, FormatAmmo(ammo)) && !isCurrent)
            {
                _ammoByTarget[GetTargetKey(category, name)] = ammo.Id;
                ApplyConfiguration();
                CloseAmmoEditor();
            }
        }
        GUILayout.EndScrollView();
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
