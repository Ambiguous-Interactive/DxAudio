# DxAudio brand, docs and editor suite: design bundle (2026-11)

This folder holds everything from the 2026-11-22 design session, so you can implement it without the original session or the
canvas. It is reference material for the tracking issues, not shipped package content.

| Folder | What it holds |
| --- | --- |
| `png/` | A full-page screenshot of every artboard at its design width, plus `*-phone.png` at 400 px for the fluid pages |
| `html/` | Static snapshots of every artboard (rendered DOM, scripts removed). They open in any browser offline except for Google Fonts. Interactive states are frozen in their default state. |
| `source/` | The original `.dc.html` artboard sources and `canvas.json`. The interactive behavior (state, click handlers) lives in each file's `<script type="text/x-dc">` block, so read them to see what each control should do. They need the design-canvas runtime to run, so use `html/` to look at them. |
| `brand/` | Production-ready drafts: `dxaudio-mark.svg`, `dxaudio-mark-light.svg`, `dxaudio-banner.svg` (800×200), and `tokens.json`, the seed for the brand tokens milestone (A1) |
| `research.md` | Sources, the measured baselines, and the decisions that depend on outside evidence |
| `render-report.json` | Render check output: frame size, rendered size, horizontal overflow, phone-width scroll width, console errors |
| `dxaudio-design-bundle.zip` | This whole folder in one download |

## Artboards

| Artboard | Screenshot | Snapshot | Source | Interactive in source |
| --- | --- | --- | --- | --- |
| Brand system | `png/Main.png` | `html/Main.html` | `source/Main.dc.html` | Accent swatch tweak |
| Roadmap | `png/Roadmap.png` | `html/Roadmap.html` | `source/Roadmap.dc.html` | No |
| GitHub README | `png/Readme.png` | `html/Readme.html` | `source/Readme.dc.html` | No |
| Docs home | `png/Docs.png` | `html/Docs.html` | `source/Docs.dc.html` | No |
| Docs guide page | `png/DocsPage.png` | `html/DocsPage.html` | `source/DocsPage.dc.html` | No |
| Studio | `png/Studio.png` | `html/Studio.html` | `source/Studio.dc.html` | Take select, play, silence, show info |
| Signal Graph | `png/Graph.png` | `html/Graph.html` | `source/Graph.dc.html` | Select node, solo, bypass, audition, silence |
| Mix Monitor | `png/Monitor.png` | `html/Monitor.html` | `source/Monitor.dc.html` | Clip select, capture, bus mute, drop-reason silence |
| Problems | `png/Problems.png` | `html/Problems.html` | `source/Problems.dc.html` | Severity per rule, reset |

## System at a glance

- **Family grammar (shared with DxMessaging and DxVisualizer):** ground `#0D1117`, a flat-top hexagon ring in the product
  accent with the glyph in its light tint; Space Grotesk titles, IBM Plex Sans body, JetBrains Mono code.
- **DxAudio accent:** signal teal `#2FD4B4`, tint `#A8F0E0`. The glyph is a five-bar waveform, so it never reads as
  DxMessaging's emitter arcs. DxTerminal's accent was not found; confirm that it is not teal or green before shipping.
- **Signal colors:** SFX teal, Music `#8FA8FF`, Ducks `#C9A7FF`, Effects `#F2C58A`.
- **Severity:** error `#FF7B72` circle, warning `#FFA657` diamond, info `#B4BCC8` square. Severity never reuses a signal color,
  and its shape always goes with its color.
- **Editor windows** keep Unity's skin grays. The brand shows up only in tab strips, selection, and signal and severity marks.
- **Docs site** matches DxMessaging's `extra.css`: full 1px borders on every box (never a top-only or left accent bar),
  contained-pill active nav, callouts with a complete border. Section tabs: Home, Getting Started, Guides, Concepts,
  Editor Tools, Reference, Troubleshooting.
- **Problems** are DXA rule IDs over checks that already exist (`DxAudioValidation`, DSP chain slots, `SfxDropReason`).
  Severity is set per project. Silencing only hides a problem and changes no behavior: a silence for one asset is shared
  and needs a reason, and a hide for yourself stays in your own EditorPrefs.

## Contrast checks (WCAG 2.x, computed)

| Pair | Ratio |
| --- | --- |
| White on badge fill `#12705F` | 5.99 (the old `#1A8F7A` was 4.00 and failed) |
| Muted `#9AA3AF` on ground | 7.42 |
| Accent on ground | 10.09 |
| Warning `#FFA657` on warning box `#2D2418` | 7.87 |
| Info `#B4BCC8` on info box `#23262B` | 7.93 |
| Editor muted `#9A9A9A` on `#232323` | 5.59 |
| Light mark ring `#0F8F78` on white (graphic, needs 3:1) | 4.02 |
