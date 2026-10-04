using DotNetty.Buffers;
using Nirvana.DevPlugin;
using Nirvana.DevPlugin.Enums;
using Nirvana.DevPlugin.Extensions;
using Nirvana.DevPlugin.Packet;
using Serilog;

namespace Nirvana.Development.Packet.Login.Server;

public class SPacketDisconnect : FPacket {
    public static readonly RegisterPacket RegisterPacket = new(EnumConnectionState.Login, EnumPacketDirection.ClientBound, 0);

    // 主动下发给客户端的原因（JSON 文本组件）。
    // 为 null 说明这个实例是从服务端读出来的，需要原样透传（不能重新编码）。
    private readonly string? _outgoingReason;

    private string? _reason;

    public SPacketDisconnect() { }

    /**
     * 由代理主动下发给客户端（认证失败等）。
     * 客户端会弹出「无法连接至服务器」界面，并把 reason 显示在标题下方。
     */
    public SPacketDisconnect(string reason)
    {
        _outgoingReason = reason;
    }

    public override void ReadFromBuffer(BGameConnection connection, IByteBuffer buffer)
    {
        base.ReadFromBuffer(connection, buffer);
        _reason = buffer.ReadStringFromBuffer();
    }

    public override void WriteToBuffer(IByteBuffer buffer)
    {
        if (_outgoingReason != null) {
            // 与网易服务器自己的登录断开包格式一致：VarInt 长度 + UTF-8 JSON 文本组件
            buffer.WriteStringToBuffer(_outgoingReason);
            return;
        }

        base.WriteToBuffer(buffer);
    }

    public override bool HandlePacket(BGameConnection connection)
    {
        Log.Debug("断开原因: {0}", _reason);
        return false;
    }
}