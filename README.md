# MoonDropEQ

水月雨 MOONDROP 耳机的 Windows 桌面 EQ 遥控器：把官网的「选设备 → 挑预设 → 应用」压缩成**一次点击**或**一个热键**。

[下载最新版](https://github.com/WinGua/MoonDropEQ/releases/latest) · [适配新设备](docs/adding-device.md) · [问题反馈](https://github.com/WinGua/MoonDropEQ/issues)

## 支持设备

| 设备 | Vendor ID | Product ID | HID Report ID | 验证级别 |
| --- | --- | --- | --- | --- |
| MOONDROP Rays（光束） | `0x35D8` | `0x011B` | `0x4B` | 官方实机 |

设备参数统一放在 `Core/devices.json`。其他水月雨第二代 DSP 设备协议相近，但参数和时序未经验证。

### 适配新设备

1. 底栏「设备适配」→「设备采集」
2. 按提示在官网应用一次「默认」和一条「自定义曲线」
3. 点「采集提交」，选择提交 Issue 或按 PR 指南提交

采集只读取耳机信息，不会写入。完整步骤和验证要求见 [docs/adding-device.md](docs/adding-device.md)。

## 使用

1. 插好耳机，双击 `MoonDropEQ.exe`（免安装，单文件）
2. 点击预设卡片，或按对应热键

首次运行若被 SmartScreen 拦截，选「更多信息 → 仍要运行」。设置、热键、曲线抓取等操作都在应用内。

## 数据

预设保存在 `%APPDATA%\MoonDropEQ\presets.json`，纯文本，可备份或迁移。

## 构建与维护

只使用 GitHub Actions 云端构建，本地无需安装 .NET SDK：

- push `main` → 构建；push `v*` 标签 → 发布 Release
- 构建前自动执行 `tools/validate-devices.ps1` 校验 `Core/devices.json`
- 产物只有 `MoonDropEQ.exe`：Actions artifact 会被自动打包成 zip，Release 直接提供 exe
- 工作流：`.github/workflows/build.yml`

协议抓包、固件升级适配与发版流程见 [docs/maintenance.md](docs/maintenance.md)。

## 安全说明

- 只写 EQ 参数区与保存命令，**不触碰固件分区、不涉及 OTA 升级**
- 写入内容与官网页面发出的字节完全一致
- 每次应用会写一次 flash，正常使用频率下远低于寿命
- 建议保留官网作为曲线编辑器，本工具负责日常快速切换

## 免责声明

非官方工具，与水月雨（MOONDROP）无隶属关系。协议基于官网公开前端实现分析得出，固件或官网更新后可能需要适配。使用风险自负。
