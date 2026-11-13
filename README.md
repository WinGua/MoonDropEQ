# MoonDropEQ

水月雨 MOONDROP 耳机的 Windows 桌面 EQ 遥控器。把官网调音工具的「选设备 → 挑预设 → 应用」压缩成**一次点击**或**一个热键**。

[下载最新版](https://github.com/WinGua/MoonDropEQ/releases/latest) · [适配新设备](docs/adding-device.md) · [问题反馈](https://github.com/WinGua/MoonDropEQ/issues)

## 功能

- **一键切换内置预设**：槽位名称来自设备描述（Rays 为默认 / 音乐游戏 / 3A 游戏 / FPS1 / FPS2）
- **抓取与回放曲线**：在官网调好音后保存到本地，之后一键切换
- **全局热键与托盘**：窗口最小化也能切换，关窗口默认收进托盘
- **设备适配采集**：一键采集新机型的 HID 报文与只读探测结果
- **自适应界面**：宽屏双栏、窄屏单栏，支持 90% / 100% / 110% / 125% 缩放
- **单文件 exe**：免安装，自包含运行时，约 65 MB

<a id="supported-devices"></a>

## 支持设备

| 设备 | Vendor ID | Product ID | HID Report ID | 验证级别 |
| --- | --- | --- | --- | --- |
| MOONDROP Rays（光束） | `0x35D8` | `0x011B` | `0x4B` | 官方实机 |

设备参数统一放在 `Core/devices.json`。其他水月雨第二代 DSP 设备协议相近，但参数和时序未经验证，不要直接套用。

### 适配新设备

1. 底栏「设备适配」→「设备采集」
2. 按提示在官网应用一次「默认」和一条「自定义曲线」
3. 点「采集提交」，选择提交 Issue 或按 PR 指南提交

采集只读取耳机信息，不会写入。完整步骤和验证要求见 [docs/adding-device.md](docs/adding-device.md)。

## 使用

1. 插上耳机，双击 `MoonDropEQ.exe`
2. 点击预设卡片，或按对应热键

### 添加自定义曲线

1. 在官网连接耳机，应用想用的预设
2. 回到本工具点「抓取曲线」
3. 命名保存，之后即可点击或热键切换

## 设置与预设管理

底部「设置」可调整：

- **关闭窗口时**：最小化到托盘或直接退出
- **窗口材质**：Mica / 亚克力 / 纯色
- **主题**：跟随系统 / 深色 / 浅色
- **界面缩放**：90% / 100% / 110% / 125%

设置即时生效，保存在 `%APPDATA%\MoonDropEQ\settings.json`。

选中预设后，可在左侧信息卡重命名、设置热键或删除；任意卡片也可右键操作。热键支持 `Ctrl / Alt / Shift / Win` + 字母、数字、`F1–F24`，单功能键仅支持 `F1–F24`。

## 工作原理

官网调音工具通过 **WebHID** 与耳机通信。本工具使用相同的 HID 报文直接对话，所有协议参数都来自 `Core/devices.json`，不同机型可以完全不同。

通用帧格式：

```
[reportId][op][cmd][len][payload...]
```

- `reportId`、读写 `op`、各 `cmd`、PEQ 段数、单段字节数、槽位表都由设备描述决定
- 输出报告长度按接口能力补齐（Rays 为固定 64 字节）
- 工具只原样存取 PEQ 载荷、不解码，因此写入结果与官网一致

**Rays 的参数示例**：

| 项 | 值 |
| --- | --- |
| Report ID | `0x4B` |
| 读 / 写 op | `0x80` / `0x01` |
| PEQ 段数 × 单段 | 8 段 × 33 字节 |
| 自定义曲线工作槽 | `7` |

Rays 的实际报文：

| 操作 | 报文 |
| --- | --- |
| 切换内置预设 | `01 0F 01 <index>` → 写增益 → `01 01 00` 保存 |
| 应用自定义曲线 | `01 0F 01 07` → 8 × `01 09 21 <33 字节>` → `01 0A 04 00 00 FF FF` → 写增益 → 保存 |

Rays 内置槽位：`0` 默认、`1` 音乐游戏、`2` 3A 游戏、`3` FPS1、`4` FPS2；`5–7` 为无效索引。自定义曲线只有一块共享工作区，同一时刻只能生效一条。

其他机型如果没有内置槽位、没有自定义曲线，或段数与槽位不同，界面会按 `devices.json` 里的 `slots`、`bandCount`、`capabilities` 自动调整，不需要改代码。新增机型见 [docs/adding-device.md](docs/adding-device.md)。

## 预设文件

路径：`%APPDATA%\MoonDropEQ\presets.json`，纯文本，可备份或迁移。

```jsonc
{
  "version": 2,
  "presets": [
    {
      "id": "builtin-fps1", "name": "FPS1", "kind": "BuiltIn",
      "deviceId": "rays", "index": 3, "hotkey": "Ctrl+Alt+4"
    },
    {
      "id": "curve-cs2", "name": "CS2", "kind": "Curve",
      "deviceId": "rays", "preGainDb": -7.5, "globalGainDb": 0,
      "bands": ["00 00 ...", "..."], "hotkey": "Ctrl+Alt+6"
    }
  ]
}
```

`BuiltIn` 按 `index` 切槽；`Curve` 写入 PEQ 工作区，`bands` 为十六进制字符串，段数与单段字节数由设备描述决定（Rays 为 8 段 × 33 字节）。修改后重启生效；设置页底部可直接定位该文件。

## 常见问题

| 现象 | 处理 |
| --- | --- |
| 显示「未连接」 | 确认耳机已插好，工具会按 VID/PID 自动识别 |
| 应用失败 / 写入无反应 | 官网可能正占用设备，关闭网页后重试 |
| 提示热键被占用 | 换一个组合键后重启 |
| SmartScreen 拦截 | exe 未签名，选「更多信息 → 仍要运行」 |
| 抓取曲线失败 | 先在官网成功应用一次，再点抓取 |

## 构建与维护

只使用 GitHub Actions 云端构建，本地无需安装 .NET SDK：

- push `main` → 构建并上传产物；push `v*` 标签 → 创建 Release
- 构建前自动执行 `tools/validate-devices.ps1` 校验 `Core/devices.json`
- 构建产物只包含 `MoonDropEQ.exe`：Actions artifact 是 GitHub 自动打包的 zip，Release 资产是单个应用 zip
- 工作流：`.github/workflows/build.yml`

协议抓包、固件升级适配与发版流程见 [docs/maintenance.md](docs/maintenance.md)；新机型接入见 [docs/adding-device.md](docs/adding-device.md)。

## 安全说明

- 只写 EQ 参数区与保存命令，**不触碰固件分区、不涉及 OTA 升级**
- 写入内容与官网页面发出的字节完全一致
- 每次应用会写一次 flash，正常使用频率下远低于寿命
- 建议保留官网作为曲线编辑器，本工具负责日常快速切换

## 许可证

[GNU General Public License v3.0](LICENSE)。对外分发修改版时，必须继续以 GPL-3.0 开源并提供源代码；仅自行修改、不对外分发不受限制。

Copyright (C) 2026 WinGua。第三方组件许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 免责声明

非官方工具，与水月雨（MOONDROP）无隶属关系。协议基于官网公开前端实现分析得出，固件或官网更新后可能需要适配。使用风险自负。
