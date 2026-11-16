// 菜单栏：只用来看状态、打开设置、退出。Snug 的操作都在手机上，Mac 这边不用管。
// 每次展开都按 AppState 现做，和设置窗口永远一致
import AppKit
import Observation
import SnugCore

@MainActor
final class StatusBarController: NSObject, NSMenuDelegate {
    private let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let state: AppState
    private let intents: any AppIntents
    private let navigation: any Navigation

    init(state: AppState, intents: any AppIntents, navigation: any Navigation) {
        self.state = state
        self.intents = intents
        self.navigation = navigation
        super.init()
        let menu = NSMenu()
        menu.delegate = self
        menu.autoenablesItems = false
        item.menu = menu
        item.button?.image = Self.icon
        refreshButton()
    }

    /// 菜单栏图标：SF Symbols 的 iphone.gen3（系统图标本身就是模板图，深色菜单栏自动反白）
    private static let icon: NSImage? = NSImage(systemSymbolName: "iphone.gen3", accessibilityDescription: "PocketDesk")?
        .withSymbolConfiguration(.init(pointSize: 15, weight: .medium))

    /// 关着手机屏时图标变灰；提示文字随状态变
    private func refreshButton() {
        withObservationTracking {
            item.button?.appearsDisabled = !state.config.phoneScreen || !state.accessibilityTrusted
            item.button?.toolTip = "PocketDesk：\(StatusText(state).headline)"
        } onChange: { [weak self] in
            DispatchQueue.main.async { MainActor.assumeIsolated { self?.refreshButton() } }
        }
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let text = StatusText(state)
        menu.addItem(info(text.headline))
        if let detail = text.detail { menu.addItem(info(detail)) }
        if state.legacyAppRunning { menu.addItem(info("检测到伴侣冲突，请自行退出对方后重启 PocketDesk")) }
        menu.addItem(.separator())
        if !state.manualPhoneScreen {
            let enable = action("本次手动保持手机屏", #selector(startPhone), symbol: "hand.raised")
            enable.isEnabled = state.config.phoneScreen && state.accessibilityTrusted && !state.quitting
            menu.addItem(enable)
        }
        if state.manualPhoneScreen || state.phone.screen != .off {
            menu.addItem(action("停止本次手机屏", #selector(stopPhone), symbol: "stop.circle"))
        }
        let layoutItem = NSMenuItem(title: "屏幕布局", action: nil, keyEquivalent: "")
        let layoutMenu = NSMenu()
        for (index, layout) in PhoneLayout.allCases.enumerated() {
            let option = action(layout.title, #selector(selectLayout(_:)), symbol: "rectangle.split.2x1")
            option.tag = index
            option.state = state.config.phoneLayout == layout ? .on : .off
            option.isEnabled = !state.quitting && state.phone.work == nil
            layoutMenu.addItem(option)
        }
        layoutItem.submenu = layoutMenu
        menu.addItem(layoutItem)
        menu.addItem(action("设置…", #selector(openSettings), key: ",", symbol: "gearshape"))
        menu.addItem(action("退出 PocketDesk", #selector(quit), key: "q", symbol: "power"))
    }

    private func info(_ title: String) -> NSMenuItem {
        let it = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        it.isEnabled = false
        return it
    }

    private func action(_ title: String, _ sel: Selector, key: String = "", symbol: String) -> NSMenuItem {
        let it = NSMenuItem(title: title, action: sel, keyEquivalent: key)
        it.target = self
        it.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)
        return it
    }

    @objc private func selectLayout(_ sender: NSMenuItem) {
        guard PhoneLayout.allCases.indices.contains(sender.tag) else { return }
        intents.setPhoneLayout(PhoneLayout.allCases[sender.tag])
    }
    @objc private func startPhone() { intents.startManualPhoneScreen() }
    @objc private func stopPhone() { intents.stopPhoneScreen() }
    @objc private func openSettings() { navigation.openSettings() }
    @objc private func quit() { intents.quit() }
}
