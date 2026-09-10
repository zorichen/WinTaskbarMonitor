<p align="center"><img src="assets/icon-256.png" width="96" alt="TaskbarMonitor icon"></p>

# TaskbarMonitor

一个简洁的 Windows 11 任务栏硬件监控工具。贴在天气右侧，用两行显示 CPU 占用率、内存占用率、CPU 温度和硬盘温度。

![运行界面](assets/screenshot.png)

```text
CPU  50%        内存  50%
CPU  50°C       硬盘  50°C
```

## 下载与安装

从 [Releases](https://github.com/zorichen/TaskbarMonitor/releases/latest) 下载：

- **TaskbarMonitor-1.0.0-Setup-x64.exe**：安装版，包含开始菜单入口、可选桌面快捷方式和卸载程序。
- **TaskbarMonitor-1.0.0-Portable-x64.zip**：免安装版，解压后运行 `TaskbarMonitor.exe`。运行数据仍存放在用户的 LocalAppData 目录。
- **SHA256SUMS.txt**：下载文件的 SHA256 校验值。

系统要求：Windows 11 x64、.NET Framework 4.7.2 或更新版本。初版针对主屏底部、图标居中的任务栏；副屏、垂直任务栏和第三方任务栏替换软件尚未验证。

程序启动会请求管理员权限，用于读取 CPU 和磁盘温度。首次安装可勾选 **PawnIO 温度驱动**；该选项默认不勾选，已有驱动时不会重复安装。也可以从 [PawnIO 官网](https://pawnio.eu/) 安装官方签名版。没有可用传感器时显示 `—`。

安装包和应用目前没有商业代码签名证书。PawnIO 安装包保持原始官方签名，下载后可核对校验值。

## 使用

- 右键小组件或托盘图标：查看传感器来源、微调左右位置、保存诊断报告或退出。
- 双击：查看详细读数和小组件内存占用。
- 自动适应系统浅色/深色主题以及屏幕缩放。
- 不上传遥测数据。

### 登录自启动（主分支新增，尚未包含在 v1.0.0 Release）

从当前源码构建的安装包提供可选的 **登录后自动启动**，默认不勾选。安装时启用后，任务管理器 → 启动应用中会出现带应用图标的 **TaskbarMonitor**，可以在那里禁用或重新启用。

普通权限启动器在登录后等待 15 秒，再调用安装时授权的管理员计划任务读取温度。任务本身没有登录触发器，因此禁用启动应用入口后，不会从另一条路径自动启动。实际出现时间还受 Windows 启动应用调度影响。

自启动绑定到运行安装程序的 Windows 账户；如果提权时输入了另一个管理员账户的凭据，绑定的也是该管理员账户。每个安装目录使用独立任务和快捷方式。重新安装时取消此选项或卸载应用，会删除该安装记录的启动入口与任务，保留监控设置和共享 PawnIO 驱动。

便携版需要放在固定目录，再在管理员终端运行 `TaskbarMonitorStartup.exe --install-startup`；移动或删除目录前，先运行 `TaskbarMonitorStartup.exe --remove-startup`。日常登录启动器不需要再次确认 UAC。启动诊断写入 `%LOCALAPPDATA%\TaskbarMonitor\startup-launcher.log`。

| 数据 | 采样方式 | 间隔 |
| --- | --- | --- |
| CPU 占用率 | `GetSystemTimes` 忙碌时间比例 | 1 秒 |
| 内存占用率 | `GlobalMemoryStatusEx` 物理内存占用 | 1 秒 |
| CPU 温度 | LibreHardwareMonitor，优先 CPU Package | 每次采集完成后等待 5 秒 |
| 磁盘温度 | NVMe 综合温度等主温度传感器 | 每次采集完成后等待 5 秒 |

仅启用 CPU 和磁盘硬件采集。后台采样，复用传感器对象，只重绘数值变化的区域。CPU 使用忙碌时间口径，可能与任务管理器某些版本的利用率口径略有差别。多块磁盘时显示主温度最高的磁盘，详情中标明来源。

一次本机短时实测（16 逻辑处理器、四项全开、45 秒）：进程平均 CPU 占用约 0.024%，平均工作集约 71.4 MiB。这是短时测试结果，不是所有硬件或长期运行的保证。

## 配置与诊断

数据目录：`%LOCALAPPDATA%\TaskbarMonitor`。安装和卸载不会删除这里的设置。

退出后可编辑 `settings.json`：

```json
{"LoadIntervalMs":1000,"TemperatureIntervalMs":5000,"LeftOffset":0,"WeatherWidth":160}
```

间隔最低为 1 秒和 5 秒。偏移、备用天气宽度以 96 DPI 逻辑像素计。默认使用天气按钮真实边界定位；识别失败时使用备用宽度。

诊断命令（管理员终端）：

```powershell
.\TaskbarMonitor.exe --diagnose
.\TaskbarMonitor.exe --capture-preview
.\TaskbarMonitor.exe --quit
```

`--diagnose` 约 7 秒后将结果写入数据目录并退出；`--capture-preview` 启动后保存一次组件自身的渲染图。`status.json` 每 30 秒更新，只保存在本地。诊断包含硬件型号和传感器标识，分享前请检查内容。

卸载 TaskbarMonitor 会移除程序和快捷方式，但保留用户设置与共享的 PawnIO 驱动。需要移除驱动时，请在 Windows“已安装的应用”中单独卸载 PawnIO。

## 从源码构建

Windows PowerShell 5.1 或 PowerShell 7：

```powershell
.\build.ps1
```

使用 Windows 自带的 .NET Framework C# 编译器，无需额外 SDK。首次构建自动从官方 GitHub Release 下载固定版本的 LibreHardwareMonitor 0.9.6，并校验 SHA256。

制作安装包需要 [Inno Setup 6 或 7](https://jrsoftware.org/isinfo.php)：

```powershell
.\package.ps1 -InnoCompiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

结果在 `dist/`，包含安装包、免安装包和校验文件。`tools/package_icon.py` 使用 Pillow 从原图生成九档 ICO；常规构建直接使用仓库内已生成的 ICO。

自启动验证：先运行 `tools\test-startup.ps1 -Phase Prepare`，再依次以管理员运行 `-Phase Register`、普通用户运行 `-Phase Launch`、管理员运行 `-Phase Remove`。测试使用独立目录、任务和占位程序，不停止已安装的监控，不访问硬件。`tools\test-installer.ps1` 涉及同一个安装器 AppId；检测到正式安装时会拒绝执行，应在一次性 Windows 虚拟机内验证完整安装/卸载。

## 许可与参考

项目原创代码使用 [MIT License](LICENSE)。依赖保持各自许可，见 [第三方声明](THIRD-PARTY-NOTICES.md) 和 [参考项目](REFERENCES.md)。LibreHardwareMonitor DLL 保持独立且未经修改。
