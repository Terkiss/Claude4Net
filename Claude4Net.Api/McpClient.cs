using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    /// <summary>
    /// MCP(Model Context Protocol) 서버와 상호작용하기 위한 클라이언트 클래스입니다.
    /// 서버가 제공하는 도구 목록을 조회하거나 도구를 직접 호출하는 기능을 제공합니다.
    /// </summary>
    public class McpClient
    {
        private readonly McpStdioTransport _transport;

        /// <summary>
        /// McpClient의 새 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="transport">통신에 사용할 MCP 전송 계층</param>
        public McpClient(McpStdioTransport transport)
        {
            _transport = transport;
        }

        /// <summary>
        /// MCP 서버에서 사용 가능한 모든 도구 목록을 비동기적으로 가져옵니다.
        /// </summary>
        /// <returns>MCP 도구 리스트</returns>
        public async Task<List<McpTool>> ListToolsAsync()
        {
            var response = await _transport.SendRequestAsync("tools/list", null);
            if (response.Result.HasValue)
            {
                var tools = response.Result.Value.GetProperty("tools").Deserialize<List<McpTool>>();
                return tools ?? new List<McpTool>();
            }
            return new List<McpTool>();
        }

        /// <summary>
        /// 특정 도구를 실행하도록 MCP 서버에 요청합니다.
        /// </summary>
        /// <param name="name">실행할 도구 이름</param>
        /// <param name="arguments">도구 인자 데이터</param>
        /// <returns>도구 실행 결과</returns>
        public async Task<McpCallToolResult> CallToolAsync(string name, Dictionary<string, object> arguments)
        {
            var response = await _transport.SendRequestAsync("tools/call", new { name, arguments });
            if (response.Result.HasValue)
            {
                return response.Result.Value.Deserialize<McpCallToolResult>() ?? new McpCallToolResult();
            }
            return new McpCallToolResult { IsError = true };
        }
    }
}
