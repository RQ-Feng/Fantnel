using System;
using Microsoft.AspNetCore.Mvc;
using Nirvana.Common;
using Nirvana.Common.Utils.CodeTools;

namespace Fantnel.Servlet.OthersController;

[ApiController]
[Route("[controller]")]
public class NirvanaController : ControllerBase {
    // 添加配置选项
    [HttpGet("/api/nirvana/add")]
    public IActionResult AddConfig(string name, string? value, string? property)
    {
        NirvanaConfig.AddByTypeName(name, value, property);
        return Ok(Code.ToJson(ErrorCode.Success));
    }

    // 设置配置
    [HttpGet("/api/nirvana/set")]
    public IActionResult SetConfig(string mode, string? value)
    {
        if ("gameMemory".Equals(mode, StringComparison.OrdinalIgnoreCase)) {
            NirvanaConfig.SetGameMemory(value);
        } else {
            NirvanaConfig.SetValue(mode, value);
        }

        return Ok(Code.ToJson(ErrorCode.Success));
    }

    // 获取配置
    [HttpGet("/api/nirvana/get")]
    public IActionResult GetConfig()
    {
        var config = NirvanaConfig.GetJsonObject();
        return Ok(Code.ToJson(ErrorCode.Success, config));
    }
}