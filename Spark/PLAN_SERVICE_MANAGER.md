# Spark — Local Service Manager Plan

> **Created:** 2026-03-13
> **Status:** In Progress
> **Depends On:** PLAN.md CP-1 through CP-5 (all complete)
> **Scope:** Auto-detect, auto-start, monitor, and visually surface the availability of local AI services (Ollama, Stable Diffusion / Forge, MusicGen). Provide no-exception probing, configurable download sources, and one-click install/run from the UI.

---

## 0. Problem Statement

Currently, Spark silently fails or throws first-chance exceptions when local services (Ollama, Stable Diffusion Forge, MusicGen) aren't running. The user has to manually start each service, know the correct URLs, and debug connectivity issues. This plan adds:

1. **No-exception service probing** — all availability checks swallow errors and return clean status.
2. **Periodic health monitoring** — a background timer re-probes services every N seconds.
3. **Visual status indicators** — stop-sign / green-check icons in the toolbar for each service.
4. **Double-click or context menu to auto-download, install, and run** a missing service.
5. **Configurable endpoints and download sources** — stored in `spark_services.json` alongside the project or in `%AppData%\Spark\`.
6. **Model availability detection** — for Ollama: list installed models; for SD: current checkpoint.

---

## 1. Architecture

### 1.1 `ServiceEndpointConfig` (record, persisted in `spark_services.json`)

```
{
  "ollama": {
    "baseUrl": "http://localhost:11434",
    "autoStart": true,
    "executablePath": "",            // auto-detected or user-set
    "downloadUrl": "https://ollama.com/download/windows",
    "preferredModels": ["llama3", "mistral"]
  },
  "stableDiffusion": {
    "baseUrl": "http://127.0.0.1:7860",
    "autoStart": true,
    "executablePath": "",
    "downloadUrl": "https://github.com/lllyasviel/stable-diffusion-webui-forge/releases",
    "preferredCheckpoint": ""
  },
  "musicGen": {
    "baseUrl": "http://localhost:7860",
    "autoStart": false,
    "executablePath": "",
    "downloadUrl": "https://github.com/facebookresearch/audiocraft",
    "preferredModel": "facebook/musicgen-medium"
  }
}
```

### 1.2 `LocalServiceManager` (singleton service)

- Owns a `DispatcherTimer` (10s interval, configurable).
- Holds `ServiceStatus` objects for each service (Ollama, SD, MusicGen).
- Each `ServiceStatus` has: `IsAvailable`, `StatusText`, `LastChecked`, `Models[]`.
- **Probe methods are fully exception-safe** — wrap all HTTP calls in try/catch, never throw.
- Fires `PropertyChanged` for each status property so the UI can bind directly.
- Provides commands: `ProbeNowCommand`, `StartServiceCommand`, `OpenDownloadPageCommand`.
- On startup, does an initial probe of all services.

### 1.3 `ServiceStatusViewModel` (per-service visual state)

- `IsAvailable` (bool) — drives green/red indicator.
- `StatusText` (string) — e.g. "✓ Ollama online — 3 models" or "🛑 Not running".
- `Models` (ObservableCollection<string>) — for Ollama model list, SD checkpoint.
- `CanAutoStart` (bool) — true if executable path is known.
- Commands: `StartCommand`, `StopCommand`, `DownloadCommand`, `ConfigureCommand`.

### 1.4 UI Integration

- **Toolbar**: Add 3 small status indicators (Ollama, SD, MusicGen) to the toolbar right side.
  - Green circle = online, red stop sign = offline.
  - Tooltip shows status text.
  - Double-click → attempt auto-start or open download page.
  - Right-click context menu: Start, Stop, Configure URL, Open download page.
- **ControlPanel**: Status section shows aggregated service health.
- **MusicPanel/SfxPanel**: Replace inline status with binding to `LocalServiceManager`.

### 1.5 Configuration Storage

- **Global config**: `%AppData%\Spark\spark_services.json` — default URLs, executable paths.
- **Per-project override**: `spark_services.json` in project dir (optional, merges over global).
- Configuration UI accessible from toolbar "⚙ Options" or from service indicator context menu.

---

## 2. Implementation Plan

### Phase A — Core Infrastructure

| # | Task | File(s) |
|---|---|---|
| A1 | Create `ServiceEndpointConfig` record with JSON serialization | `Services/ServiceEndpointConfig.cs` |
| A2 | Create `LocalServiceManager` with timer-based probing | `Services/LocalServiceManager.cs` |
| A3 | Make `OllamaClient` accept configurable base URL from config | `Services/OllamaClient.cs` (minor) |
| A4 | Make `ImageGenerator` accept configurable base URL from config | `ImageGenerator.cs` (minor) |
| A5 | Make `MusicGenClient` accept configurable base URL from config | `Services/MusicGenClient.cs` (minor) |
| A6 | Register `LocalServiceManager` in `AppBootstrap` | `Services/AppBootstrap.cs` |

### Phase B — No-Exception Probing

| # | Task | File(s) |
|---|---|---|
| B1 | Add `SafeProbeAsync()` to each client — returns status, never throws | `Services/OllamaClient.cs`, `ImageGenerator.cs`, `Services/MusicGenClient.cs` |
| B2 | `LocalServiceManager.ProbeAllAsync()` — calls all three, updates status | `Services/LocalServiceManager.cs` |
| B3 | Short HTTP timeout (3s) for probes, separate from generation timeout | All clients |

### Phase C — Visual Status Indicators

| # | Task | File(s) |
|---|---|---|
| C1 | Add service status properties to `StatusViewModel` or new VM | `ViewModels/StatusViewModel.cs` |
| C2 | Add toolbar service indicators to `MainWindow.xaml` | `MainWindow.xaml` |
| C3 | Context menu on indicators: Start, Download, Configure | `MainWindow.xaml` |
| C4 | Wire MusicViewModel/SfxViewModel to use shared status | `ViewModels/MusicViewModel.cs`, `ViewModels/SfxViewModel.cs` |

### Phase D — Auto-Start & Download

| # | Task | File(s) |
|---|---|---|
| D1 | `LocalServiceManager.TryStartServiceAsync()` — launch process | `Services/LocalServiceManager.cs` |
| D2 | Auto-detect installed paths (Ollama: PATH/registry, Forge: known dirs) | `Services/LocalServiceManager.cs` |
| D3 | `OpenDownloadPage()` — launch browser to download URL | `Services/LocalServiceManager.cs` |
| D4 | Model pull for Ollama (`ollama pull <model>`) | `Services/LocalServiceManager.cs` |

### Phase E — Configuration UI

| # | Task | File(s) |
|---|---|---|
| E1 | Load/save `spark_services.json` (global + per-project merge) | `Services/ServiceEndpointConfig.cs` |
| E2 | Options dialog or panel for editing service URLs/paths | Future (use context menu "Configure" for now) |

---

## 3. Exit Criteria

- [x] All service probes are fully exception-safe (no first-chance exceptions in debugger).
- [x] Toolbar shows green/red indicators for Ollama, Stable Diffusion, MusicGen.
- [x] Double-click on a red indicator attempts to start the service or opens download page.
- [x] Service URLs are configurable via `spark_services.json`.
- [x] Health monitoring re-probes every 10 seconds silently.
- [ ] MusicGen/SFX panels show service status from shared manager (not own inline check).
- [x] Ollama model list is surfaced in the service status tooltip.

---

## 4. Naming Conventions

Per `.editorconfig`:
- Private instance fields: `m_` prefix, camelCase.
- Static fields: `s_` prefix, camelCase.
- Constants: PascalCase, no prefix.
- No `var` — use explicit types.

---

## 5. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Auto-start fails on different OS configurations | Graceful fallback to "Open download page"; never block UI |
| MusicGen and SD Forge both default to port 7860 | Config stores separate URLs; probe distinguishes by API shape |
| Timer-based probing adds background HTTP traffic | 10s interval is light; probes use 3s timeout; timer stops when app is minimized (future) |
| Process.Start security restrictions | Catch all exceptions; show user-friendly message |
