using Xunit;
using Claude4Net.Tools;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using System;
using System.Collections.Generic;

namespace Claude4Net.Tests
{
    public class ToolTests
    {
        [Fact]
        public async Task LsTool_ShouldListFiles()
        {
            // Arrange
            var tool = new LsTool();
            var arguments = JsonSerializer.Serialize(new { path = "." });

            // Act
            var result = await tool.ExecuteAsync(arguments, new object());

            // Assert
            Assert.NotNull(result);
            var json = JsonSerializer.Serialize(result);
            var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("entries").GetArrayLength() > 0);
        }

        [Fact]
        public async Task BashTool_ShouldExecuteEcho()
        {
            // Arrange
            var tool = new BashTool();
            var arguments = JsonSerializer.Serialize(new { command = "echo Hello" });

            // Act
            var result = await tool.ExecuteAsync(arguments, new object());

            // Assert
            Assert.NotNull(result);
            var json = JsonSerializer.Serialize(result);
            var doc = JsonDocument.Parse(json);
            Assert.Equal(0, doc.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Contains("Hello", doc.RootElement.GetProperty("output").GetString());
        }
    }
}
