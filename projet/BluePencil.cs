using System;
using System.Globalization;
using System.Collections.Generic;
using EtatJoueurMod;
using UnityEngine;

public enum CombatCollectPriority
{
    Collect,
    Combat
}

public enum CombatTargetKind
{
    NetworkId,
    PlayerGlobalId
}

public sealed class CombatTarget
{
    public CombatTargetKind Kind;
    public uint NetId;
    public int PlayerGlobalId;
    public string Category;
    public string Type;
    public string Name;
    public float X;
    public float Y;
    public float Distance;
    public int Health;
    public bool HasHealth;
    public int? AmmoId;

    // Portée de la cible (champ menzil du jeu), capturée au début du combat.
    // Renseignée pour les NPC uniquement ; 0 = inconnue.
    public float Portee;

    // Placement décidé par BluePencil au démarrage du combat (NPC uniquement).
    // StayOutOfReach : rester hors de la portée du NPC (entre Portee et ma portée),
    // en visant AimDistance du NPC. PlacementReason explique la décision.
    public bool StayOutOfReach;
    public float AimDistance;
    public string PlacementReason;

    // Comportement normal : point d'arrivée tiré au sort autour du NPC, exprimé
    // en décalage par rapport à sa position (il suit donc le NPC s'il bouge).
    public bool HasApproachPoint;
    public float ApproachOffsetX;
    public float ApproachOffsetY;

    // BluePencil classifies targets; CopperWire chooses the matching player weapon.
    public TargetCategory? WeaponCategory;

    public string Key
    {
        get
        {
            return Kind == CombatTargetKind.NetworkId
                ? "net:" + NetId
                : "player:" + PlayerGlobalId;
        }
    }
}

public sealed class CollectActionContext
{
    public uint NetId { get; }
    public string Type { get; }
    public string Category { get; }
    public float X { get; }
    public float Y { get; }
    public Guid ActionInstanceId { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? ArrivedAtUtc { get; private set; }
    public long CallbackSequenceBaseline { get; }
    public long RewardSequenceBaseline { get; }

    public CollectActionContext(
        uint netId,
        string type,
        string category,
        float x,
        float y,
        long callbackSequenceBaseline,
        long rewardSequenceBaseline)
    {
        NetId = netId;
        Type = type;
        Category = category;
        X = x;
        Y = y;
        CallbackSequenceBaseline = callbackSequenceBaseline;
        RewardSequenceBaseline = rewardSequenceBaseline;
    }

    internal void BindToAction(BehaviorAction action)
    {
        if (action == null || action.Type != BehaviorActionType.Collect)
            throw new ArgumentException("Le contexte doit être lié à une action Collect.", nameof(action));

        ActionInstanceId = action.InstanceId;
        StartedAtUtc = action.StartedAtUtc;
    }

    internal void MarkArrived(DateTime timestampUtc)
    {
        if (!ArrivedAtUtc.HasValue)
            ArrivedAtUtc = timestampUtc;
    }
}

public sealed class CombatActionContext
{
    public CombatTarget Target;
}

public sealed class BluePencil
{
    private enum CollectibleKeyResource
    {
        None,
        NormalChest
    }

    private const int NavigationPriority = 10;
    private const int EngagedActionPriority = 200;

    // LongRange contre un NPC : le bot vise ce décalage sous sa propre portée,
    // car il se déplace à la case près ; en visant pile sa portée, il arriverait
    // souvent juste au-delà. Il ne reste hors de la portée du NPC que si ce
    // point visé est au-delà de la portée du NPC.
    public const float LongRangeAimInset = 0.5f;

    // Comportement normal face à un NPC : le point d'arrivée est tiré au sort
    // tout autour du NPC, entre ApproachMinimumRatio x maximum et le maximum,
    // le maximum étant la plus petite des deux portées moins ApproachEdgeMargin.
    public const float ApproachEdgeMargin = 0.5f;
    public const float ApproachMinimumRatio = 1f / 3f;
    private static readonly System.Random _approachRandom = new System.Random();
    private static readonly Dictionary<string, CollectibleKeyResource> RequiredKeyByCollectibleType =
        new Dictionary<string, CollectibleKeyResource>(StringComparer.Ordinal)
        {
            { nameof(Chest), CollectibleKeyResource.NormalChest }
        };

    private readonly BehaviorBrain _brain;
    private readonly HashSet<uint> _confirmedCollectibles = new HashSet<uint>();
    private readonly HashSet<uint> _skippedCollectiblesUntilHidden = new HashSet<uint>();
    private readonly List<uint> _skippedCollectiblesToRemove = new List<uint>();
    private readonly HashSet<uint> _excludedMonsterTargets = new HashSet<uint>();
    private readonly List<uint> _excludedMonsterTargetsToRemove = new List<uint>();
    private Func<PnjInfo, bool> _allowPnj;
    private Func<NavireInfo, bool> _allowShip;
    // Contexte Raid : liste de cibles imposée par RaidWire (remplace celle de l'utilisateur,
    // sans Collecte ni cibles joueurs).
    private Func<PnjInfo, bool> _raidAllowPnj;
    // Cibles prioritaires en Raid (boss) : choisies avant toute autre, même plus éloignées.
    private Func<PnjInfo, bool> _raidPriorityPnj;
    private Func<CollectibleInfo, bool> _allowCollectible;
    private bool _collectEnabled;
    private bool _combatEnabled;
    private bool _longRange;
    private int _lastMap;
    private bool _hasMap;
    private CombatCollectPriority _priority = CombatCollectPriority.Collect;
    private Func<TargetCategory, string, int?> _resolveAmmo;

    public BluePencil(BehaviorBrain brain)
    {
        _brain = brain ?? throw new ArgumentNullException(nameof(brain));
    }

    public bool CombatEnabled { get { return _combatEnabled || _raidAllowPnj != null; } }

    public void SetRaidContext(Func<PnjInfo, bool> allowPnj, Func<PnjInfo, bool> priorityPnj = null)
    {
        _raidAllowPnj = allowPnj;
        _raidPriorityPnj = allowPnj == null ? null : priorityPnj;
    }

    private Func<PnjInfo, bool> EffectiveAllowPnj
    {
        get { return _raidAllowPnj ?? _allowPnj; }
    }
    public bool LongRange { get { return _longRange; } }

    public void SetLongRange(bool enabled)
    {
        _longRange = enabled;
    }

    public void Configurer(
        bool collectEnabled,
        bool combatEnabled,
        CombatCollectPriority priority,
        bool longRange = false,
        Func<PnjInfo, bool> allowPnj = null,
        Func<NavireInfo, bool> allowShip = null,
        Func<TargetCategory, string, int?> resolveAmmo = null,
        Func<CollectibleInfo, bool> allowCollectible = null)
    {
        _collectEnabled = collectEnabled;
        _combatEnabled = combatEnabled;
        _priority = priority;
        _longRange = longRange;
        _allowPnj = allowPnj;
        _allowShip = allowShip;
        _resolveAmmo = resolveAmmo;
        _allowCollectible = allowCollectible;
    }

    public BehaviorActionType Decide(
        EtatJeuSnapshot snapshot,
        bool fleeing = false,
        bool collectWhileFleeing = false)
    {
        if (snapshot == null || snapshot.Joueur == null)
            return BehaviorActionType.None;

        // Le SystemState Respawn appartient à RespawnWire / BehaviorBrain :
        // BluePencil ne le modifie jamais et ne décide rien tant qu'il est actif.
        if (_brain.IsRespawning || snapshot.Joueur.Vie <= 0)
            return BehaviorActionType.None;

        _candidateSnapshot = null;
        SynchroniserCarte(snapshot);
        PurgeSkippedCollectiblesNoLongerVisible(snapshot);
        PurgeExcludedMonsterTargets(snapshot);

        // Fuite : plus aucun cycle Combat ; la Collecte n'est permise que si
        // l'option correspondante est cochée, sinon seule la Navigation reste.
        bool inRaid = _raidAllowPnj != null;
        bool collectAllowed = _collectEnabled && !inRaid && (!fleeing || collectWhileFleeing);
        bool combatAllowed = (_combatEnabled || inRaid) && !fleeing;

        BehaviorAction active = _brain.CurrentAction;
        if (active != null && !active.IsFinished)
        {
            if (active.Type == BehaviorActionType.Collect)
            {
                if (collectAllowed && EvaluerCollecte(snapshot, active))
                    return BehaviorActionType.Collect;
            }
            else if (active.Type == BehaviorActionType.Combat)
            {
                // Raid : un boss visible prend le pas sur un combat en cours contre un mob.
                if (combatAllowed && PriorityPreemptsActiveCombat(snapshot, active))
                    _brain.CancelCurrent();
                else if (combatAllowed && EvaluerCombat(snapshot, active))
                    return BehaviorActionType.Combat;
            }
        }

        CollectibleInfo collectible = collectAllowed
            ? ObtenirCollectibleCandidat(snapshot)
            : null;
        CombatTarget combat = combatAllowed
            ? ObtenirCombatCandidat(snapshot)
            : null;

        if (collectible != null && combat != null)
        {
            if (_priority == CombatCollectPriority.Collect)
                DemarrerCollecte(collectible);
            else
                DemarrerCombat(combat, snapshot);
        }
        else if (collectible != null)
        {
            DemarrerCollecte(collectible);
        }
        else if (combat != null)
        {
            DemarrerCombat(combat, snapshot);
        }
        else
        {
            DemarrerNavigation();
        }

        return _brain.CurrentActionType;
    }

    public void CompleteCurrent()
    {
        _brain.CompleteCurrent();
    }

    public void FailCurrent()
    {
        ExcludeCurrentMonsterTarget();
        _brain.FailCurrent();
    }

    public void CancelCurrent()
    {
        _brain.CancelCurrent();
    }

    /*
     * Un monstre dont le combat a échoué est ignoré tant qu'il reste visible.
     * Appelé uniquement par FailCurrent, c'est-à-dire quand l'exécuteur signale
     * un échec ; les échecs décidés ici même n'excluent rien.
     */
    private void ExcludeCurrentMonsterTarget()
    {
        CombatActionContext context = _brain.GetCurrentContext<CombatActionContext>();
        CombatTarget target = context == null ? null : context.Target;
        if (target != null
            && target.Kind == CombatTargetKind.NetworkId
            && target.WeaponCategory == TargetCategory.Monster
            && target.NetId != 0)
            _excludedMonsterTargets.Add(target.NetId);
    }

    /*
     * Seule détection du changement de carte. BluePencil décide ce qu'il
     * implique : l'action en cours est annulée et la mémoire est effacée (Reset).
     * Renvoie true si la carte a changé depuis le dernier appel.
     *
     * Idempotente : appelée deux fois avec le même snapshot (par CopperWire puis
     * par Decide), le deuxième appel ne réinitialise rien et renvoie false.
     */
    public bool SynchroniserCarte(EtatJeuSnapshot snapshot)
    {
        if (snapshot == null || snapshot.Joueur == null)
            return false;

        int carte = snapshot.Joueur.Harita;
        bool changee = _hasMap && _lastMap != carte;
        if (changee)
        {
            Reset();
        }

        _lastMap = carte;
        _hasMap = true;
        return changee;
    }

    public void Reset()
    {
        _brain.Reset();
        _confirmedCollectibles.Clear();
        _skippedCollectiblesUntilHidden.Clear();
        _skippedCollectiblesToRemove.Clear();
        _excludedMonsterTargets.Clear();
        _excludedMonsterTargetsToRemove.Clear();
        _hasMap = false;
    }

    private bool EvaluerCollecte(EtatJeuSnapshot snapshot, BehaviorAction action)
    {
        CollectActionContext context = action.Context as CollectActionContext;
        if (context == null
            || context.NetId == 0
            || context.ActionInstanceId != action.InstanceId
            || string.IsNullOrEmpty(context.Type))
        {
            _brain.FailCurrent();
            return false;
        }

        if (!_collectEnabled || _raidAllowPnj != null)
        {
            _brain.CancelCurrent();
            return false;
        }

        CollectConfirmationState confirmation = GameState.ObtenirConfirmationCollecte(
            snapshot,
            context.NetId,
            context.Type,
            context.StartedAtUtc,
            context.CallbackSequenceBaseline,
            context.RewardSequenceBaseline);
        if (confirmation == CollectConfirmationState.Confirmed
            || confirmation == CollectConfirmationState.CallbackExecuted)
        {
            _confirmedCollectibles.Add(context.NetId);
            _brain.CompleteCurrent();
            return false;
        }

        if (!HasRequiredCollectibleKey(context.Type, snapshot.Joueur))
        {
            _brain.CancelCurrent();
            return false;
        }

        if (context.ArrivedAtUtc.HasValue)
        {
            _skippedCollectiblesUntilHidden.Add(context.NetId);
            _brain.FailCurrent();
            return false;
        }

        return true;
    }

    private bool EvaluerCombat(EtatJeuSnapshot snapshot, BehaviorAction action)
    {
        CombatActionContext context = action.Context as CombatActionContext;
        if (context == null || context.Target == null)
        {
            _brain.FailCurrent();
            return false;
        }

        if (!_combatEnabled && _raidAllowPnj == null)
        {
            _brain.CancelCurrent();
            return false;
        }

        if (context.Target.Kind == CombatTargetKind.NetworkId
            && PossedeRecompense(snapshot, context.Target.NetId, RewardSourceType.Monstre,
                action.StartedAtUtc, RewardSourceType.Pnj))
        {
            _brain.CompleteCurrent();
            return false;
        }

        CombatTarget observed = TrouverCibleCombat(snapshot, context.Target);
        if (observed == null)
        {
            _brain.FailCurrent();
            return false;
        }

        if (observed.HasHealth && observed.Health <= 0)
        {
            _brain.CompleteCurrent();
            return false;
        }

        context.Target.Category = observed.Category;
        context.Target.Type = observed.Type;
        context.Target.Name = observed.Name;
        context.Target.X = observed.X;
        context.Target.Y = observed.Y;
        context.Target.Distance = observed.Distance;
        context.Target.Health = observed.Health;
        context.Target.HasHealth = observed.HasHealth;
        context.Target.WeaponCategory = observed.WeaponCategory;
        context.Target.AmmoId = observed.AmmoId;
        return true;
    }

    private CollectibleInfo ObtenirCollectibleCandidat(EtatJeuSnapshot snapshot)
    {
        CollectibleInfo best = null;
        if (snapshot.Collectibles == null)
            return null;

        for (int i = 0; i < snapshot.Collectibles.Count; i++)
        {
            CollectibleInfo item = snapshot.Collectibles[i];
            if (item == null || item.Id == 0 || IsInvalidDistance(item.Distance))
                continue;
            if (_allowCollectible != null && !_allowCollectible(item))
                continue;
            if (_confirmedCollectibles.Contains(item.Id))
                continue;
            if (_skippedCollectiblesUntilHidden.Contains(item.Id))
                continue;
            if (!HasRequiredCollectibleKey(item.Type, snapshot.Joueur))
            {
                continue;
            }
            if (best == null || item.Distance < best.Distance)
                best = item;
        }
        return best;
    }

    private void PurgeSkippedCollectiblesNoLongerVisible(EtatJeuSnapshot snapshot)
    {
        if (_skippedCollectiblesUntilHidden.Count == 0)
            return;

        _skippedCollectiblesToRemove.Clear();
        foreach (uint netId in _skippedCollectiblesUntilHidden)
        {
            if (!EstCollectibleVisible(snapshot, netId))
                _skippedCollectiblesToRemove.Add(netId);
        }

        for (int i = 0; i < _skippedCollectiblesToRemove.Count; i++)
        {
            uint netId = _skippedCollectiblesToRemove[i];
            _skippedCollectiblesUntilHidden.Remove(netId);
        }
        _skippedCollectiblesToRemove.Clear();
    }

    private static bool HasRequiredCollectibleKey(string collectibleType, FicheJoueur player)
    {
        CollectibleKeyResource requiredResource;
        if (string.IsNullOrEmpty(collectibleType)
            || !RequiredKeyByCollectibleType.TryGetValue(collectibleType, out requiredResource)
            || requiredResource == CollectibleKeyResource.None)
            return true;

        if (player == null)
            return false;

        switch (requiredResource)
        {
            case CollectibleKeyResource.NormalChest:
                return player.ClesCoffre >= 1;
            default:
                return true;
        }
    }

    private static CollectibleInfo FindCollectible(EtatJeuSnapshot snapshot, uint netId)
    {
        if (snapshot == null || snapshot.Collectibles == null)
            return null;

        for (int i = 0; i < snapshot.Collectibles.Count; i++)
        {
            CollectibleInfo item = snapshot.Collectibles[i];
            if (item != null && item.Id == netId)
                return item;
        }

        return null;
    }

    private void PurgeExcludedMonsterTargets(EtatJeuSnapshot snapshot)
    {
        if (_excludedMonsterTargets.Count == 0)
            return;

        _excludedMonsterTargetsToRemove.Clear();
        foreach (uint netId in _excludedMonsterTargets)
        {
            bool visible = false;
            if (snapshot.Pnjs != null)
            {
                for (int i = 0; i < snapshot.Pnjs.Count; i++)
                {
                    PnjInfo pnj = snapshot.Pnjs[i];
                    if (pnj != null && pnj.Id == netId)
                    {
                        visible = true;
                        break;
                    }
                }
            }

            if (!visible)
                _excludedMonsterTargetsToRemove.Add(netId);
        }

        foreach (uint netId in _excludedMonsterTargetsToRemove)
            _excludedMonsterTargets.Remove(netId);
        _excludedMonsterTargetsToRemove.Clear();
    }

    // Le meilleur candidat est calculé une seule fois par décision (Decide remet ce cache à zéro) :
    // en Raid, le test de préemption et la sélection normale s'appuient sur le même résultat.
    private EtatJeuSnapshot _candidateSnapshot;
    private CombatTarget _candidate;

    private CombatTarget ObtenirCombatCandidat(EtatJeuSnapshot snapshot)
    {
        if (ReferenceEquals(_candidateSnapshot, snapshot))
            return _candidate;

        _candidate = CalculerCombatCandidat(snapshot);
        _candidateSnapshot = snapshot;
        return _candidate;
    }

    private CombatTarget CalculerCombatCandidat(EtatJeuSnapshot snapshot)
    {
        CombatTarget best = null;

        // Le gagnant parmi les PNJ est retenu par référence ; l'objet CombatTarget n'est créé qu'une fois.
        PnjInfo bestPnj = null;
        TargetCategory bestCategory = default(TargetCategory);
        bool bestIsPriority = false;
        Func<PnjInfo, bool> allowPnj = EffectiveAllowPnj;
        if (allowPnj != null && snapshot.Pnjs != null)
        {
            for (int i = 0; i < snapshot.Pnjs.Count; i++)
            {
                PnjInfo pnj = snapshot.Pnjs[i];
                if (pnj == null || pnj.Id == 0 || pnj.Vie <= 0
                    || IsInvalidDistance(pnj.Distance) || !allowPnj(pnj))
                    continue;

                // Option « cibles à PV max » : une cible déjà entamée n'est pas engagée.
                // PV max inconnu (<= 0) : pas de filtre. Ne concerne que le choix d'une
                // nouvelle cible, jamais un combat déjà en cours.
                // Le boss de Raid en est exclu : la priorité Raid fait foi, même entamé.
                bool priority = _raidPriorityPnj != null && _raidPriorityPnj(pnj);
                if (!priority && Plugin.OnlyFullHealthTargets && pnj.VieMax > 0 && pnj.Vie < pnj.VieMax)
                    continue;
                if (AUneRecompenseConnue(snapshot, pnj.Id, RewardSourceType.Monstre, RewardSourceType.Pnj))
                    continue;

                // La catégorie (NPC/Monster) vient uniquement de TargetCatalog. Un
                // nom absent du catalogue, ou présent des deux côtés, n'est jamais
                // engagé : on ne devine ni l'arme ni la portée à utiliser.
                TargetCategory weaponCategory;
                if (!ResolveWeaponCategory(pnj, out weaponCategory))
                    continue;
                if (weaponCategory == TargetCategory.Monster
                    && _excludedMonsterTargets.Contains(pnj.Id))
                    continue;

                // Boss prioritaires : le plus faible en PV d'abord (à PV égaux, le plus proche) ;
                // les autres cibles : le plus proche.
                if (bestPnj == null
                    || priority && !bestIsPriority
                    || priority == bestIsPriority
                        && (priority
                            ? pnj.Vie < bestPnj.Vie || pnj.Vie == bestPnj.Vie && pnj.Distance < bestPnj.Distance
                            : pnj.Distance < bestPnj.Distance))
                {
                    bestPnj = pnj;
                    bestCategory = weaponCategory;
                    bestIsPriority = priority;
                }
            }

            if (bestPnj != null)
            {
                best = new CombatTarget
                {
                    Kind = CombatTargetKind.NetworkId,
                    NetId = bestPnj.Id,
                    Category = bestPnj.Categorie,
                    Type = bestPnj.Type,
                    Name = bestPnj.Nom,
                    X = bestPnj.X,
                    Y = bestPnj.Y,
                    Distance = bestPnj.Distance,
                    Health = bestPnj.Vie,
                    HasHealth = true,
                    WeaponCategory = bestCategory,
                    AmmoId = ResolveAmmo(bestCategory, bestPnj.Nom),
                    Portee = bestPnj.Portee
                };
            }
        }

        if (_allowShip != null && _raidAllowPnj == null && snapshot.Navires != null)
        {
            for (int i = 0; i < snapshot.Navires.Count; i++)
            {
                NavireInfo ship = snapshot.Navires[i];
                if (ship == null || ship.IdGlobal == 0
                    || IsInvalidDistance(ship.Distance) || !_allowShip(ship))
                    continue;

                CombatTarget candidate = new CombatTarget
                {
                    Kind = CombatTargetKind.PlayerGlobalId,
                    PlayerGlobalId = ship.IdGlobal,
                    Category = "player",
                    Name = ship.Nom,
                    X = ship.X,
                    Y = ship.Y,
                    Distance = ship.Distance,
                    HasHealth = false
                };
                if (IsCloser(candidate, best))
                    best = candidate;
            }
        }

        return best;
    }

    private bool DemarrerCollecte(CollectibleInfo collectible)
    {
        long callbackSequence;
        long rewardSequence;
        GameState.ObtenirSequencesConfirmationCollecte(
            out callbackSequence,
            out rewardSequence);

        var context = new CollectActionContext(
            collectible.Id,
            collectible.Type,
            collectible.Categorie,
            collectible.X,
            collectible.Y,
            callbackSequence,
            rewardSequence);
        bool started = _brain.TryPreempt(
            BehaviorActionType.Collect,
            EngagedActionPriority,
            false,
            context);
        BehaviorAction action = _brain.CurrentAction;
        if (started && action != null && action.Type == BehaviorActionType.Collect)
        {
            context.BindToAction(action);
        }
        return started;
    }

    private bool DemarrerCombat(CombatTarget target, EtatJeuSnapshot snapshot)
    {
        float ownRange = snapshot.Joueur.Portee;
        DeciderPlacement(target, ownRange);

        bool started = _brain.TryPreempt(
            BehaviorActionType.Combat,
            EngagedActionPriority,
            false,
            new CombatActionContext { Target = target });

        if (started && target.WeaponCategory == TargetCategory.Npc)
        {
            string porteeNpc = float.IsNaN(target.Portee) || float.IsInfinity(target.Portee)
                ? "illisible"
                : target.Portee.ToString("0.##", CultureInfo.InvariantCulture);
        }
        return started;
    }

    /*
     * Placement face à un NPC, décidé une fois au démarrage du combat
     * (R = ma portée, r = portée du NPC) :
     * - LongRange coché et R - décalage > r -> rester hors de sa portée, viser R - décalage
     * - sinon (décoché, pas plus de portée que lui, valeur illisible) -> normal
     */
    private void DeciderPlacement(CombatTarget target, float ownRange)
    {
        target.StayOutOfReach = false;
        target.AimDistance = 0f;
        target.PlacementReason = null;
        target.HasApproachPoint = false;
        target.ApproachOffsetX = 0f;
        target.ApproachOffsetY = 0f;

        if (target.WeaponCategory != TargetCategory.Npc)
            return;

        if (!_longRange)
        {
            target.PlacementReason = "LongRange décoché, comportement normal";
            DeciderApprocheAleatoire(target, ownRange);
            return;
        }

        float npcRange = target.Portee;
        if (float.IsNaN(npcRange) || float.IsInfinity(npcRange) || npcRange < 0f
            || float.IsNaN(ownRange) || float.IsInfinity(ownRange) || ownRange <= 0f)
        {
            target.PlacementReason = "portée illisible, comportement normal";
            DeciderApprocheAleatoire(target, ownRange);
            return;
        }

        float aim = ownRange - LongRangeAimInset;
        if (aim <= npcRange)
        {
            target.PlacementReason = "pas plus de portée que lui, comportement normal";
            DeciderApprocheAleatoire(target, ownRange);
            return;
        }

        target.StayOutOfReach = true;
        target.AimDistance = aim;
        target.PlacementReason = "fonce jusqu'au premier tir et s'arrête, puis hors de sa portée (replacement à "
            + aim.ToString("0.##", CultureInfo.InvariantCulture) + ")";
    }

    /*
     * Comportement normal : tirer au sort le point d'arrivée autour du NPC
     * (R = ma portée, r = portée du NPC, si lisible) :
     * - maximum = min(R, r) - ApproachEdgeMargin, pour être dans les deux portées ;
     * - distance au hasard entre ApproachMinimumRatio x maximum et le maximum ;
     * - direction au hasard tout autour du NPC.
     * Sans maximum utilisable, pas de point : le bot va sur la position du NPC.
     */
    private void DeciderApprocheAleatoire(CombatTarget target, float ownRange)
    {
        if (float.IsNaN(ownRange) || float.IsInfinity(ownRange) || ownRange <= 0f)
            return;

        float maximum = ownRange;
        float npcRange = target.Portee;
        if (!float.IsNaN(npcRange) && !float.IsInfinity(npcRange) && npcRange > 0f)
            maximum = Math.Min(maximum, npcRange);
        maximum -= ApproachEdgeMargin;
        if (maximum <= 0f)
            return;

        float minimum = maximum * ApproachMinimumRatio;
        float distance = minimum + (float)_approachRandom.NextDouble() * (maximum - minimum);
        double angle = _approachRandom.NextDouble() * 2.0 * Math.PI;

        target.HasApproachPoint = true;
        target.ApproachOffsetX = (float)Math.Cos(angle) * distance;
        target.ApproachOffsetY = (float)Math.Sin(angle) * distance;
        target.PlacementReason += ", arrivée au hasard à "
            + distance.ToString("0.##", CultureInfo.InvariantCulture) + " du NPC";
    }

    private void DemarrerNavigation()
    {
        if (!_brain.HasCurrentAction)
            _brain.StartAction(
                BehaviorActionType.Navigation,
                NavigationPriority,
                true);
    }

    private CombatTarget TrouverCibleCombat(EtatJeuSnapshot snapshot, CombatTarget engaged)
    {
        if (engaged.Kind == CombatTargetKind.NetworkId)
        {
            if (snapshot.Pnjs == null || EffectiveAllowPnj == null)
                return null;

            for (int i = 0; i < snapshot.Pnjs.Count; i++)
            {
                PnjInfo pnj = snapshot.Pnjs[i];
                if (pnj == null || pnj.Id != engaged.NetId || !EffectiveAllowPnj(pnj))
                    continue;

                // A target that is no longer classifiable is invalid and is abandoned.
                TargetCategory weaponCategory;
                if (!ResolveWeaponCategory(pnj, out weaponCategory))
                    return null;

                return new CombatTarget
                {
                    Kind = CombatTargetKind.NetworkId,
                    NetId = pnj.Id,
                    Category = pnj.Categorie,
                    Type = pnj.Type,
                    Name = pnj.Nom,
                    X = pnj.X,
                    Y = pnj.Y,
                    Distance = pnj.Distance,
                    Health = pnj.Vie,
                    HasHealth = true,
                    WeaponCategory = weaponCategory,
                    AmmoId = ResolveAmmo(weaponCategory, pnj.Nom),
                    Portee = pnj.Portee
                };
            }
        }
        else if (snapshot.Navires != null && _allowShip != null && _raidAllowPnj == null)
        {
            for (int i = 0; i < snapshot.Navires.Count; i++)
            {
                NavireInfo ship = snapshot.Navires[i];
                if (ship == null || ship.IdGlobal != engaged.PlayerGlobalId || !_allowShip(ship))
                    continue;
                return new CombatTarget
                {
                    Kind = CombatTargetKind.PlayerGlobalId,
                    PlayerGlobalId = ship.IdGlobal,
                    Category = "player",
                    Name = ship.Nom,
                    X = ship.X,
                    Y = ship.Y,
                    Distance = ship.Distance,
                    HasHealth = false
                };
            }
        }
        return null;
    }

    private static bool EstCollectibleVisible(EtatJeuSnapshot snapshot, uint netId)
    {
        if (snapshot.Collectibles == null)
            return false;
        for (int i = 0; i < snapshot.Collectibles.Count; i++)
        {
            CollectibleInfo item = snapshot.Collectibles[i];
            if (item != null && item.Id == netId)
                return true;
        }
        return false;
    }

    private static bool PossedeRecompense(
        EtatJeuSnapshot snapshot,
        uint netId,
        RewardSourceType type,
        DateTime startedAtUtc,
        RewardSourceType? alternateType = null)
    {
        if (snapshot.Rewards == null)
            return false;
        for (int i = 0; i < snapshot.Rewards.Count; i++)
        {
            RewardEvent reward = snapshot.Rewards[i];
            if (reward == null || reward.SourceNetId != netId || reward.TimestampUtc < startedAtUtc)
                continue;
            if (reward.SourceType == type || alternateType.HasValue && reward.SourceType == alternateType.Value)
                return true;
        }
        return false;
    }

    private static bool AUneRecompenseConnue(
        EtatJeuSnapshot snapshot,
        uint netId,
        RewardSourceType type,
        RewardSourceType? alternateType = null)
    {
        if (netId == 0 || snapshot.Rewards == null)
            return false;

        for (int i = 0; i < snapshot.Rewards.Count; i++)
        {
            RewardEvent reward = snapshot.Rewards[i];
            if (reward == null || reward.SourceNetId != netId)
                continue;
            if (reward.SourceType == type
                || alternateType.HasValue && reward.SourceType == alternateType.Value)
                return true;
        }
        return false;
    }

    /*
     * Catégorie d'arme d'une cible. Le catalogue reste la référence, sauf pour les cibles de la
     * liste interne d'une Raid (boss de type navire d'événement absents du catalogue) :
     * RaidWire.WeaponCategoryFor, la même règle que la console pour le choix des munitions.
     */
    private bool ResolveWeaponCategory(PnjInfo pnj, out TargetCategory category)
    {
        if (_raidAllowPnj != null && _raidAllowPnj(pnj))
        {
            category = RaidWire.WeaponCategoryFor(pnj.Nom);
            return true;
        }

        return TargetCatalog.TryGetCategory(pnj.Nom, out category);
    }

    /*
     * Raid, boss prioritaires : le combat en cours est abandonné dès que le meilleur candidat
     * (ObtenirCombatCandidat : boss d'abord, le moins de PV, puis le plus proche) est un boss
     * différent de la cible engagée. Mêmes filtres que la sélection d'une nouvelle cible.
     */
    private bool PriorityPreemptsActiveCombat(EtatJeuSnapshot snapshot, BehaviorAction active)
    {
        if (_raidPriorityPnj == null || snapshot.Pnjs == null)
            return false;

        CombatActionContext context = active.Context as CombatActionContext;
        CombatTarget engaged = context == null ? null : context.Target;
        if (engaged == null || engaged.Kind != CombatTargetKind.NetworkId)
            return false;

        CombatTarget best = ObtenirCombatCandidat(snapshot);
        if (best == null || best.Kind != CombatTargetKind.NetworkId || best.NetId == engaged.NetId)
            return false;

        PnjInfo bestPnj = null;
        for (int i = 0; i < snapshot.Pnjs.Count; i++)
        {
            PnjInfo pnj = snapshot.Pnjs[i];
            if (pnj != null && pnj.Id == best.NetId)
            {
                bestPnj = pnj;
                break;
            }
        }

        if (bestPnj == null || !_raidPriorityPnj(bestPnj))
            return false;

        return true;
    }

    private static bool IsCloser(CombatTarget candidate, CombatTarget current)
    {
        return current == null || candidate.Distance < current.Distance;
    }

    private int? ResolveAmmo(TargetCategory category, string targetName)
    {
        return _resolveAmmo == null ? (int?)null : _resolveAmmo(category, targetName);
    }

    private static bool IsInvalidDistance(float distance)
    {
        return float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f;
    }
}
