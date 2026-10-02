using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Nirvana.WPFLauncher.Utils;

/**
 * JSON 反序列化保护
 * 远程接口（尤其第三方 SDK）在失败时经常返回 HTML 或纯文本，
 * 直接 JsonSerializer.Deserialize 只会抛出 "'0xE8' is an invalid start of a value" 这类无法定位的错误。
 * @author Fantnel
 */
public static class JsonSafe {
    /**
     * 反序列化，失败时抛出可读错误（含来源、HTTP 状态与正文片段）
     * @param text 响应正文
     * @param what 来源描述，如 "4399 登录响应"
     * @param response 原始响应（可选，用于附加状态码与 Content-Type）
     */
    public static T? Deserialize<T>(string? text, string what, HttpResponseMessage? response = null)
    {
        if (string.IsNullOrWhiteSpace(text)) {
            return default;
        }

        try {
            return JsonSerializer.Deserialize<T>(text);
        } catch (JsonException e) {
            throw new Exception($"{what} 的返回内容不是合法 JSON：{e.Message}{Describe(response)} ｜ 内容片段：{Preview(text)}");
        }
    }

    /**
     * 反序列化 UTF-8 字节内容（如解密后的响应体），失败时抛出可读错误
     */
    public static T? DeserializeUtf8<T>(byte[]? utf8Json, string what, HttpResponseMessage? response = null)
    {
        if (utf8Json == null || utf8Json.Length == 0) {
            return default;
        }

        try {
            return JsonSerializer.Deserialize<T>(utf8Json);
        } catch (JsonException e) {
            throw new Exception($"{what} 的返回内容不是合法 JSON：{e.Message}{Describe(response)} ｜ 内容片段：{Preview(Encoding.UTF8.GetString(utf8Json))}");
        }
    }

    /**
     * 判断响应内容是否看起来是 JSON（用于区分 "JSON 正常响应" 与 "纯文本错误信息"）
     */
    public static bool LooksLikeJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        var first = text.TrimStart()[0];
        return first is '{' or '[';
    }

    /**
     * 截断正文用于报错展示
     */
    public static string Preview(string? text)
    {
        if (string.IsNullOrEmpty(text)) {
            return "(空)";
        }

        var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= 200 ? flat : flat[..200] + "…";
    }

    private static string Describe(HttpResponseMessage? response)
    {
        if (response == null) {
            return "";
        }

        var type = response.Content.Headers.ContentType?.ToString();
        return $"（HTTP {(int)response.StatusCode}{(string.IsNullOrEmpty(type) ? "" : $", {type}")}）";
    }
}
