using System;
using EtatJoueurMod;

public enum BehaviorActionType
{
    None,
    Navigation,
    Collect,
    Combat
}

public enum BehaviorSystemState
{
    Normal,
    Respawn
}

public enum BehaviorActionState
{
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum BehaviorActionResult
{
    None,
    Success,
    Failure,
    Cancelled
}

public sealed class BehaviorAction
{
    public BehaviorActionType Type { get; private set; }
    public int Priority { get; private set; }
    public bool CanBePreempted { get; private set; }
    public Guid InstanceId { get; private set; }
    public BehaviorActionState State { get; private set; }
    public BehaviorActionResult Result { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public object Context { get; private set; }

    public bool IsFinished
    {
        get
        {
            return State == BehaviorActionState.Completed
                || State == BehaviorActionState.Failed
                || State == BehaviorActionState.Cancelled;
        }
    }

    public BehaviorAction(
        BehaviorActionType type,
        int priority,
        bool canBePreempted,
        object context = null)
    {
        if (type == BehaviorActionType.None)
            throw new ArgumentOutOfRangeException(nameof(type));

        Type = type;
        Priority = priority;
        CanBePreempted = canBePreempted;
        Context = context;
        InstanceId = Guid.NewGuid();
        State = BehaviorActionState.Running;
        Result = BehaviorActionResult.None;
        StartedAtUtc = DateTime.UtcNow;
    }

    public void Complete()
    {
        Finish(BehaviorActionState.Completed, BehaviorActionResult.Success);
    }

    public void Fail()
    {
        Finish(BehaviorActionState.Failed, BehaviorActionResult.Failure);
    }

    public void Cancel()
    {
        Finish(BehaviorActionState.Cancelled, BehaviorActionResult.Cancelled);
    }

    private void Finish(BehaviorActionState state, BehaviorActionResult result)
    {
        if (IsFinished)
            return;

        State = state;
        Result = result;
        FinishedAtUtc = DateTime.UtcNow;
    }
}

public sealed class BehaviorBrain
{
    private BehaviorAction _currentAction;
    private BehaviorSystemState _systemState = BehaviorSystemState.Normal;

    public BehaviorSystemState SystemState
    {
        get { return _systemState; }
    }

    public bool IsRespawning
    {
        get { return _systemState == BehaviorSystemState.Respawn; }
    }

    public BehaviorAction CurrentAction { get { return _currentAction; } }

    public BehaviorActionType CurrentActionType
    {
        get
        {
            return HasCurrentAction
                ? _currentAction.Type
                : BehaviorActionType.None;
        }
    }

    public bool HasCurrentAction
    {
        get { return _currentAction != null && !_currentAction.IsFinished; }
    }

    public void EnterRespawn()
    {
        if (_systemState == BehaviorSystemState.Respawn)
            return;

        if (HasCurrentAction)
            _currentAction.Cancel();

        _currentAction = null;
        _systemState = BehaviorSystemState.Respawn;
    }

    public bool ExitRespawn()
    {
        if (_systemState != BehaviorSystemState.Respawn)
            return false;

        _systemState = BehaviorSystemState.Normal;
        return true;
    }

    public bool StartAction(
        BehaviorActionType type,
        int priority,
        bool canBePreempted,
        object context = null)
    {
        if (type == BehaviorActionType.None
            || HasCurrentAction
            || IsRespawning)
            return false;

        _currentAction = new BehaviorAction(type, priority, canBePreempted, context);
        return true;
    }

    public bool TryPreempt(
        BehaviorActionType type,
        int priority,
        bool canBePreempted,
        object context = null)
    {
        if (IsRespawning)
            return false;

        if (type == BehaviorActionType.None)
            return false;
        if (!HasCurrentAction)
            return StartAction(type, priority, canBePreempted, context);
        if (_currentAction.Type != BehaviorActionType.Navigation
            || _currentAction.Type == type
            || !_currentAction.CanBePreempted
            || priority <= _currentAction.Priority)
            return false;

        _currentAction.Cancel();
        _currentAction = null;
        return StartAction(type, priority, canBePreempted, context);
    }

    public BehaviorActionResult CompleteCurrent()
    {
        return FinishCurrent(BehaviorActionResult.Success);
    }

    public BehaviorActionResult FailCurrent()
    {
        return FinishCurrent(BehaviorActionResult.Failure);
    }

    public BehaviorActionResult CancelCurrent()
    {
        return FinishCurrent(BehaviorActionResult.Cancelled);
    }

    public T GetCurrentContext<T>() where T : class
    {
        return _currentAction == null ? null : _currentAction.Context as T;
    }

    public void Reset()
    {
        if (HasCurrentAction)
            _currentAction.Cancel();

        _currentAction = null;
        _systemState = BehaviorSystemState.Normal;
    }

    private BehaviorActionResult FinishCurrent(BehaviorActionResult result)
    {
        if (_currentAction == null)
            return BehaviorActionResult.None;

        if (!_currentAction.IsFinished)
        {
            if (result == BehaviorActionResult.Success)
                _currentAction.Complete();
            else if (result == BehaviorActionResult.Failure)
                _currentAction.Fail();
            else
                _currentAction.Cancel();
        }

        _currentAction = null;
        return result;
    }
}
