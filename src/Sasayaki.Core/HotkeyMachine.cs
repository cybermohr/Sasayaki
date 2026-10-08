namespace Sasayaki.Core;

public enum GestureState { Idle, Provisional, Hold, TapPending, Latched, StopPressed, StopPending, Busy }
public enum GestureAction { Begin, CommitGesture, PauseCapture, ResumeCapture, Finish, Cancel, Latched }
public readonly record struct GestureEvent(GestureAction Action, long Timestamp);

/// <summary>Pure, monotonic-clock gesture recognition. Called serially by the host.</summary>
public sealed class HotkeyMachine
{
    public const int HoldMilliseconds = 250;
    public const int DoublePressMilliseconds = 350;
    public GestureState State { get; private set; }
    public bool IsRecording => State is not (GestureState.Idle or GestureState.Busy);
    private long pressedAt, releasedAt;
    public event Action<GestureEvent>? Action;
    private void Emit(GestureAction action, long timestamp) => Action?.Invoke(new(action, timestamp));

    public void Down(long now)
    {
        Tick(now);
        switch (State)
        {
            case GestureState.Idle:
                pressedAt = now; State = GestureState.Provisional; Emit(GestureAction.Begin, now); break;
            case GestureState.TapPending:
                State = GestureState.Latched;
                Emit(GestureAction.CommitGesture, now); Emit(GestureAction.ResumeCapture, now); Emit(GestureAction.Latched, now); break;
            case GestureState.Latched:
                State = GestureState.StopPressed; break;
            case GestureState.StopPending:
                State = GestureState.Busy; Emit(GestureAction.Finish, now); break;
        }
    }

    public void Up(long now)
    {
        Tick(now);
        switch (State)
        {
            case GestureState.Provisional:
                releasedAt = now; State = GestureState.TapPending; Emit(GestureAction.PauseCapture, now); break;
            case GestureState.Hold:
                State = GestureState.Busy; Emit(GestureAction.Finish, now); break;
            case GestureState.StopPressed:
                releasedAt = now; State = GestureState.StopPending; break;
        }
    }

    public void Tick(long now)
    {
        if (State == GestureState.Provisional && now - pressedAt >= HoldMilliseconds)
        {
            State = GestureState.Hold; Emit(GestureAction.CommitGesture, now);
        }
        else if (State == GestureState.TapPending && now - releasedAt > DoublePressMilliseconds)
        {
            State = GestureState.Busy; Emit(GestureAction.CommitGesture, now);
            // Latency begins at physical release, not at the end of the ambiguity window.
            Emit(GestureAction.Finish, releasedAt);
        }
        else if (State == GestureState.StopPending && now - releasedAt > DoublePressMilliseconds)
            State = GestureState.Latched;
    }

    public void AbortCandidate(long now)
    {
        if (State is GestureState.Provisional or GestureState.TapPending) Cancel(now);
    }

    public void Cancel(long now)
    {
        var active = State != GestureState.Idle;
        State = GestureState.Idle;
        if (active) Emit(GestureAction.Cancel, now);
    }
    public void Complete() => State = GestureState.Idle;
}
