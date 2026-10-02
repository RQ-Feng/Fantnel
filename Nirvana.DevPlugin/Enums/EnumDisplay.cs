namespace Nirvana.DevPlugin.Enums;

/**
 * 日志用的枚举中文名
 * 保留英文原名（括号里），方便和协议文档/抓包对照
 */
public static class EnumDisplay {
    public static string ToDisplay(this EnumConnectionState state)
    {
        var name = state switch {
            EnumConnectionState.Handshake => "握手",
            EnumConnectionState.Status => "状态查询",
            EnumConnectionState.Login => "登录",
            EnumConnectionState.Configuration => "配置",
            EnumConnectionState.Play => "游戏",
            _ => "未知"
        };
        return $"{name}({state})";
    }

    public static string ToDisplay(this EnumPacketDirection direction)
    {
        var name = direction switch {
            EnumPacketDirection.ServerBound => "上行",
            EnumPacketDirection.ClientBound => "下行",
            _ => "未知"
        };
        return $"{name}({direction})";
    }
}
