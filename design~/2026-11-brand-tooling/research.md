# Research, baselines and evidence-backed decisions

## Baselines measured on `main` at `dd9b01d` (2026-11-22)

| Measure | Value | Command / location |
| --- | --- | --- |
| Stale version pins | 4 (`README.md` lines 44 and 59, `Documentation~/install.md` lines 19 and 42 say `0.3.0`; `package.json` says `0.4.0`) | `grep -n "0\.3\.0" README.md Documentation~/*.md` |
| Color literals in editor code | 15 across 2 files; 0 USS files | `grep -cE "new Color\(\|Color32\(\|#[0-9a-fA-F]{6}" Editor/*.cs` |
| C# snippets in docs | 25, none compiled in CI | ` grep -c '```csharp' Documentation~/*.md README.md ` |
| README length | 406 lines, no banner, no docs site | `wc -l README.md` |
| Editor UI technology | 17 editor files use UI Toolkit, 1 uses IMGUI | `grep -l UnityEngine.UIElements Editor/*.cs` |
| DSP graph limits | 16 nodes (input and output count), 32 edges, memory budget; cycles already refused | `Runtime/Core/Dsp/DspGraph.cs`, `Editor/DspGraphEdits.cs:414` |

## Family references

- DxMessaging banner: `docs/images/DxMessaging-banner.svg` on `Ambiguous-Interactive/DxMessaging@master` (800×200, amber
  `#F4A836`, hexagon badge, `data-sync="version"` chip).
- DxMessaging docs: MkDocs Material, `docs/stylesheets/extra.css`. Rules copied: full 1px borders; "Active nav item:
  contained pill, never a left accent bar"; "ADMONITIONS - contained complete border"; slate palette on `#0D1117`.
- DxVisualizer brand folder: `docs/images/brand/` (mark, mark-light, mark-mono, favicon set, OG 1200×630, store
  card/cover/icon, `capture-manifest.json` for automated screenshots). Accent `#5B9DFF`.

## Decisions that depend on outside evidence

| Decision | Evidence | Source |
| --- | --- | --- |
| Organize docs by user need, but label tabs plainly | Diátaxis' four modes (tutorial, how-to, reference, explanation); GitHub recommends it for repo docs; sources warn against a rigid four-folder layout | https://diataxis.fr/ · https://github.blog/developer-skills/documentation-done-right-a-developers-guide/ |
| README banner switches with the theme | `<picture>` + `prefers-color-scheme` is GitHub's supported approach; the `#gh-dark-mode-only` fragment is outdated; GitHub mobile apps may always report light | https://github.blog/developer-skills/github/how-to-make-your-images-in-markdown-on-github-adjust-for-dark-mode-and-light-mode/ |
| Keep the custom graph canvas instead of Unity Graph Toolkit | Graph Toolkit is experimental and needs Unity 6.2+; DxAudio's minimum is 2021.3 | https://docs.unity3d.com/Manual/gtk/gtk-index.html · https://discussions.unity.com/t/unity-s-graph-toolkit-experimental-available-today-in-unity-6-2/1664909 |
| Spike Zensical against MkDocs Material before committing | Material is in maintenance mode (fixes through Nov 2026); Zensical is its successor, before 1.0 | https://sv0-docs.pages.dev/docs/architecture/research/2026-03-11-documentation-framework-comparison |
| DAW patterns for the Mix Monitor (clips with waveforms, drawn fades, automation-style gain lane, channel strips, peak hold, loudness) | Current DAWs (Bitwig 6, Live 12) emphasize sharper visuals, grids visible through clips, and customization; no source covered meter design specifically, so meter conventions follow common practice | https://www.soundonsound.com/reviews/bitwig-studio-6 · https://rekkerd.org/review-bitwig-studio-6/ · https://musicradar.com/news/ableton-live-vs-bitwig-studio |
| Shape + color for severity | Colors that must be told apart need to differ in more than hue; shape keeps them legible for colorblind users and in grayscale | WCAG 1.4.1 Use of Color |
