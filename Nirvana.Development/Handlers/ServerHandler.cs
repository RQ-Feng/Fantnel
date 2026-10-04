using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Nirvana.Development.Connection;
using Nirvana.Development.Utils;
using Nirvana.DevPlugin.Entities;

namespace Nirvana.Development.Handlers;

public class ServerHandler(InterceptorConfig config, Interceptor? interceptor = null) : ChannelHandlerAdapter {
    public override void ChannelActive(IChannelHandlerContext context)
    {
        var channel = context.Channel;
        var gameConnection = new GameConnection {
            Config = config,
            ClientChannel = channel
        };
        channel.GetAttribute(ChannelAttribute.Connection).Set(gameConnection);
        interceptor?.RegisterConnection(gameConnection);
        gameConnection.Prepare();
    }

    public override void ChannelRead(IChannelHandlerContext context, object message)
    {
        if (message is IByteBuffer buffer) {
            context.Channel.GetAttribute(ChannelAttribute.Connection).Get().OnClientReceived(buffer);
        }
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        var gameConnection = context.Channel.GetAttribute(ChannelAttribute.Connection).Get();
        interceptor?.UnregisterConnection(gameConnection);
        gameConnection.Shutdown();
    }
}