using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Nirvana.Common;
using Nirvana.Common.Manager;
using Nirvana.Common.Utils;
using Nirvana.Public.Entities.Update;
using Serilog;

namespace Nirvana.Public.Utils.Update;

public static class UpdateTools {
    
    // 检查更新
    public static void CheckUpdate(string[] args)
    {
        CheckUpdateAsync(args).GetAwaiter().GetResult();
    }

    // 检查更新
    private static async Task CheckUpdateAsync(string[] args)
    {
        if (InfoManager.FantnelInfo == null) {
            Log.Warning("无法连接至服务器，已跳过更新检查！");
            return;
        }

        if (!PublicProgram.UpdateVersion.Equals(InfoManager.FantnelInfo.UpdateVersions)) {
            Log.Warning("更新器版本不匹配（本地 {0}，服务器 {1}），已跳过更新检查！",
                PublicProgram.UpdateVersion, InfoManager.FantnelInfo.UpdateVersions);
            return;
        }

        // --- Fantnel ---
        var update = 0; // 0:正常检查 1:不检查 2:已被检查
        if (args.Any(arg => arg == "--update_false")) {
            update = 2;
        }

        // 正常检查
        if (update == 0 && PublicProgram.Release) {
            await new EntityUpdate {
                Mode = PathUtil.SystemArch,
                Name = "Fantnel",
                SafeMode = true,
                Command = ""
            }.CheckUpdateSafe();
        }

        // --- Fantnel UI ---
        // 前端 UI 随程序发布（resources/static），不再从远端 ui.* 包更新

        // --- Static ---
        update = 0; // 0:正常检查 1:不检查 2:已被检查
        if (args.Any(arg => arg == "--update_static_false")) {
            update = 2;
        }

        if (update == 0) {
            await new EntityUpdate {
                Mode = "static"
            }.CheckUpdateSafe();
        }

        // --- Static System ---
        update = 0; // 0:正常检查 1:不检查 2:已被检查
        if (args.Any(arg => arg == "--update_static_system_false")) {
            update = 2;
        }

        if (update == 0) {
            await new EntityUpdate {
                Mode = "static." + PathUtil.DetectOperating,
                Name = "Resource System"
            }.CheckUpdateSafe();
        }

        // --- Static Linux System ---
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            update = 0; // 0:正常检查 1:不检查 2:已被检查
            if (args.Any(arg => arg == "--update_static_linux_system_false")) {
                update = 2;
            }

            if (update == 0) {
                await new EntityUpdate {
                    Mode = "static." + PathUtil.SystemArch,
                    Name = "Resource Linux"
                }.CheckUpdateSafe();
            }
        }
    }
}