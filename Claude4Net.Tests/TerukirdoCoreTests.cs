using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.SDK.Terukirdo;
using Claude4Net.Runtime.Terukirdo;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class TerukirdoCoreTests
    {
        [Fact]
        public void TierRouter_Classifies_HighRisk_As_Tier3()
        {
            var router = new TerukirdoTierRouter();
            
            var tierAuth = router.ClassifyIntent("인증 시스템 auth token 로직 수정", TerukirdoMode.Orchestrator);
            var tierDeploy = router.ClassifyIntent("production 서버 deploy 및 release 진행", TerukirdoMode.Orchestrator);
            var tierDb = router.ClassifyIntent("users 테이블 drop table migration", TerukirdoMode.Orchestrator);

            Assert.Equal(AdaptiveLoopTier.Tier3_HighRisk_Release, tierAuth);
            Assert.Equal(AdaptiveLoopTier.Tier3_HighRisk_Release, tierDeploy);
            Assert.Equal(AdaptiveLoopTier.Tier3_HighRisk_Release, tierDb);
        }

        [Fact]
        public void TierRouter_Classifies_Conversation_As_Tier0()
        {
            var router = new TerukirdoTierRouter();

            var tierGreeting = router.ClassifyIntent("안녕 테르키르도!", TerukirdoMode.Orchestrator);
            var tierWeather = router.ClassifyIntent("오늘 날씨 어때?", TerukirdoMode.Orchestrator);

            Assert.Equal(AdaptiveLoopTier.Tier0_Companion, tierGreeting);
            Assert.Equal(AdaptiveLoopTier.Tier0_Companion, tierWeather);
        }

        [Fact]
        public void TierRouter_Classifies_CompanionMode_Always_As_Tier0()
        {
            var router = new TerukirdoTierRouter();

            var tierCompanion = router.ClassifyIntent("로그인 API 수정해줘", TerukirdoMode.Companion);
            var tierSecretary = router.ClassifyIntent("배포 일정 정리", TerukirdoMode.MaidSecretary);

            Assert.Equal(AdaptiveLoopTier.Tier0_Companion, tierCompanion);
            Assert.Equal(AdaptiveLoopTier.Tier0_Companion, tierSecretary);
        }

        [Fact]
        public void TierRouter_Classifies_MinorTypo_As_Tier1()
        {
            var router = new TerukirdoTierRouter();

            var tierTypo = router.ClassifyIntent("README.md 오탈자 typo 수정", TerukirdoMode.Orchestrator);
            var tierComment = router.ClassifyIntent("단순 주석 comment 정리", TerukirdoMode.Orchestrator);

            Assert.Equal(AdaptiveLoopTier.Tier1_LowRisk, tierTypo);
            Assert.Equal(AdaptiveLoopTier.Tier1_LowRisk, tierComment);
        }

        [Fact]
        public void TierRouter_Classifies_FeatureImplementation_As_Tier2()
        {
            var router = new TerukirdoTierRouter();

            var tierFeature = router.ClassifyIntent("대시보드 실시간 차트 기능 구현", TerukirdoMode.Orchestrator);

            Assert.Equal(AdaptiveLoopTier.Tier2_MediumRisk_RalphLoop, tierFeature);
        }

        [Fact]
        public void PrimeDirective_Blocks_DestructiveCommands()
        {
            var pd = new TerukirdoPrimeDirective();

            var resultRm = pd.ValidateAction("execute_command", "rm -rf /");
            var resultFormat = pd.ValidateAction("execute_command", "format c:");

            Assert.False(resultRm.IsAllowed);
            Assert.Contains("Prime Directive Violation", resultRm.ViolationReason);

            Assert.False(resultFormat.IsAllowed);
            Assert.Contains("Prime Directive Violation", resultFormat.ViolationReason);
        }

        [Fact]
        public void PrimeDirective_RequiresApproval_For_ForcePush()
        {
            var pd = new TerukirdoPrimeDirective();

            var resultForce = pd.ValidateAction("git", "push --force origin main");

            Assert.True(resultForce.IsAllowed);
            Assert.True(resultForce.RequiresMasterApproval);
            Assert.Contains("Force-push", resultForce.ViolationReason);
        }

        [Fact]
        public async Task MemoryService_Rejects_MasterPreference_Without_OptIn()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TerukirdoTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var memory = new TerukirdoMemoryService(null, tempDir);

                await Assert.ThrowsAsync<SecurityException>(async () =>
                {
                    await memory.SaveMasterPreferenceAsync("FavoriteDrink", "EarlGrey", userOptInConfirmed: false);
                });
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task MemoryService_Persists_MasterPreference_With_OptIn()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TerukirdoTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var memory = new TerukirdoMemoryService(null, tempDir);
                await memory.SaveMasterPreferenceAsync("FavoriteDrink", "EarlGrey", userOptInConfirmed: true);

                string memoryFile = Path.Combine(tempDir, "docs", "Terukirdo_memory.txt");
                Assert.True(File.Exists(memoryFile));
                string content = await File.ReadAllTextAsync(memoryFile);
                Assert.Contains("FavoriteDrink: EarlGrey", content);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task Orchestrator_Processes_Input_And_Switches_Mode()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TerukirdoTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var memory = new TerukirdoMemoryService(null, tempDir);
                var orchestrator = new TerukirdoOrchestrator(null, memory);

                // Initial status check
                var status = await orchestrator.GetStatusAsync();
                Assert.Equal(TerukirdoMode.Orchestrator, status.CurrentMode);

                // Switch to Companion mode
                orchestrator.SetMode(TerukirdoMode.Companion);
                Assert.Equal(TerukirdoMode.Companion, orchestrator.CurrentMode);

                // Process conversation input
                var context = new TerukirdoContext { Mode = TerukirdoMode.Companion };
                var result = await orchestrator.ProcessInputAsync("안녕 테르키르도!", context);

                Assert.True(result.IsSuccess);
                Assert.Equal(AdaptiveLoopTier.Tier0_Companion, result.TierUsed);
                Assert.Contains("주인님", result.Output);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ResolveProtocolVersion_Detects_Version_From_File()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TerukirdoProto_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                // Create Terukirdo_Protocol_v5.6.md in root
                File.WriteAllText(Path.Combine(tempDir, "Terukirdo_Protocol_v5.6.md"), "# Terukirdo Protocol v5.6");
                string resolved = TerukirdoOrchestrator.ResolveProtocolVersion(tempDir);
                Assert.Equal("v5.6", resolved);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
