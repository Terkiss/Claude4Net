using System;
using Claude4Net.SDK;

namespace Claude4Net.Runtime;

public sealed class AppStateSnapshot
{
    public string? CurrentCwd { get; }
    public string SessionId { get; }
    public string ActiveProvider { get; }
    public string ActiveModel { get; }
    public PermissionMode CurrentPermissionMode { get; }

    private AppStateSnapshot(
        string? currentCwd,
        string sessionId,
        string activeProvider,
        string activeModel,
        PermissionMode currentPermissionMode)
    {
        CurrentCwd = currentCwd;
        SessionId = sessionId;
        ActiveProvider = activeProvider;
        ActiveModel = activeModel;
        CurrentPermissionMode = currentPermissionMode;
    }

    public static AppStateSnapshot Capture()
    {
        return new AppStateSnapshot(
            AppState.CurrentCwd,
            AppState.SessionId,
            AppState.ActiveProvider,
            AppState.ActiveModel,
            AppState.CurrentPermissionMode
        );
    }

    public void Restore()
    {
        AppState.CurrentCwd = CurrentCwd;
        AppState.SessionId = SessionId;
        AppState.ActiveProvider = ActiveProvider;
        AppState.ActiveModel = ActiveModel;
        AppState.CurrentPermissionMode = CurrentPermissionMode;
    }
}
