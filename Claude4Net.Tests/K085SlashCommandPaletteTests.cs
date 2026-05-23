using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.Cli.Ui.Input;
using Claude4Net.Commands;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K085SlashCommandPaletteTests
    {
        [Fact]
        public void PaletteOpensWhenSlashTyped()
        {
            var composer = new PromptComposer();
            Assert.False(composer.IsCommandPaletteVisible);

            // Type '/'
            composer.ProcessKey(new ConsoleKeyInfo('/', ConsoleKey.Divide, false, false, false));

            Assert.True(composer.IsCommandPaletteVisible);
            Assert.Equal("", composer.PaletteFilterText);
            Assert.Equal(0, composer.PaletteSelectedIndex);
        }

        [Fact]
        public void PaletteFiltersCommandsCorrectly()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('/', ConsoleKey.Divide, false, false, false));

            // Type 'd'
            composer.ProcessKey(new ConsoleKeyInfo('d', ConsoleKey.D, false, false, false));
            Assert.Equal("d", composer.PaletteFilterText);

            // Type 'o'
            composer.ProcessKey(new ConsoleKeyInfo('o', ConsoleKey.O, false, false, false));
            Assert.Equal("do", composer.PaletteFilterText);
        }

        [Fact]
        public void PaletteClosesOnEscape()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('/', ConsoleKey.Divide, false, false, false));
            Assert.True(composer.IsCommandPaletteVisible);

            composer.ProcessKey(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
            Assert.False(composer.IsCommandPaletteVisible);
            Assert.Equal("/", composer.GetState().Text);
        }

        [Fact]
        public void PaletteAutocompletesOnEnter()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('/', ConsoleKey.Divide, false, false, false));

            // Filter to doctor
            composer.ProcessKey(new ConsoleKeyInfo('d', ConsoleKey.D, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('o', ConsoleKey.O, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false));

            // Press Enter
            composer.ProcessKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

            Assert.False(composer.IsCommandPaletteVisible);
            Assert.Equal("/doctor", composer.GetState().Text);
        }

        [Fact]
        public void PaletteNavigationAndWrapping()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('/', ConsoleKey.Divide, false, false, false));

            // We should have multiple commands matching empty filter.
            int count = CommandRegistry.GetCommands().Count;
            Assert.True(count > 1);

            Assert.Equal(0, composer.PaletteSelectedIndex);

            // DownArrow should increment
            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
            Assert.Equal(1, composer.PaletteSelectedIndex);

            // UpArrow should decrement back to 0
            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
            Assert.Equal(0, composer.PaletteSelectedIndex);

            // UpArrow from 0 should wrap to count - 1
            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
            Assert.Equal(count - 1, composer.PaletteSelectedIndex);

            // DownArrow from count - 1 should wrap to 0
            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
            Assert.Equal(0, composer.PaletteSelectedIndex);
        }

        [Fact]
        public void PaletteClosesWhenSlashBackspace()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('/', ConsoleKey.Divide, false, false, false));
            Assert.True(composer.IsCommandPaletteVisible);

            // Backspace the slash
            composer.ProcessKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
            Assert.False(composer.IsCommandPaletteVisible);
            Assert.Equal("", composer.GetState().Text);
        }
    }

    public static class TestAssemblyInitializer
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        public static void Initialize()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var assemblyName = new System.Reflection.AssemblyName(args.Name);
                string dllName = assemblyName.Name + ".dll";

                // 1. AppDomain Base Directory
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string localPath = System.IO.Path.Combine(baseDir, dllName);
                if (System.IO.File.Exists(localPath))
                {
                    try { return System.Reflection.Assembly.LoadFrom(localPath); } catch { }
                }

                // 2. Scan other projects in the solution under the solution root folder
                try
                {
                    var dirInfo = new System.IO.DirectoryInfo(baseDir);
                    var solutionRoot = dirInfo.Parent?.Parent?.Parent?.Parent;
                    if (solutionRoot != null && solutionRoot.Exists)
                    {
                        var files = solutionRoot.GetFiles(dllName, System.IO.SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            if (file.FullName.Contains("bin") || file.FullName.Contains("obj"))
                            {
                                try { return System.Reflection.Assembly.LoadFrom(file.FullName); } catch { }
                            }
                        }
                    }
                }
                catch { }

                // 3. Scan NuGet packages folder
                try
                {
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string nugetDir = System.IO.Path.Combine(userProfile, ".nuget", "packages");
                    if (System.IO.Directory.Exists(nugetDir))
                    {
                        string pkgDir = System.IO.Path.Combine(nugetDir, assemblyName.Name.ToLowerInvariant());
                        if (System.IO.Directory.Exists(pkgDir))
                        {
                            var files = System.IO.Directory.GetFiles(pkgDir, dllName, System.IO.SearchOption.AllDirectories);
                            if (files.Length > 0)
                            {
                                try { return System.Reflection.Assembly.LoadFrom(files[0]); } catch { }
                            }
                        }
                    }
                }
                catch { }

                return null;
            };
        }
    }
}
