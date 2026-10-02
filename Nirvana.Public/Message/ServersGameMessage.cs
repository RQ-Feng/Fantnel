using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nirvana.Common.Utils.CodeTools;
using Nirvana.Public.Manager;
using Nirvana.WPFLauncher.Entities.WPFLauncher.NetGame;
using Nirvana.WPFLauncher.Entities.WPFLauncher.NetGame.GameCharacters;
using Nirvana.WPFLauncher.Protocol;
using Serilog;

namespace Nirvana.Public.Message;

public static class ServersGameMessage {
    // 服务器列表[普通信息] - 缓存
    public static readonly List<EntityNetGameItem> ServerList = [];

    // 并发补齐「图片/版本」时的**全局**并发上限
    // 每一项都要发一次远端详情请求（NPFLauncher.GetNetGameDetailByIdAsync）。
    // 实测：并发开大（6 × 3 个缓存线程）远端会直接返回「服务器繁忙」，20 个一起失败。
    // 原来每个调用点是串行、只有 3 个缓存线程并行 ≈ 全局 3 并发 —— 保持同一量级才稳。
    private const int ImageRepairConcurrency = 3;

    // 全局并发闸门：所有调用点（含后台缓存线程）共用，避免叠加出高并发
    private static readonly SemaphoreSlim ImageRepairGate = new(ImageRepairConcurrency, ImageRepairConcurrency);

    /**
     * 获取服务器列表[普通信息]
     * @param offset 偏移量
     * @param pageSize 每页数量
     * @param safeImage 是否安全获取图片
     * @return 服务器列表[普通信息]
     */
    private static async Task<EntityNetGameItem[]> GetServerList(int offset = 0, int pageSize = 10, bool safeImage = true)
    {
        var index = -pageSize;
        var count = pageSize + offset;

        while (true) {
            // 缓存图片下载
            CacheManager.DownloadCacheImage();

            // ServerList 有 就用缓存
            // 分页
            if (ServerList.Count >= count) {
                var list = ServerList.Skip(offset).Take(pageSize).ToArray();

                // 无须修复图片
                if (!safeImage) {
                    return list;
                }

                // 修复没有图片的游戏项
                // 原来是 foreach + await 串行：一批 15 个就要等 15 次远端往返。
                // 改成有界并发（全局最多 3 个在途），总耗时≈串行的 1/3。
                // 每个 item 是不同对象，GetCacheImageUrl 内部也有锁，并发安全。
                await Parallel.ForEachAsync(
                    list.Where(item => !item.TitleImageSafe()),
                    new ParallelOptions { MaxDegreeOfParallelism = ImageRepairConcurrency },
                    async (item, _) => {
                        await ImageRepairGate.WaitAsync();
                        try {
                            await GetFirstImageAndVerByCache(item);
                        } catch (Exception e) {
                            // 单项失败不要影响整批（原来抛出去会让整个列表请求失败）
                            Log.Warning("获取服务器图片/版本失败: {0} - {1}", item.EntityId, e.Message);
                        } finally {
                            ImageRepairGate.Release();
                        }
                    });

                return list;
            }

            if (++index > 0) {
                // 最后一页, 减少数量，避免丢失数据
                count--;
                pageSize--;
                if (pageSize <= 0) {
                    return [];
                }
            } else {
                var items = await NPFLauncher.GetAvailableNetGamesAsync(ServerList.Count);
                if (items.Length == 0) {
                    index = 1;
                }

                AddServerList(items);
                Thread.Sleep(500);
            }
        }
    }

    public static EntityNetGameItem[] GetServerListTo(int offset = 0, int pageSize = 10, bool safeImage = true, string version = "")
    {
        return GetServerListToAsync(offset, pageSize, safeImage, version).GetAwaiter().GetResult();
    }

    private static async Task<EntityNetGameItem[]> GetServerListToAsync(int offset = 0, int pageSize = 10, bool safeImage = true, string version = "")
    {
        // 没有版本号, 直接获取
        if (string.IsNullOrEmpty(version)) {
            return await GetServerList(offset, pageSize, safeImage);
        }

        return await GetServerListTo(offset, pageSize, safeImage, item => item.Version == version);
    }

    private static async Task<EntityNetGameItem[]> GetServerListTo(int offset, int pageSize, bool safeImage, Func<EntityNetGameItem, bool> filter)
    {
        var index = -pageSize;
        var count = pageSize + offset;
        var pageSize1 = ServerList.Count;

        while (true) {
            var list = await GetServerList(0, pageSize1, safeImage);
            var list1 = list.Where(filter.Invoke).ToArray();
            if (list1.Length >= count) {
                return list1.Skip(offset).Take(pageSize).ToArray();
            }

            if (++index > 0) {
                // 最后一页, 减少数量，避免丢失数据
                count--;
                pageSize--;
                if (pageSize <= 0) {
                    return [];
                }
            } else {
                var items = await NPFLauncher.GetAvailableNetGamesAsync(ServerList.Count);
                if (items.Length == 0) {
                    index = 1;
                } else {
                    pageSize1 += 20;
                }

                AddServerList(items);
                Thread.Sleep(510);
            }
        }
    }

    // 获取 主页图片 / 版本
    private static async Task GetFirstImageAndVer(EntityNetGameItem item)
    {
        var details = await NPFLauncher.GetNetGameDetailByIdAsync(item.EntityId);
        // if (details != null && details.BriefImageUrls.Length > 0)
        item.Version = details is { McVersionList.Length: > 0 } ? details.McVersionList[0].Name : "";
        item.TitleImageUrl = details is { BriefImageUrls.Length: > 0 } ? details.BriefImageUrls[0] : "";
    }

    // 获取 主页图片[缓存保存] / 版本
    private static async Task GetFirstImageAndVerByCache(EntityNetGameItem item)
    {
        await GetFirstImageAndVer(item);
        CacheManager.GetCacheImageUrl(item);
    }

    // 服务器列表[普通信息] - 添加
    private static void AddServerList(EntityNetGameItem gameItem)
    {
        foreach (var item in ServerList.Where(item => item.EntityId == gameItem.EntityId)) {
            if (item.TitleImageUrl == "" && gameItem.TitleImageUrl != "") {
                item.TitleImageUrl = gameItem.TitleImageUrl;
                CacheManager.GetCacheImageUrl(item);
            }

            return;
        }

        ServerList.Add(gameItem);
    }

    // 服务器列表[普通信息] - 添加
    private static void AddServerList(EntityNetGameItem[] gameItems)
    {
        foreach (var item in gameItems) {
            AddServerList(item);
        }
    }

    /**
     * 获取服务器上的指定游戏角色
     * @param serverId 服务器ID
     * @param name 游戏角色名称
     * @return 服务器上的指定游戏角色
     */
    public static async Task<EntityGameCharacter> GetUserName(string serverId, string name)
    {
        for (var i = 0; i < 3; i++) {
            try {
                var games = await NPFLauncher.GetNetGameCharactersAsync(serverId);
                if (games == null) {
                    throw new ErrorCodeException(ErrorCode.NotFound);
                }

                foreach (var game in games) {
                    if (game.Name == name) {
                        return game;
                    }
                }
            } catch (Exception e) {
                Log.Error("获取游戏角色 {0} 失败 {1}", name, e.Message);
            }

            Thread.Sleep(800);
        }

        throw new ErrorCodeException(ErrorCode.NotFoundName);
    }
}