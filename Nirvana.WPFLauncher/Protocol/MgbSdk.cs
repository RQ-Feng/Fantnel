using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Nirvana.WPFLauncher.Entities.Pc4399;
using Nirvana.WPFLauncher.Http;
using Nirvana.WPFLauncher.Utils;

namespace Nirvana.WPFLauncher.Protocol;

public class MgbSdk(string gameId) : IDisposable {
    private readonly HttpWrapper _sdk = new("https://mgbsdk.matrix.netease.com");

    public void Dispose()
    {
        _sdk.Dispose();
        GC.SuppressFinalize(this);
    }

    public static string GenerateSAuth(string sdkUid, string sessionId, string channel, string platform, string userId = "", string timestamp = "")
    {
        var str = Guid.NewGuid().ToString("N");
        return JsonSerializer.Serialize(new EntityMgbSdkSAuthJson {
            AppChannel = channel,
            ClientLoginSn = str,
            DeviceId = str,
            GameId = "x19",
            LoginChannel = channel,
            SdkUid = sdkUid,
            SessionId = sessionId,
            Timestamp = timestamp,
            Platform = platform,
            SourcePlatform = platform,
            Udid = str,
            UserId = userId
        });
    }

    public async Task AuthSession(string cookie)
    {
        var httpResponseMessage = await _sdk.PostAsync($"/{gameId}/sdk/uni_sauth", cookie);
        var responseText = await httpResponseMessage.Content.ReadAsStringAsync();
        var dictionary = JsonSafe.Deserialize<Dictionary<string, object>>(responseText, $"MGB SDK /{gameId}/sdk/uni_sauth", httpResponseMessage);
        if (dictionary == null) {
            throw new HttpRequestException($"MGB SDK 返回内容为空：{JsonSafe.Preview(responseText)}");
        }

        if (dictionary.TryGetValue("code", out var code) && "200".Equals(code?.ToString())) {
            return;
        }

        if (dictionary.TryGetValue("msg", out var msg)) {
            throw new HttpRequestException(msg?.ToString() ?? "MGB SDK 登录失败");
        }

        throw new HttpRequestException(dictionary.TryGetValue("status", out var status)
            ? status?.ToString() ?? "MGB SDK 登录失败"
            : $"MGB SDK 未知响应：{JsonSafe.Preview(responseText)}");
    }
}