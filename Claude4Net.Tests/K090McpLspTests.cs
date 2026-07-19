using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.Runtime.Mcp;
using Claude4Net.SDK;
using Claude4Net.Tools;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K090McpLspTests : IDisposable
    {
        private readonly string _originalBaseDir;

        public K090McpLspTests()
        {
            _originalBaseDir = AppState.SystemBaseDir;
            AppState.SystemBaseDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "K090Tests_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(AppState.SystemBaseDir);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(AppState.SystemBaseDir))
            {
                try { System.IO.Directory.Delete(AppState.SystemBaseDir, true); } catch { }
            }
            AppState.SystemBaseDir = _originalBaseDir;
        }

        [Fact]
        public async Task McpClient_ListAndCallTools_MockE2E_Works()
        {
            // 1. Arrange: Setup Mock Transport & Registry
            using var transport = new McpMockTransport();

            // Register handler for tools/list
            transport.RegisterHandler("tools/list", (p) => new
            {
                tools = new List<object>
                {
                    new
                    {
                        name = "get_weather",
                        description = "Get the current weather",
                        inputSchema = new { type = "object", properties = new { location = new { type = "string" } } }
                    }
                }
            });

            // Register handler for tools/call
            transport.RegisterHandler("tools/call", (p) =>
            {
                var name = p.GetProperty("name").GetString();
                Assert.Equal("get_weather", name);
                return new
                {
                    content = new List<object>
                    {
                        new { type = "text", text = "Sunny, 22C" }
                    },
                    isError = false
                };
            });

            var services = new ServiceCollection();
            var sp = services.BuildServiceProvider();
            var orchestrator = ToolOrchestrator.CreateForTest(new List<ITool>(), null, sp);

            using var registry = new McpRegistry();

            // 2. Act: Register the server
            await registry.RegisterServerAsync(transport, orchestrator);

            // 3. Assert: Verify the tool is in ToolOrchestrator
            var tool = orchestrator.GetTool("get_weather");
            Assert.NotNull(tool);
            Assert.Equal("get_weather", tool.Name);
            Assert.Equal("Get the current weather", tool.Description);

            // Execute the tool via orchestrator
            var request = new ToolUseRequest
            {
                Id = "req-1",
                Name = "get_weather",
                Input = new Dictionary<string, object> { ["location"] = "Seoul" }
            };
            var result = await orchestrator.ExecuteToolAsync(request, new object());

            Assert.False(result.IsError);
            Assert.Equal("Sunny, 22C", result.Content?.ToString());
        }

        [Fact]
        public async Task McpClient_PromptsAndResources_Works()
        {
            using var transport = new McpMockTransport();
            transport.RegisterHandler("prompts/list", (p) => new
            {
                prompts = new List<object>
                {
                    new
                    {
                        name = "code_review",
                        description = "Review the provided code",
                        arguments = new List<object>
                        {
                            new { name = "code", description = "The code content", required = true }
                        }
                    }
                }
            });

            transport.RegisterHandler("prompts/get", (p) => new
            {
                description = "Review prompt content",
                messages = new List<object>
                {
                    new { role = "user", content = new { type = "text", text = "Review this: custom_code" } }
                }
            });

            transport.RegisterHandler("resources/list", (p) => new
            {
                resources = new List<object>
                {
                    new
                    {
                        uri = "file://workspace/readme.md",
                        name = "README",
                        description = "Workspace README file",
                        mimeType = "text/markdown"
                    }
                }
            });

            transport.RegisterHandler("resources/read", (p) => new
            {
                contents = new List<object>
                {
                    new
                    {
                        uri = "file://workspace/readme.md",
                        mimeType = "text/markdown",
                        text = "Hello Workspace"
                    }
                }
            });

            using var client = new McpRuntimeClient(transport);
            await client.StartAsync();

            // List & Get Prompts
            var prompts = await client.ListPromptsAsync();
            Assert.Single(prompts);
            Assert.Equal("code_review", prompts[0].Name);

            var promptResult = await client.GetPromptAsync("code_review", new Dictionary<string, string> { ["code"] = "custom_code" });
            Assert.NotNull(promptResult);
            Assert.Equal("Review prompt content", promptResult.Description);
            Assert.Equal("user", promptResult.Messages[0].Role);
            Assert.Equal("Review this: custom_code", promptResult.Messages[0].Content.Text);

            // List & Read Resources
            var resources = await client.ListResourcesAsync();
            Assert.Single(resources);
            Assert.Equal("README", resources[0].Name);

            var resourceResult = await client.ReadResourceAsync("file://workspace/readme.md");
            Assert.NotNull(resourceResult);
            Assert.Single(resourceResult.Contents);
            Assert.Equal("Hello Workspace", resourceResult.Contents[0].Text);
        }

        [Fact]
        public async Task LspTool_MockE2E_GoToDefinition_Works()
        {
            // 1. Arrange: Setup Mock LSP Client
            var lspClient = LspMockServer.CreateMockLspClient((req) =>
            {
                Assert.Equal("textDocument/definition", req.Method);
                return new JsonRpcResponse
                {
                    Id = req.Id,
                    Result = JsonSerializer.SerializeToElement(new List<object>
                    {
                        new
                        {
                            uri = "file:///d:/Project/Test.cs",
                            range = new
                            {
                                start = new { line = 10, character = 5 },
                                end = new { line = 10, character = 15 }
                            }
                        }
                    })
                };
            });

            var lspTool = new LspTool(lspClient);

            // 2. Act: Execute LspTool
            var args = JsonSerializer.Serialize(new
            {
                operation = "goToDefinition",
                filePath = "Test.cs",
                line = 5,
                character = 10
            });

            var executeResult = await lspTool.ExecuteAsync(args, new object());

            // 3. Assert
            Assert.NotNull(executeResult);
            var resultStr = JsonSerializer.Serialize(executeResult);
            Assert.Contains("goToDefinition", resultStr);
            Assert.Contains("file:///d:/Project/Test.cs", resultStr);
        }
    }
}
