using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.SDK;

namespace Claude4Net.Cli.Bootstrap;

/// <summary>
/// CLI options for Claude4Net.
/// </summary>
public sealed class CliOptions
{
    /// <summary>
    /// Whether to start the web dashboard.
    /// </summary>
    public bool StartDashboard { get; set; }

    /// <summary>
    /// Permission mode argument.
    /// </summary>
    public string? PermissionModeArg { get; set; }

    /// <summary>
    /// Whether to exit immediately after a smoke test.
    /// </summary>
    public bool SmokeExit { get; set; }

    /// <summary>
    /// Whether to run the doctor command.
    /// </summary>
    public bool IsDoctor { get; set; }

    /// <summary>
    /// Arguments for the doctor command.
    /// </summary>
    public string? DoctorArgs { get; set; }

    /// <summary>
    /// Whether to use the legacy CLI interface. (Reserved for Lumen migration)
    /// </summary>
    public bool LegacyCli { get; set; }

    /// <summary>
    /// Whether to use the new Lumen interactive CLI. (Opt-in for K044)
    /// </summary>
    public bool UseLumen { get; set; }

    /// <summary>
    /// Directory path for workspace option.
    /// </summary>
    public string? WorkspaceDir { get; set; }

    public string? Provider { get; set; }
    public string? Model { get; set; }

    /// <summary>
    /// Whether to start the in-process OpenAI-compatible API server.
    /// </summary>
    public bool StartApi { get; set; }

    /// <summary>
    /// Port for the in-process API server (Default: 7836).
    /// </summary>
    public int ApiPort { get; set; } = 7836;

    /// <summary>
    /// Validation or migration error message if invalid/deprecated arguments are supplied.
    /// </summary>
    public string? ValidationError { get; set; }

    /// <summary>
    /// Remaining non-option arguments.
    /// </summary>
    public string[] RemainingArgs { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Parses the command-line arguments into a <see cref="CliOptions"/> instance.
    /// </summary>
    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        var remaining = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--dashboard", StringComparison.OrdinalIgnoreCase))
            {
                options.StartDashboard = true;
                options.ValidationError = $"{arg}: Dashboard and Lumen now start automatically. Legacy UI has been removed.";
            }
            else if (arg.Equals("--smoke-exit", StringComparison.OrdinalIgnoreCase))
            {
                options.SmokeExit = true;
            }
            else if (arg.Equals("--legacy-cli", StringComparison.OrdinalIgnoreCase))
            {
                options.LegacyCli = true;
                options.ValidationError = $"{arg}: Dashboard and Lumen now start automatically. Legacy UI has been removed.";
            }
            else if (arg.Equals("--lumen", StringComparison.OrdinalIgnoreCase))
            {
                options.UseLumen = true;
                options.ValidationError = $"{arg}: Dashboard and Lumen now start automatically. Legacy UI has been removed.";
            }
            else if (arg.Equals("--yolo", StringComparison.OrdinalIgnoreCase))
            {
                options.PermissionModeArg = "yolo";
            }
            else if (arg.Equals("--setworkspace", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                options.WorkspaceDir = args[++i];
            }
            else if (arg.Equals("--permission-mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                options.PermissionModeArg = args[++i];
            }
            else if (i == 0 && arg.Equals("doctor", StringComparison.OrdinalIgnoreCase))
            {
                options.IsDoctor = true;
                options.DoctorArgs = string.Join(" ", args.Skip(1));
                break;
            }
            else if (arg.Equals("--provider", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                options.Provider = args[++i];
            }
            else if (arg.Equals("--model", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                options.Model = args[++i];
            }
            else if (arg.Equals("--api", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && (args[i + 1].Equals("on", StringComparison.OrdinalIgnoreCase) || args[i + 1].Equals("off", StringComparison.OrdinalIgnoreCase)))
                {
                    options.StartApi = args[++i].Equals("on", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    options.StartApi = true;
                }
            }
            else if (arg.Equals("--api-port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int port) && port > 0)
                {
                    options.ApiPort = port;
                }
            }
            else
            {
                remaining.Add(arg);
            }
        }


        options.RemainingArgs = remaining.ToArray();
        return options;
    }

    /// <summary>
    /// Helper to parse permission mode.
    /// </summary>
    public static bool TryParsePermissionMode(string raw, out PermissionMode mode)
    {
        string normalized = raw.Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

        mode = normalized switch
        {
            "readonly" => PermissionMode.ReadOnly,
            "workspacewrite" => PermissionMode.WorkspaceWrite,
            "prompt" => PermissionMode.Prompt,
            "dangerfullaccess" => PermissionMode.DangerFullAccess,
            "default" => PermissionMode.Default,
            "yolo" => PermissionMode.Yolo,
            "bypasspermissions" => PermissionMode.BypassPermissions,
            _ => default
        };

        return normalized is "readonly" or "workspacewrite" or "prompt" or "dangerfullaccess" or "default" or "yolo" or "bypasspermissions";
    }
}
