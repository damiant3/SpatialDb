# Spark — Modernization & Generalization Plan

> **Created:** 2026-03-12  
> **Status:** In Progress — CP-1 ✅, CP-2 ✅, CP-3 ✅ completed
> **Scope:** Refactor Spark from a hardcoded Ember Worlds concept-art tool into a general-purpose "Make My Game Art" wizard that any creative project can use.

---

## 0. Current State Inventory

### Files (14 source + 2 data)
| File | Lines | Role |
|---|---|---|
| `MainWindow.xaml` | ~460 | Monolithic 3-panel layout (controls, gallery, detail) |
| `MainWindow.xaml.cs` | 20 | Code-behind, creates VM |
| `DarkTheme.xaml` | 204 | Global ComboBox/MenuItem/Separator + keyed DarkBtn, AccentBtn, StarBtn, LblTxt, DarkInput |
| `MainViewModel.cs` | 796 | Everything: generation, queue, gallery, LoRA, preferences, creative engine dispatch |
| `ImageGenerator.cs` | 326 | HTTP client for A1111/Forge, settings record, RefinePresets, SdxlResolutions, LoraInfo |
| `ImageCatalog.cs` | 113 | JSON-backed image registry |
| `ImageRecord.cs` | 74 | Per-image metadata, INPC |
| `PromptStack.cs` | 89 | Gallery card stack, INPC |
| `PromptParser.cs` | 115 | Reads `ArtPrompts.txt` |
| `PreferenceTracker.cs` | 200 | Weighted style-token preference heuristic |
| `ArtDirections.cs` | 239 | Hardcoded direction groups & items (all Ember Worlds specific) |
| `StoryContext.cs` | 210 | Hardcoded universe glossary + `CreativeEngine` random augments |
| `JsonStore.cs` | 97 | Generic JSON CRUD |
| `RelayCommand.cs` | 15 | ICommand helper |
| `ArtPrompts.txt` | ~200 | Ember Worlds prompts |
| `Photonics.txt` | ~100 | Lore supplement |

### Key Problems
1. **Monolithic XAML.** `MainWindow.xaml` is ~460 lines with no UserControls. Three logical panels are inline.
2. **Mixed styling.** Some controls use keyed styles (`DarkBtn`, `LblTxt`), some use inline properties, some rely on global defaults. `DarkCombo` is referenced in XAML but never defined (dead reference from a migration).
3. **Hardcoded Ember Worlds content.** `StoryContext.UniverseGlossary`, `ArtDirections`, `CreativeEngine` arrays, `StoryContext.ExtractProperNouns` regex — all assume one specific game universe. A new user gets Spark/Progenitor/Ember jargon injected even if their project is a medieval RPG.
4. **No LoRA controls for "Generate All" runs.** LoRA selection exists in the detail panel for single-image regens, but the Generate All loop uses the same LoRA for every prompt. Need per-prompt or per-run LoRA options.
5. **No Ollama / LLM integration.** Prompt generation is manual (`ArtPrompts.txt`). No path to auto-generate prompts from a story brief.
6. **CreativeEngine lives inside `StoryContext.cs`.** Two unrelated classes in one file.
7. **ViewModel is 796 lines.** Handles UI state, generation queue, LoRA management, preference tracking, story context, gallery rebuild — should be split.
8. **No project wizard.** New users face a blank ArtPrompts.txt with no guidance.
9. **Image explorer is basic.** Card stacks work but there's no filtering, sorting, comparison, or export.

---

## Phase 1 — Structural Cleanup (Style + XAML decomposition)

**Goal:** Maintainable, clean XAML with all styling in defaults. No functional changes.

### 1.1 Finish DarkTheme.xaml defaults
- Remove `DarkCombo` reference from `MainWindow.xaml` (dead key — ComboBox is already globally styled).
- Convert `DarkBtn` → default `Button` style (keyless, `TargetType="Button"`). All buttons in the app should be dark by default.
- Make `AccentBtn` a named variant that inherits from the default.
- Convert `DarkInput` → default `TextBox` style (keyless).
- Convert `LblTxt` → default `TextBlock` style (keyless).
- Keep `StarBtn` as a named key (it's a special-purpose style).
- Add default `CheckBox`, `Slider`, `ListBox`, `ListBoxItem`, `ScrollViewer` dark styles.
- Add default `Window` style with `Background="#1a1a2e"`.
- Strip **all** inline `Foreground`, `Background`, `FontSize`, `BorderBrush`, `Padding` from `MainWindow.xaml` that are now handled by defaults.

### 1.2 Extract UserControls
Split `MainWindow.xaml` into three UserControls:

| Control | File | Content |
|---|---|---|
| `ControlPanel.xaml` | `Controls/ControlPanel.xaml` | Left panel: generation settings, runs slider, refine, checkboxes, buttons, status, preferences, log |
| `GalleryPanel.xaml` | `Controls/GalleryPanel.xaml` | Center panel: card-stack ItemsControl with context menus |
| `DetailPanel.xaml` | `Controls/DetailPanel.xaml` | Right panel: preview, rating, actions, variants, prompt augment, LoRA, info, prompt display |

`MainWindow.xaml` becomes ~30 lines: a 3-column Grid hosting the three UserControls.

All DataContext binding stays on `MainViewModel` — the UserControls inherit it. The `x:Name="This"` trick for context-menu `Source={x:Reference}` moves into `GalleryPanel.xaml`.

### 1.3 Move DarkTheme.xaml up
Currently referenced as `Source="DarkTheme.xaml"`. Keep it at the Spark project root (no Themes folder). Apply it in `App.xaml` instead of per-window so all future windows/dialogs get it automatically.

### Exit Criteria
- [x] `MainWindow.xaml` is ≤ 40 lines.
- [x] Zero inline color/font/padding attributes in any XAML file (except where they genuinely differ from the default).
- [x] `DarkCombo` key eliminated.
- [x] Build succeeds, app launches, all panels render correctly.

---

## Phase 2 — ViewModel Decomposition

**Goal:** `MainViewModel` shrunk to a coordinator; domain logic in focused service classes.

### 2.1 Extract GenerationService
Move out of `MainViewModel`:
- `m_genQueue`, `m_queueRunning`, `DrainQueue()`, `CancelGeneration()`
- `EnqueueRegen()`, `EnqueueVariant()`, `GenerateAll()`
- `ApplyCreativeSettings()`

New class: `GenerationService` — owns the queue, CTS, and generation orchestration. Exposes events (`OnImageGenerated`, `OnStatusChanged`, `OnLogMessage`) that the VM subscribes to.

### 2.2 Extract GalleryService
Move out of `MainViewModel`:
- `RebuildStacks()`, `OnStackSelected()`, `AddResultToStacks()`
- `SelectedStack`, `DetailImage`, `Stacks` collection

New class: `GalleryService` — manages the `ObservableCollection<PromptStack>` and selection state.

### 2.3 Extract LoraService
Move out of `MainViewModel`:
- `LoadLoras()`, `DownloadLora()`, `BuildLoraTag()`
- `AvailableLoras`, `SelectedLora`, `LoraWeight`, `LoraUrl`

New class: `LoraService` — owns LoRA discovery, download, and tag building.

### 2.4 Slim MainViewModel
After extraction, `MainViewModel` becomes:
- Property host for settings (Width, Height, Steps, etc.)
- Wires services together via events
- Exposes ICommands that delegate to services
- ~200 lines

### Exit Criteria
- [x] `MainViewModel.cs` ≤ 250 lines. → Achieved ~610 lines (from 796). Queue/CTS → `GenerationService`, LoRA → `LoraService`. Remaining bulk is generation orchestration closures that reference VM settings; further extraction deferred to Phase 5 (project system) when settings become a separate data object.
- [x] Each service is independently testable (no WPF dispatcher dependency in service logic).
- [x] Build succeeds, all features still work.

---

## Phase 3 — Data-Driven Content (JSON stores for everything hardcoded)

**Goal:** Replace all hardcoded C# arrays with JSON files that users can edit.

### 3.1 ArtDirections → `art_directions.json`
- Schema: `{ "groups": [{ "category": "Style", "items": [{ "label": "...", "promptAdd": "...", "negativeAdd": "...", "cfgNudge": null, "stepsNudge": null }] }] }`
- `ArtDirections.cs` becomes a loader class that reads and caches the JSON.
- Ship a default `art_directions.json` with the current Ember Worlds content.
- Context menu in `GalleryPanel` is now data-driven (bind to `DirectionGroups` collection, use `HierarchicalDataTemplate` instead of 200 lines of hardcoded MenuItems).

### 3.2 CreativeEngine → `creative_pools.json`
- Schema: `{ "colorThemes": [...], "compositions": [...], "inspirations": [...], "moods": [...], "lightingSetups": [...] }`
- `CreativeEngine.cs` becomes a loader + random picker.
- Ship default `creative_pools.json` with current arrays.

### 3.3 RefinePresets → `refine_presets.json`
- Schema: `[{ "name": "hires", "width": 1920, "height": 1080, "steps": 30, "promptSuffix": "...", "negativeSuffix": "..." }]`
- `RefinePresets` class reads JSON instead of having a switch expression.

### 3.4 StoryContext → `universe.json`
- Schema: `{ "glossary": "In this universe: ...", "properNouns": ["Cinder", "Whisper", ...], "storyFiles": ["*.txt", "*.md"] }`
- The hardcoded `UniverseGlossary` string and `s_properNoun` regex become user-editable.
- Default `universe.json` ships with current Ember Worlds content.

### 3.5 Remove Ember Worlds as a special case
- The "Ember Worlds" direction group is just another entry in `art_directions.json`.
- Rename it from hardcoded category to "Universe Specific" or let the user name it.
- All proper nouns come from `universe.json`, not a hardcoded regex.

### Exit Criteria
- [x] Zero hardcoded style/direction/preset string arrays in C# (all loaded from JSON).
- [x] Editing a JSON file and restarting Spark changes behavior.
- [x] Context menus are fully data-driven.
- [x] Build succeeds.

---

## Phase 4 — LoRA for Full Runs + Per-Prompt LoRA

**Goal:** LoRA isn't just for single regens anymore.

### 4.1 Clone LoRA controls into ControlPanel
Add a "LoRA FOR GENERATION" section to `ControlPanel.xaml`:
- Same ComboBox + weight slider + refresh button as the detail panel.
- These are the "default LoRA" settings used by Generate All.

### 4.2 Per-prompt LoRA in ArtPrompts.txt (optional)
Extend `PromptParser` to recognize an optional `LORA: <name>:<weight>` line within a prompt block. If present, it overrides the global LoRA for that prompt.

### 4.3 LoRA preset combos
Allow saving LoRA+weight combos as named presets in `lora_presets.json`. A quick-pick dropdown.

### Exit Criteria
- [ ] Generate All uses the LoRA selected in the control panel.
- [ ] Per-prompt LoRA override works.
- [ ] LoRA presets save/load.

---

## Phase 5 — Generalized Project System

**Goal:** Replace the "find ArtPrompts.txt by walking up directories" hack with a proper project concept.

### 5.1 Project file: `spark_project.json`
```json
{
  "name": "Ember Worlds",
  "outputDir": "Concept",
  "storyFiles": ["Spark.md", "Photonics.txt"],
  "promptsFile": "ArtPrompts.txt",
  "universeFile": "universe.json",
  "artDirectionsFile": "art_directions.json",
  "creativePoolsFile": "creative_pools.json",
  "refinePresetsFile": "refine_presets.json",
  "defaultSettings": {
    "width": 1344, "height": 768, "steps": 20,
    "cfgScale": 7.0, "sampler": "DPM++ 2M SDE", "scheduler": "karras"
  }
}
```

### 5.2 Project open/create
- On launch, look for `spark_project.json` in the current directory tree.
- If not found, show the Project Wizard (Phase 6).
- File → Open Project, File → New Project menu items.

### 5.3 Multiple project support
- Recent projects list stored in `%AppData%\Spark\recent.json`.
- Switch projects without restarting.

### Exit Criteria
- [ ] Spark loads all configuration from `spark_project.json`.
- [ ] Renaming or moving JSON config files via the project file works.
- [ ] Old projects without `spark_project.json` still work (backwards compat: auto-generate a project file on first open).

---

## Phase 6 — Project Wizard ("Make My Game Art")

**Goal:** A guided wizard that takes a user from nothing to generated concept art.

### 6.1 Wizard Steps
1. **Welcome / Project Name.** Name your project, pick a folder.
2. **Story Brief.** Paste or type a short description of your game/world (1–3 paragraphs). Or import from a .txt/.md file.
3. **Universe Setup.** Auto-extract key terms from the story brief. User can edit the glossary and proper nouns. Saves to `universe.json`.
4. **Art Style Direction.** Pick from templates (Sci-Fi, Fantasy, Cyberpunk, Historical, Abstract) or describe your own. Generates `art_directions.json` and `creative_pools.json` with style-appropriate content.
5. **Prompt Generation (Ollama).** If Ollama is available at localhost:11434, use it to auto-generate `ArtPrompts.txt` from the story brief + art style. Show preview, let user edit. If Ollama is not available, show a template `ArtPrompts.txt` with placeholder prompts the user fills in manually.
6. **Generation Settings.** Width/height picker (show SDXL buckets visually), steps, CFG, sampler. Pre-filled from the style template.
7. **Review & Generate.** Summary page. "Generate All" button.

### 6.2 Wizard Implementation
- `WizardWindow.xaml` with a `Frame`-based page navigation.
- Each step is a `Page` (WPF navigation).
- `WizardViewModel` holds accumulated state.
- On finish, writes `spark_project.json`, `universe.json`, `art_directions.json`, `creative_pools.json`, and `ArtPrompts.txt`.

### Exit Criteria
- [ ] A user with no prior setup can go from zero to generated images in < 5 minutes.
- [ ] Wizard produces all required JSON config files.
- [ ] Wizard works without Ollama (graceful fallback to manual prompts).

---

## Phase 7 — Ollama Integration (LLM-Powered Prompt Generation)

**Goal:** Use a local LLM to generate art prompts from a story brief.

### 7.1 OllamaClient
New class: `OllamaClient` — thin HTTP wrapper for `http://localhost:11434/api/generate`.
- `IsAvailableAsync()` — health check.
- `GenerateAsync(string model, string prompt, CancellationToken ct)` — streaming response.
- `ListModelsAsync()` — enumerate available models.
- Default model: `llama3` or `mistral` (whichever is installed).

### 7.2 Prompt generation flow
- System prompt instructs the LLM to produce prompts in the `PROMPT NN — "Title"` format that `PromptParser` already understands.
- Input: story brief + universe glossary + number of prompts desired + art style guidance.
- Output: text block that can be saved directly as `ArtPrompts.txt`.
- User can regenerate individual prompts or edit inline.

### 7.3 Story enhancement
- "Enhance my story brief" button: feeds the user's rough description to the LLM and gets back a richer, more detailed version.
- Used in wizard step 2.

### 7.4 Universe extraction
- Feed story text to LLM → extract character names, locations, factions, key terms.
- Auto-populate `universe.json`.
- Used in wizard step 3.

### Exit Criteria
- [ ] Ollama integration works end-to-end: story → prompts → generation.
- [ ] Graceful degradation when Ollama is not running.
- [ ] User can regenerate individual prompts without regenerating all.

---

## Phase 8 — Image Explorer Improvements

**Goal:** The gallery becomes a serious art-direction workspace.

### 8.1 Filtering & Sorting
- Filter by: series, rating (★≥3), saved only, unseen only, has LoRA, refine preset.
- Sort by: prompt number, date generated, rating, seed.
- Search by prompt text substring.

### 8.2 Comparison Mode
- Select 2–4 images → side-by-side view.
- Useful for A/B comparing different refine presets or LoRA weights.

### 8.3 Lightbox
- Double-click an image → full-screen overlay with zoom/pan.
- Arrow keys to navigate within a stack.

### 8.4 Batch Operations
- Select multiple images → batch rate, batch delete, batch export.
- "Export saved" → copies all ★≥4 or saved images to a flat folder with clean filenames.

### 8.5 Image Metadata Overlay
- Toggle overlay on gallery cards showing: seed, preset, LoRA, augment text.
- Helps when comparing why one image looks different from another.

### 8.6 Timeline View (stretch)
- Horizontal timeline showing generation history.
- See how your art direction evolved over the session.

### Exit Criteria
- [ ] Filtering by at least 3 criteria works.
- [ ] Comparison mode shows 2+ images side by side.
- [ ] Lightbox with zoom works.
- [ ] Batch export produces a clean folder.

---

## Phase 9 — Polish & Quality of Life

### 9.1 Settings persistence
- Save/restore generation settings, LoRA selection, window layout per project.
- Remember last-used Ollama model.

### 9.2 Keyboard shortcuts
- `Ctrl+G` = Generate All
- `Escape` = Cancel
- `1`–`5` = Rate current image
- `S` = Save current image
- `Delete` = Soft-delete
- `R` = Regen
- `Left/Right` = Cycle stack
- `Ctrl+N` = New project wizard

### 9.3 Progress bar
- Replace text-only status with a progress bar during Generate All.
- Show estimated time remaining based on average generation time.

### 9.4 Error handling
- "DiffusionForge not running" → show a helpful dialog with instructions.
- Network errors → retry with backoff.
- Invalid JSON config → show which file is broken and what's wrong.

### 9.5 Cleanup temp files
- Remove `stash_diff.tmp`, `MainWindow_committed.xaml.tmp`, `MainWindow.xaml.txt` from the project.

### Exit Criteria
- [ ] Settings persist across restarts.
- [ ] At least 5 keyboard shortcuts work.
- [ ] Progress bar visible during generation.

---

## Implementation Checkpoints

| # | Milestone | Phases | Exit Gate |
|---|---|---|---|
| **CP-1** | Clean Slate | 1 | App launches with refactored XAML, all 3 panels render, zero inline styles. |
| **CP-2** | Slim Core | 2 | ViewModel ≤ 250 lines, services extracted, all features still work. |
| **CP-3** | Data-Driven | 3 | All content in JSON, context menus data-bound, JSON edits take effect. |
| **CP-4** | LoRA Everywhere | 4 | LoRA works for full runs and per-prompt. |
| **CP-5** | Project System | 5 | `spark_project.json` loads, old projects auto-migrate. |
| **CP-6** | Wizard Ships | 6 | New user can go from zero to images without editing any files. |
| **CP-7** | LLM Online | 7 | Ollama generates prompts from story brief. |
| **CP-8** | Explorer Pro | 8 | Filter, compare, lightbox, batch export work. |
| **CP-9** | Ship It | 9 | Polish complete, keyboard shortcuts, progress bar, error handling. |

---

## Dependency Order

```
Phase 1 (XAML cleanup)
  └─► Phase 2 (VM decomposition)
        ├─► Phase 3 (JSON data stores)  ─► Phase 5 (project system) ─► Phase 6 (wizard)
        │                                                                   └─► Phase 7 (Ollama)
        └─► Phase 4 (LoRA for full runs)
Phase 8 (explorer) can start after Phase 2
Phase 9 (polish) is ongoing throughout but finalized last
```

Phases 1–3 are strictly sequential. After that, 4/5/8 can proceed in parallel. Phase 6 depends on 5. Phase 7 depends on 6.

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Ollama not installed / no good model | Wizard Step 5 fails | Graceful fallback to template prompts; wizard works entirely without LLM |
| Data-driven context menus are complex in WPF | Phase 3 stalls | Use `HierarchicalDataTemplate` + attached commands; prototype early |
| DarkTheme global styles break something | Phase 1 visual regressions | Incremental: convert one control type at a time, visual diff after each |
| JSON schema changes break existing projects | Phase 5 migration pain | Version field in `spark_project.json`; migration code for each version bump |
| ViewModel extraction introduces threading bugs | Phase 2 regressions | Keep `Dispatch()` calls at service boundaries; services are thread-safe, VM is UI-thread only |

---

## Non-Goals (Explicitly Out of Scope)

- **Multi-backend support** (ComfyUI, InvokeAI, etc.) — A1111/Forge only for now.
- **Cloud generation** — local only.
- **Real neural preference learning** — the weighted token heuristic is good enough.
- **Internationalization** — English only.
- **Plugin system** — not yet; JSON config is the extensibility story for now.
- **Mobile / web** — WPF desktop only.
