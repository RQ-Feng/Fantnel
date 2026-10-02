using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Nirvana.Common;
using Nirvana.Common.Entities;
using Nirvana.Common.Entities.Login;
using Nirvana.Common.Manager;
using Nirvana.Common.Utils;
using Nirvana.Common.Utils.CodeTools;
using Nirvana.Public.Entities.Nirvana;
using Nirvana.Public.Manager;
using Nirvana.WPFLauncher.Entities.MPay;
using Nirvana.WPFLauncher.Entities.WPFLauncher;
using Nirvana.WPFLauncher.Entities.WPFLauncher.Login;
using Nirvana.WPFLauncher.Http;
using Nirvana.WPFLauncher.Protocol;
using Serilog;

namespace Nirvana.Public.Message;

public static class AccountMessage {
    // 保存/修改 游戏账号锁
    private static readonly Lock GameSaveAccountLock = new();

    // 登录游戏 锁
    private static readonly Lock LoginLock = new();

    // 默认 自动登录 已执行完成
    private static readonly List<EntityAccount> IsDefaultLogin = [];

    private static string? _session4399Id; // 验证ID
    public static string? Captcha4399; // 验证内容
    public static byte[]? Captcha4399Bytes; // 验证码图片

    /**
     * Session 4399
     */
    public static void UpdateCaptcha()
    {
        lock (GameSaveAccountLock) {
            var captchaId = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            Captcha4399Bytes = X19Extensions.Pt4399.ApiRawB("/ptlogin/captcha.do?captchaId=" + captchaId);
            _session4399Id = captchaId;
        }
    }

    /**
     * 根据 账号Id 获取账号
     * @param id 账号Id
     * @return 账号实体
     */
    private static EntityAccount GetAccount(int id, bool safeUserId = true)
    {
        var entity = GetAccountList(safeUserId);
        foreach (var item in entity) {
            if (item.Id == id) {
                return item;
            }
        }

        throw new ErrorCodeException(ErrorCode.NotFound);
    }

    /**
     * 切换账号 [安全]
     * @param id 账号Id
     */
    public static void SwitchAccount(int id)
    {
        var account = GetAccount(id);
        foreach (var gameAccount in InfoManager.GameAccountList.Where(gameAccount => gameAccount.Equals(account))) {
            InfoManager.SetGameAccount(gameAccount);
            break;
        }
    }

    // 强制切换账号
    public static void SwitchAccountToForce(int id)
    {
        InfoManager.SetGameAccount(GetAccount(id, false));
    }

    // 禁止默认登录
    public static void DisableDefaultLogin()
    {
        foreach (var gameAccount in GetAccountList1(false).Item1) {
            IsDefaultLogin.Add(gameAccount);
        }
    }

    /**
     * 获取登录成功后的账号列表
     * @return 账号实体数组
     */
    public static EntityAccount[] GetLoginAccountList()
    {
        var accountList = GetAccountList();
        return accountList.Where(account => InfoManager.GameAccountList.Any(gameAccount => gameAccount.Equals(account))).ToArray();
    }

    /**
     * 获取所有账号列表
     * @return 账号实体数组
     */
    public static EntityAccount[] GetAccountList(bool safeUserId = true)
    {
        return GetAccountList1(safeUserId: safeUserId).Item1;
    }

    /**
     * 获取所有账号列表 和 账号文件路径
     * @return 账号实体数组 和 账号文件路径
     */
    private static (EntityAccount[], string) GetAccountList1(bool defaultLogin = true, bool safeUserId = true)
    {
        var (entity, path) = Tools.GetValueOrDefaultList<EntityAccount>("account.json");

        // 给 账号 赋值 Id
        var index = -1;
        foreach (var item in entity) {
            index++;
            item.Id = index;
            // 登录成功 同步 UserId, Token
            foreach (var gameAccount in InfoManager.GameAccountList.Where(gameAccount => gameAccount.Equals(item))) {
                item.UserId = gameAccount.UserId;
                item.Token = gameAccount.Token;
                break;
            }
        }

        if (defaultLogin) {
            DefaultLogin(entity);
        }

        // 避免因配置加载的账号导致显示 UserId
        if (safeUserId) {
            foreach (var item in entity) {
                var flag = InfoManager.GameAccountList.Any(gameAccount => gameAccount.Equals(item));
                if (!flag) {
                    item.UserId = null;
                    item.Token = null;
                }
            }
        }

        return (entity, path);
    }

    // 登录游戏账号
    public static void Login(int id)
    {
        Login(GetAccount(id));
    }

    // 登录游戏账号
    private static void Login(EntityAccount account)
    {
        if (account.Password == null) {
            throw new ErrorCodeException(ErrorCode.PasswordError);
        }

        lock (LoginLock) {
            EntityAuthenticationOtp? result = null; // 登录结果

            switch (account.Type) {
                case "cookie":
                    result = NPFLauncher.LoginWithCookie(account.Password);
                    break;
                case "4399" or "4399com" or "163Email" when account.Account == null:
                    throw new ErrorCodeException(ErrorCode.AccountError);
                case "4399" or "4399com" when _session4399Id == null || Captcha4399 == null:
                    throw new ErrorCodeException(ErrorCode.CaptchaNot);
                case "4399": {
                    var cookie = N4399.LoginWithPasswordAsync(account.Account, account.Password, _session4399Id, Captcha4399);
                    result = NPFLauncher.LoginWithCookie(cookie.GetAwaiter().GetResult());
                    UpdateCaptcha();
                    break;
                }
                case "4399com": {
                    var cookie = NCom4399.LoginWithPassword(account.Account, account.Password, Captcha4399, _session4399Id);
                    result = NPFLauncher.LoginWithCookie(cookie);
                    UpdateCaptcha();
                    break;
                }
                case "163Email": {
                    var mpay = new MPay();
                    var mPayUser = mpay.LoginWithEmail(account.Account, account.Password);
                    var cookie = GenerateCookie(mPayUser, mpay.GetDevice());
                    result = NPFLauncher.LoginWithCookie(cookie);
                    break;
                }
            }

            // 登录完成
            if (result == null || result.EntityId.Length < 1) {
                throw new ErrorCodeException(ErrorCode.LoginError);
            }

            account.UserId = result.EntityId;
            account.Token = result.Token;
            InfoManager.AddAccount(account);
        }

        // 登录成功后 保存账号
        SaveAccount();
        CacheManager.CacheServer();

        // 登录成功 → 启动续登心跳
        StartKeepAlive();
    }

    private static string GenerateCookie(EntityMPayUserResponse user, EntityDevice device)
    {
        return JsonSerializer.Serialize(new EntityX19Cookie {
            SdkUid = user.User.Id,
            SessionId = user.User.Token,
            Udid = Guid.NewGuid().ToString("N").ToUpper(),
            DeviceId = device.Id,
            AimInfo = "{\"aim\":\"127.0.0.1\",\"country\":\"CN\",\"tz\":\"+0800\",\"tzid\":\"\"}"
        }, NPFLauncher.DefaultOptions);
    }

    // 保存账号到文件
    public static void UpdateAccount(EntityAccount account, bool defaultLogin = true)
    {
        lock (GameSaveAccountLock) {
            // 获取账号列表
            var (accountList, accountPath) = GetAccountList1(defaultLogin);

            if (account.Id == null) {
                throw new ErrorCodeException(ErrorCode.IdError);
            }

            // 修改账号
            accountList[account.Id.Value] = account;

            // 写入文件
            File.WriteAllText(accountPath, JsonSerializer.Serialize(accountList), Encoding.UTF8);
        }

        // 自动登录账号
        AutoLogin(account);
    }

    // 保存账号到文件
    public static void SaveAccount(EntityAccount account)
    {
        lock (GameSaveAccountLock) {
            // 获取账号列表
            var (accountList, accountPath) = GetAccountList1();

            // cookie 默认 假账号
            if (account.Account == null && account.Type == "cookie") {
                // 10 位时间戳
                account.Account = GetSuffix("Co");
            }

            // 增加账号
            accountList = accountList.Append(account).ToArray();

            // 写入文件
            File.WriteAllText(accountPath, JsonSerializer.Serialize(accountList), Encoding.UTF8);
        }

        // 自动登录账号
        AutoLogin(account);
    }

    // 保存账号到文件
    private static void SaveAccount()
    {
        lock (GameSaveAccountLock) {
            // 获取账号列表
            var (accountList, accountPath) = GetAccountList1();
            // 写入文件
            File.WriteAllText(accountPath, JsonSerializer.Serialize(accountList), Encoding.UTF8);
        }
    }

    // 自动登录账号
    private static void AutoLogin(EntityAccount account, bool useConfig = false)
    {
        try {
            // 检查是否已登录过
            var disabled = IsDefaultLogin.Any(defaultLogin => defaultLogin.Equals(account));
            if (disabled) {
                return;
            }

            IsDefaultLogin.Add(account);

            Exception? exception = null;
            var success = false;

            if (account is { UserId: not null, Token: not null }) {
                try {
                    if (AutoUpdateAccount(account)) {
                        success = true;
                    }
                } catch (Exception e) {
                    exception = e;
                }
            }

            if (!success && account.Type is "cookie" or "163Email") {
                var isAutoLogin = true;
                if (useConfig) {
                    isAutoLogin = account.IsConfig();
                }

                if (isAutoLogin) {
                    Login(account);
                    success = true;
                    exception = null;
                }
            }

            if (!success && exception != null) {
                throw exception;
            }
        } catch (Exception e) {
            Log.Error("自动登录失败: {0}: {1}", account.Id, e.Message);
        }
    }

    // 真正的自动登录
    public static void AutoLogin1(EntityAccount account)
    {
        if ("4399".Equals(account.Type) || "4399com".Equals(account.Type)) {
            UpdateCaptcha();
            Captcha4399 = GetCaptcha4399Content();
        }

        Login(account);
    }

    public static void AutoSwitchAccount(EntityAccount account)
    {
        foreach (var gameAccount in InfoManager.GameAccountList.Where(gameAccount => gameAccount.Equals(account))) {
            InfoManager.SetGameAccount(gameAccount);
            return;
        }

        AutoLogin1(account);
    }

    public static bool AutoUpdateAccount(EntityAccount account, Action? onFailure = null)
    {
        InfoManager.SetGameAccount(account);
        Exception? exception = null;

        try {
            ProbeAccount();
            // 登录成功
            InfoManager.AddAccount(account);
            StartKeepAlive();
            return true;
        } catch (Exception e) {
            exception = e;
        }

        // 业务请求失败最常见的原因是 token 过期：先续期一次再判定失效。
        // 原来没有这一步，偶发一次失败就把 UserId/Token 清空并落盘（表现为「过一段时间就退登」）。
        if (RefreshToken(account)) {
            try {
                ProbeAccount();
                InfoManager.AddAccount(account);
                return true;
            } catch (Exception) {
                // 续期后仍然失败 → 继续走下面的清理逻辑
            }
        }

        InfoManager.SetGameAccount(null);
        account.UserId = null;
        account.Token = null;
        UpdateAccount(account, false);

        onFailure?.Invoke();
        return exception == null ? false : throw exception;
    }

    // 用一个业务请求探测登录态
    // 该接口在 code != 0（未登录）时会抛异常，所以「不抛异常」即登录态有效；
    // 不看返回条数：账号没有免费皮肤时返回空数组，以前会被误判成未登录。
    private static void ProbeAccount()
    {
        NPFLauncher.GetFreeSkinListAsync(0, 1).GetAwaiter().GetResult();
    }

    // ================= 续登（登录态续期） =================
    //
    // 官方启动器（WPFLauncher）的做法：登录成功后会注册 LOGIN_SUCCESSFUL_LONG_TIME 事件，
    // 之后每 30 分钟 POST /authentication/update，body 是 {"entity_id": <user_id>}，
    // 返回体里的 token 就是新的 token，客户端写回并持久化（同时广播 UPDATE_USER_TOKEN）。
    //
    // Fantnel 原来没有任何续期动作，只在业务请求失败时清空 UserId/Token，
    // 所以 token 一到期就表现为：列表页能加载（不需要登录态），一进具体游戏就「没有登录」，
    // 代理也会因为认证失败而失效。

    // 续登周期
    // 官方启动器是 30 分钟，但实测本机 token 寿命只有约 20 分钟：
    //   2026-10-02 22:34 登录 → 22:55 就被判「原因10: 请先登录」
    // 30 分钟会在第一次续期之前就过期，所以默认取 10 分钟（约 2 倍余量）。
    // 调试可用命令行参数覆盖：--refresh_interval <分钟>
    private static readonly TimeSpan RefreshInterval = GetRefreshInterval();

    private static TimeSpan GetRefreshInterval()
    {
        const int defaultMinutes = 10;
        try {
            var minutes = Nirvana.Public.Utils.RestartTools.Get("refresh_interval", Environment.GetCommandLineArgs(), defaultMinutes);
            return TimeSpan.FromMinutes(minutes > 0 ? minutes : defaultMinutes);
        } catch (Exception) {
            return TimeSpan.FromMinutes(defaultMinutes);
        }
    }

    // 单次续登的重试次数（官方是 5 次）
    private const int RefreshRetry = 3;

    private static readonly Lock RefreshLock = new();
    private static bool _keepAliveStarted;

    // 启动续登心跳（登录成功后调用；重复调用无副作用）
    public static void StartKeepAlive()
    {
        lock (RefreshLock) {
            if (_keepAliveStarted) {
                return;
            }

            _keepAliveStarted = true;
        }

        Log.Information("续登心跳已启动：每 {0} 分钟续期一次", RefreshInterval.TotalMinutes);
        _ = Task.Run(async () => {
            while (true) {
                await Task.Delay(RefreshInterval);
                try {
                    RefreshAllTokens();
                } catch (Exception e) {
                    Log.Warning("续登出错: {0}", e.Message);
                }
            }
        });
    }

    // 给所有已登录账号续期
    public static void RefreshAllTokens()
    {
        foreach (var account in InfoManager.GameAccountList.ToArray()) {
            if (account is not { UserId: not null, Token: not null }) {
                continue;
            }

            if (RefreshToken(account)) {
                Log.Information("续登成功: {0}", account.Account ?? account.Name);
            }

            // 多账号时错开，避免打太高并发
            Thread.Sleep(300);
        }
    }

    // 单个账号续期：POST /authentication/update {"entity_id": <userId>}
    // 成功时把返回的新 token 写回账号并落盘
    //
    // 注意（从官方启动器 aeo.a 反推）：该接口的加密档位是 aeq.c ——
    // **请求体必须加密**后作为裸参数发送（和 /authentication-otp 完全一样），
    // 同时带上该账号自己的 user-id / user-token 签名头（官方 IsAuthentication=true 时都会加）。
    // 直接发明文 JSON 会被服务端丢掉（表现为返回 null）。
    public static bool RefreshToken(EntityAccount account)
    {
        if (account is not { UserId: not null, Token: not null }) {
            return false;
        }

        var json = JsonSerializer.Serialize(new EntityRefreshRequest { EntityId = account.UserId }, NPFLauncher.DefaultOptions);

        for (var i = 0; i < RefreshRetry; i++) {
            try {
                var response = X19Extensions.Core1.HttpWrapper
                    .PostAsync("/authentication/update", HttpUtil.HttpEncrypt(Encoding.UTF8.GetBytes(json)), "application/octet-stream", options => {
                        // 签名是对**明文**请求体算的（官方 ss.e(Resource, Body) 用的是加密前的 body）
                        options.AddHeaders(Nirvana.WPFLauncher.Utils.TokenUtil.Compute("/authentication/update", json, account.UserId, account.Token));
                    })
                    .GetAwaiter().GetResult();

                var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                var decrypted = HttpUtil.HttpDecrypt(body);
                var text = decrypted == null ? null : Encoding.UTF8.GetString(decrypted);

                // 只取 entity.token：这个接口的其它字段类型不稳定。
                // 实测 entity.aid 返回的是**数字**，而 EntityAuthenticationOtp.Aid 是 string，
                // 套严格实体直接反序列化失败 → 会把服务端已经发出来的新 token 丢掉，
                // 而旧 token 服务端已经轮换作废，下一次重试就变成 code 22「帐号在另一处登录」。
                var node = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
                var token = node?["entity"]?["token"]?.GetValue<string>();

                if (!string.IsNullOrEmpty(token)) {
                    account.Token = token;
                    SaveAccount(); // 持久化到 account.json
                    return true;
                }

                // code 10 / 22 = 会话失效 / 账号在另一处登录（官方也是这两个码）
                Log.Warning("续登被拒: code={0}, message={1}, 响应={2}", node?["code"], node?["message"],
                    string.IsNullOrEmpty(text) ? "(空)" : text[..Math.Min(300, text.Length)]);
            } catch (Exception e) {
                Log.Warning("续登失败({0}/{1}): {2}", i + 1, RefreshRetry, e.Message);
            }

            Thread.Sleep(1000);
        }

        return false;
    }

    // 续登请求体 {"entity_id":"..."}
    private class EntityRefreshRequest {
        [JsonPropertyName("entity_id")]
        public required string EntityId { get; set; }
    }

    // 删除账号到文件
    public static void DeleteAccount(int id)
    {
        lock (GameSaveAccountLock) {
            // 获取账号列表
            var (accountList, accountPath) = GetAccountList1();

            // 删除账号
            accountList[id] = null!;

            // 写入文件
            File.WriteAllText(accountPath, JsonSerializer.Serialize(accountList), Encoding.UTF8);
        }
    }

    // 全自动执行默认登录
    private static void DefaultLogin(EntityAccount[] entity)
    {
        // 默认登录
        foreach (var item in entity) {
            AutoLogin(item, true);
        }
    }

    /**
     * 获取4399验证码内容
     * @return 4399验证码内容
     */
    public static string GetCaptcha4399Content()
    {
        return GetCaptcha4399ContentAsync().GetAwaiter().GetResult();
    }

    /**
     * 获取4399验证码内容
     * @return 4399验证码内容
     */
    private static async Task<string> GetCaptcha4399ContentAsync()
    {
        if (Captcha4399Bytes == null) {
            throw new ErrorCodeException(ErrorCode.Failure);
        }

        var response = await X19Extensions.Nirvana.ApiBytes<EntityResponse<string>>("/api/fantnel/captcha", Captcha4399Bytes);
        return response?.Data ?? throw new ErrorCodeException(ErrorCode.Failure);
    }

    private static string GetSuffix(string prefix)
    {
        var date = DateTimeOffset.Now;
        var md = date.ToString("MMdd");
        var accountList = GetAccountList();
        for (var i = 0; i < 1024; i++) {
            var context = prefix + md + "x" + i;
            if (accountList.Any(entityAccount => context.Equals(entityAccount.Name))) {
                continue;
            }

            return context;
        }

        return date.ToUnixTimeSeconds().ToString();
    }
}