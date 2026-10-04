using System;
using System.Text.Json;
using System.Threading.Tasks;
using Nirvana.Cipher.Entities.Yggdrasil;
using Nirvana.Cipher.Yggdrasil;
using Nirvana.Common.Entities;
using Nirvana.Common.Entities.Login;
using Nirvana.WPFLauncher.Http;
using Nirvana.WPFLauncher.Protocol;
using Serilog;

namespace Nirvana.Cipher.Cipher.Nirvana.Connection;

public static class NetEaseConnection {
    // 认证失败 后通过 涅槃云 认证
    public static bool IsServerAuthenticated = true;

    /**
     * 最近一次认证失败的原因。
     * 用于把「到底为什么进不去」告知客户端（白端断连提示 / 开局断开包）。
     */
    public static string? LastFailureReason;

    public static void CreateAuthenticator(string serverId, string gameId, string gameVersion, string modInfo, EntityUserInfo userInfo, Action<bool> handle)
    {
        Task.Run(() => {
            try {
                CreateAuthenticatorAsync(serverId, gameId, gameVersion, modInfo, userInfo, handle).GetAwaiter().GetResult();
            } catch (Exception e) {
                // 兜底：任何异常都必须回调一次，否则调用方（代理/客户端认证）会一直等，
                // 表现就是白端卡在 “Logging in...”。
                Log.Error("[认证] 认证流程异常: {0}: {1}", e.GetType().Name, e.Message);
                handle.Invoke(false);
            }
        }).GetAwaiter().GetResult();
    }

    public static async Task CreateAuthenticatorAsync(string serverId, string gameId, string gameVersion, string modInfo, EntityUserInfo userInfo, Action<bool> handle)
    {
        var success = false;
        try {
            var pair = Md5Mapping.GetMd5FromGameVersion(gameVersion);
            success = await CreateAuthenticatorAsync(new GameProfile {
                GameId = gameId,
                GameVersion = gameVersion,
                BootstrapMd5 = pair.BootstrapMd5,
                DatFileMd5 = pair.DatFileMd5,
                Mods = JsonSerializer.Deserialize<ModList>(modInfo),
                User = new UserProfile {
                    User = userInfo
                }
            }, serverId);
        } catch (Exception e) {
            Log.Error("[认证] 构建认证信息失败: {0}: {1}", e.GetType().Name, e.Message);
        }

        handle.Invoke(success);
    }

    public static void CreateAuthenticator(GameProfile gameProfile, string serverId)
    {
        CreateAuthenticatorAsync(gameProfile, serverId).GetAwaiter().GetResult();
    }

    private static async Task<bool> CreateAuthenticatorAsync(GameProfile gameProfile, string serverId)
    {
        LastFailureReason = "网易认证失败，请尝试重新启动游戏客户端";

        Exception? exception;
        try {
            X19.GetCrcSalt();
            Log.Warning("[认证] 正在进入服务器: {0}", serverId);
            await StandardYggdrasil.JoinServerAsync(gameProfile, serverId);
            Log.Information("[认证] 进入服务器成功!");
            return true;
        } catch (Exception e) {
            exception = e;
            LastFailureReason = $"网易认证失败：{e.Message}";
            // 官方认证为什么失败必须打出来：CrcSalt 缺失（服务端未下发 crcSalt）
            // 或者认证服务器不可达，都会在这里被吞掉，只剩一个“认证失败”没法排查。
            Log.Error("[认证] 官方认证失败: {0}: {1}", exception.GetType().Name, exception.Message);
        }

        if (IsServerAuthenticated) {
            try {
                Log.Warning("[认证] 正在向涅槃服务器验证: {0}", serverId);
                var data = await X19Extensions.Nirvana.ApiAsync<EntityResponseBase>($"/api/fantnel/authenticated?id={serverId}", gameProfile);
                if (data == null) {
                    LastFailureReason = "网易认证失败：验证服务无响应";
                    Log.Error("[认证] 验证接口无响应，角色信息: {0}", JsonSerializer.Serialize(gameProfile));
                    Log.Error("[认证] 验证失败!");
                    return false;
                }

                if (data.Code == 1) {
                    Log.Information("[认证] 验证成功!");
                    return true;
                }

                LastFailureReason = $"网易认证失败：{data.Message}";
                Log.Information("[认证] 验证被拒: {0}", data.Message);
            } catch (Exception e) {
                LastFailureReason = "网易认证失败：验证服务不可用";
                // 回退接口本身异常（例如 /api/fantnel/authenticated 返回 500）不能向外抛，
                // 否则上层的 handle 不会被回调，客户端会一直卡在 “Logging in...”。
                Log.Error("[认证] 涅槃服务器验证异常: {0}: {1}", e.GetType().Name, e.Message);
            }
        }

        Log.Error("[认证] 验证异常，角色信息: {0}", JsonSerializer.Serialize(gameProfile));
        Log.Error("[认证] 验证失败: {0}", exception.Message);
        return false;
    }
}