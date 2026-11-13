# 维护与协议适配指南

> 目的：将来官方升级固件 / 改版、或需要适配新机型时，不必从头读整个项目。
> 这份文档记录**已实测确认的协议事实**、**代码入口**、**复现方法**和**发版流程**。

---

## 1. 项目速览

| 项 | 内容 |
| --- | --- |
| 形态 | Windows 桌面工具（.NET 8 + WPF + WPF-UI） |
| 作用 | 直接与耳机 HID 通信，切换内置预设 / 回放自定义 EQ 曲线 |
| 依赖官网 | **仅开发时参考过**，运行时不访问任何水月雨服务器 |
| 数据位置 | `%APPDATA%\MoonDropEQ\presets.json`（预设）、`settings.json`（设置） |
| 构建 | GitHub Actions（`windows-latest`）→ 单文件 exe → Release |

### 关键文件

| 文件 | 职责 |
| --- | --- |
| `Core/devices.json` | **设备描述**：VID/PID、Report ID、命令字、段数、槽位、时序 |
| `Core/DeviceProfile.cs` | 设备描述模型与匹配规则 |
| `Core/HidEqDevice.cs` | 按设备描述工作的 HID 帧封装与读写方法 |
| `Services/PresetApplier.cs` | 应用流程时序（内置切槽 / 曲线写入） |
| `Services/PresetStore.cs` | 预设 JSON 读写；按设备描述生成默认项 |
| `Models/Preset.cs` | `Kind` = `BuiltIn`（按索引）/ `Curve`（十六进制载荷，段数由设备描述决定） |
| `Services/HotkeyService.cs` | 全局热键（`RegisterHotKey`） |
| `Services/TrayService.cs` | 托盘菜单 |
| `MainWindow.xaml(.cs)` | 主界面与页面切换（设置 / 设备适配 / 采集 / 关于） |
| `Views/` | 设置页、关于页（普通 UserControl） |
| `Themes/AppDark.xaml`、`AppLight.xaml` | 两套配色，切换主题时整体替换 |

---

## 2. Rays 已实测确认的协议事实

> 本章参数以 MOONDROP Rays 为准。其他机型以 `Core/devices.json` 中的设备描述为准，新机型接入见 [adding-device.md](adding-device.md)。

### 2.1 设备标识

| 项 | 值 |
| --- | --- |
| 型号 | MOONDROP Rays（光束） |
| Vendor ID | `0x35D8` |
| Product ID | `0x011B` |
| HID Report ID | `0x4B` |
| 正确接口 | HID `mi_03`（另一个 `mi_04` 不可写） |

### 2.2 帧格式

```
[reportId=0x4B][op][cmd][len][payload...]   总长固定 64 字节，不足补 0
```

| op | 值 |
| --- | --- |
| 读 | `0x80` |
| 写 | `0x01` |

| cmd | 值 | 说明 |
| --- | --- | --- |
| `EQ_ACTIVE` | `0x0F` | 当前槽位号（1 字节载荷） |
| `EQ_UPDATE` | `0x09` | 单段 PEQ 参数（**33 字节**载荷） |
| `COMMIT` | `0x0A` | 应用曲线时必发：`01 0A 04 00 00 FF FF` |
| `PRE_GAIN` | `0x23` | 前级增益，int16 LE = dB × 256 |
| `GLOBAL_GAIN` | `0x03` | 全局增益，同上 |
| `SAVE_FLASH` | `0x01` | 保存到 flash（空载荷） |

读响应格式：`[0x4B][0x80][cmd][len][payload...]` —— 载荷从**第 5 个字节**开始。
- 读槽位：`payload[0]` = 索引
- 读单段：`payload[0..32]` = 33 字节原始参数

### 2.3 槽位

| 索引 | 含义 |
| --- | --- |
| 0 | 默认 |
| 1 | 音乐游戏 |
| 2 | 3A 游戏 |
| 3 | FPS1 |
| 4 | FPS2 |
| 5 / 6 / 7 | 无效索引（会沿用上一条数据）；写入曲线时用 7 作为 PEQ 工作槽 |

每套曲线固定 **8 段**，每段 **33 字节**。这 33 字节的浮点编码**从未解码**，工具始终原样存取——这是刻意的设计，避免理解错格式。

### 2.4 应用时序（与官网一致）

**切内置预设**

```
01 0F 01 <index>          (等 150ms)
01 23 02 00 00            前级增益归零
01 03 02 00 00            全局增益归零
01 01 00                  保存
```

**应用自定义曲线**

```
01 0F 01 07               切到 PEQ 工作槽
8 × 01 09 21 <33字节>      每段间隔 30ms
01 0A 04 00 00 FF FF      提交（漏了这条曲线不生效）
01 23 02 <preGain×256>    前级增益
01 03 02 <global×256>     全局增益
01 01 00                  保存
```

> 早期的错误实现用了 11 字节短格式（u16 频点）并且漏了 `01 0A`，结果是「有电磁声但听感不变」。这是重要的排错经验。

---

## 3. 什么时候需要适配

| 症状 | 可能原因 | 处理 |
| --- | --- | --- |
| 启动显示「未连接」 | 固件改了 VID/PID 或接口 | 重新抓包，确认设备标识 |
| 应用内置预设无反应 | `EQ_ACTIVE` 命令或槽位映射变了 | 抓包对比命令字 |
| 应用曲线无反应 | 载荷长度/格式变了，或缺提交命令 | 抓包对比 33 字节结构与 `01 0A` |
| 抓取曲线失败 | 读不到 8 段完整数据 | 检查读命令响应长度 |
| 某些索引越界 | 槽位数量变化 | 调整 `Core/devices.json` 的 `slots` |

**软件不会因此损坏设备**：只写 EQ 参数区与保存命令，不触碰固件 / OTA。

---

## 4. 复现抓包（核心方法）

思路：用调试模式启动 Edge，给页面里的 `HIDDevice.sendReport` 挂钩子，把官网发出的每一条报文录下来。

### 4.1 启动浏览器

```powershell
msedge.exe --remote-debugging-port=9333 --user-data-dir="$env:TEMP\eqcap-profile" --new-window https://hub.moondroplab.tech/
```

### 4.2 注入钩子（通过 CDP `Runtime.evaluate`）

```js
(() => {
  if (window.__hidHookInstalled) return 'installed';
  window.__hidLog = [];
  const hex = a => Array.from(new Uint8Array(a.buffer ? a.buffer : a))
    .map(b => b.toString(16).padStart(2, '0')).join(' ');
  const wrap = (proto, name) => {
    const orig = proto[name];
    if (!orig) return;
    proto[name] = function (rid, data) {
      try { window.__hidLog.push({ rid, d: hex(data) }); } catch (e) {}
      return orig.apply(this, arguments);
    };
  };
  if (window.HIDDevice) {
    wrap(HIDDevice.prototype, 'sendReport');
    wrap(HIDDevice.prototype, 'sendFeatureReport');
  }
  window.__hidHookInstalled = true;
  return 'ok';
})()
```

随后在页面上正常操作（连接 → 应用某个预设），再读取 `JSON.stringify(window.__hidLog)`。

### 4.3 分析要点

1. 先找 `01 0F 01 xx`（切槽位）
2. 再数 `01 09` 后面的长度字节（应为 `0x21` = 33）
3. 找 `01 0A` 后面的载荷
4. 找 `01 23` / `01 03` 的增益值，最后是 `01 01 00` 保存

---

## 5. 本地快速验证（不依赖 UI）

用 Python + `hidapi` 就能复现，不必编译整个程序：

```python
import hid, time
VID, PID, RID = 0x35D8, 0x011B, 0x4B

def open_dev():
    for d in hid.enumerate(VID, PID):
        if d.get("interface_number") == 3:
            dev = hid.device(); dev.open_path(d["path"]); dev.set_nonblocking(0); return dev
    raise SystemExit("device not found")

def send(dev, op, cmd, payload=b"", expect=True):
    buf = bytes([RID, op, cmd, len(payload)]) + payload
    buf += bytes(64 - len(buf))
    dev.write(buf)
    return bytes(dev.read(64, 500)) if expect else None

dev = open_dev()
print("index =", send(dev, 0x80, 0x0F)[4])
```

验证顺序：

1. 读槽位号是否有合理回包
2. 写槽位 `01 0F 01 03` → 读回是否变成 3
3. 读 8 段，写出再读回，**逐字节比对是否一致**
4. 听感 A/B：1kHz 纯音 / 粉红噪声 / 扫频（比普通歌曲更容易听出差异）

---

## 6. 修改代码的位置

| 变化 | 改哪里 |
| --- | --- |
| VID / PID / ReportId | `Core/devices.json` |
| 新增/改名命令字 | `Core/devices.json` 的 `commands` + `PresetApplier` 时序 |
| 载荷长度变化 | `Core/devices.json` 的 `bandPayloadSize`、`bandCount` |
| 槽位映射变化 | `Core/devices.json` 的 `slots`、`curveSlot` |
| 增加新机型 | 在 `Core/devices.json` 里新增一条设备描述 |
| 校验设备描述 | `pwsh tools/validate-devices.ps1`（CI 构建前会自动执行） |

设备描述由 `Core/DeviceRegistry.cs` 在启动时加载，`HidEqDevice` 按描述收发报文。只有遇到设备描述无法表达的特殊流程（例如额外握手或双通道分别写入）才需要改代码。

新机型的采集、提交和验证流程见 [adding-device.md](adding-device.md)。

---

## 7. 发版流程

1. 改 `MoonDropEQ.csproj` 的 `<Version>`（关于页与检查更新都读程序集版本）
2. 提交并推送 `main` → Actions 自动构建（失败可在 Check Run 的 annotations 里读到编译错误）
3. 打标签并推送：

```powershell
git tag -a v0.2.0 -m "MoonDropEQ v0.2.0"
git push origin v0.2.0
```

4. workflow 会创建 Release 并上传 `MoonDropEQ-win-x64.zip`

**重发同一版本**（覆盖已有 Release 附件）：

```powershell
git tag -d v0.2.0
git push origin :refs/tags/v0.2.0
git tag -a v0.2.0 -m "MoonDropEQ v0.2.0"
git push origin v0.2.0
```

---

## 8. 已知坑

| 现象 | 说明 / 处理 |
| --- | --- |
| `git push` 报 SSL 证书错误 | 本机 OpenSSL 链不完整，加 `-c http.sslBackend=schannel` |
| GitHub API 403/限流 | 匿名 60 次/小时；脚本里用已存凭据（`git credential fill`） |
| 写入失败 | 官网页面正占用设备，关掉网页重试 |
| 不保存就断电 | 槽位与曲线会回滚到 flash 里的值（应用时默认都会保存） |
| 托盘图标空白 | 图标来自嵌入资源 `Assets/app.ico`，检查是否被打包 |
| 关于页图标糊 | 官方只有 32px favicon，图标是按轮廓重建的，不要再直接放大原图 |
| WPF-UI 与 WPF 同名类型冲突 | `MenuItem` / `MessageBox` 等要用完整命名空间 |

---

## 9. 不受官网改版影响的清单

域名、页面 UI、账号体系、CDN、收藏夹同步、网页后端 API —— 全部与本工具无关。

只要**耳机固件协议不变**，官网怎么改都不影响；反之固件一变，才需要按第 4、5、6 节处理。
