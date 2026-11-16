// 首版没有在线更新或下载入口。保留渲染接口，避免旧菜单、测试状态误接回上游安装链。
import AppKit
import SwiftUI
import SnugCore

extension UpdateState {
    var summary: String { "首版仅支持本地构建，在线更新已停用" }
}

struct UpdateSection: View {
    let state: AppState
    let intents: any AppIntents
    let navigation: any Navigation

    var body: some View {
        Section {
            SettingLabel("软件更新", detail: state.update.summary, symbol: "arrow.down", tint: .gray)
        }
    }
}

struct UpdateView: View {
    let state: AppState
    let current: Version
    let intents: any AppIntents
    let close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("PocketDesk 本地构建").font(.title3.bold())
            Text("在线检查、下载和安装已停用。请从当前项目显式构建；不会连接 Snug 更新源。")
                .foregroundStyle(.secondary)
            HStack {
                Spacer()
                Button("关闭", action: close).keyboardShortcut(.cancelAction)
            }
        }
        .padding(20)
        .frame(width: 480)
    }
}

@MainActor
final class UpdateWindowController {
    private let state: AppState
    private let intents: any AppIntents
    private var window: NSWindow?

    init(state: AppState, intents: any AppIntents) {
        self.state = state
        self.intents = intents
    }

    func show(activating: Bool) {
        guard let current = Updater.current else { return }
        if window == nil {
            let view = UpdateView(state: state, current: current, intents: intents) { [weak self] in self?.window?.close() }
            window = NSWindow(hosting: view, title: "PocketDesk 本地构建")
        }
        window?.present(activating: activating)
    }
}
