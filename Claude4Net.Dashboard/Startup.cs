using Claude4Net.Dashboard.Components;
using Claude4Net.Dashboard.Hubs;
using Claude4Net.Dashboard.Services;
using Claude4Net.SDK;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Claude4Net.Dashboard;

public static class Startup
{
    public static async Task StartAsync(string[] args, string host, int port)
    {
        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls($"http://{host}:{port}");

            // Add services to the container.
            builder.Services.AddRazorComponents()
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
                .AddInteractiveWebAssemblyRenderMode()
                .AddAdditionalAssemblies(typeof(Claude4Net.Dashboard.Client._Imports).Assembly);

            app.MapHub<AgentHub>("/agentHub");
            app.MapHub<ControlPlaneHub>("/controlPlaneHub");

            // Set DashboardServer._host via reflection
            var field = typeof(DashboardServer).GetField("_host", BindingFlags.Static | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(null, app);
            }

            await app.StartAsync();
        }
        catch (Exception)
        {
            var field = typeof(DashboardServer).GetField("_host", BindingFlags.Static | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(null, null);
            }
            throw;
        }
    }
}
