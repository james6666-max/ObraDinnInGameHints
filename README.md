# Obra Dinn 游戏内智能提示模组

这是一个面向《Return of the Obra Dinn》Windows Steam 版的社区 BepInEx 模组。它在航海日志中提供小黄鸭入口，根据当前存档进度推荐调查目标，并按“先整理思路、再逐层提示、最终提示双重确认”的流程工作。

模组不会自动填写人物推测，不修改游戏存档，也不会启动浏览器或本地服务器。提示内容仅在本地构建时嵌入 DLL；本仓库不包含提示源数据、答案 JSON 或带嵌入数据的构建产物。

原创源码、脚本和文档采用 [MIT 许可证](LICENSE)。第三方提示内容和游戏资源不在此授权范围内。当前提供源码供审查、修改和本地构建，暂不提供包含第三方提示数据的公开 DLL。

## 提示来源与致谢

感谢 [@Yide-Zhang](https://github.com/Yide-Zhang) 制作的 [ObraDinn-HintsAndCheck（奥伯拉丁提示与验证工具）](https://github.com/Yide-Zhang/ObraDinn-HintsAndCheck)。本模组的本地提示与答案映射来自该项目；本项目负责游戏内界面、进度读取和逐层展示，不代表原作者参与或为本模组背书。

需要手动获取时，可在上游下载这两个文件：

- [faces_data.json：分层提示（含剧透）](https://github.com/Yide-Zhang/ObraDinn-HintsAndCheck/blob/dcfa6c557e01078e449ae753967fc752844367f3/faces_data.json)
- [correct_name_list.json：答案映射（含剧透）](https://github.com/Yide-Zhang/ObraDinn-HintsAndCheck/blob/dcfa6c557e01078e449ae753967fc752844367f3/correct_name_list.json)

不想看到答案正文，直接使用下方安装命令即可，不必打开 JSON。工具从原仓库下载固定版本并核对 SHA256，不将提示正文打印到终端。署名与链接不等于再分发授权；提示内容不适用本项目 MIT 许可证。

## 快速安装（Windows）

**只需准备一次：**

1. 下载本仓库源码（GitHub 页面 **Code → Download ZIP**），解压到普通可写目录，不要在压缩包中运行。已有 Git 的用户也可以克隆仓库，无需安装 Visual Studio。
2. 在 Steam 库中右键游戏 → **管理 → 浏览本地文件**，记下包含 `ObraDinn.exe` 的目录。
3. 若尚未装 BepInEx，前往 [BepInEx 5.4.23.5 官方下载页](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)，下载 **BepInEx_win_x86_5.4.23.5.zip**，解压到游戏目录。本项目验证的 build 9550393 是 32 位游戏，即使 Windows 是 64 位也应选 x86。其他游戏版本请按实际程序位数选择；已有能正常工作的 BepInEx 5 不必覆盖。参见 [官方安装指南](https://docs.bepinex.dev/articles/user_guide/installation/index.html)。
4. 启动游戏一次，再退出，确认游戏目录生成了 `BepInEx/LogOutput.log`。在 Windows 的“启用或关闭 Windows 功能”中启用 **.NET Framework 3.5（包括 .NET 2.0 和 3.0）**；若编译器已经存在则跳过。

**开始安装：**关闭游戏，在解压后的本项目目录打开 PowerShell，把下面的 `-GamePath` 改为你刚才找到的目录，然后运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Install.ps1 -GamePath 'D:\steam\steamapps\common\ObraDinn' -DownloadHintData
```

`-ExecutionPolicy Bypass` 只作用于这次 PowerShell 进程，不修改系统策略；请先确认你下载的是本仓库源码。安装不需要默认以管理员身份运行。

该命令检查依赖，从原作者仓库获取固定版本的两个数据文件，在本机编译，再安装到 `BepInEx/plugins/ObraDinnInGameHints/`。只替换本插件 DLL；已有版本会备份，游戏存档与提示记录保留。数据与产物仅保存在本地，不上传。

安装完成后启动游戏，打开航海日志，点击右下角小黄鸭或按 **H**。先看普通提示，最终提示需要连续两次确认。更新源码后重跑同一命令即可；已有数据校验一致时不会重复下载。修改过数据的用户应使用下面的离线安装方式。

### 离线安装与预检查

已有自己的数据时，将两个 JSON 放入 `work/private_hint_source/`，然后省略 `-DownloadHintData`；也可以指定目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Install.ps1 -GamePath 'D:\steam\steamapps\common\ObraDinn' -PrivateDataDirectory 'C:\MyHints'
```

只检查环境、不下载、不构建、不安装：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Install.ps1 -GamePath 'D:\steam\steamapps\common\ObraDinn' -CheckOnly
```

### 常见问题

| 现象 | 处理方式 |
| --- | --- |
| 找不到游戏或程序集 | `-GamePath` 应指向含 `ObraDinn.exe` 的目录，不是 `ObraDinn_Data`。 |
| 缺少 BepInEx 或没有小黄鸭 | 检查 BepInEx 位数、是否成功生成 `BepInEx/LogOutput.log`，确认插件已加载。必要时关闭该游戏 Steam Overlay 后重试。 |
| 找不到 .NET 3.5 编译器 | 启用 Windows 的 .NET Framework 3.5 功能；仅安装 .NET 8/9 不能替代。 |
| 下载失败 | 检查能否访问 `raw.githubusercontent.com`，或用上面的固定版本文件链接下载后离线安装。不要保存 GitHub 网页 HTML 冒充 JSON。 |
| 数据校验不一致 | 工具不会覆盖你修改过的文件。自定义数据请省略下载参数，或指定新的数据目录。 |
| 游戏正在运行 | 完全退出游戏，再安装。工具不会自动结束游戏进程。 |
| 写入失败 | 检查文件占用和游戏目录写权限，确认备份后重试，不要删除其他插件。 |

恢复旧版时，退出游戏，将插件目录内对应 `.bak` 备份复制回 `ObraDinnInGameHints.dll`。版本修复记录见 [CHANGELOG.md](CHANGELOG.md)。

## 使用须知

- 当前验证环境为 Windows Steam build 9550393、BepInEx 5.4.23.5；其他版本需要自行验证兼容性。
- 提示按已访问回忆的数量逐层解锁，仍可能提到尚未看过的场景。推荐会参考已填写答案是否正确，因此也可能间接验证你的猜测。
- 提示记录与游戏存档分开保存，不会自动填写人物答案。在同一存档槽重新开局或恢复旧档后，可在面板设置中重置提示记录。

## 卸载

退出游戏，删除游戏目录中的 `BepInEx/plugins/ObraDinnInGameHints/` 即可。若还要清除提示记录，另行删除 `BepInEx/config/ObraDinnInGameHints/state.json`。这不会影响游戏存档；请保留其他插件和游戏文件。

## 反馈与参与

安装或使用遇到问题，欢迎[提交 Issue](https://github.com/james6666-max/ObraDinnInGameHints/issues)，注明游戏版本、BepInEx 版本、操作步骤和报错。日志与截图请先去掉个人路径和剧情答案，不必上传完整存档。

想修改代码或提交 PR，请阅读[贡献指南](CONTRIBUTING.md)，其中包含构建方法、测试命令和源码目录说明。普通玩家按上面的快速安装操作即可。

## 许可证

原创源码、脚本和文档采用 [MIT 许可证](LICENSE)。第三方提示数据与游戏资源不在授权范围内；署名和链接也不代表已取得再分发许可。当前仅提供源码，含第三方提示数据的 DLL 请留作本地使用。
