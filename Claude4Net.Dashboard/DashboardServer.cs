using Claude4Net.Dashboard.Components;
using Claude4Net.Dashboard.Hubs;
using Claude4Net.Dashboard.Services;
using Claude4Net.SDK;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Claude4Net.Dashboard;

public class DashboardServer
{
    private static IHost? _host;
    public static IServiceProvider? Services => _host?.Services;
    public const int DefaultPort = 5000;

    public static int ResolvePort(Microsoft.Extensions.Configuration.IConfiguration? configuration, string? environmentPort = null)
    {
        string? raw = environmentPort;
        if (string.IsNullOrWhiteSpace(raw) && configuration != null)
        {
            raw = configuration["Dashboard:Port"];
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultPort;
        }

        if (int.TryParse(raw, out int port) && port >= 1 && port <= 65535)
        {
            return port;
        }

        throw new InvalidOperationException($"Invalid Dashboard Port: '{raw}'. Port must be an integer between 1 to 65535.");
    }

    public static async Task StartAsync(string[] args, int port = DefaultPort)
    {
        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls($"http://localhost:{port}");
            builder.WebHost.UseStaticWebAssets();

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents()
                .AddInteractiveWebAssemblyComponents();

            builder.Services.AddSignalR();
            builder.Services.AddSingleton<IAgentEventBroadcaster, SignalRBroadcaster>();
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error", createScopeForErrors: true);
                app.UseHsts();
            }

            app.UseStaticFiles();
            app.UseAntiforgery();

            try
            {
                app.MapStaticAssets();
            }
            catch (InvalidOperationException)
            {
                // Ignore if static assets manifest is missing (common in some test environments)
            }

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode()
                .AddInteractiveWebAssemblyRenderMode()
                .AddAdditionalAssemblies(typeof(Claude4Net.Dashboard.Client._Imports).Assembly);

            app.MapHub<AgentHub>("/agentHub");
            app.MapHub<ControlPlaneHub>("/controlPlaneHub");

            _host = app;
            await app.StartAsync();
        }
        catch (Exception)
        {
            _host = null;
            throw;
        }
    }

    public static async Task RunAsync(string[] args, int port = DefaultPort)
    {
        await StartAsync(args, port);
        if (_host != null)
        {
            await _host.WaitForShutdownAsync();
        }
    }

    public static async Task StopAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
        }
    }
}
