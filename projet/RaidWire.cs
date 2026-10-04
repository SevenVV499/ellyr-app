using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EtatJoueurMod;
using UnityEngine;

/*
 * Module Carte Raid.
 *
 * Responsabilités : choisir le type de Raid d'après le niveau, vérifier le médaillon,
 * lancer l'entrée (reproduit le clic du joueur : MenuManager.raidmapgir / raidmapAcemigir),
 * confirmer par la carte, maintenir l'état « Raid active » et fournir la liste interne
 * des cibles. Navigation, Combat, Réparation et Réapparition restent aux modules existants.
 *
 * Le décompte du jeu (~10 s) est annulé par tout déplacement ou attaque : pendant l'entrée
 * le navire reste donc immobile (CopperWire appelle HaltAllActivity tant que Tick renvoie true).
 */
public static class RaidWire
{
    private enum Phase
    {
        Idle,
        Settling,
        Counting,
        Active,
        Blocked,
        CoolingDown
    }

    private enum RaidKind
    {
        Petite,
        Grande
    }

    private sealed class RaidSpec
    {
        public RaidKind Kind;
        public string Label;
        public int MinLevel;
        public int MaxLevel;
        public int MapId;
        public string MobName;
        public string BossName;
    }

    private static readonly RaidSpec Petite = new RaidSpec
    {
        Kind = RaidKind.Petite,
        Label = "Petite Raid",
        MinLevel = 1,
        MaxLevel = 10,
        MapId = 41,
        MobName = "sunburst",
        BossName = "ameterasu"
    };

    private static readonly RaidSpec Grande = new RaidSpec
    {
        Kind = RaidKind.Grande,
        Label = "Grande Raid",
        MinLevel = 11,
        MaxLevel = 15,
        MapId = 42,
        MobName = "leviathan",
        BossName = "behemoth"
    };

    // Immobilité avant l'envoi de l'ordre d'entrée (laisse retomber un déplacement en cours).
    private const float SettleSeconds = 1f;

    // Décompte du jeu ≈ 10 s : au-delà, l'entrée est considérée comme avortée.
    private const float CountdownTimeoutSeconds = 25f;

    private const int MaxEntryAttempts = 3;
    private const float CooldownSeconds = 120f;
    private const float FleeCooldownSeconds = 10f;
    private const float MaxSnapshotAgeSeconds = 3f;

    private static Phase _phase = Phase.Idle;
    private static MenuManager _menu;
    private static RaidSpec _spec;
    private static float _phaseStartedAt;
    private static float _cooldownUntil;
    private static int _attempt;
    private static int _counterBefore;
    private static int _blockedMap;

    public static bool IsActive
    {
        get { return _phase == Phase.Active; }
    }

    public static bool IsEntering
    {
        get { return _phase == Phase.Settling || _phase == Phase.Counting; }
    }

    // Non nul uniquement dans une Raid active : remplace la liste de cibles de l'utilisateur.
    public static Func<PnjInfo, bool> ActiveTargetFilter
    {
        get { return _phase == Phase.Active && _spec != null ? IsRaidTarget : (Func<PnjInfo, bool>)null; }
    }

    // Cibles à traiter avant toutes les autres (boss), seulement si l'option est cochée.
    public static Func<PnjInfo, bool> ActivePriorityFilter
    {
        get { return _phase == Phase.Active && _spec != null && Plugin.RaidBossPriority ? IsBoss : (Func<PnjInfo, bool>)null; }
    }

    public static string Description
    {
        get
        {
            if (!Plugin.RaidEnabled)
                return "désactivée";
            switch (_phase)
            {
                case Phase.Settling:
                case Phase.Counting:
                    return "entrée en cours (" + _spec.Label + ", essai " + _attempt + ")";
                case Phase.Active:
                    return "active (" + _spec.Label + ")";
                case Phase.Blocked:
                    return "bloquée (carte inattendue " + _blockedMap + ")";
                case Phase.CoolingDown:
                    return "indisponible (pause)";
                default:
                    return "en attente";
            }
        }
    }

    public static void Reset()
    {
        _phase = Phase.Idle;
        _spec = null;
        _cooldownUntil = 0f;
        _attempt = 0;
    }

    /*
     * Renvoie true tant que le navire doit rester immobile (entrée en cours ou module bloqué).
     * actionEngaged : une action Combat/Collecte est engagée ; l'entrée attend sa fin.
     */
    public static bool Tick(Player player, EtatJeuSnapshot snapshot, bool fleeing, bool actionEngaged)
    {
        if (!Plugin.RaidEnabled)
        {
            if (_phase != Phase.Idle)
                Reset();
            return false;
        }

        FicheJoueur joueur = snapshot == null ? null : snapshot.Joueur;
        if (joueur == null)
            return false;

        float now = Time.time;
        int map = joueur.Harita;
        RaidSpec mapSpec = SpecForMap(map);
        RaidSpec levelSpec = SpecForLevel(joueur.Niveau);

        if (_phase == Phase.Blocked)
        {
            if (map == _blockedMap)
                return true;
            _phase = Phase.Idle;
        }

        // ---- Le joueur est dans une carte de Raid (41 / 42)
        if (mapSpec != null)
        {
            if (IsEntering)
            {
                if (_spec != null && mapSpec.MapId == _spec.MapId)
                {
                    EnterActive(_spec, now);
                    return false;
                }

                // Carte inattendue après l'entrée : mauvaise analyse ou mauvaise règle.
                // Le bot reste à l'arrêt (aucune action) jusqu'à ce que la carte change.
                _blockedMap = map;
                _phase = Phase.Blocked;
                Plugin.Logger.LogError("[RaidWire] Carte inattendue après l'entrée : attendu "
                    + (_spec == null ? 0 : _spec.MapId) + ", obtenu " + map + ". Bot à l'arrêt.");
                return true;
            }

            if (_phase == Phase.Active)
                return false;

            // Présence déjà dans la Raid (démarrage du bot dedans, entrée manuelle).
            if (levelSpec != null && levelSpec.MapId == mapSpec.MapId)
            {
                EnterActive(levelSpec, now);
                return false;
            }
            return false;
        }

        // ---- Carte normale
        if (_phase == Phase.Active)
        {
            _phase = Phase.Idle;
            _spec = null;
        }

        if (_phase == Phase.CoolingDown)
        {
            if (now < _cooldownUntil)
                return false;
            _phase = Phase.Idle;
        }

        bool fresh = (DateTime.UtcNow - snapshot.Timestamp).TotalSeconds <= MaxSnapshotAgeSeconds;
        bool available = levelSpec != null
            && joueur.Vie > 0
            && fresh
            && !fleeing
            && Counter(joueur, levelSpec) > 0;

        switch (_phase)
        {
            case Phase.Idle:
                if (!available || actionEngaged)
                    return false;
                _spec = levelSpec;
                _attempt = 1;
                _counterBefore = Counter(joueur, levelSpec);
                _phase = Phase.Settling;
                _phaseStartedAt = now;
                return true;

            case Phase.Settling:
                if (!available || levelSpec != _spec)
                {
                    Abort(fleeing);
                    return false;
                }
                if (now - _phaseStartedAt < SettleSeconds)
                    return true;
                if (!InvokeEntry(_spec))
                {
                    GiveUp("commande d'entrée indisponible");
                    return false;
                }
                _phase = Phase.Counting;
                _phaseStartedAt = now;
                return true;

            case Phase.Counting:
                if (fleeing || joueur.Vie <= 0)
                {
                    Abort(fleeing);
                    return false;
                }
                if (now - _phaseStartedAt < CountdownTimeoutSeconds)
                    return true;
                if (_attempt >= MaxEntryAttempts)
                {
                    GiveUp("aucun changement de carte après " + MaxEntryAttempts + " essais");
                    return false;
                }
                _attempt++;
                _phase = Phase.Settling;
                _phaseStartedAt = now;
                Plugin.Logger.LogWarning("[RaidWire] Pas de changement de carte : nouvel essai " + _attempt + ".");
                return true;
        }

        return false;
    }

    private static void EnterActive(RaidSpec spec, float now)
    {
        _spec = spec;
        _phase = Phase.Active;
    }

    private static void Abort(bool fleeing)
    {
        _phase = Phase.CoolingDown;
        _cooldownUntil = Time.time + (fleeing ? FleeCooldownSeconds : 2f);
    }

    private static void GiveUp(string reason)
    {
        _phase = Phase.CoolingDown;
        _cooldownUntil = Time.time + CooldownSeconds;
        Plugin.Logger.LogWarning("[RaidWire] Entrée abandonnée : " + reason + ". Pause " + (int)CooldownSeconds + " s.");
    }

    // Reproduit l'appel du bouton du jeu : les contrôles natifs (cooldown, stock, carte) restent actifs.
    private static bool InvokeEntry(RaidSpec spec)
    {
        try
        {
            // Instance mise en cache : la recherche dans la scène n'a lieu qu'une fois (ou si l'objet a disparu).
            if (_menu == null)
                _menu = UnityEngine.Object.FindObjectOfType<MenuManager>();
            MenuManager menu = _menu;
            if (menu == null)
            {
                Plugin.Logger.LogError("[RaidWire] MenuManager introuvable.");
                return false;
            }

            // Petite Raid (talisman du soleil) = raidmapgir / oyuncuTilsim ;
            // Grande Raid (talisman de Behemoth) = raidmapAcemigir / oyuncuAcemiTilsim.
            if (spec.Kind == RaidKind.Petite)
                menu.raidmapgir();
            else
                menu.raidmapAcemigir();
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[RaidWire] Erreur d'entrée : " + e);
            return false;
        }
    }

    /*
     * Le catalogue de cibles fait foi pour l'orthographe. Les quatre mots-clés ci-dessous ne
     * servent qu'à repérer, dans le catalogue, les entrées qui sont des cibles de Raid ; ensuite
     * seuls les noms exacts du catalogue sont reconnus (avant l'initialisation du catalogue,
     * le mot-clé seul fait office de nom).
     */
    private static readonly string[] Keywords =
        { Petite.MobName, Petite.BossName, Grande.MobName, Grande.BossName };

    private static readonly object CatalogGate = new object();
    private static object _cachedMonsters;
    private static object _cachedNpcs;
    private static Dictionary<string, string> _catalogKeys = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> _keyCache = new Dictionary<string, string>();

    /*
     * Clé canonique (mot-clé) d'un nom de cible de Raid, ou null si le nom n'appartient à aucune
     * des deux Raids. Sert à exclure ces cibles de l'onglet Cibles et à retrouver la munition
     * choisie dans l'onglet Carte Raid, quelle que soit l'orthographe du catalogue.
     */
    public static string TargetKey(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        // Appelé pour chaque PNJ à chaque décision (y compris hors Raid, via le filtre de
        // cibles) : le résultat est mémorisé par nom pour éviter toute normalisation répétée.
        lock (CatalogGate)
        {
            Dictionary<string, string> catalogKeys = CatalogKeys();
            string cached;
            if (_keyCache.TryGetValue(name, out cached))
                return cached;

            string key = ComputeTargetKey(name, catalogKeys);
            if (_keyCache.Count > 2048)
                _keyCache.Clear();
            _keyCache[name] = key;
            return key;
        }
    }

    private static string ComputeTargetKey(string name, Dictionary<string, string> catalogKeys)
    {
        string normalized = NormalizeName(name);
        if (normalized.Length == 0)
            return null;

        for (int i = 0; i < Keywords.Length; i++)
        {
            if (normalized == Keywords[i])
                return Keywords[i];
        }

        string key;
        return catalogKeys.TryGetValue(normalized, out key) ? key : null;
    }

    private static Dictionary<string, string> CatalogKeys()
    {
        IReadOnlyList<string> monsters = TargetCatalog.Monsters;
        IReadOnlyList<string> npcs = TargetCatalog.Npcs;
        lock (CatalogGate)
        {
            if (ReferenceEquals(_cachedMonsters, monsters) && ReferenceEquals(_cachedNpcs, npcs))
                return _catalogKeys;

            var map = new Dictionary<string, string>();
            AddCatalogKeys(map, monsters);
            AddCatalogKeys(map, npcs);
            _catalogKeys = map;
            _keyCache.Clear();
            _cachedMonsters = monsters;
            _cachedNpcs = npcs;
            return map;
        }
    }

    private static void AddCatalogKeys(Dictionary<string, string> map, IReadOnlyList<string> names)
    {
        if (names == null)
            return;
        for (int i = 0; i < names.Count; i++)
        {
            string normalized = NormalizeName(names[i]);
            if (normalized.Length == 0 || map.ContainsKey(normalized))
                continue;
            for (int k = 0; k < Keywords.Length; k++)
            {
                if (normalized.Contains(Keywords[k]))
                {
                    map[normalized] = Keywords[k];
                    break;
                }
            }
        }
    }

    /*
     * Catégorie d'arme d'une cible de Raid, d'après le catalogue : monstre (harpon) seulement si
     * l'entrée n'existe que parmi les monstres, sinon NPC (canon). Même règle pour la console
     * (choix de la munition) et pour le combat.
     */
    public static TargetCategory WeaponCategoryFor(string name)
    {
        string key = TargetKey(name);
        if (key == null)
            return TargetCategory.Npc;

        bool monster = CatalogHasKey(TargetCatalog.Monsters, key);
        bool npc = CatalogHasKey(TargetCatalog.Npcs, key);
        return monster && !npc ? TargetCategory.Monster : TargetCategory.Npc;
    }

    private static bool CatalogHasKey(IReadOnlyList<string> names, string key)
    {
        if (names == null)
            return false;
        for (int i = 0; i < names.Count; i++)
        {
            if (TargetKey(names[i]) == key)
                return true;
        }
        return false;
    }

    private static bool IsRaidTarget(PnjInfo pnj)
    {
        if (pnj == null || _spec == null)
            return false;
        string key = TargetKey(pnj.Nom);
        if (key == _spec.MobName)
            return true;
        // Boss : ciblé seulement si l'option « boss en priorité » est cochée.
        return Plugin.RaidBossPriority && key == _spec.BossName;
    }

    private static bool IsBoss(PnjInfo pnj)
    {
        return pnj != null && _spec != null && TargetKey(pnj.Nom) == _spec.BossName;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string decomposed = name.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        for (int i = 0; i < decomposed.Length; i++)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(decomposed[i]) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(decomposed[i]));
        }
        return builder.ToString();
    }

    private static int Counter(FicheJoueur joueur, RaidSpec spec)
    {
        return spec.Kind == RaidKind.Petite ? joueur.Talisman : joueur.TalismanAcemi;
    }

    private static RaidSpec SpecForLevel(int level)
    {
        if (level >= Petite.MinLevel && level <= Petite.MaxLevel)
            return Petite;
        if (level >= Grande.MinLevel && level <= Grande.MaxLevel)
            return Grande;
        return null;
    }

    private static RaidSpec SpecForMap(int map)
    {
        if (map == Petite.MapId)
            return Petite;
        if (map == Grande.MapId)
            return Grande;
        return null;
    }
}
