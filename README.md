# Obra Dinn 游戏内智能提示模组

这是一个供本机使用的《Return of the Obra Dinn》Windows Steam 版 BepInEx 模组。它在航海日志中提供原创小黄鸭入口，根据当前存档进度推荐调查目标，并按“先整理思路、再逐层提示、最终提示双重确认”的流程工作。

模组不会自动填写人物推测，不修改游戏存档，也不会启动浏览器或本地服务器。提示内容仅在本地构建时嵌入 DLL；本仓库不包含提示源数据、答案 JSON 或带嵌入数据的构建产物。

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

本仓库有意不提交私密提示源。请在本机准备提示源目录（包含 `faces_data.json` 与 `correct_name_list.json`），再执行：

```powershell
.\tools\Test.ps1
.\tools\Build.ps1 `
  -PrivateDataDirectory .\path\to\private_hint_source `
  -DuckIconSource .\assets\duck-source-generated.png
```

`Build.ps1` 会从游戏目录的 BepInEx 与 `ObraDinn_Data\Managed` 读取编译引用，输出到被忽略的 `dist\`，并将提示数据以内嵌资源写入 DLL。若游戏安装位置不是默认的 `D:\steam\steamapps\common\ObraDinn`，可额外传入 `-GamePath`。

`Test.ps1` 执行纯逻辑测试、状态存储测试、UI 布局/鼠标接管状态测试和提示数据结构检查。测试输出不打印提示正文或剧情答案。

## 无剧透原则与私有使用限制

- 代码只依据存档可见回忆、当前页面和已确认状态选择候选；普通提示按调查进度逐层解锁，最终提示需要连续两次确认。
- 本仓库的 README、测试和日志不应包含人物身份、死因、下落或任何提示正文。
- 提示源来自本机辅助项目的私有处理结果；相关上游仓库当前没有明确允许再分发的许可证。因此本项目仅限个人本机使用，不创建公开发行包，不上传提示源、不上传嵌入提示数据的 DLL，也不要把 `work/`、`obj/`、`dist/` 或游戏目录内容提交到 GitHub。
- 若要更新游戏、BepInEx、中文补丁或提示源，应重新进行版本兼容性检查；不要直接覆盖游戏本体文件。

## 目录说明

```text
src/ObraDinnInGameHints/  模组源码
tests/                    .NET 3.5 纯逻辑测试
tools/                    提示数据转换、图标准备、测试和构建脚本
assets/                   原创小黄鸭源图
ANALYSIS/                 不含剧情内容的技术分析（本地审计资料，不建议公开）
```
