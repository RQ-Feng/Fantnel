using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Nirvana.Common.Utils;
using Nirvana.Common.Utils.Progress;
using Nirvana.Game.Launcher.Entities;
using Serilog;

namespace Nirvana.Game.Launcher.Services.Bedrock;

public sealed class LauncherService : IDisposable {
    private readonly object _disposeLock = new();
    private readonly IProgress<EntityProgressUpdate> _progress;

    private volatile bool _disposed;

    private Process? _gameProcess;

    private LauncherService(EntityLaunchPeGame entityLaunchGame)
    {
        _progress = new Progress<EntityProgressUpdate>();
        Entity = entityLaunchGame ?? throw new ArgumentNullException(nameof(entityLaunchGame));
        LastProgress = new EntityProgressUpdate {
            Id = Identifier,
            Percent = 0,
            Message = "Initialized"
        };
    }

    private Guid Identifier { get; } = Guid.NewGuid();

    public EntityLaunchPeGame Entity { get; }

    public EntityProgressUpdate LastProgress { get; private set; }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        lock (_disposeLock) {
            if (_disposed) {
                return;
            }

            _disposed = true;
        }

        try {
            if (_gameProcess != null) {
                _gameProcess.Exited -= OnGameProcessExited;
                if (!_gameProcess.HasExited) {
                    _gameProcess.CloseMainWindow();
                    if (!_gameProcess.WaitForExit(5000)) {
                        _gameProcess.Kill();
                    }
                }

                _gameProcess.Dispose();
                _gameProcess = null;
            }
        } catch (Exception ex) {
            Log.Warning(ex, "释放游戏进程时出错，游戏: {0}", Entity.GameId);
        }
    }

    public event Action<Guid>? Exited;

    private async Task LaunchGameAsync()
    {
        try {
            if (_disposed) return;
            await DownloadGameResourcesAsync().ConfigureAwait(false);
            if (!_disposed) {
                var port = await LaunchProxyAsync().ConfigureAwait(false);
                if (!_disposed) {
                    await StartGameProcessAsync(port).ConfigureAwait(false);
                }
            }
        } catch (OperationCanceledException) {
            UpdateProgress(100, "启动已取消");
        } catch (Exception ex2) {
            Log.Error(ex2, "启动游戏失败，游戏: {0}", Entity.GameId);
            UpdateProgress(100, "启动失败");
        }
    }

    private async Task DownloadGameResourcesAsync()
    {
        UpdateProgress(5, "正在安装游戏资源");
        if (!await InstallerService.DownloadMinecraftAsync().ConfigureAwait(false)) {
            throw new InvalidOperationException("下载 Minecraft 资源失败");
        }
    }

    private Task<int> LaunchProxyAsync()
    {
        UpdateProgress(60, "正在启动代理");
        var availablePort = Tools.GetUnusedPort();
        return Task.FromResult(availablePort);
    }

    private Task StartGameProcessAsync(int port)
    {
        UpdateProgress(70, "正在启动游戏进程");
        var launchPath = GetLaunchPath();
        ValidateLaunchPath(launchPath);
        ConfigService.GenerateLaunchConfig(Entity.SkinPath, Entity.RoleName, Entity.GameId, port);
        var argumentsPath = Path.Combine(PathUtil.CppGamePath, "launch.cppconfig");
        var process = CommandService.StartGame(launchPath, argumentsPath);
        if (process == null) {
            Log.Error("[启动] 游戏启动失败. 启动类型: {0}, 角色: {1}", Entity.LaunchType, Entity.RoleName);
            throw new InvalidOperationException("启动游戏进程失败");
        }

        SetupGameProcess(process);
        UpdateProgress(100, "运行中");
        Log.Information("[启动] 游戏启动成功. 启动类型: {0}, 进程 ID: {1}, 角色: {2}", Entity.LaunchType, process.Id, Entity.RoleName);
        return Task.CompletedTask;
    }

    private string GetLaunchPath()
    {
        if (Entity.LaunchType == EnumLaunchType.Custom && !string.IsNullOrEmpty(Entity.LaunchPath))
            return Path.Combine(Entity.LaunchPath, "windowsmc", "Minecraft.Windows.exe");
        return Path.Combine(PathUtil.CppGamePath, "windowsmc", "Minecraft.Windows.exe");
    }

    private static void ValidateLaunchPath(string launchPath)
    {
        if (!File.Exists(launchPath))
            throw new FileNotFoundException("找不到可执行文件: " + launchPath, launchPath);
    }

    private void SetupGameProcess(Process process)
    {
        _gameProcess = process;
        _gameProcess.EnableRaisingEvents = true;
        _gameProcess.Exited += OnGameProcessExited;
    }

    private void OnGameProcessExited(object? sender, EventArgs e)
    {
        try {
            Exited?.Invoke(Identifier);
        } catch (Exception ex) {
            Log.Warning(ex, "游戏进程退出处理出错，游戏: {0}", Entity.GameId);
        }
    }

    private void UpdateProgress(int percent, string message)
    {
        if (_disposed) {
            return;
        }

        var value = LastProgress = new EntityProgressUpdate {
            Id = Identifier,
            Percent = percent,
            Message = message
        };
        try {
            _progress.Report(value);
            if (percent == 100) {
                SyncProgressBarUtil.ProgressBar.ClearCurrent();
            }
        } catch (Exception ex) {
            Log.Warning(ex, "上报启动进度失败，游戏: {0}", Entity.GameId);
        }
    }

    public Process? GetProcess()
    {
        return _disposed ? _gameProcess : null;
    }

    public void ShutdownAsync()
    {
        Dispose();
    }
}