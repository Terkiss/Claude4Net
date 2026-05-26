using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude4Net.Runtime
{
    public enum SecurityProfileLevel
    {
        Strict,
        Permissive,
        Development
    }

    public class SecurityProfile
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SecurityProfileLevel Level { get; set; } = SecurityProfileLevel.Strict;

        public List<string> AllowedCommandPatterns { get; set; } = new();
        public List<string> BlockedCommandPatterns { get; set; } = new();

        public List<string> AllowedFolders { get; set; } = new();
        public List<string> BlockedFolders { get; set; } = new();

        public bool AllowOutsideWorkspace { get; set; } = false;
        public bool BlockDirectoryTraversal { get; set; } = true;
        public bool RequireApprovalForSensitiveTools { get; set; } = true;
    }

    public class SecurityPolicyConfig
    {
        public string ActiveProfile { get; set; } = "Strict";
        public Dictionary<string, SecurityProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static SecurityPolicyConfig Parse(string json)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                options.Converters.Add(new JsonStringEnumConverter());

                return JsonSerializer.Deserialize<SecurityPolicyConfig>(json, options) ?? CreateDefault();
            }
            catch
            {
                return CreateDefault();
            }
        }

        public static SecurityPolicyConfig CreateDefault()
        {
            var config = new SecurityPolicyConfig
            {
                ActiveProfile = "Strict"
            };

            // Strict Profile: White-list only safe commands, no outside access, directory traversal blocked.
            config.Profiles["Strict"] = new SecurityProfile
            {
                Level = SecurityProfileLevel.Strict,
                AllowedCommandPatterns = new List<string> { "^dotnet build$", "^dotnet test$", "^git status$" },
                BlockedCommandPatterns = new List<string> { ".*" }, // blocks everything not explicitly allowed by default
                AllowedFolders = new List<string>(),
                BlockedFolders = new List<string> { @"C:\Windows", @"/etc", @"/var" },
                AllowOutsideWorkspace = false,
                BlockDirectoryTraversal = true,
                RequireApprovalForSensitiveTools = true
            };

            // Permissive Profile: Allowed by default, blacklist dangerous commands.
            config.Profiles["Permissive"] = new SecurityProfile
            {
                Level = SecurityProfileLevel.Permissive,
                AllowedCommandPatterns = new List<string> { ".*" },
                BlockedCommandPatterns = new List<string>
                {
                    @"(^|\s)(sudo|su|runas)\b",
                    @"(^|\s)(format|mkfs|diskpart|dd)\b",
                    @"(^|\s)(rm\s+-rf|del\s+/f|Remove-Item\s+.*-Force)\b"
                },
                AllowedFolders = new List<string>(),
                BlockedFolders = new List<string> { @"C:\Windows" },
                AllowOutsideWorkspace = true,
                BlockDirectoryTraversal = true,
                RequireApprovalForSensitiveTools = true
            };

            // Development Profile: Fully trusted.
            config.Profiles["Development"] = new SecurityProfile
            {
                Level = SecurityProfileLevel.Development,
                AllowedCommandPatterns = new List<string> { ".*" },
                BlockedCommandPatterns = new List<string>(),
                AllowedFolders = new List<string> { "*" },
                AllowOutsideWorkspace = true,
                BlockDirectoryTraversal = false,
                RequireApprovalForSensitiveTools = false
            };

            return config;
        }

        public static SecurityPolicyConfig LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return CreateDefault();
            }
            try
            {
                string json = File.ReadAllText(filePath);
                return Parse(json);
            }
            catch
            {
                return CreateDefault();
            }
        }
    }
}
