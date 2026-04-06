using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading;

namespace Claude4Net.SDK
{
    public interface IOutputHandler
    {
        Task WriteAsync(string text);
        Task SendFileAsync(string filePath, string? text = null);
    }

    public record InputContext(string Text, IOutputHandler Output);

    public interface IInputBroker
    {
        bool TryWrite(InputContext context);
        ValueTask<InputContext> ReadAsync(CancellationToken cancellationToken = default);
    }

    public class ChannelBroker : IInputBroker
    {
        private readonly Channel<InputContext> _channel;

        public ChannelBroker()
        {
            _channel = Channel.CreateUnbounded<InputContext>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false 
            });
        }

        public bool TryWrite(InputContext context)
        {
            return _channel.Writer.TryWrite(context);
        }

        public async ValueTask<InputContext> ReadAsync(CancellationToken cancellationToken = default)
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }
    }
}
