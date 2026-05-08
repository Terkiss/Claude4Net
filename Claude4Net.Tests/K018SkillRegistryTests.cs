using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using System.Collections.Generic;
using System.Linq;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K018SkillRegistryTests : IDisposable
    {
        private readonly string _tempWorkspace;

        public K018SkillRegistryTests()
        {
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_SkillTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
            AppState.CurrentCwd = _tempWorkspace;
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempWorkspace))
            {
                Directory.Delete(_tempWorkspace, true);
            }
            AppState.CurrentCwd = null;
        }

        [Fact]
        public async Task SkillRegistryService_InitializesEmptyRegistry()
        {
            // Arrange
            var service = new SkillRegistryService(_tempWorkspace);

            // Act
            await service.LoadAsync();
            var skills = service.ListSkills();

            // Assert
            Assert.Empty(skills);
        }

        [Fact]
        public async Task SkillRegistryService_RegistersAndResolvesSkill()
        {
            // Arrange
            var service = new SkillRegistryService(_tempWorkspace);
            await service.LoadAsync();
            var record = new SkillRegistryRecord
            {
                Id = "skill-001",
                DisplayName = "Test Skill",
                Aliases = new List<string> { "test-alias" },
                Description = "A test skill"
            };

            // Act
            service.RegisterSkill(record);
            var resolvedById = service.ResolveSkill("skill-001");
            var resolvedByAlias = service.ResolveSkill("test-alias");

            // Assert
            Assert.NotNull(resolvedById);
            Assert.Equal("Test Skill", resolvedById.DisplayName);
            Assert.NotNull(resolvedByAlias);
            Assert.Equal("skill-001", resolvedByAlias.Id);
        }

        [Fact]
        public async Task SkillRegistryService_UpdatesMetrics()
        {
            // Arrange
            var service = new SkillRegistryService(_tempWorkspace);
            await service.LoadAsync();
            service.RegisterSkill(new SkillRegistryRecord { Id = "s1" });

            // Act
            service.UpdateMetrics("s1", success: true, score: 0.8);
            service.UpdateMetrics("s1", success: true, score: 1.0);
            service.UpdateMetrics("s1", success: false);

            // Assert
            var skill = service.ResolveSkill("s1");
            Assert.NotNull(skill);
            Assert.Equal(2, skill.Metrics.SuccessCount);
            Assert.Equal(1, skill.Metrics.FailureCount);
            Assert.Equal(0.9, skill.Metrics.AverageScore, precision: 1);
            Assert.NotNull(skill.Metrics.LastUsed);
        }

        [Fact]
        public async Task SkillRegistryService_SavesAndLoadsFromFile()
        {
            // Arrange
            var service1 = new SkillRegistryService(_tempWorkspace);
            await service1.LoadAsync();
            service1.RegisterSkill(new SkillRegistryRecord { Id = "persisted-skill", DisplayName = "Save Me" });
            await service1.SaveAsync();

            // Act
            var service2 = new SkillRegistryService(_tempWorkspace);
            await service2.LoadAsync();
            var skill = service2.ResolveSkill("persisted-skill");

            // Assert
            Assert.NotNull(skill);
            Assert.Equal("Save Me", skill.DisplayName);
        }

        [Fact]
        public async Task SkillRegistryService_HandlesDuplicateId()
        {
            // Arrange
            var service = new SkillRegistryService(_tempWorkspace);
            await service.LoadAsync();
            service.RegisterSkill(new SkillRegistryRecord { Id = "dup", DisplayName = "Original" });

            // Act
            service.RegisterSkill(new SkillRegistryRecord { Id = "dup", DisplayName = "Updated" });

            // Assert
            var skills = service.ListSkills();
            Assert.Single(skills);
            Assert.Equal("Updated", skills[0].DisplayName);
        }

        [Fact]
        public void SkillRegistryService_DetectsIdFromSidecar()
        {
            // Arrange
            var service = new SkillRegistryService(_tempWorkspace);
            string skillPath = Path.Combine(_tempWorkspace, "some_skill.md");
            File.WriteAllText(skillPath, "# Content");
            File.WriteAllText(skillPath + ".skill_id", "sidecar-id-123");

            // Act
            string? id = service.GetIdFromSidecar(skillPath);

            // Assert
            Assert.Equal("sidecar-id-123", id);
        }

        [Fact]
        public void SkillRegistryService_ThrowsOnOutsidePath()
        {
            // Arrange
            string baseDir = Path.GetTempPath();
            string repoDir = Path.Combine(baseDir, "repo_" + Guid.NewGuid().ToString("N"));
            string outsideDir = Path.Combine(baseDir, "outside_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(repoDir);
            Directory.CreateDirectory(outsideDir);

            try
            {
                var service = new SkillRegistryService(repoDir);
                string outsideFile = Path.Combine(outsideDir, "skill.md");
                File.WriteAllText(outsideFile, "content");

                // Act & Assert
                Assert.Throws<UnauthorizedAccessException>(() =>
                    service.RegisterSkill(new SkillRegistryRecord { Id = "evil", SourcePath = outsideFile }));
            }
            finally
            {
                if (Directory.Exists(repoDir)) Directory.Delete(repoDir, true);
                if (Directory.Exists(outsideDir)) Directory.Delete(outsideDir, true);
            }
        }

        [Fact]
        public void SkillRegistryService_RejectsSiblingPrefixPath()
        {
            // Arrange
            string baseDir = Path.GetTempPath();
            string repoDir = Path.Combine(baseDir, "repo_" + Guid.NewGuid().ToString("N"));
            string siblingDir = repoDir + "-other";

            Directory.CreateDirectory(repoDir);
            Directory.CreateDirectory(siblingDir);

            try
            {
                var service = new SkillRegistryService(repoDir);
                string dangerousSiblingPath = Path.Combine(siblingDir, "skill.md");
                File.WriteAllText(dangerousSiblingPath, "evil");

                // Act & Assert
                Assert.Throws<UnauthorizedAccessException>(() =>
                    service.RegisterSkill(new SkillRegistryRecord { Id = "evil", SourcePath = dangerousSiblingPath }));

                string? result = service.GetIdFromSidecar(dangerousSiblingPath);
                Assert.Null(result);
            }
            finally
            {
                if (Directory.Exists(repoDir)) Directory.Delete(repoDir, true);
                if (Directory.Exists(siblingDir)) Directory.Delete(siblingDir, true);
            }
        }

        [Fact]
        public void SkillRegistryService_RejectsSymlinkEscape()
        {
            // Arrange
            string baseDir = Path.GetTempPath();
            string repoDir = Path.Combine(baseDir, "repo_" + Guid.NewGuid().ToString("N"));
            string outsideDir = Path.Combine(baseDir, "outside_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(repoDir);
            Directory.CreateDirectory(outsideDir);

            string linkPath = Path.Combine(repoDir, "outlink");
            string outsideFile = Path.Combine(outsideDir, "skill.md");
            File.WriteAllText(outsideFile, "secret");

            try
            {
                // Attempt to create symlink.
                try { Directory.CreateSymbolicLink(linkPath, outsideDir); }
                catch (IOException) { return; } // Skip if no permission
                catch (UnauthorizedAccessException) { return; } // Skip if no permission

                var service = new SkillRegistryService(repoDir);
                string escapedPath = Path.Combine(linkPath, "skill.md");

                // Act & Assert
                Assert.Throws<UnauthorizedAccessException>(() =>
                    service.RegisterSkill(new SkillRegistryRecord { Id = "escaped", SourcePath = escapedPath }));

                string? result = service.GetIdFromSidecar(escapedPath);
                Assert.Null(result);
            }
            finally
            {
                if (Directory.Exists(repoDir)) Directory.Delete(repoDir, true);
                if (Directory.Exists(outsideDir)) Directory.Delete(outsideDir, true);
            }
        }

        [Fact]
        public void SkillRegistryService_ResolveFinalPathInternal_HandlesSegmentSymlinks()
        {
            // Arrange
            string root = Path.GetPathRoot(Path.GetFullPath(".")) ?? "C:\\";
            string ws = Path.Combine(root, "repo");
            string link = Path.Combine(ws, "outlink");
            string target = Path.Combine(root, "outside");
            string file = Path.Combine(link, "skill.md");

            // Act
            string resolved = SkillRegistryService.ResolveFinalPathInternal(file, p =>
            {
                if (p.Equals(link, StringComparison.OrdinalIgnoreCase)) return target;
                return null;
            });

            // Assert
            Assert.Equal(Path.Combine(target, "skill.md"), resolved);
        }

        [Theory]
        [InlineData("..\\evil.md")]
        [InlineData("../evil.md")]
        [InlineData("C:\\evil.md")]
        [InlineData("/etc/passwd")]
        public void SkillRegistryService_GetIdFromSidecar_ReturnsNullOnOutsidePath(string dangerousPath)
        {
            // Arrange
            var service = new SkillRegistryService(_tempWorkspace);

            // Act
            string? result = service.GetIdFromSidecar(dangerousPath);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void SkillRegistryService_ResolveFinalPath_ResolvesCorrectly()
        {
            // Arrange
            var service = new SkillRegistryService(_tempWorkspace);
            string subDir = Path.Combine(_tempWorkspace, "sub");
            Directory.CreateDirectory(subDir);
            string filePath = Path.Combine(subDir, "test.txt");
            File.WriteAllText(filePath, "test");

            // Act
            string resolved = service.ResolveFinalPath(filePath);

            // Assert
            Assert.Equal(Path.GetFullPath(filePath), resolved);
        }
    }
}
