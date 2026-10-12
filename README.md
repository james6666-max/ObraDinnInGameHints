# Obra Dinn 游戏内智能提示模组

这是一个面向《Return of the Obra Dinn》Windows Steam 版的社区 BepInEx 模组。它在航海日志中提供小黄鸭入口，根据当前存档进度推荐调查目标，并按“先整理思路、再逐层提示、最终提示双重确认”的流程工作。

模组不会自动填写人物推测，不修改游戏存档，也不会启动浏览器或本地服务器。提示内容仅在本地构建时嵌入 DLL；本仓库不包含提示源数据、答案 JSON 或带嵌入数据的构建产物。

原创源码、脚本和文档采用 [MIT 许可证](LICENSE)。第三方提示内容和游戏资源不在此授权范围内。当前提供源码供审查、修改和本地构建，暂不提供包含第三方提示数据的公开 DLL。

## v1.0.5

- 修复鼠标在提示面板内被游戏每帧拉回中央、导致光标抽搐和点击失效的问题。
- 面板打开期间临时接管 `RInput`：保留原方法执行但强制其走非锁定分支，并使用物理鼠标坐标供游戏 UI 命中测试。
- 暂停游戏自绘 `MouseCursor`，避免与系统光标重叠；关闭面板、隐藏日志、插件禁用或销毁时恢复原始状态。
- 保留 900×590 面板、分隔线、有人物双栏/无人物单栏及按可见按钮自动居中布局。
- 修复提示数据的船员索引到游戏真实 ID 映射。

## 使用前提

- Windows Steam 版《Return of the Obra Dinn》（当前验证版本为 Steam build 9550393）。
- BepInEx 5.4.23.5，已能正常加载其他 BepInEx 插件。
- 构建时需要游戏目录中的 Unity/游戏程序集，以及 Windows 自带 .NET Framework 3.5 编译器（脚本默认使用 `C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe`）。

## 安装已构建的 DLL

1. 退出游戏。
2. 将本地构建得到的 `ObraDinnInGameHints.dll` 放入游戏目录：
   `ObraDinn\BepInEx\plugins\ObraDinnInGameHints\ObraDinnInGameHints.dll`
3. 使用 BepInEx 能够注入的方式启动游戏。若 Steam 叠加层阻止直接注入，可关闭该游戏的 Steam Overlay，或使用本机已有的“带提示模组”直接启动快捷方式。
4. 打开航海日志后，右下角会出现小黄鸭入口。面板内使用系统鼠标点击；键盘快捷键默认为 `H`，可在面板设置中调整。

模组状态保存在 `BepInEx\config\ObraDinnInGameHints\state.json`，与游戏存档分开。提示面板不会写入人物姓名、下落或正确标记等游戏推测数据。

## 卸载

退出游戏后删除 `BepInEx\plugins\ObraDinnInGameHints` 目录即可。若还要清除模组自己的提示查看记录，可另行删除 `BepInEx\config\ObraDinnInGameHints\state.json`；这不会影响游戏存档。不要删除游戏目录中的其他 BepInEx 或游戏文件。

## 从源码构建

本仓库有意不提交第三方提示源。请在本机准备有权使用的提示源目录（包含 `faces_data.json` 与 `correct_name_list.json`），建议放在被 Git 忽略的 `work/private_hint_source/`，再执行：

```powershell
.\tools\Test.ps1
.\tools\Build.ps1 `
  -PrivateDataDirectory .\work\private_hint_source `
  -DuckIconSource .\assets\duck-source-generated.png
```

`Build.ps1` 会从游戏目录的 BepInEx 与 `ObraDinn_Data\Managed` 读取编译引用，输出到被忽略的 `dist\`，并将提示数据以内嵌资源写入 DLL。若游戏安装位置不是默认的 `D:\steam\steamapps\common\ObraDinn`，可额外传入 `-GamePath`。

`Test.ps1` 执行纯逻辑测试、状态存储测试、UI 布局/鼠标接管状态测试和提示数据结构检查。测试输出不打印提示正文或剧情答案。

若已安装游戏与 BepInEx，可在没有私有提示源的情况下检查全部插件源码是否兼容本机程序集：

```powershell
.\tools\Test-Compile.ps1 -GamePath 'D:\steam\steamapps\common\ObraDinn'
```

此检查只在 `obj\compile-check\` 生成不含资源的验证 DLL，不能用于安装。真正安装仍需通过 `Build.ps1` 构建。编译和逻辑测试不能代替游戏内验证；修改后应检查日志开关、存档切换、插件禁用后重新启用，以及关闭面板后的鼠标恢复。

## 数据来源、授权与公开发布

- 候选仅从存档可见人物中选择；普通提示根据已访问相关回忆的比例逐层解锁，最终提示需要连续两次确认。提示数据没有逐条回忆依赖，比例门槛不能保证每条提示只涉及你已看过的场景。
- 推荐逻辑会将已填写的身份与下落同游戏答案比较，以跳过已填对的分支，即使游戏尚未成组确认。推荐变化可能间接透露猜测是否正确；希望完全保留原版推理体验的玩家应暂停智能提示。
- 提示记录按存档槽保存；在同一槽重新开局或恢复旧存档后，请在面板设置中重置该槽提示记录，以免旧记录影响推荐。
- 本仓库的 README、测试和日志不应包含人物身份、死因、下落或任何提示正文。
- 本地使用的第三方提示源来自 [Yide-Zhang/ObraDinn-HintsAndCheck](https://github.com/Yide-Zhang/ObraDinn-HintsAndCheck)，核对版本为 `dcfa6c557e01078e449ae753967fc752844367f3`。截至 2026-10-12，未在该仓库发现许可证声明。公开可见不等于获得再分发授权；本项目 MIT 许可证不能授权其提示正文、答案或其他第三方内容。
- 可公开本项目原创源码、脚本、无剧透测试和文档。获得相应授权或替换为可再分发的原创数据前，不上传现有提示源、嵌入数据的 DLL、打包产物或游戏程序集。游戏与 BepInEx/Unity/Harmony 依赖由用户自行安装，未随源码分发。
- `assets/duck-source-generated.png` 按本项目既有记录为生成的小黄鸭素材，非游戏提取图；原作者对该素材拥有的可授权权益随 MIT 提供。游戏内人物图像从已安装游戏运行时读取，不包含在仓库内。
- 不要上传整个本地工作目录；`work/`、`obj/`、`dist/` 和游戏目录内容不属于源码发布。公开前运行 `.\tools\Test-PublicSource.ps1`，检查当前待提交文件和本地可达 Git 历史。该检查不能代替人工核对授权、改名后的敏感内容和 GitHub Release 附件。
- 若要更新游戏、BepInEx、中文补丁或提示源，应重新进行版本兼容性检查；不要直接覆盖游戏本体文件。

## 社区贡献与验证

提交修改前运行 `.\tools\Test.ps1` 和 `.\tools\Test-PublicSource.ps1`；有游戏依赖时再运行 `.\tools\Test-Compile.ps1`。测试使用合成占位内容，无需下载真实提示或游戏资源。投稿代码按本项目 MIT 许可证贡献，提交者应确保自己有权提供相关内容。

报告问题时请提供游戏版本、BepInEx 版本、复现步骤和脱敏后的相关错误。不要附完整存档、提示正文、人物答案或未裁剪的剧透截图。模组会在本机写入诊断日志；上传前检查其中的本地路径等信息。

发布前仍需实机检查：开关日志和提示面板、逐层解锁、最终提示两次确认、存档切换、插件禁用后重新启用，以及关闭面板后的鼠标恢复。自动测试通过并不代表这些场景已实机验收。

## 目录说明

```text
src/ObraDinnInGameHints/  模组源码
tests/                    .NET 3.5 纯逻辑测试
tools/                    提示数据转换、图标准备、测试和构建脚本
assets/                   原创小黄鸭源图
ANALYSIS/                 不含剧情内容的技术分析（本地审计资料，不建议公开）
```
