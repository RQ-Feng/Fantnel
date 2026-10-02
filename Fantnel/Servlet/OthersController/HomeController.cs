using System.IO;
using Microsoft.AspNetCore.Mvc;
using Nirvana.Common.Manager;
using Nirvana.Common.Utils;
using Nirvana.Common.Utils.CodeTools;
using Serilog;

namespace Fantnel.Servlet.OthersController;

[ApiController]
[Route("[controller]")]
public class HomeController : ControllerBase {
    // 前端资源缺失只提醒一次，避免每个 404 都刷日志
    private static bool _missingWarned;
    // 设置主题
    [HttpGet("/api/theme/set")]
    public IActionResult SetTheme(string name)
    {
        ConfigUtil.SaveConfig("theme", name);
        return Ok(Code.ToJson(ErrorCode.Success));
    }

    // 获取主题
    [HttpGet("/api/theme")]
    public IActionResult GetTheme()
    {
        // 从配置中获取主题
        var theme = ConfigUtil.GetConfig("theme", "default");
        return Ok(Code.ToJson(ErrorCode.Success, theme));
    }

    // 获取首页信息
    [HttpGet("/api/home")]
    public IActionResult HomeInfo()
    {
        return Ok(InfoManager.FantnelInfo);
    }

    public static string GetIndexHtml()
    {
        // 获取运行目录路径
        var indexPath = Path.Combine(PathUtil.WebSitePath, "index.html");
        if (System.IO.File.Exists(indexPath)) {
            return System.IO.File.ReadAllText(indexPath);
        }

        // 前端资源缺失时不能返回空字符串：那会拼成 200 + 空 HTML，
        // 浏览器只看到白屏，且控制台没有任何报错，极难排查。
        if (!_missingWarned) {
            _missingWarned = true;
            Log.Warning("前端资源缺失，界面无法显示: {0}", indexPath);
            Log.Warning("修复: cd web && npm run build，再把 dist 复制到 {0}", PathUtil.WebSitePath);
            Log.Warning("或者用 dotnet build Fantnel/Fantnel.csproj -p:BuildWebUi=true 让构建顺便产出前端");
        }

        return BuildMissingPage(indexPath);
    }

    // 前端资源缺失时的诊断页
    private static string BuildMissingPage(string indexPath)
    {
        var dirExists = Directory.Exists(PathUtil.WebSitePath) ? "是" : "否";
        return $$"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
              <meta charset="utf-8">
              <title>Fantnel - 前端资源缺失</title>
              <style>
                body { margin: 0; padding: 40px; background: #1e1e1e; color: #ddd;
                       font-family: "Segoe UI", "Microsoft YaHei", sans-serif; line-height: 1.7; }
                h1 { color: #e57373; font-size: 22px; margin: 0 0 4px; }
                h2 { font-size: 16px; margin: 28px 0 8px; color: #ccc; }
                p { margin: 6px 0; }
                code { background: #2d2d2d; padding: 2px 6px; border-radius: 4px;
                       color: #9cdcfe; font-family: Consolas, monospace; }
                pre { background: #252526; border: 1px solid #3c3c3c; border-radius: 6px;
                      padding: 12px 14px; overflow-x: auto; color: #ce9178;
                      font-family: Consolas, monospace; font-size: 13px; }
                table { border-collapse: collapse; margin-top: 8px; }
                td, th { border: 1px solid #3c3c3c; padding: 6px 12px; text-align: left; }
                th { background: #2d2d2d; color: #bbb; font-weight: 500; }
                .hint { color: #888; font-size: 13px; }
              </style>
            </head>
            <body>
              <h1>前端资源缺失，界面无法显示</h1>
              <p>服务已启动，但没找到前端入口文件 <code>index.html</code>。</p>
              <table>
                <tr><th>期望路径</th><td><code>{{indexPath}}</code></td></tr>
                <tr><th>静态目录存在</th><td>{{dirExists}}</td></tr>
              </table>
              <h2>修复方式</h2>
              <pre>cd web
            npm run build
            robocopy dist "..\Fantnel\bin\Debug\net10.0\resources\static" /E</pre>
              <p>或让构建顺便产出前端（需要 Node.js）：</p>
              <pre>dotnet build Fantnel\Fantnel.csproj -p:BuildWebUi=true</pre>
              <p class="hint">注意：robocopy 不要加 /MIR，否则会删掉 resources/static/image 等远端资源。</p>
            </body>
            </html>
            """;
    }
}