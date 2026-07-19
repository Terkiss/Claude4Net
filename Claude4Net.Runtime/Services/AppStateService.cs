using System;
using System.Collections.Generic;
using Claude4Net.SDK;

namespace Claude4Net.Runtime.Services
{
    public class AppStateService : IAppState
    {
        public string SessionId 
        { 
            get => AppState.SessionId; 
            set => AppState.SessionId = value; 
        }
        
        public string? CurrentCwd 
        { 
            get => AppState.CurrentCwd; 
            set => AppState.CurrentCwd = value; 
        }

        public PermissionMode CurrentPermissionMode 
        { 
            get => AppState.CurrentPermissionMode; 
            set => AppState.CurrentPermissionMode = value; 
        }

        public string ActiveProvider 
        { 
            get => AppState.ActiveProvider; 
            set => AppState.ActiveProvider = value; 
        }

        public string ActiveModel 
        { 
            get => AppState.ActiveModel; 
            set => AppState.ActiveModel = value; 
        }

        public void LoadDiscordApprovers() => AppState.LoadDiscordApprovers();
    }
}
