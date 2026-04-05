using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK; // AuthManager 등에서 API KEY를 가져올 경우 사용



namespace Claude4Net.Tools
{
    public class DiscordEngineInput
    {
        public string? sender { get; set; }
        public string? message { get; set; }
        public string? image { get; set; }
    }

    public class DiscordEngineTool : ITool
    {
        public string Name => "discord_engine";
        public string Description => "Discord Engine";
        public Task<object> ExecuteAsync(string input, object parameters) => throw new NotImplementedException();

    }
    class Discord
    {
        string api_key = string.Empty;
        public Discord()
        {

            api_key = AuthManager.GetDiscordApiKey();
        }

    }
}