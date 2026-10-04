using System;
using Nirvana.Cipher.Cipher.Nirvana.Connection;
using Nirvana.Common.Entities.Login;
using Nirvana.Common.Manager;
using Nirvana.Development;
using Nirvana.DevPlugin.Entities;
using Nirvana.Heypixel;
using Nirvana.Public.Message;
using Nirvana.WPFLauncher.Entities.WPFLauncher.NetGame;
using Nirvana.WPFLauncher.Entities.WPFLauncher.NetGame.GameCharacters;
using Nirvana.WPFLauncher.Entities.WPFLauncher.NetGame.GameDetails;
using Nirvana.WPFLauncher.Entities.WPFLauncher.RentalGame;
using Nirvana.WPFLauncher.Entities.WPFLauncher.RentalGame.GameCharacters;
using Serilog;

namespace Nirvana.Public.Manager;

public class InterceptorManager {
    private readonly EntityAccount _availableUser;

    private readonly string _entityId;
    private readonly string _mods;
    private readonly string _versionName;
    public readonly Interceptor Interceptor;

    static InterceptorManager()
    {
        HeypixelProtocol.Init();
    }

    public InterceptorManager(EntityQueryNetGameDetailItem server, EntityGameCharacter character, EntityMcVersion version, EntityNetGameServerAddress address, string mods, int port)
    {
        _mods = mods;
        _versionName = version.Name;
        _entityId = server.EntityId;
        _availableUser = InfoManager.GetGameAccount();
        // 创建代理
        Interceptor = Interceptor.CreateInterceptor(false, mods, server.EntityId, server.Name, version.Name, address.Host, address.Port, character.Name, _availableUser, YggdrasilCallback, port);
    }

    public InterceptorManager(EntityRentalGameDetails server, EntityRentalGamePlayerList character, string versionName, EntityRentalGameServerAddress address, string mods, int port)
    {
        _mods = mods;
        _versionName = versionName;
        _entityId = server.EntityId;
        _availableUser = InfoManager.GetGameAccount();
        // 创建代理
        Interceptor = Interceptor.CreateInterceptor(true, mods, server.EntityId, server.ServerName, versionName, address.McServerHost, address.McServerPort, character.Name, _availableUser, YggdrasilCallback, port);
    }

    private void YggdrasilCallback(InterceptorConfig config, string serverId)
    {
        NetEaseConnection.CreateAuthenticator(serverId, config.GameId, _versionName, _mods, _availableUser, success => {
            if (!success) {
                // 认证失败必须让玩家看得见：先下发带原因的断连包，
                // 客户端会弹出「无法连接至服务器」并显示这句话，而不是一直停在 Logging in...
                Interceptor.DisconnectClients(NetEaseConnection.LastFailureReason ?? "网易认证失败，请尝试重新启动游戏客户端");
                try {
                    AccountMessage.AutoUpdateAccount(_availableUser, () => { ActiveGameAndProxies.CloseProxy(Interceptor); });
                } catch (Exception e) {
                    Log.Error("认证失败: {0}: {1}", _availableUser.Account, e.Message);
                }
            }
        });
    }
}