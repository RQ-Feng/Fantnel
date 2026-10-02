using DotNetty.Buffers;
using Nirvana.Development.Manager;
using Nirvana.DevPlugin;
using Nirvana.DevPlugin.Enums;
using Nirvana.DevPlugin.Events.Event;
using Nirvana.DevPlugin.Extensions;
using Nirvana.DevPlugin.Packet;
using Serilog;

namespace Nirvana.Development.Packet.Handshake.Client;

public class CHandshake : BPacket {
    public static readonly RegisterPacket RegisterPacket = new(EnumConnectionState.Handshake, EnumPacketDirection.ServerBound, 0);

    private int _nextState;

    private int _packetVersion;
    private string? _serverAddress;
    private ushort _serverPort;

    public override void ReadFromBuffer(BGameConnection connection, IByteBuffer buffer)
    {
        _packetVersion = buffer.ReadVarIntFromBuffer();
        _serverAddress = buffer.ReadStringFromBuffer();
        _serverPort = buffer.ReadUnsignedShort();
        _nextState = buffer.ReadVarIntFromBuffer();
    }

    public override void WriteToBuffer(IByteBuffer buffer)
    {
        if (_serverAddress == null) {
            return;
        }

        buffer.WriteVarInt(_packetVersion);
        buffer.WriteStringToBuffer(_serverAddress);
        buffer.WriteShort(_serverPort);
        buffer.WriteVarInt(_nextState);
    }

    public override bool HandlePacket(BGameConnection connection)
    {
        if (_serverAddress == null) {
            return false;
        }

        if (EventManager.TriggerEvent<IEventCHandshake>(handshake => handshake.OnCHandshake(_packetVersion, _serverAddress, _serverPort, _nextState), ProtocolVersion) != null) {
            return true;
        }

        connection.ProtocolVersion = _packetVersion.ToProtocolVersion();
        connection.State = (EnumConnectionState)_nextState;
        _serverPort = (ushort)connection.Config.ForwardPort;
        var serverAddress = connection.ProtocolVersion switch {
            > EnumProtocolVersion.V1180 and <= EnumProtocolVersion.V1206 => connection.Config.ForwardAddress + "\0FML3\0",
            > EnumProtocolVersion.V1180 => connection.Config.ForwardAddress + "\0FORGE",
            <= EnumProtocolVersion.V1122 => connection.Config.ForwardAddress + "\0FML\0",
            _ => connection.Config.ForwardAddress + "\0FML2\0"
        };
        // 把握手里的原始协议号也打出来：映射结果（如 772 -> V1210）直接可核对，
        // 按版本注册的包（LoginAcknowledged / FinishConfiguration 等）能否匹配全看它。
        Log.Information("协议 {0}({1})，下一状态: {2}，地址: {3} -> {4}", _packetVersion, connection.ProtocolVersion, connection.State.ToDisplay(), _serverAddress, serverAddress.Replace("\0", "|"));
        _serverAddress = serverAddress;
        return false;
    }
}