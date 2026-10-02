using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Nirvana.Common;
using Nirvana.Public.Entities.Proxy;
using Serilog;

namespace Nirvana.Public.Manager;

/**
 * 代理历史记录
 *
 * 存储位置：NirvanaConfig 的 "proxyHistory" 配置项。
 * 注意：这里存的是 **JSON 字符串**而不是 JsonArray ——
 *   NirvanaConfig.AddFromJsonNode 只认 String / Number / Bool / Object，
 *   数组在下次启动时读不回来，会被丢掉。
 */
public static class ProxyHistoryManager {
    private const string ConfigKey = "proxyHistory";

    // 最多保留多少条
    private const int MaxCount = 12;

    private static readonly Lock HistoryLock = new();

    // 读取历史（最新在前）
    public static List<EntityProxyHistory> GetHistory()
    {
        lock (HistoryLock) {
            return Load().OrderByDescending(item => item.Time).ToList();
        }
    }

    // 记录一次成功的代理启动（同一服务器+类型只保留最新一条）
    public static void Record(string id, string name, string mode, string version, string player)
    {
        if (string.IsNullOrEmpty(id)) {
            return;
        }

        lock (HistoryLock) {
            var list = Load();

            // 同一个服务器只留最新的一条，避免列表被反复启动刷满
            list.RemoveAll(item => item.Id == id && item.Mode == mode);
            list.Insert(0, new EntityProxyHistory {
                Id = id,
                Name = string.IsNullOrEmpty(name) ? id : name,
                Mode = mode,
                Version = version,
                Player = player,
                Time = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            });

            if (list.Count > MaxCount) {
                list.RemoveRange(MaxCount, list.Count - MaxCount);
            }

            try {
                NirvanaConfig.SetValue(ConfigKey, JsonSerializer.Serialize(list));
            } catch (Exception e) {
                // 历史记录失败不能影响代理启动
                Log.Warning("保存代理历史失败 : {0}", e.Message);
            }
        }
    }

    private static List<EntityProxyHistory> Load()
    {
        try {
            var json = NirvanaConfig.GetValue(ConfigKey, () => "[]");
            // 配置项默认值是空字符串（未写过历史时），空/空白直接当空列表，不要报错
            if (string.IsNullOrWhiteSpace(json)) {
                return [];
            }

            return JsonSerializer.Deserialize<List<EntityProxyHistory>>(json) ?? [];
        } catch (Exception e) {
            Log.Warning("读取代理历史失败 : {0}", e.Message);
            return [];
        }
    }
}
