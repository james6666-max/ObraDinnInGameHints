# 贡献指南

欢迎修复问题、改进提示交互和完善文档。安装与使用请先看 [README](README.md#快速安装windows)。提交者应有权提供贡献内容，原创代码按本项目 [MIT 许可证](LICENSE) 贡献。

## 开发环境与目录

源码需兼容 .NET Framework 3.5 和当前使用的 Unity/BepInEx 5 API。Windows 的 .NET Framework 3.5 编译器用于逻辑测试；完整插件编译还需要本机安装游戏及 BepInEx。

| 目录 | 用途 |
| --- | --- |
| `src/ObraDinnInGameHints/` | 插件入口、游戏进度读取、提示逻辑、状态存储和界面 |
| `tests/` | 不含真实剧情的 C# 逻辑回归测试 |
| `tools/` | 数据获取、转换、构建、安装及验证脚本 |
| `assets/` | 本项目小黄鸭图标源图 |

`work/`、`obj/` 和 `dist/` 是被 Git 忽略的本地数据或输出目录，不应随贡献提交。

## 验证修改

在仓库根目录运行以下检查；它们使用合成数据，不需要游戏资源或真实提示内容：

```powershell
.\tools\Test.ps1
.\tools\Test-PublicSource.ps1
```

`Test.ps1` 检查提示逻辑、状态存储、UI 辅助逻辑和数据转换；`Test-PublicSource.ps1` 检查待提交路径及本地可达 Git 历史，防止误提交数据与产物。后者不能识别改名后的敏感正文，也不覆盖未拉取的远端历史或 Release 附件。

若修改安装器或公开源码检查器，分别运行其合成环境测试：

```powershell
.\tools\Test-Install.ps1
.\tools\Test-PublicSource.Tests.ps1
```

已安装游戏及 BepInEx 时，可检查完整源码是否与本机程序集兼容：

```powershell
.\tools\Test-Compile.ps1 -GamePath 'D:\steam\steamapps\common\ObraDinn'
```

此命令在 `obj/compile-check/` 生成不含提示资源的验证 DLL，不能用于安装。

## 构建用于实测的版本

将有权使用的 `faces_data.json` 与 `correct_name_list.json` 放入 `work/private_hint_source/`。来源和便捷下载方式见 [README](README.md#提示来源与致谢)。然后运行：

```powershell
.\tools\Build.ps1 `
  -GamePath 'D:\steam\steamapps\common\ObraDinn' `
  -PrivateDataDirectory .\work\private_hint_source `
  -DuckIconSource .\assets\duck-source-generated.png
```

输出为 `dist/ObraDinnInGameHints/ObraDinnInGameHints.dll`。关闭游戏后，可使用 README 中的安装工具安装，或手动备份旧版并将该 DLL 复制到游戏的 `BepInEx/plugins/ObraDinnInGameHints/`。

涉及游戏交互的修改还需实测：开关日志和面板、提示逐层解锁、最终提示两次确认、切换或回退存档、插件禁用后重新启用，以及关闭面板后的鼠标恢复。提交 PR 时写清测试环境、实际通过的场景和未测试项，避免将编译通过描述为实机验证通过。

## 数据、素材与发布范围

- 提示源来自 [@Yide-Zhang](https://github.com/Yide-Zhang) 的 [ObraDinn-HintsAndCheck](https://github.com/Yide-Zhang/ObraDinn-HintsAndCheck)，已核对版本为 `dcfa6c557e01078e449ae753967fc752844367f3`。截至 2026-10-12 未发现该仓库的许可证声明，本项目 MIT 许可不覆盖其数据。
- 获得相应授权或改用可再分发的数据前，不提交提示正文、答案 JSON、嵌入数据的 DLL、安装包或游戏程序集。BepInEx、Unity 和 Harmony 依赖由用户自行安装，不随源码分发。
- 小黄鸭源图按项目既有记录为生成素材，不是游戏提取图；原作者对该素材拥有的可授权权益随 MIT 提供。人物图像从已安装游戏运行时读取，不包含在仓库中。
- Issue、PR、测试和日志示例都应使用无剧透占位内容。上传日志与截图前删除个人路径、人物答案和其他无关信息。
