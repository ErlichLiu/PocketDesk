#!/usr/bin/env python3
"""生成手机屏的 EDID 块（C 头文件）。

只描述一个首选时序 1080×1900@60（CVT-RB），显示器名 "PocketDesk"，
其余档位由驱动在 EvtIddCxParseMonitorDescription 里按同一宽高比上报。
EDID 里的名字只能是 ASCII，所以中文名「PocketDesk 手机屏」留给应用层做展示，
系统显示设置与 UU远程 的屏幕列表里看到的是 "PocketDesk"。
"""
from pathlib import Path

H_ACTIVE = 1080
V_ACTIVE = 1900
H_BLANK, H_FRONT, H_SYNC = 160, 48, 32
V_BLANK, V_FRONT, V_SYNC = 23, 3, 5
VSYNC = 60

MANUFACTURER = "PKD"
PRODUCT = 0x5044  # 'PD' (experimental, not a registered product ID)
SERIAL = 1
NAME = "PocketDesk"


def manufacturer_id(code: str) -> int:
    a, b, c = (ord(ch) - ord("A") + 1 for ch in code)
    return (a << 10) | (b << 5) | c


def detailed_timing() -> list[int]:
    pixel_clock = (H_ACTIVE + H_BLANK) * (V_ACTIVE + V_BLANK) * VSYNC
    clock_10khz = round(pixel_clock / 10000)
    # 220ppi 折算出的物理尺寸，Windows 用它在「缩放」里给出合适的建议值
    h_mm = round(H_ACTIVE / 220 * 25.4)
    v_mm = round(V_ACTIVE / 220 * 25.4)
    return [
        clock_10khz & 0xFF, (clock_10khz >> 8) & 0xFF,
        H_ACTIVE & 0xFF, H_BLANK & 0xFF,
        ((H_ACTIVE >> 8) << 4) | ((H_BLANK >> 8) & 0x0F),
        V_ACTIVE & 0xFF, V_BLANK & 0xFF,
        ((V_ACTIVE >> 8) << 4) | ((V_BLANK >> 8) & 0x0F),
        H_FRONT & 0xFF, H_SYNC & 0xFF,
        ((V_FRONT & 0x0F) << 4) | (V_SYNC & 0x0F),
        ((H_FRONT >> 8) << 6) | (((H_SYNC >> 8) & 0x03) << 4) | (((V_FRONT >> 4) & 0x03) << 2) | ((V_SYNC >> 4) & 0x03),
        h_mm & 0xFF, v_mm & 0xFF,
        ((h_mm >> 8) << 4) | ((v_mm >> 8) & 0x0F),
        0, 0,
        0x1A,  # 数字输入，水平/垂直同步均为正
    ]


def monitor_name() -> list[int]:
    text = (NAME + "\n").encode("ascii")[:13].ljust(13, b" ")
    return [0, 0, 0, 0xFC, 0] + list(text)


def range_limits() -> list[int]:
    # 60~60Hz，水平 60~70kHz，最大像素时钟 200MHz
    return [0, 0, 0, 0xFD, 0, 60, 60, 60, 70, 20, 0, 0, 0, 0, 0, 0, 0, 0]


def build() -> list[int]:
    """按 EDID 1.4 的字段顺序拼接，每段单独构造，避免手写长度出错。"""
    header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00]
    vendor = [manufacturer_id(MANUFACTURER) >> 8 & 0xFF, manufacturer_id(MANUFACTURER) & 0xFF]
    product = [PRODUCT & 0xFF, (PRODUCT >> 8) & 0xFF]
    serial = [SERIAL & 0xFF, (SERIAL >> 8) & 0xFF, 0x00, 0x00]
    week_year = [0x01, 0x28]          # 第 1 周，2026 年（1990 + 36）
    version = [0x01, 0x04]            # EDID 1.4
    video_input = [0xA5]              # 数字输入，8bit/色，DisplayPort
    sizes = [round(H_ACTIVE / 220 * 25.4) & 0xFF, round(V_ACTIVE / 220 * 25.4) & 0xFF]
    gamma = [0x78]                    # 2.2
    features = [0x0A]                 # 首选时序 + 支持 sRGB
    chromaticity = [0x50, 0xA0, 0x23, 0x0B, 0x50, 0x54, 0x00, 0x02, 0x00, 0xD1]
    established = [0xC0, 0x01, 0x01]  # 只有首选时序，其余为空（0x01 表示未使用）
    standard = [0x01] * 16
    edid = (header + vendor + product + serial + week_year + version + video_input + sizes + gamma
            + features + chromaticity + established + standard)
    edid += detailed_timing()
    edid += monitor_name()
    edid += range_limits()
    edid += [0x00, 0x00, 0x00, 0x10, 0x00] + [0] * 13  # 第四个描述符：占位
    edid += [0x00]                                     # 扩展块数量
    assert len(edid) == 127, len(edid)
    edid.append((256 - (sum(edid) % 256)) % 256)
    return edid


def main() -> None:
    edid = build()
    rows = []
    for i in range(0, 128, 19):
        chunk = edid[i:i + 19]
        rows.append("    " + ",".join(f"0x{b:02X}" for b in chunk))
    body = ",\n".join(rows)
    out = f"""// 由 scripts/gen-edid.py 生成，不要手改。
// 手机屏的 EDID：首选时序 {H_ACTIVE}x{V_ACTIVE}@{VSYNC}，显示器名 "{NAME}"。
#pragma once

namespace PocketDeskIdd
{{
    static constexpr unsigned char szEdidBlock[128] =
    {{
{body}
    }};
}}
"""
    target = Path(__file__).resolve().parent.parent / "idd" / "PocketDeskEdid.h"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(out, encoding="utf-8")
    total = sum(edid) % 256
    print(f"已写入 {target}（校验和 {total}, 应为 0），{H_ACTIVE}x{V_ACTIVE}@{VSYNC}")


if __name__ == "__main__":
    main()
