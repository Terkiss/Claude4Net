using Xunit;
using Claude4Net.SDK;
using Claude4Net.Commands;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace Claude4Net.Tests
{
    public class D08CoordinateTests
    {
        [Fact]
        public async Task Coordinate_Flow_ShouldWork()
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var cmd = CommandRegistry.FindCommand("coordinate");
            Assert.NotNull(cmd);
            Assert.NotNull(cmd.Handler);

            // 1. Start Task
            string startRes = await cmd.Handler("start T1 Build a website", services);
            Assert.Contains("started", startRes);
            Assert.Contains("T1", startRes);

            // 2. Check List
            string listRes = await cmd.Handler!("list", services);
            Assert.Contains("T1", listRes);
            Assert.Contains("Planning", listRes);

            // 3. Update Gates
            string gateRes1 = await cmd.Handler!("gate T1 DesignDoc true Finalized design", services);
            Assert.Contains("updated", gateRes1);
            string gateRes2 = await cmd.Handler!("gate T1 ResourceCheck true Resources verified", services);
            Assert.Contains("updated", gateRes2);

            // 4. Check Status
            string statusRes = await cmd.Handler!("status T1", services);
            Assert.Contains("DesignDoc", statusRes);
            Assert.Contains("Finalized design", statusRes);

            // 5. Change Phase
            string phaseRes = await cmd.Handler!("phase T1 Execution", services);
            Assert.Contains("transitioned to Execution", phaseRes);

            // 6. Approve
            string approveRes = await cmd.Handler!("approve T1 All good", services);
            Assert.Contains("set to Approved", approveRes);

            // Verify final state in AppState
            var task = AppState.GetCoordinatedTasks().FirstOrDefault(t => t.Id == "T1");
            Assert.NotNull(task);
            Assert.Equal(CoordinatePhase.Execution, task.CurrentPhase);
            Assert.Equal(ReviewerDecision.Approved, task.ReviewStatus);
            var gate = task.Gates.FirstOrDefault(g => g.Name == "DesignDoc");
            Assert.NotNull(gate);
            Assert.True(gate.IsPassed);
        }

        [Fact]
        public async Task Coordinate_InvalidTask_ShouldReturnError()
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var cmd = CommandRegistry.FindCommand("coordinate");
            Assert.NotNull(cmd);
            Assert.NotNull(cmd.Handler);
            
            string res = await cmd.Handler("status NON_EXISTENT", services);
            Assert.Contains("not found", res);
        }
    }
}
