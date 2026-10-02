using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DotNetty.Buffers;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nirvana.Development.Analysis;
using Nirvana.Development.Handlers;
using Nirvana.Development.Manager;
using Nirvana.DevPlugin;
using Nirvana.DevPlugin.Enums;
using Nirvana.DevPlugin.Events.Event;
using Nirvana.DevPlugin.Extensions;
using Serilog;

namespace Nirvana.Development.Connection;

public class GameConnection : BGameConnection {
    private MultithreadEventLoopGroup? _workerGroup;

    public void Prepare()
    {
        if (_workerGroup != null) {
            Shutdown();
        }

        _workerGroup = new MultithreadEventLoopGroup();
        var bootstrap = new Bootstrap();
        bootstrap.Group(_workerGroup);
        bootstrap.Channel<TcpSocketChannel>();
        bootstrap.Option(ChannelOption.TcpNodelay, true); // 禁用Nagle算法
        bootstrap.Option(ChannelOption.SoKeepalive, true); // 保持连接
        bootstrap.Option(ChannelOption.Allocator, PooledByteBufferAllocator.Default);
        bootstrap.Option(ChannelOption.SoSndbuf, 1048576); // 发送缓冲区大小
        bootstrap.Option(ChannelOption.SoRcvbuf, 1048576); // 接收缓冲区大小
        bootstrap.Option(ChannelOption.WriteBufferHighWaterMark, 1048576); // 发送缓冲区水水位
        bootstrap.Option(ChannelOption.ConnectTimeout, TimeSpan.FromSeconds(30.0)); // 连接超时时间
        bootstrap.Handler(new ActionChannelInitializer<IChannel>(channel => {
            channel.Pipeline.AddLast("splitter", new MessageDeserializer21Bit());
            channel.Pipeline.AddLast("handler", new ClientHandler(this));
            channel.Pipeline.AddLast("pre-encoder", new MessageSerializer21Bit());
            channel.Pipeline.AddLast("encoder", new MessageSerializer());
        }));
        var initialized = false;
        // Config.ForwardAddress = "localhost";
        // Config.ForwardPort = 25577;
        Task.Run(() => {
            EventManager.TriggerEvent<IEventParseAddress>(eventAddress => { eventAddress.OnParseAddress(Config); }, ProtocolVersion);
            var serverChannel = (IPAddress.TryParse(Config.ForwardAddress, out var address) ? bootstrap.ConnectAsync(address, Config.ForwardPort) : bootstrap.ConnectAsync(Config.ForwardAddress, Config.ForwardPort)).ContinueWith(channel => {
                if (!channel.IsFaulted) {
                    return channel.GetAwaiter().GetResult();
                }

                Log.Error(channel.Exception, "Failed to connect to remote server {0}:{1}", Config.ForwardAddress, Config.ForwardPort);
                return null;
            });
            ServerChannel = serverChannel.GetAwaiter().GetResult();
            initialized = true;
        });
        while (!initialized) {
            Thread.Sleep(100);
        }

        if (ServerChannel == null) {
            Shutdown();
        }
    }

    public void OnServerReceived(IByteBuffer buffer)
    {
        HandlePacketReceived(buffer, EnumPacketDirection.ClientBound, data => { ClientChannel?.WriteAndFlushAsync(data); });
    }

    public void OnClientReceived(IByteBuffer buffer)
    {
        HandlePacketReceived(buffer, EnumPacketDirection.ServerBound, data => { ServerChannel?.WriteAndFlushAsync(data); });
    }

    public void Shutdown()
    {
        EventManager.TriggerEvent<IEventConnectionClosed>(eventClosed => { eventClosed.OnConnectionClosed(); }, ProtocolVersion);
        Log.Debug("Shutting down connection...");
        ClientChannel?.CloseAsync();
        ServerChannel?.CloseAsync();
        _workerGroup?.ShutdownGracefullyAsync();
    }

    // 已知包的帧体解析失败（帧本身已被完整切分，不会造成流错位）
    private sealed class PacketParseException(int id, Exception inner)
        : Exception($"解析包体失败, Id: {id}", inner);

    private void HandlePacketReceived(IByteBuffer buffer, EnumPacketDirection direction, Action<object> onRedirect)
    {
        buffer.MarkReaderIndex();
        var id = buffer.ReadVarIntFromBuffer();

        bool handled;
        try {
            handled = PacketManager.TriggerEvent(iPacket => {
                try {
                    // Log.Information("Handle[{0}.{1}]: {2}[{3}]", direction, State, iPacket.GetType().Name, id);
                    iPacket.PacketId = id;
                    iPacket.ProtocolVersion = ProtocolVersion;
                    iPacket.ReadFromBuffer(this, buffer);
                } catch (Exception exception) {
                    Log.Error(exception, "Cannot read packet from buffer, direction: {0}, Id: {1}, ProtocolVersion: {2}", direction, id, ProtocolVersion);
                    throw new PacketParseException(id, exception);
                }

                try {
                    if (iPacket.HandlePacket(this)) {
                        return true;
                    }
                } catch (Exception exception) {
                    Log.Error(exception, "Cannot handle packet, direction: {0}, Id: {1}, ProtocolVersion: {2}", direction, id, ProtocolVersion);
                    throw;
                }

                try {
                    buffer.Clear();
                    buffer.WriteVarInt(id);
                    iPacket.WriteToBuffer(buffer);
                    buffer.MarkReaderIndex();
                    buffer.ReadVarIntFromBuffer();
                } catch (Exception exception) {
                    Log.Error(exception, "Cannot write packet to buffer, direction: {0}, Id: {1}, ProtocolVersion: {2}", direction, id, ProtocolVersion);
                    throw;
                }

                return false;
            }, State, direction, id, ProtocolVersion, Config.GameId) != null;
        } catch (PacketParseException) {
            // 一个畸形/不认识的包不应直接打断整条连接：
            // 回滚 readerIndex 后原样透传给对端，同时把帧内容打出来便于定位。
            buffer.ResetReaderIndex();
            Log.Warning("包体解析失败，已原样透传: direction={0}, Id={1}, ProtocolVersion={2}, 帧长={3}, 内容={4}",
                direction, id, ProtocolVersion, buffer.ReadableBytes, HexDump(buffer));
            onRedirect(buffer);
            return;
        }

        if (!handled) {
            // Log.Information("Handle[{0}.{1}]: {2}", direction, State, id);
            buffer.ResetReaderIndex();
            onRedirect(buffer);
        }
    }

    // 打印帧头部十六进制：用于区分「加密/分帧错位（字节随机）」和「包体真的缺字段（字节规整）」
    private static string HexDump(IByteBuffer buffer)
    {
        var length = Math.Min(buffer.ReadableBytes, 64);
        if (length <= 0) {
            return "(空帧)";
        }

        var bytes = new byte[length];
        buffer.GetBytes(buffer.ReaderIndex, bytes, 0, length);
        var text = Convert.ToHexString(bytes);
        return buffer.ReadableBytes > length ? $"{text} ...(共{buffer.ReadableBytes}字节)" : text;
    }

    public static void EnableCompression(IChannel channel, int threshold)
    {
        if (threshold < 0) {
            if (channel.Pipeline.Get("decompress") is NettyCompressionDecoder) {
                channel.Pipeline.Remove("decompress");
            }

            if (channel.Pipeline.Get("compress") is NettyCompressionEncoder) {
                channel.Pipeline.Remove("compress");
            }
        } else {
            if (channel.Pipeline.Get("decompress") is NettyCompressionDecoder nettyCompressionDecoder) {
                nettyCompressionDecoder.Threshold = threshold;
            } else {
                channel.Pipeline.AddAfter("splitter", "decompress", new NettyCompressionDecoder(threshold));
            }

            if (channel.Pipeline.Get("compress") is NettyCompressionEncoder nettyCompressionEncoder) {
                nettyCompressionEncoder.Threshold = threshold;
            } else {
                channel.Pipeline.AddBefore("encoder", "compress", new NettyCompressionEncoder(threshold));
            }
        }
    }

    public static void EnableEncryption(IChannel channel, byte[] secretKey)
    {
        channel.Pipeline.AddBefore("splitter", "decrypt", new NettyEncryptionDecoder(secretKey));
        channel.Pipeline.AddBefore("pre-encoder", "encrypt", new NettyEncryptionEncoder(secretKey));
    }
}