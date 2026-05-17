using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 에이전트의 출력을 처리하는 핸들러 인터페이스입니다. (CLI, Discord 등 다양한 환경 지원)
    /// </summary>
    public interface IOutputHandler
    {
        /// <summary> 텍스트 내용을 출력합니다. </summary>
        Task WriteAsync(string text);
        /// <summary> 작업 완료 메시지를 출력합니다. </summary>
        Task CompleteAsync(string finalMessage);
        /// <summary> 파일을 전송합니다. </summary>
        Task SendFileAsync(string filePath, string? text = null);
    }

    /// <summary>
    /// 입력 요청의 컨텍스트 정보를 담는 레코드입니다.
    /// </summary>
    /// <param name="Text">입력된 텍스트 내용</param>
    /// <param name="Output">출력 핸들러</param>
    /// <param name="Approval">사용자 승인 핸들러 (선택 사항)</param>
    public record InputContext(string Text, IOutputHandler Output, IUserApprovalHandler? Approval = null, CancellationToken CancellationToken = default);

    /// <summary>
    /// 입력을 수신하고 전달하는 브로커 인터페이스입니다.
    /// </summary>
    public interface IInputBroker
    {
        /// <summary> 컨텍스트를 브로커에 기록(쓰기)합니다. </summary>
        bool TryWrite(InputContext context);
        /// <summary> 브로커로부터 컨텍스트를 비동기적으로 읽어옵니다. </summary>
        ValueTask<InputContext> ReadAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// System.Threading.Channels를 사용한 입력 브로커 구현체입니다.
    /// </summary>
    public class ChannelBroker : IInputBroker
    {
        private readonly Channel<InputContext> _channel;

        public ChannelBroker()
        {
            // 무제한 채널 생성 (다수의 쓰기 가능, 단일 읽기 최적화)
            _channel = Channel.CreateUnbounded<InputContext>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false 
            });
        }

        /// <summary> 채널에 입력을 추가합니다. </summary>
        public bool TryWrite(InputContext context)
        {
            return _channel.Writer.TryWrite(context);
        }

        /// <summary> 채널에서 입력을 대기하여 읽어옵니다. </summary>
        public async ValueTask<InputContext> ReadAsync(CancellationToken cancellationToken = default)
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }
    }
}
