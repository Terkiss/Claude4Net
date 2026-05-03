using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Discord;
using Discord.WebSocket;

namespace Claude4Net.Tools
{
    /// <summary>
    /// DiscordEngineTool 실행을 위한 입력 매개변수 클래스입니다.
    /// </summary>
    public class DiscordEngineInput
    {
        /// <summary>
        /// 메시지를 보낼 디스코드 채널 ID입니다.
        /// </summary>
        public ulong? channel_id { get; set; }
        
        /// <summary>
        /// 보낼 메시지 내용입니다.
        /// </summary>
        public string message { get; set; } = string.Empty;
        
        /// <summary>
        /// (선택 사항) 첨부할 로컬 파일의 전체 경로입니다.
        /// </summary>
        public string? file_path { get; set; }
    }

    /// <summary>
    /// 디스코드 봇을 통해 특정 채널로 메시지를 전송하는 도구입니다.
    /// </summary>
    public class DiscordEngineTool : ITool
    {
        public string Name => "discord_send";
        public string Description => "Send a message to a specific Discord channel. If channel_id is not provided, it cannot be used.";
        
        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                channel_id = new { type = "integer", description = "The Discord channel ID to send the message to." },
                message = new { type = "string", description = "The message content to send." },
                file_path = new { type = "string", description = "(Optional) The full local file path of an image or file to attach." }
            },
            required = new[] { "message" }
        };

        /// <summary>
        /// 디스코드 메시지 전송을 비동기적으로 수행합니다.
        /// </summary>
        /// <param name="arguments">JSON 형식의 전송 매개변수</param>
        /// <param name="context">실행 컨텍스트</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>전송 결과 상태</returns>
        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            // 1. 파라미터 역직렬화
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<DiscordEngineInput>(arguments, options)
                        ?? throw new ArgumentException("Invalid arguments");

            // [유효성 검사] 채널 ID가 없으면 전송 불가
            if (input.channel_id == null)
            {
                return new { status = "Error", message = "Channel ID is required for this tool." };
            }

            // 2. 디스코드 API 토큰 획득
            string? token = AuthManager.GetDiscordApiKey();
            if (string.IsNullOrEmpty(token)) throw new Exception("Discord API Key not found.");

            // 3. [인증 및 연결] 일회성 클라이언트를 생성하여 로그인 및 시작
            using var client = new DiscordSocketClient();
            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();

            // [연결 대기] Connected 상태가 될 때까지 최대 10회 재시도/대기
            int retry = 0;
            while (client.ConnectionState != ConnectionState.Connected && retry < 10)
            {
                await Task.Delay(500, ct);
                retry++;
            }

            // 4. [채널 획득] 지정된 ID의 채널을 가져옵니다.
            var channel = await client.GetChannelAsync(input.channel_id.Value) as IMessageChannel;
            if (channel == null)
            {
                await client.LogoutAsync();
                return new { status = "Error", message = $"Channel {input.channel_id} not found or inaccessible." };
            }

            // 5. [메시지 전송] 파일 경로가 있으면 파일을 첨부하고, 없으면 텍스트만 전송합니다.
            if (!string.IsNullOrEmpty(input.file_path) && File.Exists(input.file_path))
            {
                await channel.SendFileAsync(input.file_path, input.message);
            }
            else
            {
                await channel.SendMessageAsync(input.message);
            }
            
            // 6. [연결 종료] 사용 완료 후 명시적으로 로그아웃
            await client.LogoutAsync();

            return new
            {
                status = "Success",
                channel = input.channel_id,
                message = "Message sent successfully."
            };
        }
    }
}
