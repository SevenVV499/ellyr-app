using System;
using System.Globalization;
using EtatJoueurMod;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;

public static class CopperWire
{
    // Filet de sécurité seulement : la décision est normalement déclenchée par chaque
    // nouvel instantané (voir OnPlayerUpdate), ce délai ne sert que si aucun instantané
    // n'est publié.
    private const float DecisionIntervalSeconds = 1.0f;
    private const float NavigationStuckSeconds = 10f;

    
    
    
    
    private const float NavigationLookaheadSeconds = 0.5f;
    private const float NavigationLookaheadMinimum = 1f;

    
    
    
    private const float NavigationArrivalGraceSeconds = 0.5f;
    private const float CombatStuckSeconds = 4f;
    private const float MovementProgressThreshold = 0.5f;
    private const float MinimumCombatDistance = 2f;
    private const float CombatCallRetrySeconds = 2f;

    
    
    
    private const float NpcFollowDistanceRatio = 0.5f;
    private const float NpcFollowLeadSeconds = 1f;
    private const float NpcFollowOrderInterval = 0.5f;
    private const float NpcFollowMinSpeed = 0.2f;
    private const float NpcFollowSampleInterval = 0.25f;

    private static string _npcContinuousFollowKey;
    private static Vector2 _npcFollowSamplePosition;
    private static float _npcFollowSampleTime;
    private static Vector2 _npcFollowVelocity;
    private static bool _hasNpcFollowVelocity;
    private static float _npcFollowNextOrderAt;

    
    
    
    
    private const float NpcBandEdgeMargin = 0.15f;
    private const int NpcCellSearchRadius = 12;

    
    private enum NpcRangeMode
    {
        Normal,        
        StayOutOfReach 
    }

    private static readonly BehaviorBrain Brain = new BehaviorBrain();
    private static readonly BluePencil Planner = new BluePencil(Brain);

    private static Player _localPlayer;
    private static uint _cachedIdentityNetId;
    private static NetworkIdentity _cachedIdentity;
    private static int _cachedPlayerGlobalId;
    private static Player _cachedTargetPlayer;

    private static BehaviorAction _executedAction;
    private static int _actionMapId;
    private static bool _hasActionMap;
    private static float _nextDecisionAt;
    private static bool _tickInProgress;

    private static float _actionStartedAt;
    private static float _lastProgressAt;
    private static Vector3 _lastProgressPosition;
    private static bool _hasProgressPosition;
    private static float _lastTargetDistance = float.PositiveInfinity;
    private static string _lastMoveCell;
    private static string _trackedCombatTargetKey;
    private static Vector2 _lastTrackedCombatTargetPosition;
    
    
    private static float _npcBandNextOrderAt;
    private const float NpcBandOrderInterval = 0.25f;
    private static float _lastAttackCallAt = float.NegativeInfinity;
    private static float _combatNoAttackSince = -1f;
    private static GameObject _ownedCombatTarget;
    private static bool _ownsCombat;
    private static int _lastRandomMap;
    private static string _lastRandomCell;
    private static uint _lastCollectTargetId;
    private static string _lastCollectNativeCell;
    private static bool _hasCollectDestination;
    private static bool _collectArrived;
    private static bool _automationEnabled;
    private static long _lastCollectSignalRevision;
    private static long _lastSnapshotRevision;
    private static bool _hasAmmoSelectionAttempt;
    private static Guid _ammoSelectionActionId;
    private static TargetCategory _ammoSelectionCategory;
    private static int _ammoSelectionId;
    private static float _monsterAmmoWaitSince = -1f;
    private static bool _monsterCombatArmed;
    private static bool _monsterShotTimestampCaptured;
    private static float _monsterShotTimestampBefore;
    private static long _npcCannonShotSequenceBefore;
    private static bool _npcCannonShotConfirmed;
    
    
    private static long _liveCannonShotSequence;
    private static uint _liveCannonShotTargetNetId;

    
    
    
    private static float _lastLocalCannonShotTime = float.NegativeInfinity;

    
    private static bool _npcStoppedWhileReloading;
    private static Guid _combatActionInstanceId;
    private static string _combatTargetKey;

    static CopperWire()
    {
        RespawnWire.Configurer(Brain);
    }

    private static bool IssueCollectMove(Player player, uint targetNetId, NetworkIdentity identity)
    {
        if (player == null || player.aiLerp == null || targetNetId == 0
            || identity == null || identity.netId != targetNetId)
            return false;

        try
        {
            bool hasPickupPoint = CollectibleRuntime.TryGetPickupPoint(
                identity,
                Brain.GetCurrentContext<CollectActionContext>()?.Type,
                out Vector3 destination);
            if (!hasPickupPoint)
                destination = identity.transform.position;

            int mapId = player.harita;
            int column;
            string row;
            string nativeCell;
            if (!TryGetMapCell(
                    mapId,
                    destination.x,
                    destination.y,
                    out column,
                    out row,
                    out nativeCell))
                return false;

            bool sameCollectRoute = _lastCollectTargetId == targetNetId
                && _hasCollectDestination
                && string.Equals(
                    _lastCollectNativeCell,
                    nativeCell,
                    StringComparison.Ordinal);
            if (!sameCollectRoute)
            {
                _collectArrived = false;
                _lastMoveCell = null;
                if (!IssueMove(player, mapId, destination.x, destination.y))
                    return false;

                _lastCollectTargetId = targetNetId;
                _lastCollectNativeCell = nativeCell;
                _hasCollectDestination = true;
            }

            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(
                "[CopperWire] Déplacement natif vers le collectible "
                + targetNetId + " échoué : " + e);
            return false;
        }
    }

    public static bool AutomationEnabled { get { return _automationEnabled; } }
    public static BehaviorSystemState SystemState { get { return Brain.SystemState; } }
    public static BehaviorAction CurrentAction { get { return Brain.CurrentAction; } }
    public static bool LongRange { get { return Planner.LongRange; } }
    public static CombatTarget CurrentCombatTarget
    {
        get { return Brain.GetCurrentContext<CombatActionContext>()?.Target; }
    }

    

    public static void SetAutomationEnabled(bool enabled)
    {
        if (_automationEnabled == enabled)
            return;

        _automationEnabled = enabled;
        if (enabled)
        {
            _nextDecisionAt = 0f;
            return;
        }

        if (_executedAction != null && _executedAction.Type == BehaviorActionType.Combat)
            StopCombatForOwnedTarget();
        ReleaseMovementForUser(ObtenirPlayerLocal());
        ClearActionTracking();
        _executedAction = null;
        Planner.CancelCurrent();
        RespawnWire.Reset();
        SurvivalWire.Reset();
    }

    public static void SetLongRange(bool enabled)
    {
        Planner.SetLongRange(enabled);
    }

    public static int? GetSelectedAmmoId(TargetCategory category)
    {
        Player player = ObtenirPlayerLocal();
        if (player == null)
            return null;

        return category == TargetCategory.Monster
            ? player.seciliZipkinId
            : player.seciligulleid;
    }

    public static void Configurer(
        bool collectEnabled,
        bool combatEnabled,
        CombatCollectPriority priority,
        bool longRange = false,
        Func<PnjInfo, bool> allowPnj = null,
        Func<NavireInfo, bool> allowShip = null,
        Func<TargetCategory, string, int?> resolveAmmo = null,
        Func<CollectibleInfo, bool> allowCollectible = null)
    {
        Planner.Configurer(
            collectEnabled,
            combatEnabled,
            priority,
            longRange,
            allowPnj,
            allowShip,
            resolveAmmo,
            allowCollectible);
    }

    internal static void OnPlayerUpdate(Player instance)
    {
        if (instance == null || !instance.isLocalPlayer)
            return;

        long collectSignalRevision = GameState.RevisionSignauxCollecte;
        bool newCollectSignal = collectSignalRevision != _lastCollectSignalRevision;
        long snapshotRevision = GameState.RevisionInstantane;
        bool newSnapshot = snapshotRevision != _lastSnapshotRevision;
        if (_automationEnabled)
        {
            ReactToNpcCannonShot(instance);
            StopApproachWhileReloading(instance);
            FollowEngagedNpc(instance);
            CheckNavigationArrival(instance);
        }
        else
            ClearNpcFollowTracking();

        if (_tickInProgress
            || !newCollectSignal && !newSnapshot && Time.time < _nextDecisionAt)
            return;

        _lastCollectSignalRevision = collectSignalRevision;
        _lastSnapshotRevision = snapshotRevision;
        _nextDecisionAt = Time.time + DecisionIntervalSeconds;
        _tickInProgress = true;
        try
        {
            Tick(instance);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[CopperWire] Erreur de tick : " + e);
        }
        finally
        {
            _tickInProgress = false;
        }
    }

    private static void Tick(Player hookedPlayer)
    {
        Player player = ObtenirPlayerLocal();
        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        if (player == null || player != hookedPlayer || snapshot == null || snapshot.Joueur == null)
            return;

        if (!_automationEnabled)
            return;

        
        
        bool etaitActif = RespawnWire.IsActive;
        RespawnWire.Tick(player, snapshot);
        if (etaitActif || RespawnWire.IsActive || Brain.IsRespawning)
        {
            SurvivalWire.Reset();
            if (_executedAction != null)
            {
                CleanupAction(_executedAction, player);
                _executedAction = null;
            }
            return;
        }

        
        
        
        if (snapshot.Timestamp <= RespawnWire.DernierSnapshotConfirme)
            return;

        
        
        
        if (Planner.SynchroniserCarte(snapshot))
        {
            CleanupAction(_executedAction, player);
            _executedAction = null;
            ClearTargetCaches();
            _lastRandomMap = 0;
            _lastRandomCell = null;
        }
        _actionMapId = snapshot.Joueur.Harita;
        _hasActionMap = true;

        bool fleeEnded;
        bool fleeing = SurvivalWire.Tick(player, snapshot, out fleeEnded);
        // Arrêt total (navire immobile) : pause de réparation « Stopped » quand la
        // fuite n'est pas active. Seule la réparation travaille.
        if (!fleeing && SurvivalWire.IsRepairPaused)
        {
            _wasHalted = true;
            HaltAllActivity(player, snapshot);
            return;
        }

        if (_wasHalted)
        {
            _wasHalted = false;
            _lastMoveCell = null;
            _nextDecisionAt = 0f;
        }

        _haltStopIssued = false;
        if (fleeing)
        {
            // La fuite est prioritaire : Combat abandonné et interdit, Collecte
            // seulement si l'option est cochée, sinon seule la Navigation continue.
            // La réparation (traitée par SurvivalWire) reste parallèle.
            AbandonActionForFlee(player);
        }
        else if (fleeEnded)
        {
            _lastMoveCell = null;
            _nextDecisionAt = 0f;
        }

        BehaviorAction activeCombatAction = Brain.CurrentAction;
        bool bypassCombatRewardEvaluation = false;
        if (activeCombatAction != null
            && activeCombatAction == _executedAction
            && activeCombatAction.Type == BehaviorActionType.Combat)
        {
            CombatTargetEvaluation evaluation = EvaluateCombatTarget(activeCombatAction, snapshot);
            if (evaluation == CombatTargetEvaluation.Completed)
            {
                Planner.CompleteCurrent();
            }
            else if (evaluation == CombatTargetEvaluation.Invalidated)
            {
                Planner.FailCurrent();
            }
            else
            {
                bypassCombatRewardEvaluation = Planner.CombatEnabled
                    && HasPlannerMatchedCombatReward(activeCombatAction, snapshot);
            }
        }

        BehaviorActionType selected;
        BehaviorAction action;
        if (bypassCombatRewardEvaluation)
        {
            RefreshCombatTargetFromSnapshot(activeCombatAction, snapshot);
            selected = BehaviorActionType.Combat;
            action = activeCombatAction;
        }
        else
        {
            selected = Planner.Decide(snapshot, fleeing, Plugin.FleeCollectEnabled);
            action = Brain.CurrentAction;
        }

        if (action != _executedAction)
        {
            CleanupAction(_executedAction, player);
            _executedAction = null;
            if (action == null)
                return;
            BeginAction(action, player, snapshot.Joueur.Harita, snapshot.Joueur);
        }
        else if (action == null)
        {
            return;
        }

        ExecutionResult result;
        switch (selected)
        {
            case BehaviorActionType.Navigation:
                result = ExecuteNavigation(player, snapshot.Joueur.Harita);
                break;
            case BehaviorActionType.Collect:
                result = ExecuteCollect(player, snapshot);
                break;
            case BehaviorActionType.Combat:
                result = ExecuteCombat(player, snapshot);
                break;
            default:
                return;
        }

        if (result == ExecutionResult.Running)
            return;

        if (result == ExecutionResult.Completed)
            Planner.CompleteCurrent();
        else
        {
            Planner.FailCurrent();
        }

        CleanupAction(action, player);
        _executedAction = null;

        
        
        
        
        if (result == ExecutionResult.Completed)
            _nextDecisionAt = 0f;
    }

    private static void BeginAction(
        BehaviorAction action,
        Player player,
        int mapId,
        FicheJoueur joueur)
    {
        _executedAction = action;
        _actionMapId = mapId;
        _hasActionMap = true;
        _actionStartedAt = Time.time;
        _lastProgressAt = Time.time;
        _lastProgressPosition = player.transform.position;
        _hasProgressPosition = true;
        _lastTargetDistance = float.PositiveInfinity;
        _lastMoveCell = null;
        _trackedCombatTargetKey = null;
        ClearNpcFollowTracking();
        _lastCollectTargetId = 0;
        _hasCollectDestination = false;
        _lastCollectNativeCell = null;
        _collectArrived = false;
        _lastAttackCallAt = float.NegativeInfinity;
        _combatNoAttackSince = -1f;
        _ownedCombatTarget = null;
        _ownsCombat = false;
        _hasAmmoSelectionAttempt = false;
        _ammoSelectionActionId = Guid.Empty;
        _monsterAmmoWaitSince = -1f;
        _monsterCombatArmed = false;
        _npcCannonShotSequenceBefore = _liveCannonShotSequence;
        _npcCannonShotConfirmed = false;
        _npcStoppedWhileReloading = false;
        CombatActionContext combatContext = action.Type == BehaviorActionType.Combat
            ? action.Context as CombatActionContext
            : null;
        _combatActionInstanceId = combatContext == null ? Guid.Empty : action.InstanceId;
        _combatTargetKey = combatContext == null || combatContext.Target == null
            ? null
            : combatContext.Target.Key;
        if (combatContext != null
            && combatContext.Target != null
            && combatContext.Target.WeaponCategory == TargetCategory.Monster)
        {
            _monsterShotTimestampBefore = joueur.DernierTirHarpon;
            _monsterShotTimestampCaptured = true;
        }
        else
        {
            _monsterShotTimestampBefore = 0f;
            _monsterShotTimestampCaptured = false;
        }
    }

    private static bool _haltStopIssued;
    private static bool _wasHalted;

    private static void HaltAllActivity(Player player, EtatJeuSnapshot snapshot)
    {
        if (_executedAction != null || Brain.HasCurrentAction)
        {
            if (_executedAction != null)
            {
                CleanupAction(_executedAction, player);
                _executedAction = null;
            }
            Planner.CancelCurrent();
            _haltStopIssued = false;
        }

        if (_haltStopIssued)
            return;

        // Ordre de déplacement vers la case actuelle : annule la destination en cours.
        Vector3 position = player.transform.position;
        _lastMoveCell = null;
        if (IssueMove(player, snapshot.Joueur.Harita, position.x, position.y))
            _haltStopIssued = true;
    }

    // Fuite : le Combat est abandonné (la Collecte aussi, sauf si l'option est
    // cochée) ; la Navigation en cours continue normalement, sans déplacement
    // spécifique.
    private static void AbandonActionForFlee(Player player)
    {
        BehaviorAction current = Brain.CurrentAction;
        if (current != null
            && current == _executedAction
            && (current.Type == BehaviorActionType.Navigation
                || current.Type == BehaviorActionType.Collect && Plugin.FleeCollectEnabled))
            return;

        if (_executedAction != null)
        {
            CleanupAction(_executedAction, player);
            _executedAction = null;
        }
        Planner.CancelCurrent();
    }

    private static ExecutionResult ExecuteNavigation(Player player, int mapId)
    {
        if (string.IsNullOrEmpty(_lastMoveCell))
        {
            if (!ChooseAndIssueRandomDestination(player, mapId))
            {
                if (Time.time - _actionStartedAt >= NavigationStuckSeconds)
                    return ExecutionResult.Failed;
                return ExecutionResult.Running;
            }
        }

        TrackPlayerProgress(player);
        if (IsNearNavigationDestination(player))
            return ExecutionResult.Completed;
        if (Time.time - _lastProgressAt >= NavigationStuckSeconds)
            return ExecutionResult.Failed;
        return ExecutionResult.Running;
    }

    private static ExecutionResult ExecuteCollect(Player player, EtatJeuSnapshot snapshot)
    {
        CollectActionContext context = Brain.GetCurrentContext<CollectActionContext>();
        if (context == null || context.NetId == 0)
            return ExecutionResult.Failed;

        NetworkIdentity identity = ResolveNetworkIdentity(context.NetId);
        if (identity == null)
            return SnapshotContainsCollectible(snapshot, context.NetId)
                ? ExecutionResult.Running
                : ExecutionResult.Failed;

        if (identity.netId != context.NetId)
            return ExecutionResult.Failed;

        if (!IssueCollectMove(player, context.NetId, identity))
        {
            return ExecutionResult.Failed;
        }

        TrackPlayerProgress(player);

        bool pathDestinationReached = player.aiLerp != null
            && !player.aiLerp.pathPending
            && player.aiLerp.reachedDestination;
        if (pathDestinationReached)
        {
            context.MarkArrived(DateTime.UtcNow);
            if (!_collectArrived)
                _collectArrived = true;
            return ExecutionResult.Running;
        }

        if (_collectArrived)
            return ExecutionResult.Running;

        if (Time.time - _lastProgressAt >= NavigationStuckSeconds)
            return ExecutionResult.Failed;
        return ExecutionResult.Running;
    }

    private static CombatTargetEvaluation EvaluateCombatTarget(
        BehaviorAction action,
        EtatJeuSnapshot snapshot)
    {
        if (action == null
            || action != Brain.CurrentAction
            || action.Type != BehaviorActionType.Combat
            || action.InstanceId != _combatActionInstanceId)
            return CombatTargetEvaluation.Invalidated;

        CombatActionContext context = action.Context as CombatActionContext;
        CombatTarget target = context == null ? null : context.Target;
        if (target == null
            || string.IsNullOrEmpty(_combatTargetKey)
            || !string.Equals(target.Key, _combatTargetKey, StringComparison.Ordinal))
            return CombatTargetEvaluation.Invalidated;

        if (snapshot == null)
            return CombatTargetEvaluation.Invalidated;

        if (target.Kind == CombatTargetKind.NetworkId)
        {
            if (target.NetId == 0
                || target.WeaponCategory != TargetCategory.Npc
                    && target.WeaponCategory != TargetCategory.Monster
                || snapshot.Pnjs == null)
                return CombatTargetEvaluation.Invalidated;

            for (int i = 0; i < snapshot.Pnjs.Count; i++)
            {
                PnjInfo observed = snapshot.Pnjs[i];
                if (observed == null || observed.Id != target.NetId)
                    continue;

                target.Health = observed.Vie;
                target.HasHealth = true;
                target.X = observed.X;
                target.Y = observed.Y;
                target.Distance = observed.Distance;
                return observed.Vie <= 0
                    ? CombatTargetEvaluation.Completed
                    : CombatTargetEvaluation.Running;
            }

            return HasFreshCombatReward(snapshot, target, action.StartedAtUtc)
                ? CombatTargetEvaluation.Completed
                : CombatTargetEvaluation.Invalidated;
        }

        if (target.Kind == CombatTargetKind.PlayerGlobalId)
        {
            if (target.PlayerGlobalId == 0 || snapshot.Navires == null)
                return CombatTargetEvaluation.Invalidated;

            for (int i = 0; i < snapshot.Navires.Count; i++)
            {
                NavireInfo observed = snapshot.Navires[i];
                if (observed == null || observed.IdGlobal != target.PlayerGlobalId)
                    continue;

                target.X = observed.X;
                target.Y = observed.Y;
                target.Distance = observed.Distance;
                target.Name = observed.Nom;
                target.HasHealth = false;
                return CombatTargetEvaluation.Running;
            }

            return CombatTargetEvaluation.Invalidated;
        }

        return CombatTargetEvaluation.Invalidated;
    }

    private static bool HasFreshCombatReward(
        EtatJeuSnapshot snapshot,
        CombatTarget target,
        DateTime actionStartedAtUtc)
    {
        if (snapshot.Rewards == null
            || target == null
            || target.Kind != CombatTargetKind.NetworkId
            || target.NetId == 0)
            return false;

        RewardSourceType expectedSourceType;
        if (target.WeaponCategory == TargetCategory.Monster)
            expectedSourceType = RewardSourceType.Monstre;
        else if (target.WeaponCategory == TargetCategory.Npc)
            expectedSourceType = RewardSourceType.Pnj;
        else
            return false;

        for (int i = 0; i < snapshot.Rewards.Count; i++)
        {
            RewardEvent reward = snapshot.Rewards[i];
            if (reward != null
                && reward.SourceNetId == target.NetId
                && reward.SourceType == expectedSourceType
                && reward.TimestampUtc >= actionStartedAtUtc)
                return true;
        }

        return false;
    }

    private static bool HasPlannerMatchedCombatReward(
        BehaviorAction action,
        EtatJeuSnapshot snapshot)
    {
        CombatActionContext context = action == null
            ? null
            : action.Context as CombatActionContext;
        CombatTarget target = context == null ? null : context.Target;
        if (target == null
            || target.Kind != CombatTargetKind.NetworkId
            || target.NetId == 0
            || snapshot == null
            || snapshot.Rewards == null)
            return false;

        for (int i = 0; i < snapshot.Rewards.Count; i++)
        {
            RewardEvent reward = snapshot.Rewards[i];
            if (reward != null
                && reward.SourceNetId == target.NetId
                && reward.TimestampUtc >= action.StartedAtUtc
                && (reward.SourceType == RewardSourceType.Monstre
                    || reward.SourceType == RewardSourceType.Pnj))
                return true;
        }

        return false;
    }

    private static void RefreshCombatTargetFromSnapshot(
        BehaviorAction action,
        EtatJeuSnapshot snapshot)
    {
        CombatActionContext context = action == null
            ? null
            : action.Context as CombatActionContext;
        CombatTarget target = context == null ? null : context.Target;
        if (target == null || snapshot == null)
            return;

        if (target.Kind == CombatTargetKind.NetworkId && snapshot.Pnjs != null)
        {
            for (int i = 0; i < snapshot.Pnjs.Count; i++)
            {
                PnjInfo observed = snapshot.Pnjs[i];
                if (observed == null || observed.Id != target.NetId)
                    continue;

                target.Category = observed.Categorie;
                target.Type = observed.Type;
                target.Name = observed.Nom;
                target.X = observed.X;
                target.Y = observed.Y;
                target.Distance = observed.Distance;
                target.Health = observed.Vie;
                target.HasHealth = true;
                return;
            }
        }
        else if (target.Kind == CombatTargetKind.PlayerGlobalId && snapshot.Navires != null)
        {
            for (int i = 0; i < snapshot.Navires.Count; i++)
            {
                NavireInfo observed = snapshot.Navires[i];
                if (observed == null || observed.IdGlobal != target.PlayerGlobalId)
                    continue;

                target.Name = observed.Nom;
                target.X = observed.X;
                target.Y = observed.Y;
                target.Distance = observed.Distance;
                target.HasHealth = false;
                return;
            }
        }
    }

    private static ExecutionResult ExecuteCombat(Player player, EtatJeuSnapshot snapshot)
    {
        BehaviorAction action = Brain.CurrentAction;
        CombatActionContext context = Brain.GetCurrentContext<CombatActionContext>();
        if (context == null || context.Target == null)
            return ExecutionResult.Failed;

        CombatTargetEvaluation targetEvaluation = EvaluateCombatTarget(action, snapshot);
        if (targetEvaluation == CombatTargetEvaluation.Completed)
            return ExecutionResult.Completed;
        if (targetEvaluation == CombatTargetEvaluation.Invalidated)
            return ExecutionResult.Failed;

        GameObject target = ResolveCombatTarget(context.Target);
        if (target == null)
            return ExecutionResult.Failed;

        Vector2 targetPosition;
        if (!TryGetCombatTargetPosition(context.Target, target, out targetPosition))
            return ExecutionResult.Failed;

        if (context.Target.WeaponCategory == TargetCategory.Monster)
            return ExecuteMonsterCombat(player, snapshot, context.Target, target, targetPosition);

        if (!EnsureCombatTarget(player, target))
            return ExecutionResult.Failed;
        if (managerAttackUnavailable(player, target))
            return ExecutionResult.Failed;
        ApplyCombatAmmo(player, context.Target);

        if (context.Target.WeaponCategory == TargetCategory.Npc)
            return ExecuteNpcCombat(player, snapshot, context.Target, targetPosition);

        Vector3 playerPosition = player.transform.position;
        float distance = Vector2.Distance(
            new Vector2(playerPosition.x, playerPosition.y),
            targetPosition);
        float ownRange = snapshot.Joueur.Portee;
        if (!IsFinitePositive(ownRange))
            return ExecutionResult.Failed;

        float minimumDistance = MinimumCombatDistance;
        float maximumDistance = ownRange;
        if (Planner.LongRange)
            minimumDistance = Math.Max(0f, ownRange - MovementProgressThreshold);

        if (distance >= minimumDistance && distance <= maximumDistance)
        {
            _lastProgressAt = Time.time;
            _lastTargetDistance = distance;
            return ExecutionResult.Running;
        }

        float desiredDistance = Planner.LongRange
            ? ownRange
            : Math.Max(MinimumCombatDistance, ownRange - 1f);
        if (!Planner.LongRange && maximumDistance < MinimumCombatDistance)
            desiredDistance = MinimumCombatDistance;

        Vector2 direction = new Vector2(
            playerPosition.x - targetPosition.x,
            playerPosition.y - targetPosition.y);
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.right;
        else
            direction.Normalize();

        float destinationX = targetPosition.x + direction.x * desiredDistance;
        float destinationY = targetPosition.y + direction.y * desiredDistance;
        if (!IssueMove(player, snapshot.Joueur.Harita, destinationX, destinationY))
            return ExecutionResult.Failed;

        if (distance < _lastTargetDistance - MovementProgressThreshold)
            _lastProgressAt = Time.time;
        _lastTargetDistance = distance;

        if (HasNoUsablePath(player) && Time.time - _actionStartedAt >= CombatStuckSeconds)
            return ExecutionResult.Failed;
        if (Time.time - _lastProgressAt >= CombatStuckSeconds)
            return ExecutionResult.Failed;
        return ExecutionResult.Running;
    }

    private static ExecutionResult ExecuteNpcCombat(
        Player player,
        EtatJeuSnapshot snapshot,
        CombatTarget combatTarget,
        Vector2 targetPosition)
    {
        Vector3 playerPosition = player.transform.position;
        Vector2 playerPosition2D = new Vector2(playerPosition.x, playerPosition.y);
        float distance = Vector2.Distance(playerPosition2D, targetPosition);
        float ownRange = snapshot.Joueur.Portee;
        if (!IsFinitePositive(ownRange))
            return ExecutionResult.Failed;

        

        
        NpcRangeMode rangeMode = combatTarget.StayOutOfReach
            ? NpcRangeMode.StayOutOfReach
            : NpcRangeMode.Normal;
        float ringMinimum = combatTarget.StayOutOfReach ? combatTarget.Portee : 0f;

        
        
        
        
        
        
        
        bool waitingFirstShot = rangeMode == NpcRangeMode.StayOutOfReach
            && !_npcCannonShotConfirmed
            && !_npcStoppedWhileReloading;
        NpcRangeMode moveMode = waitingFirstShot ? NpcRangeMode.Normal : rangeMode;

        bool insideCannonRange = distance <= ownRange;
        bool inDesiredRangeBand = moveMode == NpcRangeMode.StayOutOfReach
            ? distance >= ringMinimum && insideCannonRange
            : insideCannonRange;

        if (inDesiredRangeBand)
        {
            _lastTargetDistance = distance;
            if (_npcCannonShotConfirmed)
            {
                _lastProgressAt = Time.time;
                return ExecutionResult.Running;
            }

            if (Time.time - _lastProgressAt >= CombatStuckSeconds)
                return ExecutionResult.Failed;
            return ExecutionResult.Running;
        }

        if (player.aiLerp != null
            && !player.aiLerp.pathPending
            && (player.aiLerp.reachedDestination || player.aiLerp.reachedEndOfPath))
        {
            _lastMoveCell = null;
        }

        
        
        if (moveMode == NpcRangeMode.StayOutOfReach)
            return CheckNpcCombatProgress(player, distance);

        
        
        bool useApproachPoint = rangeMode == NpcRangeMode.Normal && combatTarget.HasApproachPoint;
        float destinationX = targetPosition.x + (useApproachPoint ? combatTarget.ApproachOffsetX : 0f);
        float destinationY = targetPosition.y + (useApproachPoint ? combatTarget.ApproachOffsetY : 0f);

        if (!IssueMove(player, snapshot.Joueur.Harita, destinationX, destinationY))
            return ExecutionResult.Failed;

        return CheckNpcCombatProgress(player, distance);
    }

    
    private static ExecutionResult CheckNpcCombatProgress(Player player, float distance)
    {
        TrackPlayerProgress(player);
        if (distance < _lastTargetDistance - MovementProgressThreshold)
            _lastProgressAt = Time.time;
        _lastTargetDistance = distance;

        if (HasNoUsablePath(player) && Time.time - _actionStartedAt >= CombatStuckSeconds)
            return ExecutionResult.Failed;
        if (Time.time - _lastProgressAt >= CombatStuckSeconds)
            return ExecutionResult.Failed;
        return ExecutionResult.Running;
    }

    private static ExecutionResult ExecuteMonsterCombat(
        Player player,
        EtatJeuSnapshot snapshot,
        CombatTarget combatTarget,
        GameObject target,
        Vector2 targetPosition)
    {
        ApplyCombatAmmo(player, combatTarget);
        if (combatTarget.AmmoId.HasValue
            && player.seciliZipkinId != combatTarget.AmmoId.Value)
        {
            if (_monsterAmmoWaitSince < 0f)
                _monsterAmmoWaitSince = Time.time;
            if (Time.time - _monsterAmmoWaitSince >= CombatStuckSeconds)
                return ExecutionResult.Failed;
            return ExecutionResult.Running;
        }

        _monsterAmmoWaitSince = -1f;

        GameManager manager = GameManager.gm;
        if (manager == null)
            return ExecutionResult.Failed;

        if (!_monsterShotTimestampCaptured)
        {
            _monsterShotTimestampBefore = snapshot.Joueur.DernierTirHarpon;
            _monsterShotTimestampCaptured = true;
        }

        if (!_monsterCombatArmed)
        {
            if (manager.hedefgemi == target && player.saldiridurumu)
            {
                _monsterCombatArmed = true;
            }
            else
            {
                try
                {
                    StartAttack(manager, player, target);
                    _lastAttackCallAt = Time.time;
                    _ownedCombatTarget = target;
                    _ownsCombat = true;
                    _monsterCombatArmed = true;
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError("[CopperWire] Erreur démarrage combat Monster : " + e);
                    return ExecutionResult.Failed;
                }
            }
        }

        if (managerAttackUnavailable(player, target))
            return ExecutionResult.Failed;

        if (snapshot.Joueur.DernierTirHarpon > _monsterShotTimestampBefore)
        {
            return ExecutionResult.Running;
        }

        if (!IssueMove(player, snapshot.Joueur.Harita, targetPosition.x, targetPosition.y))
            return ExecutionResult.Failed;

        TrackPlayerProgress(player);
        if (HasNoUsablePath(player) && Time.time - _actionStartedAt >= CombatStuckSeconds)
            return ExecutionResult.Failed;
        if (Time.time - _lastProgressAt >= CombatStuckSeconds)
            return ExecutionResult.Failed;

        return ExecutionResult.Running;
    }

    private static void ApplyCombatAmmo(Player player, CombatTarget target)
    {
        if (player == null || target == null || !target.WeaponCategory.HasValue
            || !target.AmmoId.HasValue)
            return;

        BehaviorAction action = Brain.CurrentAction;
        if (action == null || action.Type != BehaviorActionType.Combat)
            return;

        TargetCategory category = target.WeaponCategory.Value;
        int requestedId = target.AmmoId.Value;
        int selectedId = category == TargetCategory.Monster
            ? player.seciliZipkinId
            : player.seciligulleid;
        if (selectedId == requestedId)
            return;

        if (_hasAmmoSelectionAttempt
            && _ammoSelectionActionId == action.InstanceId
            && _ammoSelectionCategory == category
            && _ammoSelectionId == requestedId)
            return;

        GameManager manager = GameManager.gm;
        if (manager == null)
            return;

        _hasAmmoSelectionAttempt = true;
        _ammoSelectionActionId = action.InstanceId;
        _ammoSelectionCategory = category;
        _ammoSelectionId = requestedId;

        try
        {
            if (category == TargetCategory.Monster)
            {
                manager.ZipkinDegistir(requestedId);
            }
            else
            {
                manager.GulleDegistir(requestedId);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(
                "[CopperWire] Erreur de sélection des munitions "
                + category + " ID=" + requestedId + " : " + e);
        }
    }

    // Démarrage de l'attaque. GameManager.Saldir() valide la cible puis passe
    // Player.saldiridurumu à true (écriture locale, aucune commande réseau) ; le jeu
    // envoie ensuite lui-même chaque tir (GulleYarat / ZipkinYarat).
    // Constaté dans les journaux : fenêtre non active, Saldir() n'engage jamais
    // l'attaque. Repli, uniquement hors focus : cible fixée directement et
    // Player.Saldir(true), c'est-à-dire exactement l'écriture que fait Saldir().
    private static void StartAttack(GameManager manager, Player player, GameObject target)
    {
        manager.isaretliGemi = target;
        manager.Saldir();
        if (player.saldiridurumu && manager.hedefgemi == target)
            return;

        if (Application.isFocused)
            return;

        try
        {
            manager.hedefgemi = target;
            player.Saldir(true);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[CopperWire] Repli d'attaque hors focus échoué : " + e);
        }
    }

    private static bool EnsureCombatTarget(Player player, GameObject target)
    {
        GameManager manager = GameManager.gm;
        if (manager == null || target == null)
            return false;
        if (manager.hedefgemi == target && player.saldiridurumu)
            return true;
        if (Time.time - _lastAttackCallAt < CombatCallRetrySeconds)
            return true;

        try
        {
            StartAttack(manager, player, target);
            _lastAttackCallAt = Time.time;
            _ownedCombatTarget = target;
            _ownsCombat = true;
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[CopperWire] Erreur démarrage combat : " + e);
            return false;
        }
    }

    private static bool managerAttackUnavailable(Player player, GameObject target)
    {
        GameManager manager = GameManager.gm;
        if (manager == null || target == null)
            return true;

        bool attackingTarget = player.saldiridurumu && manager.hedefgemi == target;
        if (attackingTarget)
        {
            _combatNoAttackSince = -1f;
            return false;
        }

        if (_combatNoAttackSince < 0f)
            _combatNoAttackSince = Time.time;
        return Time.time - _combatNoAttackSince >= CombatStuckSeconds;
    }

    private static void StopCombatForOwnedTarget()
    {
        if (!_ownsCombat || _ownedCombatTarget == null)
        {
            _ownsCombat = false;
            _ownedCombatTarget = null;
            return;
        }

        try
        {
            GameManager manager = GameManager.gm;
            Player player = ObtenirPlayerLocal();
            if (manager != null
                && manager.hedefgemi == _ownedCombatTarget
                && (player == null || player.saldiridurumu))
            {
                manager.SaldiriDurdur();
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[CopperWire] Erreur arrêt combat : " + e);
        }
        finally
        {
            _ownsCombat = false;
            _ownedCombatTarget = null;
        }
    }

    private static void CleanupAction(BehaviorAction action, Player player)
    {
        if (action == null)
            return;

        if (action.Type == BehaviorActionType.Combat)
            StopCombatForOwnedTarget();

        ClearActionTracking();
    }

    private static void ClearActionTracking()
    {
        ClearTargetCaches();
        _lastMoveCell = null;
        _lastCollectTargetId = 0;
        _hasCollectDestination = false;
        _lastCollectNativeCell = null;
        _collectArrived = false;
        _lastTargetDistance = float.PositiveInfinity;
        _hasProgressPosition = false;
        _trackedCombatTargetKey = null;
        ClearNpcFollowTracking();
        _npcContinuousFollowKey = null;
        _monsterShotTimestampCaptured = false;
        _npcCannonShotConfirmed = false;
        _npcStoppedWhileReloading = false;
        _combatActionInstanceId = Guid.Empty;
        _combatTargetKey = null;
    }

    private static void ReleaseMovementForUser(Player player)
    {
        if (player == null || player.aiLerp == null)
            return;

        try
        {
            player.aiLerp.isStopped = false;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[CopperWire] Erreur de libération du déplacement utilisateur : " + e);
        }
    }

    private static bool ChooseAndIssueRandomDestination(Player player, int mapId)
    {
        float minX, maxX, minY, maxY;
        if (!GameState.ObtenirLimitesCarte(mapId, out minX, out maxX, out minY, out maxY)
            || maxX <= minX || maxY <= minY)
            return false;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            float x = UnityEngine.Random.Range(minX, maxX);
            float y = UnityEngine.Random.Range(minY, maxY);
            string cell;
            if (!TryGetMapCell(mapId, x, y, out cell))
                continue;

            if (mapId == _lastRandomMap && cell == _lastRandomCell)
                continue;
            Vector3 current = player.transform.position;
            if (Vector2.Distance(
                    new Vector2(current.x, current.y),
                    new Vector2(x, y)) < 5f)
                continue;

            if (!IssueMove(player, mapId, x, y))
                continue;

            _lastRandomMap = mapId;
            _lastRandomCell = cell;
            return true;
        }
        return false;
    }

    private static bool IssueMove(Player player, int mapId, float x, float y)
    {
        string cell;
        int column;
        string row;
        if (!TryGetMapCell(mapId, x, y, out column, out row, out cell))
            return false;

        return IssueMoveToCell(player, column, row);
    }

    
    private static bool IssueMoveToCell(Player player, int column, string row)
    {
        string cell = column.ToString(CultureInfo.InvariantCulture) + "|" + row;
        if (cell == _lastMoveCell)
        {
            if (player.aiLerp != null)
                player.aiLerp.isStopped = false;
            return true;
        }

        try
        {
            if (player.aiLerp != null)
                player.aiLerp.isStopped = false;
            player.HedefeGit(column, row);
            _lastMoveCell = cell;
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[CopperWire] Player.HedefeGit a échoué : " + e);
            return false;
        }
    }

    private static bool TryGetCombatTargetPosition(
        CombatTarget combatTarget,
        GameObject target,
        out Vector2 targetPosition)
    {
        targetPosition = Vector2.zero;
        if (combatTarget == null || target == null)
            return false;

        if (combatTarget.Kind == CombatTargetKind.NetworkId
            && combatTarget.WeaponCategory == TargetCategory.Monster)
        {
            if (!IsFinite(combatTarget.X) || !IsFinite(combatTarget.Y))
                return false;

            targetPosition = new Vector2(combatTarget.X, combatTarget.Y);
            return true;
        }

        bool isTrackedNpc = combatTarget.Kind == CombatTargetKind.NetworkId
            && combatTarget.WeaponCategory == TargetCategory.Npc;
        if (!isTrackedNpc)
        {
            Vector3 worldPosition = target.transform.position;
            targetPosition = new Vector2(worldPosition.x, worldPosition.y);
            return IsFinite(targetPosition.x) && IsFinite(targetPosition.y);
        }

        Vector3 npcWorldPosition = target.transform.position;
        targetPosition = new Vector2(npcWorldPosition.x, npcWorldPosition.y);
        if (!IsFinite(targetPosition.x) || !IsFinite(targetPosition.y))
            return false;

        if (!string.Equals(
                _trackedCombatTargetKey,
                combatTarget.Key,
                StringComparison.Ordinal))
        {
            _trackedCombatTargetKey = combatTarget.Key;
            _lastTrackedCombatTargetPosition = targetPosition;
            _lastMoveCell = null;
            return true;
        }

        Vector2 targetDisplacement = targetPosition - _lastTrackedCombatTargetPosition;
        if (targetDisplacement.sqrMagnitude
            > MovementProgressThreshold * MovementProgressThreshold)
        {
            _lastMoveCell = null;
            _lastTrackedCombatTargetPosition = targetPosition;
        }

        return true;
    }

    
    internal static void OnLocalCannonShot(uint targetNetId)
    {
        unchecked
        {
            _liveCannonShotSequence++;
        }
        _liveCannonShotTargetNetId = targetNetId;
        _lastLocalCannonShotTime = Time.time;
    }

    

    private static void ReactToNpcCannonShot(Player player)
    {
        if (_liveCannonShotSequence <= _npcCannonShotSequenceBefore)
            return;
        _npcCannonShotSequenceBefore = _liveCannonShotSequence;

        BehaviorAction action = Brain.CurrentAction;
        if (action == null
            || action != _executedAction
            || action.Type != BehaviorActionType.Combat)
            return;

        CombatActionContext context = Brain.GetCurrentContext<CombatActionContext>();
        CombatTarget target = context == null ? null : context.Target;
        if (target == null
            || target.WeaponCategory != TargetCategory.Npc
            || target.NetId != _liveCannonShotTargetNetId)
            return;

        bool firstShot = !_npcCannonShotConfirmed;
        _npcCannonShotConfirmed = true;
        _lastProgressAt = Time.time;

        if (firstShot && target.StayOutOfReach)
            StopShipHere(player, target);
    }

    

    private static void StopApproachWhileReloading(Player player)
    {
        if (_npcCannonShotConfirmed || _npcStoppedWhileReloading || player == null)
            return;

        BehaviorAction action = Brain.CurrentAction;
        if (action == null
            || action != _executedAction
            || action.Type != BehaviorActionType.Combat)
            return;

        CombatActionContext context = Brain.GetCurrentContext<CombatActionContext>();
        CombatTarget target = context == null ? null : context.Target;
        if (target == null
            || target.WeaponCategory != TargetCategory.Npc
            || !target.StayOutOfReach)
            return;

        if (IsCannonReloaded(player))
            return;

        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        GameObject npcObject = ResolveCombatTarget(target);
        if (snapshot == null
            || snapshot.Joueur == null
            || npcObject == null
            || !IsFinitePositive(snapshot.Joueur.Portee))
            return;

        Vector3 playerWorld = player.transform.position;
        Vector3 npcWorld = npcObject.transform.position;
        float distance = Vector2.Distance(
            new Vector2(playerWorld.x, playerWorld.y),
            new Vector2(npcWorld.x, npcWorld.y));
        if (distance > snapshot.Joueur.Portee)
            return;

        _npcStoppedWhileReloading = true;
        Plugin.Logger.LogInfo(
            "[CopperWire] NPC " + target.Name + " : à portée ("
            + distance.ToString("0.##", CultureInfo.InvariantCulture)
            + "), canon en rechargement -> arrêt, puis maintien hors de sa portée.");
        StopShipHere(player, target);
    }

    private static bool IsCannonReloaded(Player player)
    {
        float reload;
        try
        {
            reload = Convert.ToSingle((object)player.saldirihizi, CultureInfo.InvariantCulture);
        }
        catch
        {
            return true;
        }

        return !IsFinite(reload) || Time.time - _lastLocalCannonShotTime >= reload;
    }

    
    
    
    private static void StopShipHere(Player player, CombatTarget target)
    {
        if (player == null || !_hasActionMap)
            return;

        Vector3 position = player.transform.position;
        Vector2 playerPosition = new Vector2(position.x, position.y);
        _lastMoveCell = null;
        ClearNpcFollowTracking();

        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        GameObject npcObject = ResolveCombatTarget(target);
        int column;
        string row;
        float chosenDistance;
        if (snapshot != null
            && snapshot.Joueur != null
            && npcObject != null
            && TryChooseNpcBandCell(
                _actionMapId, target, snapshot.Joueur.Portee,
                new Vector2(npcObject.transform.position.x, npcObject.transform.position.y),
                playerPosition, Vector2.Distance(playerPosition,
                    new Vector2(npcObject.transform.position.x, npcObject.transform.position.y)),
                1f, out column, out row, out chosenDistance))
        {
            IssueMoveToCell(player, column, row);
            return;
        }

        IssueMove(player, _actionMapId, position.x, position.y);
    }

    

    private static bool TryChooseNpcBandCell(
        int mapId,
        CombatTarget target,
        float ownRange,
        Vector2 npcPosition,
        Vector2 reference,
        float idealDistance,
        float referenceWeight,
        out int column,
        out string row,
        out float chosenDistance)
    {
        column = 0;
        row = null;
        chosenDistance = 0f;

        if (target == null || !IsFinitePositive(ownRange) || !IsFinite(target.Portee))
            return false;

        float minimum = target.Portee + NpcBandEdgeMargin;
        float maximum = ownRange - NpcBandEdgeMargin;
        if (maximum < minimum)
            return false;
        if (float.IsNaN(idealDistance))
            idealDistance = (minimum + maximum) * 0.5f;
        idealDistance = Mathf.Clamp(idealDistance, minimum, maximum);

        float minX, maxX, minY, maxY;
        int lastColumn, lastLine;
        if (!PositionReelle.ObtenirGrille(mapId, out minX, out maxX, out minY, out maxY,
                out lastColumn, out lastLine))
            return false;

        float stepX = (maxX - minX) / lastColumn;
        float stepY = (maxY - minY) / lastLine;
        int referenceColumn = Mathf.Clamp(Mathf.RoundToInt((reference.x - minX) / stepX), 0, lastColumn);
        int referenceLine = Mathf.Clamp(Mathf.RoundToInt((reference.y - minY) / stepY), 0, lastLine);

        float bestScore = float.MaxValue;
        int bestColumn = -1;
        int bestLine = -1;
        for (int dc = -NpcCellSearchRadius; dc <= NpcCellSearchRadius; dc++)
        {
            int c = referenceColumn + dc;
            if (c < 0 || c > lastColumn)
                continue;
            for (int dl = -NpcCellSearchRadius; dl <= NpcCellSearchRadius; dl++)
            {
                int l = referenceLine + dl;
                if (l < 0 || l > lastLine)
                    continue;

                Vector2 node = new Vector2(minX + c * stepX, minY + l * stepY);
                float distance = Vector2.Distance(node, npcPosition);
                if (distance < minimum || distance > maximum)
                    continue;

                float score = Math.Abs(distance - idealDistance)
                    + referenceWeight * Vector2.Distance(node, reference);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestColumn = c;
                    bestLine = l;
                    chosenDistance = distance;
                }
            }
        }

        if (bestColumn < 0)
            return false;

        string letter = PositionReelle.LettreLigne(bestLine);
        if (string.IsNullOrEmpty(letter))
            return false;

        column = bestColumn;
        row = letter;
        return true;
    }

    private static void FollowEngagedNpc(Player player)
    {
        BehaviorAction action = Brain.CurrentAction;
        if (player == null
            || action == null
            || action != _executedAction
            || action.Type != BehaviorActionType.Combat
            || action.InstanceId != _combatActionInstanceId
            || RespawnWire.IsActive
            || Brain.IsRespawning)
        {
            ClearNpcFollowTracking();
            return;
        }

        CombatActionContext context = action.Context as CombatActionContext;
        CombatTarget target = context == null ? null : context.Target;
        if (target == null
            || target.Kind != CombatTargetKind.NetworkId
            || target.WeaponCategory != TargetCategory.Npc
            || target.NetId == 0
            || !string.Equals(target.Key, _combatTargetKey, StringComparison.Ordinal))
        {
            ClearNpcFollowTracking();
            return;
        }

        try
        {
            if (!NetworkClient.active
                || !NetworkClient.ready
                || NetworkClient.spawned == null
                || !NetworkClient.spawned.TryGetValue(target.NetId, out NetworkIdentity identity)
                || identity == null
                || identity.netId != target.NetId)
                return;

            Vector3 targetWorldPosition = identity.transform.position;
            Vector2 targetPosition = new Vector2(targetWorldPosition.x, targetWorldPosition.y);
            if (!IsFinite(targetPosition.x) || !IsFinite(targetPosition.y))
                return;

            Vector3 playerWorldPosition = player.transform.position;
            Vector2 playerPosition = new Vector2(playerWorldPosition.x, playerWorldPosition.y);

            
            if (!target.StayOutOfReach)
            {
                ClearNpcFollowTracking();
                FollowNpcContinuously(player, target.Key, playerPosition, targetPosition);
                return;
            }

            
            HoldNpcBand(player, target, playerPosition, targetPosition);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(
                "[CopperWire] Erreur de suivi du NPC NetId=" + target.NetId + " : " + e);
        }
    }

    

    private static void FollowNpcContinuously(
        Player player,
        string targetKey,
        Vector2 playerPosition,
        Vector2 targetPosition)
    {
        float now = Time.time;
        if (!string.Equals(_npcContinuousFollowKey, targetKey, StringComparison.Ordinal))
        {
            _npcContinuousFollowKey = targetKey;
            _npcFollowSamplePosition = targetPosition;
            _npcFollowSampleTime = now;
            _npcFollowVelocity = Vector2.zero;
            _hasNpcFollowVelocity = false;
            _npcFollowNextOrderAt = 0f;
            return;
        }

        float elapsed = now - _npcFollowSampleTime;
        if (elapsed >= NpcFollowSampleInterval)
        {
            Vector2 measured = (targetPosition - _npcFollowSamplePosition) / elapsed;
            _npcFollowVelocity = _hasNpcFollowVelocity
                ? Vector2.Lerp(_npcFollowVelocity, measured, 0.5f)
                : measured;
            _hasNpcFollowVelocity = true;
            _npcFollowSamplePosition = targetPosition;
            _npcFollowSampleTime = now;
        }

        if (!_hasNpcFollowVelocity || now < _npcFollowNextOrderAt)
            return;

        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        if (snapshot == null
            || snapshot.Joueur == null
            || !IsFinitePositive(snapshot.Joueur.Portee)
            || _hasActionMap && snapshot.Joueur.Harita != _actionMapId)
            return;

        float ownRange = snapshot.Joueur.Portee;
        float distance = Vector2.Distance(playerPosition, targetPosition);
        if (distance > ownRange)
            return;

        if (_npcFollowVelocity.magnitude < NpcFollowMinSpeed)
            return;

        
        if (Vector2.Dot(_npcFollowVelocity, playerPosition - targetPosition) > 0f)
            return;

        Vector2 predicted = targetPosition + _npcFollowVelocity * NpcFollowLeadSeconds;
        Vector2 direction = playerPosition - predicted;
        if (direction.sqrMagnitude < 0.0001f)
            direction = -_npcFollowVelocity;
        direction.Normalize();

        Vector2 destination = predicted + direction * (ownRange * NpcFollowDistanceRatio);

        
        
        if (Vector2.Dot(destination - playerPosition, _npcFollowVelocity) <= 0f)
            return;

        if (IssueMove(player, snapshot.Joueur.Harita, destination.x, destination.y))
            _npcFollowNextOrderAt = now + NpcFollowOrderInterval;
    }

    

    private static void HoldNpcBand(
        Player player,
        CombatTarget target,
        Vector2 playerPosition,
        Vector2 targetPosition)
    {
        if (!_npcCannonShotConfirmed && !_npcStoppedWhileReloading)
            return;
        if (Time.time < _npcBandNextOrderAt)
            return;

        EtatJeuSnapshot snapshot = GameState.ObtenirSnapshot();
        if (snapshot == null
            || snapshot.Joueur == null
            || !IsFinitePositive(snapshot.Joueur.Portee)
            || !IsFinite(target.Portee)
            || _hasActionMap && snapshot.Joueur.Harita != _actionMapId)
            return;

        float ownRange = snapshot.Joueur.Portee;
        float distance = Vector2.Distance(playerPosition, targetPosition);
        bool tooClose = distance < target.Portee;
        bool tooFar = distance > ownRange;
        if (!tooClose && !tooFar)
            return;

        int column;
        string row;
        float chosenDistance;
        bool moved;
        string detail;
        if (TryChooseNpcBandCell(
                snapshot.Joueur.Harita, target, ownRange, targetPosition,
                playerPosition, float.NaN, 0.25f,
                out column, out row, out chosenDistance))
        {
            moved = IssueMoveToCell(player, column, row);
            detail = "case à " + chosenDistance.ToString("0.##", CultureInfo.InvariantCulture);
        }
        else
        {
            Vector2 direction = playerPosition - targetPosition;
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector2.right;
            else
                direction.Normalize();
            Vector2 destination = targetPosition + direction * target.AimDistance;
            moved = IssueMove(player, snapshot.Joueur.Harita, destination.x, destination.y);
            detail = "point à " + target.AimDistance.ToString("0.##", CultureInfo.InvariantCulture);
        }

        if (!moved)
            return;

        _npcBandNextOrderAt = Time.time + NpcBandOrderInterval;
    }

    private static void ClearNpcFollowTracking()
    {
        _npcBandNextOrderAt = 0f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool TryGetMapCell(int mapId, float x, float y, out string cell)
    {
        int column;
        string row;
        return TryGetMapCell(mapId, x, y, out column, out row, out cell);
    }

    private static bool TryGetMapCell(
        int mapId,
        float x,
        float y,
        out int column,
        out string row,
        out string cell)
    {
        column = 0;
        row = null;
        cell = null;

        string label = PositionReelle.Convertir(mapId, x, y);
        if (string.IsNullOrWhiteSpace(label))
            return false;

        int separator = label.IndexOf(' ');
        if (separator <= 0 || separator == label.Length - 1)
            return false;
        if (!int.TryParse(
                label.Substring(0, separator),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out column))
            return false;

        row = label.Substring(separator + 1).Trim();
        if (row.Length == 0)
            return false;
        cell = column.ToString(CultureInfo.InvariantCulture) + "|" + row;
        return true;
    }

    private static Player ObtenirPlayerLocal()
    {
        try
        {
            if (_localPlayer != null && _localPlayer.isLocalPlayer)
                return _localPlayer;

            NetworkIdentity localIdentity = NetworkClient.localPlayer;
            if (localIdentity == null)
            {
                _localPlayer = null;
                return null;
            }

            Player local = localIdentity.GetComponent<Player>();
            _localPlayer = local != null && local.isLocalPlayer ? local : null;
            return _localPlayer;
        }
        catch (Exception e)
        {
            _localPlayer = null;
            Plugin.Logger.LogError("[CopperWire] Impossible de récupérer NetworkClient.localPlayer : " + e);
            return null;
        }
    }

    private static bool SnapshotContainsCollectible(EtatJeuSnapshot snapshot, uint netId)
    {
        if (snapshot == null || snapshot.Collectibles == null)
            return false;

        for (int i = 0; i < snapshot.Collectibles.Count; i++)
        {
            CollectibleInfo collectible = snapshot.Collectibles[i];
            if (collectible != null && collectible.Id == netId)
                return true;
        }
        return false;
    }

    private static NetworkIdentity ResolveNetworkIdentity(uint netId)
    {
        if (netId == 0)
            return null;
        if (_cachedIdentityNetId == netId && _cachedIdentity != null
            && _cachedIdentity.netId == netId)
            return _cachedIdentity;

        try
        {
            NetworkIdentity identity;
            if (!NetworkClient.spawned.TryGetValue(netId, out identity) || identity == null)
            {
                _cachedIdentity = null;
                _cachedIdentityNetId = 0;
                return null;
            }

            _cachedIdentity = identity;
            _cachedIdentityNetId = netId;
            return identity;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[CopperWire] Erreur résolution NetworkIdentity " + netId + " : " + e);
            return null;
        }
    }

    private static GameObject ResolveCombatTarget(CombatTarget target)
    {
        if (target.Kind == CombatTargetKind.NetworkId)
        {
            NetworkIdentity identity = ResolveNetworkIdentity(target.NetId);
            return identity == null ? null : identity.gameObject;
        }

        Player player = ResolvePlayerGlobalId(target.PlayerGlobalId);
        return player == null ? null : player.gameObject;
    }

    private static Player ResolvePlayerGlobalId(int globalId)
    {
        if (globalId == 0)
            return null;
        if (_cachedPlayerGlobalId == globalId && _cachedTargetPlayer != null
            && !_cachedTargetPlayer.isLocalPlayer
            && _cachedTargetPlayer.oyuncuId == globalId)
            return _cachedTargetPlayer;

        try
        {
            if (!NetworkClient.active || !NetworkClient.ready || NetworkClient.spawned == null)
                return null;

            foreach (var pair in NetworkClient.spawned)
            {
                NetworkIdentity identity = pair.Value;
                if (identity == null || identity.netId == 0)
                    continue;

                Player candidate = identity.GetComponent<Player>();
                if (candidate == null || candidate.isLocalPlayer || candidate.oyuncuId != globalId)
                    continue;
                _cachedPlayerGlobalId = globalId;
                _cachedTargetPlayer = candidate;
                return candidate;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[CopperWire] Erreur résolution Player.oyuncuId " + globalId + " : " + e);
        }

        _cachedPlayerGlobalId = 0;
        _cachedTargetPlayer = null;
        return null;
    }

    
    
    
    private static bool IsNearNavigationDestination(Player player)
    {
        if (player.aiLerp == null || player.aiLerp.pathPending)
            return false;
        if (Time.time - _actionStartedAt < NavigationArrivalGraceSeconds)
            return false;
        if (player.aiLerp.reachedDestination || player.aiLerp.reachedEndOfPath)
            return true;

        Vector3 destination = player.aiLerp.destination;
        Vector3 position = player.transform.position;
        float remaining = Vector2.Distance(
            new Vector2(position.x, position.y),
            new Vector2(destination.x, destination.y));
        if (!IsFinite(remaining))
            return false;

        float speed = player.aiLerp.speed;
        float lookahead = NavigationLookaheadMinimum;
        if (IsFinitePositive(speed))
            lookahead = Math.Max(lookahead, speed * NavigationLookaheadSeconds);
        return remaining <= lookahead;
    }

    

    private static void CheckNavigationArrival(Player player)
    {
        BehaviorAction action = Brain.CurrentAction;
        if (action == null
            || action != _executedAction
            || action.Type != BehaviorActionType.Navigation
            || string.IsNullOrEmpty(_lastMoveCell))
            return;

        if (IsNearNavigationDestination(player))
            _nextDecisionAt = 0f;
    }

    private static bool HasNoUsablePath(Player player)
    {
        return player.aiLerp != null
            && !player.aiLerp.pathPending
            && !player.aiLerp.hasPath;
    }

    private static void TrackPlayerProgress(Player player)
    {
        TrackPositionOnly(player);
    }

    private static void TrackPositionOnly(Player player)
    {
        Vector3 position = player.transform.position;
        if (!_hasProgressPosition
            || Vector2.Distance(
                new Vector2(position.x, position.y),
                new Vector2(_lastProgressPosition.x, _lastProgressPosition.y))
                >= MovementProgressThreshold)
        {
            _lastProgressAt = Time.time;
            _lastProgressPosition = position;
            _hasProgressPosition = true;
        }
    }

    private static void ClearTargetCaches()
    {
        _cachedIdentityNetId = 0;
        _cachedIdentity = null;
        _cachedPlayerGlobalId = 0;
        _cachedTargetPlayer = null;
    }

    private static bool IsFinitePositive(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private enum ExecutionResult
    {
        Running,
        Completed,
        Failed
    }

    private enum CombatTargetEvaluation
    {
        Running,
        Completed,
        Invalidated
    }
}

[HarmonyPatch(typeof(Player), "UserCode_RpcGulleYarat__GameObject__Int32__Int32__Boolean__Boolean__Boolean")]
public static class Patch_Player_RpcGulleYarat_CopperWire
{
    public static void Postfix(Player __instance, GameObject Hedef)
    {
        try
        {
            if (__instance == null || !__instance.isLocalPlayer)
                return;

            uint targetNetId = 0;
            if (Hedef != null)
            {
                NetworkIdentity identity = Hedef.GetComponent<NetworkIdentity>();
                if (identity != null)
                    targetNetId = identity.netId;
            }

            CopperWire.OnLocalCannonShot(targetNetId);
            GameState.EnregistrerTirCanonConfirme(__instance, targetNetId);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("[CopperWire] Erreur observation tir canon : " + e);
        }
    }
}

[HarmonyPatch(typeof(Player), "Update")]
public static class Patch_Player_Update_CopperWire
{
    public static void Postfix(Player __instance)
    {
        CopperWire.OnPlayerUpdate(__instance);
    }
}
