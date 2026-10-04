using System;
using System.Collections.Concurrent;
using DotNetty.Buffers;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nirvana.Common.Entities.Login;
using Nirvana.Development.Analysis;
using Nirvana.Development.Connection;
using Nirvana.Development.Handlers;
using Nirvana.Development.Manager;
using Nirvana.Development.Utils;
using Nirvana.DevPlugin.Entities;
using Nirvana.DevPlugin.Enums;
using Nirvana.DevPlugin.Events.Event;
using Serilog;

namespace Nirvana.Development;

public class Interceptor {
    private IChannel? _channel;

    // 当前已连接的客户端：认证失败等要主动发断连包时需要用到
    private readonly ConcurrentDictionary<GameConnection, byte> _connections = new();

    private UdpBroadcaster? _udpBroadcaster;

    public required MultithreadEventLoopGroup AcceptorGroup;
    public required InterceptorConfig CurrentConfig;
    public required EntityAccount CurrentUser;
    public required MultithreadEventLoopGroup WorkerGroup;

    public static Interceptor CreateInterceptor(bool isRental, string modInfo, string gameId, string serverName, string serverVersion, string forwardAddress, int forwardPort, string nickName, EntityAccount currentUser, Action<InterceptorConfig, string>? onJoinServer = null, int localPort = 25565)
    {
        var availablePort = NetworkUtil.GetAvailablePort(localPort);

        if (EventManager.TriggerEvent<IEventCreateInterceptor>(createInterceptor => createInterceptor.OnCreateInterceptor(availablePort), EnumProtocolVersion.All) != null) {
            throw new InvalidOperationException("Create Interceptor cancelled");
        }

        var parentGroup = new MultithreadEventLoopGroup();
        var childGroup = new MultithreadEventLoopGroup();
        var currentConfig = new InterceptorConfig {
            IsRental = isRental,
            LocalPort = availablePort,
            NickName = nickName,
            ForwardAddress = forwardAddress,
            ForwardPort = forwardPort <= 0 ? 25565 : forwardPort,
            ServerName = serverName,
            ServerVersion = serverVersion,
            ModInfo = modInfo,
            GameId = gameId,
            OnJoinServer = onJoinServer
        };
        var interceptor = new Interceptor {
            AcceptorGroup = parentGroup,
            WorkerGroup = childGroup,
            CurrentUser = currentUser,
            CurrentConfig = currentConfig
        };
        var serverBootstrap = new ServerBootstrap();
        serverBootstrap.Group(parentGroup, childGroup);
        serverBootstrap.Channel<TcpServerSocketChannel>();
        serverBootstrap.Option(ChannelOption.SoReuseaddr, true); // 允许地址重用
        serverBootstrap.Option(ChannelOption.SoReuseport, true); // 允许端口重用
        serverBootstrap.Option(ChannelOption.TcpNodelay, true); // 禁用Nagle算法
        serverBootstrap.Option(ChannelOption.SoKeepalive, true); // 保持连接
        serverBootstrap.Option(ChannelOption.Allocator, PooledByteBufferAllocator.Default).Option(ChannelOption.SoSndbuf, 1048576); // 发送缓冲区大小
        serverBootstrap.Option(ChannelOption.SoRcvbuf, 1048576); // 接收缓冲区大小
        serverBootstrap.Option(ChannelOption.WriteBufferHighWaterMark, 1048576); // 发送缓冲区水水位
        serverBootstrap.Option(ChannelOption.ConnectTimeout, TimeSpan.FromSeconds(10.0)); // 连接超时时间
        serverBootstrap.ChildHandler(new ActionChannelInitializer<IChannel>(channel => {
            channel.Pipeline.AddLast("splitter", new MessageDeserializer21Bit());
            channel.Pipeline.AddLast("handler", new ServerHandler(currentConfig, interceptor));
            channel.Pipeline.AddLast("pre-encoder", new MessageSerializer21Bit());
            channel.Pipeline.AddLast("encoder", new MessageSerializer());
        })).LocalAddress(availablePort);
        Log.Information("地址: {0}:{1} -> {2}:{3}", currentConfig.LocalAddress, currentConfig.LocalPort, currentConfig.ForwardAddress, currentConfig.ForwardPort);
        Log.Information("角色名: {0}", currentConfig.NickName);
        interceptor._udpBroadcaster = new UdpBroadcaster(currentConfig.LocalPort, currentConfig);
        serverBootstrap.BindAsync().ContinueWith(task => {
            if (task.IsCompletedSuccessfully) {
                interceptor._channel?.CloseAsync();
                interceptor._channel = task.GetAwaiter().GetResult();
            }
        }).ContinueWith(_ => interceptor._udpBroadcaster.StartBroadcastingAsync());
        return interceptor;
    }

    public void RegisterConnection(GameConnection connection)
    {
        _connections.TryAdd(connection, 0);
    }

    public void UnregisterConnection(GameConnection connection)
    {
        _connections.TryRemove(connection, out _);
    }

    /**
     * 认证失败等致命错误：把所有已连接的客户端断开，并把原因显示在客户端的断连界面上。
     * 不发这个包的话，客户端只会一直停在 Logging in...
     */
    public void DisconnectClients(string reason)
    {
        if (_connections.IsEmpty) {
            return;
        }

        Log.Warning("正在断开 {0} 个客户端，原因: {1}", _connections.Count, reason);
        foreach (var connection in _connections.Keys) {
            connection.SendLoginDisconnect(reason);
        }
    }

    public void ShutdownAsync()
    {
        try {
            _udpBroadcaster?.Stop();
            _channel?.CloseAsync();
            AcceptorGroup.ShutdownGracefullyAsync();
            WorkerGroup.ShutdownGracefullyAsync();
        } catch {
            // ignored
        }
    }
}