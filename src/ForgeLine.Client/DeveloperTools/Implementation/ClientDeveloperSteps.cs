using ForgeLine.Game;

namespace ForgeLine.Client;

internal sealed partial class ClientSimulationHost
{
    private HostMessage? _activeStep;

    private void StartDeveloperStep(in HostMessage message)
    {
        var failure = _activeStep.HasValue ? ClientDeveloperControlFailure.StepInProgress :
            (_pauseReasons & ClientSimulationPauseReason.Developer) == 0 ? ClientDeveloperControlFailure.DeveloperPauseRequired :
            _pauseReasons != ClientSimulationPauseReason.Developer ? ClientDeveloperControlFailure.BlockedByPauseOwner : ClientDeveloperControlFailure.None;
        if (failure != ClientDeveloperControlFailure.None)
        {
            CompleteDeveloper(message, failure, requested: message.DeveloperRequest!.Values[0].AsUInt32());
            return;
        }
        _activeStep = message;
        _requestedSteps = message.DeveloperRequest!.Values[0].AsUInt32();
        _completedSteps = 0;
        _remainingSteps = _requestedSteps;
        _controlRevision = checked(_controlRevision + 1);
        try
        {
            SynchronizeLifecyclePause();
            _measurementActive = false;
            PublishControlStatus();
        }
        catch { FinishDeveloperStep(ClientDeveloperControlFailure.Faulted); throw; }
    }

    partial void AdjustDeveloperPause(ref bool paused)
    {
        if (_activeStep.HasValue && _pauseReasons == ClientSimulationPauseReason.Developer) paused = false;
    }

    partial void PauseOwnersChanged()
    {
        if (_activeStep.HasValue && _pauseReasons != ClientSimulationPauseReason.Developer)
            FinishDeveloperStep(ClientDeveloperControlFailure.BlockedByPauseOwner);
    }

    partial void ProcessDeveloperStep(ref bool stepped)
    {
        if (_activeStep is not { } message) return;
        stepped = true;
        var failure = IsStopping ? ClientDeveloperControlFailure.Stopping :
            IsTerminalFrozen ? ClientDeveloperControlFailure.Terminal :
            !ReferenceEquals(message.DeveloperPermission, _boundAccess) || !IsAuthorized(_boundAccess) ? ClientDeveloperControlFailure.NotActivated :
            _pauseReasons != ClientSimulationPauseReason.Developer ? ClientDeveloperControlFailure.BlockedByPauseOwner : ClientDeveloperControlFailure.None;
        if (failure != ClientDeveloperControlFailure.None) { FinishDeveloperStep(failure); return; }
        try
        {
            AdvanceOneTick();
            _completedSteps++;
            _remainingSteps--;
            UpdateTerminalState();
            if (IsTerminalFrozen) FinishDeveloperStep(ClientDeveloperControlFailure.Terminal);
            else if (IsStopping) FinishDeveloperStep(ClientDeveloperControlFailure.Stopping);
            else if (_remainingSteps == 0) FinishDeveloperStep(ClientDeveloperControlFailure.None);
            PublishControlStatus();
            NotifyProgress();
        }
        catch { FinishDeveloperStep(ClientDeveloperControlFailure.Faulted); throw; }
    }

    private void FinishDeveloperStep(ClientDeveloperControlFailure failure)
    {
        if (_activeStep is not { } message) return;
        _activeStep = null;
        _remainingSteps = 0;
        _controlRevision = checked(_controlRevision + 1);
        // A failed tick has no completed snapshot boundary to republish through a control command.
        try { if (failure != ClientDeveloperControlFailure.Faulted) SynchronizeLifecyclePause(); }
        catch { failure = ClientDeveloperControlFailure.Faulted; throw; }
        finally
        {
            PublishControlStatus();
            CompleteDeveloper(message, failure, requested: _requestedSteps, completed: _completedSteps);
        }
    }

    partial void StopDeveloperControls(bool faulted)
    {
        try { FinishDeveloperStep(faulted ? ClientDeveloperControlFailure.Faulted : ClientDeveloperControlFailure.Stopping); }
        finally
        {
            lock (_boundaryGate) { _boundAccess = null; _pendingAccess = null; }
            _developerActivated = false;
            _measurementActive = false;
        }
    }
}
