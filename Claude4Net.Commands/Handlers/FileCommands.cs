using System;
using System.IO;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Spectre.Console;

namespace Claude4Net.Commands.Handlers
{
    public static class FileCommands
    {
        public static Task<string> HandleLs(string a, IServiceProvider sp)
        {
            if (string.IsNullOrEmpty(AppState.CurrentCwd)) return Task.FromResult("[red]Error:[/] Workspace is not set. Use [bold]/setworkspace <path>[/] first.");

            string currentPath = Environment.CurrentDirectory;
            var files = Directory.GetFileSystemEntries(currentPath);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[bold cyan]Directory: {Markup.Escape(currentPath)}[/]");
            foreach(var f in files)
            {
                bool isDir = Directory.Exists(f);
                string tag = isDir ? "[bold blue][[Dir]][/]" : "[grey][[File]][/]";
                sb.AppendLine($"  {tag} {Markup.Escape(Path.GetFileName(f))}");
            }
            return Task.FromResult(sb.ToString());
        }

        public static Task<string> HandlePwd(string a, IServiceProvider sp)
        {
            string currentPath = AppState.CurrentCwd ?? Environment.CurrentDirectory;
            return Task.FromResult($"[cyan]CWD:[/] {Markup.Escape(currentPath)}");
        }

        public static Task<string> HandleSetWorkspace(string a, IServiceProvider sp)
        {
            if (string.IsNullOrWhiteSpace(a)) return Task.FromResult("Usage: /setworkspace <path>");
            string newPath = Path.GetFullPath(a);
            if (Directory.Exists(newPath)) {
                AppState.CurrentCwd = newPath;
                Environment.CurrentDirectory = newPath;
                return Task.FromResult($"[bold green]Workspace set to:[/] {Markup.Escape(newPath)}\n[grey]Tools are now active for this directory.[/]");
            }
            return Task.FromResult($"[red]Error:[/] Directory not found: {Markup.Escape(newPath)}");
        }

        public static Task<string> HandleCd(string a, IServiceProvider sp)
        {
            if (string.IsNullOrEmpty(AppState.CurrentCwd)) return Task.FromResult("[red]Error:[/] Please set your workspace first using [bold]/setworkspace <path>[/]");
            if (string.IsNullOrWhiteSpace(a)) return Task.FromResult("Usage: /cd <path>");

            string combined = Path.Combine(Environment.CurrentDirectory, a);
            string newPath = Path.GetFullPath(combined);

            if (Directory.Exists(newPath)) {
                // 샌드박스 정책: 설정된 작업 공간 루트 밖으로 나가는 것을 금지합니다.
                string normalizedWorkspace = AppState.CurrentCwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string normalizedNewPath = newPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                if (normalizedNewPath.StartsWith(normalizedWorkspace, StringComparison.OrdinalIgnoreCase)) {
                    Environment.CurrentDirectory = newPath;
                    return Task.FromResult($"[green]Directory changed to:[/] {Markup.Escape(newPath)}");
                }
                return Task.FromResult($"[red]Error:[/] Cannot move outside the set workspace root: {Markup.Escape(AppState.CurrentCwd)}");
            }
            return Task.FromResult($"[red]Error:[/] Directory not found: {Markup.Escape(newPath)}");
        }
    }
}
