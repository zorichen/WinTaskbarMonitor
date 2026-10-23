# 1.1.0 — 2026-10-23

- 产品名更新为「黑皮win助手」，统一硬件监控和指定程序英文输入锁的进程、托盘与设置窗口。
- 保留原输入规则、监控设置、安装身份和登录启动任务；新增普通启动器 --show 入口。
- 固定设置窗口默认、最小、最大尺寸及工作区约束，适配高 DPI 控件布局。
- 右键菜单精简为四项；按弹出窗口 DPI 使用系统菜单字号，宽度随文字计算，修正文字重叠及整体尺寸过大。
- 本机整合版本；未发布 GitHub Release。

# 1.0.3 — 2026-10-20

- 增加 GPU 占用率、核心温度、设备来源与多 GPU 详情；支持周期性重新枚举及手动重新检测显卡坞。
- 三列从左到右为 CPU、GPU、内存/硬盘；上下分别显示占用率和温度。
- 启用 .NET Framework 4.7.2 / WinForms PerMonitorV2 缩放，修正右键菜单的高 DPI 初始化方式。
- 增加 GPU 选择、过期读数及布局检查；记录风扇曲线可行性，未启用调速。
- 实测和未验证边界见 [开发记录](docs/2026-10-20-gpu-dpi.md)。

# 1.0.2

- 产品显示名与发布包统一为 WinTaskbarMonitor。
- 保留安装身份、设置与自启动标识，支持原地升级。

# Changelog

## 1.0.1 — 2026-09-10

- Optional sign-in startup, visible and controllable in Task Manager's Startup apps.
- Non-elevated startup launcher with the original multi-resolution native icon; it starts the authorized elevated task after 15 seconds.
- Installation-scoped startup registration and uninstall cleanup, without fixed user names or application paths.
- Protect existing installations from the installer validation script.

## 1.0.0 — 2026-09-09

- Two-row CPU usage, memory usage, CPU temperature and disk temperature display beside Windows 11 weather.
- Compact label/value spacing, wider column spacing and consistent `°C` typography.
- Original multi-resolution application and notification-area icon.
- Background sampling, stale-reading placeholders, theme/DPI adaptation and position adjustments.
- Signed PawnIO 2.2.0 optional dependency; LibreHardwareMonitor 0.9.6 integration.
- Windows x64 installer, portable ZIP and SHA256 checksums.
