namespace Nirvana.Public.Entities.Proxy;

/**
 * 一条「上次代理」记录
 * 每次成功启动本地代理时写入，主页用它快速回到之前代理过的服务器。
 */
public class EntityProxyHistory {
    // 服务器 ID
    public string Id { get; set; } = string.Empty;

    // 服务器名称
    public string Name { get; set; } = string.Empty;

    // 类型 net | rental
    public string Mode { get; set; } = "net";

    // 游戏版本（1.21.7 等）
    public string Version { get; set; } = string.Empty;

    // 使用的角色名
    public string Player { get; set; } = string.Empty;

    // 启动时间（Unix 毫秒）
    public long Time { get; set; }
}
