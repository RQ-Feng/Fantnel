using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Serilog.Core;
using Serilog.Events;

namespace Nirvana.Public.Utils.ViewLogger;

public class InMemorySink : ILogEventSink {
    public static readonly InMemorySink Instance = new();

    private readonly ConcurrentBag<string> _logs = [];

    public void Emit(LogEvent logEvent)
    {
        var message = logEvent.RenderMessage();
        _logs.Add($"[{logEvent.Level}] {message}");
    }

    public static IEnumerable<string> GetLogs()
    {
        return Instance._logs.Reverse(); // 倒序输出
    }

    public static void Clear()
    {
        Instance._logs.Clear();
        // 输出被重定向（无控制台）时 Console.Clear() 会抛 IOException: 句柄无效，直接跳过
        if (!Console.IsOutputRedirected) {
            Console.Clear();
        }
    }
}