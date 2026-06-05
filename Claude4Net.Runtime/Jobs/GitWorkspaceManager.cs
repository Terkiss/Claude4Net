using System;
using System.IO;
using System.Security;

namespace Claude4Net.Runtime.Jobs
{
    public class GitWorkspaceManager
    {
        private static readonly string RootPath = @"D:\Claude4Net\AndroidWork";

        public static string PrepareWorkspace(string jobId, string repoUrl, string baseBranch)
        {
            // Path Traversal check: ensure jobId has no path traversal
            if (jobId.Contains("..") || jobId.Contains("/") || jobId.Contains("\\") || jobId.Contains(":"))
            {
                throw new SecurityException("Path traversal detected in Job ID.");
            }

            string jobDir = Path.Combine(RootPath, "jobs", jobId);
            string repoDir = Path.Combine(jobDir, @"workspace\repo");

            // Absolute path validation: must start with RootPath
            string fullJobDir = Path.GetFullPath(jobDir);
            string fullRootPath = Path.GetFullPath(RootPath);
            if (!fullJobDir.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException("Path traversal detected. Workspace path is outside the RootPath.");
            }

            Directory.CreateDirectory(repoDir);

            // Mock/Simulated workspace preparation for test purposes.
            // In a real environment, we would do a git worktree or clone.
            // For now, let's make sure the directories are created.
            return repoDir;
        }

        public static object GetWorkspaceStatus(string jobId)
        {
            string jobDir = Path.Combine(RootPath, "jobs", jobId);
            string repoDir = Path.Combine(jobDir, @"workspace\repo");

            if (!Directory.Exists(repoDir))
            {
                return new { status = "Not Found", message = "Workspace not initialized." };
            }

            // In a real environment, we would parse `git status`.
            return new
            {
                branch = "main",
                status = "Clean",
                changedFiles = new string[0]
            };
        }

        public static Task CommitAsync(string repoDir, string message)
        {
            // Simulate commit
            return Task.CompletedTask;
        }

        public static Task PushAsync(string repoDir)
        {
            // Simulate push
            return Task.CompletedTask;
        }
    }
}
