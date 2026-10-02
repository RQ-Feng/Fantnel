using System.Threading.Tasks;
using Nirvana.WPFLauncher.Entities.WPFLauncher;
using Nirvana.WPFLauncher.Http;
using Serilog;

namespace Nirvana.WPFLauncher.Protocol;

public static class InterConn {
    private static async Task LoginStart()
    {
        Log.Debug("登录开始(login-start) 响应: {0}", await X19Extensions.Core1.ApiAsync<string>("/interconn/web/game-play-v2/login-start", "{\"strict_mode\":true}"));
    }

    private static async Task GameStart(string gameId)
    {
        Log.Debug("游戏开始(start) 响应: {0}", await X19Extensions.Core1.ApiAsync<string>("/interconn/web/game-play-v2/start", new InterConnGameStart {
            GameId = gameId,
            ItemList = ["10000"]
        }));
    }

    public static async Task LoginStartAndGameStart(string gameId)
    {
        await LoginStart();
        await GameStart(gameId);
    }
}