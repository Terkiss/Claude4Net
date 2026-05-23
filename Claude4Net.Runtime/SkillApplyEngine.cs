using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// Dedicated engine to safely apply approved skill proposals.
    /// Implements preview, approval, checkpointing, path validation, diff evidence, and post-apply verification.
    /// </summary>
    public class SkillApplyEngine
    {
        private readonly SkillProposalService _proposalService;
        private readonly SkillRegistryService _skillRegistry;
        private readonly IRichApprovalHandler? _approvalHandler;

        /// <summary>
        /// Gets or sets a custom verifier to execute post-apply verification.
        /// Receives workspaceRoot and proposalId, returns true if verification passes.
        /// </summary>
        public Func<string, string, Task<bool>>? Verifier { get; set; }

        public SkillApplyEngine(
            SkillProposalService proposalService,
            SkillRegistryService skillRegistry,
            IRichApprovalHandler? approvalHandler = null)
        {
            _proposalService = proposalService ?? throw new ArgumentNullException(nameof(proposalService));
            _skillRegistry = skillRegistry ?? throw new ArgumentNullException(nameof(skillRegistry));
            _approvalHandler = approvalHandler;
        }

        public async Task<bool> ApplyAsync(string proposalId, string workspaceRoot)
        {
            // 1. Status check: The proposal MUST be in Approved state.
            await _proposalService.LoadAsync(workspaceRoot);
            var proposal = _proposalService.GetProposal(proposalId);
            if (proposal == null)
            {
                throw new InvalidOperationException($"Proposal '{proposalId}' not found.");
            }

            if (proposal.Status != SkillProposalStatus.Approved)
            {
                throw new InvalidOperationException($"Proposal must be in Approved state to be applied. Current: {proposal.Status}");
            }

            // Resolve target path
            string? targetPath = proposal.TargetPath;
            if (string.IsNullOrEmpty(targetPath) && !string.IsNullOrEmpty(proposal.SkillId))
            {
                var skill = _skillRegistry.ResolveSkill(proposal.SkillId);
                if (skill != null)
                {
                    targetPath = skill.SourcePath;
                }
                else
                {
                    targetPath = Path.Combine("skills", proposal.SkillId + ".cs");
                }
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                throw new InvalidOperationException("Proposal does not have a valid target path.");
            }

            // 2. Path validation: Ensure target paths of the skill do not mutate .agents/, .gemini/, or any forbidden directories.
            if (proposal.SkillId != null && (proposal.SkillId.StartsWith(".agents/") || proposal.SkillId.StartsWith(".gemini/")))
            {
                throw new UnauthorizedAccessException("Cannot mutate .agents/ or .gemini/ directly.");
            }

            string workspaceRootFull = Path.GetFullPath(workspaceRoot);
            string workspaceRootWithSeparator = workspaceRootFull.EndsWith(Path.DirectorySeparatorChar)
                ? workspaceRootFull
                : workspaceRootFull + Path.DirectorySeparatorChar;

            string fullPath = Path.GetFullPath(Path.IsPathRooted(targetPath) ? targetPath : Path.Combine(workspaceRootFull, targetPath));

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            bool isAtRoot = fullPath.Equals(workspaceRootFull, comparison);
            bool isInside = fullPath.StartsWith(workspaceRootWithSeparator, comparison);

            if (!isAtRoot && !isInside)
            {
                throw new UnauthorizedAccessException($"Path '{targetPath}' is outside safe boundaries of current workspace.");
            }

            var segments = fullPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => s.Equals(".agents", StringComparison.OrdinalIgnoreCase) || s.Equals(".gemini", StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException("Cannot mutate .agents/ or .gemini/ directly.");
            }

            // 3. Patch Preview
            string? oldContent = null;
            if (File.Exists(fullPath))
            {
                oldContent = await File.ReadAllTextAsync(fullPath);
            }

            string newContent = proposal.ProposedChanges ?? string.Empty;
            FileChangeType changeType = oldContent == null ? FileChangeType.Create : FileChangeType.Update;
            FileDiffPreview diffPreview = DiffService.CreatePreview(oldContent, newContent, targetPath, changeType);

            // 4. Pre-apply Checkpoint
            string sessionId = AppState.SessionId ?? Guid.NewGuid().ToString();
            var checkpointStore = new CheckpointStore(workspaceRoot, sessionId);
            string checkpointId = await checkpointStore.CreateCheckpointAsync(
                toolCallId: "skill-apply",
                toolName: "apply",
                files: new List<string> { targetPath },
                description: $"Pre-apply checkpoint for proposal {proposalId}",
                includeMemoryState: false
            );

            await checkpointStore.SaveDiffAsync(checkpointId, diffPreview.DiffContent);

            // 5. User Approval
            if (_approvalHandler != null)
            {
                bool approved = await _approvalHandler.RequestApprovalWithDiffAsync("skill-apply", proposalId, diffPreview);
                if (!approved)
                {
                    throw new OperationCanceledException("User rejected the skill application.");
                }
            }

            // 6. Apply Changes
            string? targetDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            await File.WriteAllTextAsync(fullPath, newContent);

            // 7. Evidence & Record
            proposal.EvidenceReferences.Add($"checkpoint:{checkpointId}");
            proposal.Metadata["ApplyDiff"] = diffPreview.DiffContent;
            proposal.Metadata["CheckpointId"] = checkpointId;

            _proposalService.UpdateStatus(proposalId, SkillProposalStatus.Applied);
            await _proposalService.SaveAsync(workspaceRoot);

            // 8. Post-apply Verification
            bool verificationPassed = true;
            if (Verifier != null)
            {
                verificationPassed = await Verifier(workspaceRoot, proposalId);
            }
            else
            {
                // Default verification
                bool hasCsproj = Directory.Exists(workspaceRoot) && Directory.GetFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories).Any();
                if (hasCsproj)
                {
                    try
                    {
                        var buildPsi = new System.Diagnostics.ProcessStartInfo("dotnet", "build -p:UseAppHost=false")
                        {
                            WorkingDirectory = workspaceRoot,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var buildProcess = System.Diagnostics.Process.Start(buildPsi);
                        if (buildProcess != null)
                        {
                            await buildProcess.WaitForExitAsync();
                            verificationPassed = buildProcess.ExitCode == 0;
                        }
                        else
                        {
                            verificationPassed = false;
                        }
                    }
                    catch
                    {
                        verificationPassed = false;
                    }
                }
                else
                {
                    verificationPassed = File.Exists(fullPath);
                }
            }

            // 9. Verification Status Mutation
            if (verificationPassed)
            {
                _proposalService.UpdateStatus(proposalId, SkillProposalStatus.Verified);
                await _proposalService.SaveAsync(workspaceRoot);
                return true;
            }
            else
            {
                // Revert files using checkpoint
                await checkpointStore.RestoreCheckpointAsync(checkpointId);
                _proposalService.UpdateStatus(proposalId, SkillProposalStatus.Failed);
                await _proposalService.SaveAsync(workspaceRoot);
                return false;
            }
        }
    }
}
