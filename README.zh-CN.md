# PocketDesk

面向手机与折叠屏远程操作的桌面工作区。仓库采用 **monorepo**，Windows 使用原生 .NET/WPF，macOS 目录预留，稍后独立整合。

[English](README.md) · [Windows 使用与开发](apps/windows/README.md) · [架构](docs/architecture.md)

> 实验版。PocketDesk 不负责视频推流，不替代 UU 远程；macOS 尚未导入，不能宣称已经支持。

## 已实现

- 在现有虚拟副屏上显示大按钮窗口列表，选择应用后铺满可用区域。
- 底栏返回列表或还原到电脑，恢复原有位置、最大化/最小化状态。
- 移动前保存恢复记录，核对恢复后才销账；未恢复成功会取消退出。
- OPPO Find N5 展开比例 **2248:2480**，基准 **1080×1191**；另有普通竖屏预设。
- 手动常驻，不把暂时无操作当成断连；默认保持 UU 原生手势，不自动改字号。

**手机端需要在 UU 工具栏手动选择虚拟显示器。** 只启动应用不会自动切换 UU 的观看画面，也不会把虚拟屏设为 Windows 主屏。

## 目录

```text
apps/windows     Windows 应用、测试与用户级安装脚本
apps/macos       macOS 版本预留，尚未实现
drivers/windows  未验证可加载的 IddCx 实验驱动，单独采用 MS-PL
docs             跨平台边界与设计说明
tools            提交前安全与文件范围检查
licenses         上游许可证
```

不要为了 monorepo 空建共享包或引入 Node；出现真实跨平台契约后再提炼。

## Windows 构建与安装

需要 Windows 10/11 x64、.NET 8 SDK，以及独立安装、正常工作的 [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)。

在仓库根执行：

```powershell
dotnet build apps/windows/PocketDesk.Windows.sln -c Release
powershell -NoProfile -File apps/windows/scripts/Test-PocketDesk.ps1
powershell -NoProfile -File apps/windows/scripts/Build-PocketDesk.ps1
powershell -NoProfile -File apps/windows/scripts/Install-PocketDesk.ps1
```

应用安装到 `%LOCALAPPDATA%\Programs\PocketDesk`，创建开始菜单入口。脚本不装驱动、不改证书、不修改主屏或开机启动。更新前先通过应用安全退出；`-NoLaunch` 可以只安装不运行。

## 当前限制

- 必须已有可用虚拟屏。本仓库实验驱动仍存在 Code 31，**不是可用的一键装屏方案**。
- 无可靠的 UU 断连/手机身份判断，没有自动主屏切换。
- 手势和字号实验代码默认不启用；测试替身通过不代表手机实机触控已验证。
- 远程使用时不要擅自重启 UU、显示驱动、资源管理器或电脑。
- 普通权限无法可靠操作管理员窗口；确实需要时由用户显式提权运行。

## 安全与开源

提交前运行 `python tools/check_publication.py --staged` 并用 Gitleaks 扫描。配置、恢复账本、日志、密钥、证书和构建产物不得提交。

应用采用 [MIT](LICENSE)；`drivers/windows` 改编自微软示例，采用独立的 [MS-PL](drivers/windows/LICENSE)，不是全仓库统一 MIT。参考项目 [Snug](https://github.com/zephyr4123/snug) 的版权与许可保留在 [NOTICE](NOTICE) 和 `licenses/` 中。VDD 和 UU 不随源码分发。
