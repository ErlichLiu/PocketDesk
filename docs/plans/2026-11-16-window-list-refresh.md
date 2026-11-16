# Explicit Window List Refresh Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Opening the macOS picker must start a read-only list refresh even if UU's inferred streamed display differs; startup warmup must survive ordinary hide/reset without hidden polling or extra authorization.

**Architecture:** Extract the existing presentation-to-catalog decision intact into WindowCatalog so fake-service tests exercise the same method the real panel uses. Add a narrow listing-service protocol and an app-model test target; do not change app entry code or invoke it. Then remove the streamed-display gate and separate one-shot warmup cancellation from periodic-refresh cancellation.

**Tech Stack:** Swift Testing, AppKit/Observation models, fake asynchronous listing service, existing SwiftPM and native build scripts.

1. Add the protocol, fake-service tests and app-model target. Preserve old behavior for the red run: mismatched stream blocks refresh and hide/reset cancels warmup. Reproduce those failures without launching an App or enumerating real windows.
2. Refresh whenever an authorized phone display's picker is visible; keep busy pauses and duplicate-task prevention. Hide/reset cancels periodic polling and AX enrichment but not one-shot CG warmup. Shutdown cancels all and permanently stops listing.
3. Verify matching/unknown/different streamed displays, prewarm cancellation, hidden/busy states, repeated presentation and late canceled results. Add request-start timing logs, retain cached data, bump macOS to0.2.5; preserve all authorization, desktop/bar and restoration tests.
4. Run all native/Python tests, Release build and independent review. Ask for scope approval before external push/conditional merge, Actions package generation or installing/restarting the user's running instance. Fake-model success does not prove real-device latency.
