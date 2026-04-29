using Xunit;
using Claude4Net.Core;
using Claude4Net.Providers;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Claude4Net.Tests
{
    public class CommandTests
    {
        [Fact]
        public void CommandRegistry_ShouldContainAllCommands()
        {
            // Act
            int count = CommandRegistry.GetCommandCount();

            // Assert
            Assert.True(count > 0);
        }

        [Fact]
        public async Task UserInputProcessor_ShouldRouteSlashCommandsCorrectly()
        {
            // Arrange
            string input = "/help";
            var services = new ServiceCollection();
            var serviceProvider = services.BuildServiceProvider();

            // Act
            var result = await UserInputProcessor.ProcessInputAsync(input, serviceProvider);

            // Assert
            Assert.False(result.ShouldQuery);
            Assert.Contains("Available Commands", result.ResultText);
        }
    }
}
