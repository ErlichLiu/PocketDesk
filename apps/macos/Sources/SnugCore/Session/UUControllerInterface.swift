import Foundation

/// A bounded AX snapshot. This type never accesses the system or activates UU.
public struct UUInterfaceNode: Equatable, Sendable {
    public var role: String
    public var text: String
    public var children: [UUInterfaceNode]

    public init(role: String, text: String = "", children: [UUInterfaceNode] = []) {
        self.role = role
        self.text = text.trimmingCharacters(in: .whitespacesAndNewlines)
        self.children = children
    }

    fileprivate var descendants: [Self] { [self] + children.flatMap(\.descendants) }
}

public enum UUControllerInterface {
    /// Match complete local sections, never arbitrary rows or a shared sidebar ancestor.
    public static func localControllers(in windows: [UUInterfaceNode]) -> ControllerTable {
        var matches: [ControllerTable] = []
        func accept(_ table: ControllerTable) {
            if case .shown = table, !matches.contains(table) { matches.append(table) }
        }
        for root in windows where root.role == "AXWindow" {
            accept(windowDetail(root.children))
            // Preserve the original narrow compact-section format; do not broaden it.
            for group in root.descendants where group.role == "AXGroup" {
                let nodes = group.descendants
                let tables = nodes.filter { $0.role == "AXTable" }
                guard tables.count == 1 else { continue }
                func outsideTables(_ node: UUInterfaceNode) -> [String] {
                    guard node.role != "AXTable" else { return [] }
                    return (node.role == "AXStaticText" ? [node.text] : []) + node.children.flatMap(outsideTables)
                }
                accept(ControllerTable(localSectionLabels: outsideTables(group), tableCount: 1,
                                       rows: rows(in: tables[0])))
            }
        }
        return matches.count == 1 ? matches[0] : .unrecognized
    }

    private static func windowDetail(_ children: [UUInterfaceNode]) -> ControllerTable {
        let headings = children.indices.filter { children[$0].role == "AXStaticText" && children[$0].text == "本机被控中" }
        guard headings.count == 1, let heading = headings.first else { return .unrecognized }
        // In the observed UU layout, incoming details are direct window siblings,
        // after the split-view sidebar and before the toolbar. Nested outgoing tables do not qualify.
        let section = Array(children[heading...].prefix {
            $0.role != "AXToolbar" && $0.role != "AXSplitGroup" && $0.role != "AXWindow"
        })
        guard section.allSatisfy({ ["AXStaticText", "AXImage", "AXButton", "AXTable"].contains($0.role) }) else {
            return .unrecognized
        }
        let tables = section.indices.filter { section[$0].role == "AXTable" }
        let disconnects = section.indices.filter { section[$0].role == "AXButton" && section[$0].text == "断开所有远控" }
        let counts = section.indices.compactMap { index -> (Int, Int)? in
            guard section[index].role == "AXStaticText", let count = localCount(section[index].text) else { return nil }
            return (index, count)
        }
        guard tables.count == 1, let table = tables.first, disconnects.count == 1,
              let disconnect = disconnects.first, counts.count == 1, let count = counts.first,
              count.0 < disconnect, disconnect < table,
              section.contains(where: { $0.role == "AXStaticText" && $0.text == "本账号设备" }) else {
            return .unrecognized
        }
        for node in section {
            if node.role == "AXStaticText" {
                guard ["本机被控中", "本账号设备"].contains(node.text) || localCount(node.text) != nil || duration(node.text) else {
                    return .unrecognized
                }
            }
            if node.role == "AXButton", !node.text.isEmpty, node.text != "断开所有远控" { return .unrecognized }
        }
        let rows = rows(in: section[table])
        guard rows.count == count.1, rows.allSatisfy({ row in
            row.count == 3 && !row[0].isEmpty && row[1] == "远程控制"
                && row[2].hasPrefix("连接时长 ") && duration(String(row[2].dropFirst("连接时长 ".count)))
        }) else { return .unrecognized }
        return ControllerTable(rows: rows)
    }

    private static func rows(in table: UUInterfaceNode) -> [[String]] {
        table.descendants.filter { $0.role == "AXRow" }.map {
            $0.descendants.filter { $0.role == "AXStaticText" }.map(\.text).filter { !$0.isEmpty }
        }
    }

    private static func localCount(_ text: String) -> Int? {
        let compact = text.filter { !$0.isWhitespace }
        guard compact.range(of: "^当前有[1-9][0-9]{0,2}台设备正在控制此电脑$", options: .regularExpression) != nil else { return nil }
        return Int(compact.dropFirst("当前有".count).prefix(while: \.isNumber))
    }

    private static func duration(_ text: String) -> Bool {
        text.range(of: "^[0-9]{2,}:[0-5][0-9](:[0-5][0-9])?$", options: .regularExpression) != nil
    }
}
