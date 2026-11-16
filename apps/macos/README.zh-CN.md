# PocketDesk macOS 版

原生 Swift / AppKit / SwiftUI 的 UU 远程伴侣，当前源码版本 **0.2.4**。复用 MIT 上游的显示、窗口及手势引擎，提供独立手机屏、窗口放置/还原与 Find N5 外屏/展开布局；不负责远控传输。

[English](README.md) · [开发说明](docs/development.md) · [架构](docs/architecture.md) · [Monorepo](../../README.zh-CN.md)

> 实验性源码版。测试与构建通过不代表真实手机首次交互、所有 UU 版本识别、断线/长时使用或完整窗口还原已经验收。

## 构建与测试

需要 macOS 14+、Swift 6 和完整版 Xcode。在本目录执行：

```bash
swift test --jobs 4
scripts/pocketdesk check
scripts/pocketdesk app
```

产物为 `build/PocketDesk.app`，仅本机架构、ad-hoc 签名，未公证。脚本只构建，不安装、启动、申请权限、注册登录项、退出其他应用、创建显示器、移动窗口或上传。PermissionFlow 已内置，不需要全局安装依赖。

主动安装并启动后，需要在系统「隐私与安全」中允许当前应用的「辅助功能／设备控制和数据访问」。重建可能需要重新授权。正常启动会监控真实环境；权限和新鲜自动/显式手动授权满足时，会操作显示器及窗口，因此实机验证须主动安排。

## 使用与安全边界

- 自动建屏须同时确认当前本机被控、明确的移动控制端和当前 UU 服务身份；未知设备、Windows 设备页、历史/单独推流均不构成授权。未知界面拒绝自动激活。
- 手动保持只对本次会话有效，有连接、失联和最长使用时限；关闭、停止、权限或控制端身份变化可撤销。
- **首次、无窗口或恢复失败均不默认弹应用列表。** 保留手机屏桌面与底部 bar，主动点击才选取/切换窗口，列表中可返回窗口或桌面。
- 成功选择后独立记忆窗口；新鲜授权后只恢复同一仍有效实例，不启动已退出应用或猜测同名替代窗口。记忆不授予建屏权限。
- 通用竖屏 **540×950**、Find N5 外屏 **570×1308**、展开 **1080×1191** 均为逻辑尺寸。折叠布局手动选，不按型号猜测折叠状态。
- 移动前保存还原记录；部分或失败还原须保留恢复能力，不同时启动多个进程控制同一份状态。
- 与 Snug 的应用、数据和虚拟屏身份隔离，不导入其状态、不主动退出另一应用、不默认注册登录项；上游更新与旧 `scripts/snug` 安装/发布路径均禁用。

macOS 私有显示 API 与 UU 界面变化都可能影响兼容性。首次 bar 真机交互、完整断线/停止/到期、窗口还原和手机控制 Windows 不误动 Mac，仍需实际设备验收。

## 数据与许可

Bundle ID：`cool.proma.pocketdesk`；数据：`~/Library/Application Support/PocketDesk/`；日志：`~/Library/Logs/PocketDesk.log`。配置、窗口还原、字号和上次选择均为本地运行态，不应提交。

派生自 [Snug](https://github.com/zephyr4123/snug) 2.2.1，基线 `1211dc24555099b05baccc06fe3e6e9477b906d1`。保留 AoyamaShou、zephyr4123 的 [MIT 许可](LICENSE)、原始图标和内部 `Snug*` 模块名，不冒称原创或上游正式更新。内置 PermissionFlow 保留独立许可与改动说明。完整归属见根目录 [NOTICE](../../NOTICE)。
