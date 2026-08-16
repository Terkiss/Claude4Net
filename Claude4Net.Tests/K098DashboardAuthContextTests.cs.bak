using System.Security.Claims;
using Claude4Net.Dashboard.Auth;
using Claude4Net.Dashboard.Hubs;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Claude4Net.Tests;

public class K098DashboardAuthContextTests
{
    [Fact]
    public void GetCurrent_UsesSubjectHashForWorkspace_NotEmail()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "claude4net-dashboard-auth-tests", Guid.NewGuid().ToString("N"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "google-sub-123"),
            new Claim(ClaimTypes.Email, "owner@example.com"),
            new Claim(ClaimTypes.Role, DashboardRoles.Operator)
        }, "Test"));

        var http = new DefaultHttpContext { User = principal };
        var accessor = new HttpContextAccessor { HttpContext = http };
        var options = Options.Create(new DashboardAuthOptions { DataRoot = tempRoot });

        var context = new DashboardUserContextAccessor(accessor, options).GetCurrent();

        Assert.Equal("google-sub-123", context.Subject);
        Assert.Equal("owner@example.com", context.Email);
        Assert.Equal(DashboardRoles.Operator, context.Role);
        Assert.DoesNotContain("owner@example.com", context.WorkspaceRoot);
        Assert.StartsWith(Path.Combine(tempRoot, "users"), context.WorkspaceRoot);
        Assert.True(Directory.Exists(context.WorkspaceRoot));
    }

    [Fact]
    public async Task RunRoutine_WithViewerRole_DeniesAndWritesAuditEvent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "claude4net-dashboard-auth-tests", Guid.NewGuid().ToString("N"));
        var context = new DashboardUserContext(
            "sub-viewer",
            "viewer@example.com",
            "actor-viewer",
            DashboardRoles.Viewer,
            Path.Combine(tempRoot, "users", "actor-viewer", "workspace"),
            "dashboard-actor-viewer");
        Directory.CreateDirectory(context.WorkspaceRoot);

        var hub = new ControlPlaneHub(userContextAccessor: new FixedDashboardUserContextAccessor(context));
        var result = await hub.RunRoutine("routine-1");

        Assert.False(result.Success);
        Assert.Contains("Permission denied", result.Error);
        Assert.Equal(context.SessionId, result.AuditSessionId);
        Assert.False(string.IsNullOrWhiteSpace(result.AuditEventId));
        Assert.Equal($"/replay?SessionId={Uri.EscapeDataString(context.SessionId)}", result.AuditUrl);

        var events = await new FileAgentEventStore(context.WorkspaceRoot).GetEventsAsync(context.SessionId, -1);
        var audit = Assert.IsType<DashboardCommandEvent>(Assert.Single(events));
        Assert.Equal("RunRoutine", audit.Action);
        Assert.Equal("routine-1", audit.TargetId);
        Assert.Equal("actor-viewer", audit.ActorHash);
        Assert.Equal(DashboardRoles.Viewer, audit.Role);
        Assert.Equal("DeniedOrFailed", audit.Outcome);
        Assert.False(audit.Success);
    }

    [Fact]
    public async Task RunRoutine_WithOperatorRole_UsesDashboardWorkspace()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "claude4net-dashboard-auth-tests", Guid.NewGuid().ToString("N"));
        var dashboardWorkspace = Path.Combine(tempRoot, "users", "actor-operator", "workspace");
        Directory.CreateDirectory(dashboardWorkspace);

        var context = new DashboardUserContext(
            "sub-operator",
            "operator@example.com",
            "actor-operator",
            DashboardRoles.Operator,
            dashboardWorkspace,
            "dashboard-actor-operator");

        var routine = new RoutineDefinition
        {
            Id = "routine-operator",
            Name = "Operator Routine",
            Enabled = true,
            RequiredPermissionMode = PermissionMode.ReadOnly,
            Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Manual },
            Actions = new List<RoutineAction>
            {
                new RoutineAction
                {
                    Kind = RoutineActionKind.SlashCommand,
                    Payload = "/help"
                }
            }
        };
        await new RoutineStore(dashboardWorkspace).SaveAsync(routine);

        var originalCwd = AppState.CurrentCwd;
        AppState.CurrentCwd = Path.Combine(tempRoot, "global-appstate-workspace");
        var hub = new ControlPlaneHub(userContextAccessor: new FixedDashboardUserContextAccessor(context));

        try
        {
            var result = await hub.RunRoutine("routine-operator");

            Assert.True(result.Success);
            Assert.Equal(context.SessionId, result.AuditSessionId);
            Assert.False(string.IsNullOrWhiteSpace(result.AuditEventId));
            Assert.Equal($"/replay?SessionId={Uri.EscapeDataString(context.SessionId)}", result.AuditUrl);
            var events = await new FileAgentEventStore(dashboardWorkspace).GetEventsAsync(context.SessionId, -1);
            Assert.Contains(events.OfType<DashboardCommandEvent>(), e =>
                e.Action == "RunRoutine" &&
                e.ActorHash == "actor-operator" &&
                e.Role == DashboardRoles.Operator &&
                e.Success);
        }
        finally
        {
            AppState.CurrentCwd = originalCwd;
        }
    }

    [Fact]
    public async Task SetDashboardUserRole_WithViewerRole_DeniesAndWritesAuditEvent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "claude4net-dashboard-auth-tests", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(tempRoot, "users", "actor-viewer", "workspace");
        Directory.CreateDirectory(workspace);
        var context = new DashboardUserContext(
            "sub-viewer",
            "viewer@example.com",
            "actor-viewer",
            DashboardRoles.Viewer,
            workspace,
            "dashboard-actor-viewer");
        var store = new FileDashboardAdminStore(Options.Create(new DashboardAuthOptions { DataRoot = tempRoot }));
        var hub = new ControlPlaneHub(
            userContextAccessor: new FixedDashboardUserContextAccessor(context),
            adminStore: store,
            authOptions: Options.Create(new DashboardAuthOptions { DataRoot = tempRoot }));

        var result = await hub.SetDashboardUserRole("new-admin@example.com", DashboardRoles.Admin);

        Assert.False(result.Success);
        Assert.Contains("Permission denied", result.Error);
        Assert.Equal(context.SessionId, result.AuditSessionId);
        Assert.False(string.IsNullOrWhiteSpace(result.AuditEventId));
        Assert.Null(await store.GetManagedUserAsync("new-admin@example.com"));
    }

    [Fact]
    public async Task SetDashboardUserRole_WithAdminRole_PersistsManagedUserAndWritesAuditEvent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "claude4net-dashboard-auth-tests", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(tempRoot, "users", "actor-admin", "workspace");
        Directory.CreateDirectory(workspace);
        var context = new DashboardUserContext(
            "sub-admin",
            "admin@example.com",
            "actor-admin",
            DashboardRoles.Admin,
            workspace,
            "dashboard-actor-admin");
        var options = Options.Create(new DashboardAuthOptions
        {
            DataRoot = tempRoot,
            AllowedOrigins = new[] { "https://dashboard.example.com" },
            AllowedUsers = new List<DashboardAllowedUser>
            {
                new DashboardAllowedUser { Email = "config-admin@example.com", Role = DashboardRoles.Admin }
            }
        });
        var store = new FileDashboardAdminStore(options);
        var hub = new ControlPlaneHub(
            userContextAccessor: new FixedDashboardUserContextAccessor(context),
            adminStore: store,
            authOptions: options);

        var result = await hub.SetDashboardUserRole("operator@example.com", DashboardRoles.Operator);
        var managed = await store.GetManagedUserAsync("operator@example.com");
        var settings = await hub.GetAdminSettings();

        Assert.True(result.Success);
        Assert.Equal(context.SessionId, result.AuditSessionId);
        Assert.False(string.IsNullOrWhiteSpace(result.AuditEventId));
        Assert.NotNull(managed);
        Assert.Equal(DashboardRoles.Operator, managed.Role);
        Assert.Equal("actor-admin", managed.UpdatedByActorHash);
        Assert.Contains(settings.ManagedUsers, u => u.Email == "operator@example.com" && u.Role == DashboardRoles.Operator);
        Assert.Contains(settings.ConfiguredUsers, u => u.Email == "config-admin@example.com" && u.Source == "Config");
        Assert.Equal(Path.GetFullPath(tempRoot), settings.DataRoot);

        var events = await new FileAgentEventStore(workspace).GetEventsAsync(context.SessionId, -1);
        Assert.Contains(events.OfType<DashboardCommandEvent>(), e =>
            e.Action == "SetDashboardUserRole" &&
            e.ActorHash == "actor-admin" &&
            e.Role == DashboardRoles.Admin &&
            e.Success);
    }

    private sealed class FixedDashboardUserContextAccessor : IDashboardUserContextAccessor
    {
        private readonly DashboardUserContext _context;

        public FixedDashboardUserContextAccessor(DashboardUserContext context)
        {
            _context = context;
        }

        public DashboardUserContext GetCurrent() => _context;
    }
}
