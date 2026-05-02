using Xunit;
using Claude4Net.SDK;
using Claude4Net.Commands;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class D08EvidenceTests : IDisposable
    {
        public D08EvidenceTests()
        {
            AppState.Tasks.Clear();
        }

        public void Dispose()
        {
            AppState.Tasks.Clear();
        }

        [Fact]
        public async Task Coordinate_Evidence_Enforcement_Works()
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var cmd = CommandRegistry.FindCommand("coordinate");
            Assert.NotNull(cmd);
            Assert.NotNull(cmd.Handler);

            // 1. Start Task
            await cmd.Handler("start T_EVIDENCE Test_Task", services);

            // 2. Try to pass DesignDoc (Evidence Required) without evidence
            string failRes = await cmd.Handler("gate T_EVIDENCE DesignDoc true ShouldFail", services);
            Assert.Contains("requires at least one Evidence", failRes);

            // 3. Add Evidence
            string evRes = await cmd.Handler("evidence T_EVIDENCE DesignDoc Added_proof", services);
            Assert.Contains("Evidence added", evRes);

            // 4. Try again - Should succeed now
            string successRes = await cmd.Handler("gate T_EVIDENCE DesignDoc true NowWorks", services);
            Assert.Contains("updated", successRes);

            // 5. Verify State
            var task = AppState.GetCoordinatedTasks().First(t => t.Id == "T_EVIDENCE");
            var gate = task.Gates.First(g => g.Name == "DesignDoc");
            Assert.True(gate.IsPassed);
            Assert.Single(gate.Evidences);
            Assert.Equal("Added_proof", gate.Evidences[0].Summary);
        }

        [Fact]
        public async Task Coordinate_MergeReadiness_CalculatesCorrectly()
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var cmd = CommandRegistry.FindCommand("coordinate");
            Assert.NotNull(cmd);

            // 1. Initial State (Planning)
            await cmd.Handler!("start T_READINESS Test_Task", services);
            var task = AppState.GetCoordinatedTasks().First(t => t.Id == "T_READINESS");
            
            // Base Planning score ~10 + (0/2 gates passed) * 20 = 10%
            Assert.Equal(10, task.ReadinessScore);
            Assert.Contains("DesignDoc", task.Blockers.First());

            // 2. Pass 1/2 Gates
            await cmd.Handler!("gate T_READINESS ResourceCheck true Non_essential", services);
            // 10 + (1/2 * 20) = 20%
            Assert.Equal(20, task.ReadinessScore);

            // 3. Pass all Planning gates and transition to Execution
            await cmd.Handler!("evidence T_READINESS DesignDoc Done", services);
            await cmd.Handler!("gate T_READINESS DesignDoc true Done", services);
            await cmd.Handler!("phase T_READINESS Execution", services);

            // Base Execution score ~40 + (2/4 gates passed) * 20 = 50%
            Assert.Equal(50, task.ReadinessScore);
            Assert.Contains("UnitTests", task.Blockers.First());

            // 4. Approve
            await cmd.Handler!("approve T_READINESS Good_job", services);
            // 50 + 10 = 60%
            Assert.Equal(60, task.ReadinessScore);
        }
    }
}
