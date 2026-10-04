using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nirvana.Cipher.Cipher.Nirvana.Connection;
using Nirvana.Cipher.Yggdrasil;
using Nirvana.Common;
using Nirvana.Common.Entities;
using Nirvana.Common.Manager;
using Nirvana.Common.Utils;
using Nirvana.Public.Manager;
using Nirvana.Public.Message;
using Nirvana.Public.Utils.Update;
using Nirvana.WPFLauncher.Http;
using Nirvana.WPFLauncher.Protocol;
using Serilog;

namespace Nirvana.Public.Utils;

public static class InitProgram {
    public static void CheckUpdate(string[] args, Action logInit)
    {
        // 日志初始化
        logInit.Invoke();

        // 检查更新
        Log.Information("{0}", PathUtil.ResourcePath);
        UpdateTools.CheckUpdate(args);

        // 重置日志
        logInit.Invoke();
    }

    /**
     * 核心 初始化
     */
    public static void NelInit1(string[] args)
    {
        // 插件初始化
        // 避免插件过早加载，因为这是没必要的
        // await InitializeSystemComponentsAsync();

        // 版本安全检测
        VersionCheck();

        // 创建服务
        CreateServices(args);
        Log.Information("------  完成 ------");

        // 配置初始化
        NirvanaConfig.Initialization();

        // 默认登录
        AccountMessage.GetAccountList();

        // 插件管理器初始化
        PluginMessage.Initialize();

        for (var i = 0; i < 4 && !PublicProgram.LatestVersion; i++) {
            Log.Warning("当前版本不是最新版本，建议更新至最新版本，以获得更好的体验！");
        }

        // 在线检测
        Online();

        // 缓存 服务器/租凭服/皮肤 信息/图片
        _ = Task.Run(() => {
            try {
                Thread.Sleep(1000);
                InfoManager.GetToken(); // 检查是否有游戏账号
                CacheManager.CacheServer();
            } catch (Exception) {
                // ignored
            }
        });

        // 提前获取验证服务器
        _ = Task.Run(() => { _ = StandardYggdrasil.InitializationAsync(); });

        foreach (var arg in args) {
            if ("--authenticated_false".Equals(arg)) {
                NetEaseConnection.IsServerAuthenticated = false;
                break;
            }
        }
    }

    /**
     * 版本安全检测
    */
    private static void VersionCheck()
    {
        // 未获取到服务器信息：不阻塞启动，仅告警
        // ★ 注意：前面的 logInit 会清屏，所以这条告警必须放在 NelInit1（最后一次清屏之后）才看得见
        if (InfoManager.FantnelInfo == null) {
            Log.Warning("无法连接至涅槃服务器，已跳过版本检测与更新检查，程序将以离线模式继续运行。");
            return;
        }

        // 检查是否为发布版本
        if (!PublicProgram.Release) {
            Log.Error("调试版，已跳过版本检测！");
            return;
        }

        var versions = InfoManager.FantnelInfo.Versions;
        if (versions == null || versions.Length == 0) {
            Log.Warning("检测版本失败（服务器未返回版本列表），已跳过版本检测！");
            return;
        }

        // 检查是否为最新版本
        if (versions.Last().Equals(PublicProgram.Version)) {
            return;
        }

        PublicProgram.LatestVersion = true;
    }

    // Fantnel 在线检测
    private static async void Online()
    {
        try {
            while (true)
                try {
                    // 60 * 3 = 180 秒 (3分钟)
                    for (var i = 0; i < 180; i++) {
                        await Task.Delay(1000);
                    }

                    await X19Extensions.Nirvana.ApiAsync<EntityResponse<string>>("/api/tick?mode=fantnel", new Dictionary<string, string> {
                        { "system", PublicProgram.Mode },
                        { "arch", PublicProgram.Arch },
                        { "version", PublicProgram.Version },
                        { "versionId", PublicProgram.VersionId.ToString() }
                    });
                } catch (Exception e) {
                    Log.Warning(" 在线检测异常! 错误信息: {0}", e.Message);
                }
        } catch (Exception e) {
            Log.Warning(" 在线检测出错! 错误信息: {0}", e.Message);
        }
    }

    public static void FantnelInit()
    {
        FantnelInitAsync().GetAwaiter().GetResult();
    }

    private static async Task FantnelInitAsync()
    {
        for (var i = 0; i < 3; i++) {
            try {
                var entity = await X19Extensions.Nirvana.ApiAsync<EntityInfo>("/fantnel.json");
                if (entity != null) {
                    InfoManager.FantnelInfo = entity;
                    return;
                }
            } catch (Exception e) {
                Log.Warning("连接服务器失败! 错误信息: {0}", e.Message);
            }
        }

        Log.Warning("连接服务器失败，已跳过服务器信息/版本/更新检查，程序将继续以离线模式运行。");
    }

    // 创建服务
    private static void CreateServices(string[] args)
    {
        X19.CrcSalt = RestartTools.Get("crc_salt", args);
        if (string.IsNullOrEmpty(X19.CrcSalt) && InfoManager.FantnelInfo != null) {
            X19.CrcSalt = InfoManager.FantnelInfo.CrcSalt;
        }

        if (X19.CrcSalt is { Length: > 6 }) {
            Log.Information("CRC Salt 计算完成: {0}....", X19.CrcSalt[..6]);
        } else {
            // CrcSalt 拿不到时，官方（网易）认证会在第一步就抛 CrcSaltNotSet，
            // 白端/开局都登不进网络服务器，必须显式告警。
            Log.Warning("未获取到 CrcSalt（服务端 fantnel.json 未下发 crcSalt，且未传入 --crc_salt），官方认证将不可用！");
        }

    }
}