using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K054SeedSpecTests : IDisposable
    {
        private readonly string _testWorkspace;

        public K054SeedSpecTests()
        {
            _testWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_SeedSpec_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testWorkspace);
        }

        public void Dispose()
        {
            try { Directory.Delete(_testWorkspace, true); } catch { }
        }

        [Fact]
        public async Task Store_ShouldSaveAndLoadSpec()
        {
            var store = new SeedSpecStore(_testWorkspace);
            var spec = new SeedSpecRecord
            {
                Id = "SPEC-001",
                Title = "Test Spec",
                Goal = "To test the seed spec store."
            };

            spec.AcceptanceCriteria.Add(new AcceptanceCriterion { Id = "AC-1", Description = "Must pass" });
            spec.OpenQuestions.Add(new ClarifyingQuestion { Id = "Q-1", Question = "Is this clear?" });

            await store.SaveAsync(spec);

            var loaded = await store.LoadAsync("SPEC-001");
            Assert.NotNull(loaded);
            Assert.Equal("Test Spec", loaded.Title);
            Assert.Single(loaded.AcceptanceCriteria);
            Assert.Equal("Must pass", loaded.AcceptanceCriteria[0].Description);
            Assert.Single(loaded.OpenQuestions);
        }

        [Fact]
        public async Task Store_ShouldListAllSpecs()
        {
            var store = new SeedSpecStore(_testWorkspace);
            await store.SaveAsync(new SeedSpecRecord { Id = "SPEC-101" });
            await store.SaveAsync(new SeedSpecRecord { Id = "SPEC-102" });

            var specs = store.ListSpecs().ToList();
            Assert.Equal(2, specs.Count);
            Assert.Contains(specs, s => s.Id == "SPEC-101");
            Assert.Contains(specs, s => s.Id == "SPEC-102");
        }
    }
}
