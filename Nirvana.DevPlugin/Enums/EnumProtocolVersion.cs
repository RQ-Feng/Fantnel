using System;

namespace Nirvana.DevPlugin.Enums;

public enum EnumProtocolVersion {
    None = 0, // 无法确定
    All = -1, // 所有版本
    V1076 = 5, // 1.7.6
    V108X = 47, // 1.8.x
    V1122 = 340, // 1.12.2
    V1165 = 754, // 1.16.5
    V1180 = 757, // 1.18.0
    V1200 = 763, // 1.20.0
    V1206 = 766, // 1.20.6
    V1210 = 767 // 1.21.0
}

public static class EnumProtocolVersionExtensions {
    /**
     * 把握手里的原始协议号映射为枚举
     *
     * 不能直接 (EnumProtocolVersion)packetVersion 强转：
     * 比如 1.21.7 的协议号是 772，而枚举里最大只到 V1210(767)，强转会得到
     * 一个未定义值，导致 PacketManager 里按 V1206/V1210 注册的包永远匹配不上
     * （Configuration 阶段的 Finish Configuration / Acknowledge 等全部失效）。
     *
     * 1.21.2+ 的包 ID 与 1.21.0 一致，因此：
     *   - 比已知最高版本更新的一律归到最高已知版本
     *   - 其它未定义值归到 None
     */
    public static EnumProtocolVersion ToProtocolVersion(this int protocolNumber)
    {
        if (protocolNumber <= 0) {
            return EnumProtocolVersion.None;
        }

        foreach (var value in Enum.GetValues<EnumProtocolVersion>()) {
            if ((int)value == protocolNumber) {
                return value;
            }
        }

        return protocolNumber > (int)EnumProtocolVersion.V1210
            ? EnumProtocolVersion.V1210
            : EnumProtocolVersion.None;
    }
}