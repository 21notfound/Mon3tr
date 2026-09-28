# Mon3tr 桌宠源码

本目录包含当前已安装的常驻托盘版本源码及构建所需资源。

- PetV2.cs：程序入口、人物动画、拖动与贴边隐藏、托盘、语音概率、聊天框和北京时间问候。
- UsageConnection.cs：后台账号额度连接与查询。
- Usage.cs：额度数据解析和显示格式。
- Tests.cs：功能检查。
- app.manifest：Windows 应用清单。
- assets：内置动画缓存、三段语音和透明头像图标。
- build.ps1：构建脚本。

## 重新编译

在本目录打开 PowerShell，执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

使用 Windows 自带的 .NET Framework C# 编译器，无需安装 Python、Node.js 或下载包。生成文件为 bin\Mon3tr.exe，不会覆盖 E:\Mon3tr\Mon3tr.exe，也不会修改当前运行的桌宠设置。

执行 bin\Mon3tr.exe --test 可运行检查，结果写入 bin\qa。

当前使用的实时设置在 E:\Mon3tr\settings.json。源码中的 .605 是默认人物大小，实际运行时优先读取程序旁边的设置文件。
