root = true

[*.cs]
charset = utf-8
indent_style = space
indent_size = 4
insert_final_newline = true

# PRIORITY DOCUMENT AUTHORITY
Any conflict between this document and automated style tools (such as .editorconfig or IDE suggestions) is resolved in favor of this document. This document is the source of truth for style and architectural decisions.

## WPF Architecture: MVPVM (Model-View-Presenter-ViewModel)

### Pattern overview
Spark uses **MVPVM** (Model → View → Presenter → ViewModel) for all WPF windows and dialogs:

- **Model** — Domain objects and service types (`ServiceEndpointConfig`, `ServiceEndpoint`, DTOs). Pure data, no UI awareness.
- **ViewModel** — Bindable projection of UI state. Extends `ObservableObject`. Exposes `ICommand` properties for buttons. Contains zero logic beyond property setters and `CanExecute` predicates. Lives in `Spark\ViewModels\`.
- **Presenter** — Orchestrates behavior. Owns the ViewModel, receives events via commands, calls services, updates the ViewModel. May be async. Lives in `Spark\Presenters\`.
- **View** — XAML + minimal code-behind. The code-behind must contain **only**: `InitializeComponent()`, DataContext assignment (setting the ViewModel), and window lifecycle wiring (e.g., `Loaded += ...`). No logic, no direct element manipulation, no event handlers with business logic.

### Code-behind rules
- Code-behind files (`.xaml.cs`) must be free of all code except `InitializeComponent()`, setting the DataContext to a ViewModel, and trivial lifecycle plumbing.
- All Click handlers, state changes, and data manipulation belong in the Presenter, invoked via `ICommand` bindings or Presenter methods called from ViewModel commands.
- Never reference named XAML elements (`x:Name`) from code-behind for data manipulation. Bind to ViewModel properties instead.
- If a window needs to close itself, expose a `RequestClose` event on the ViewModel that the code-behind subscribes to in the constructor.

### ViewModel rules
- ViewModels expose `ICommand` (via `RelayCommand`) for all user-initiated actions.
- ViewModels expose bindable properties via `ObservableObject.SetField`.
- ViewModels do not call services, start processes, or read files directly.
- ViewModels may reference other ViewModels but never Presenters or Views.

### Presenter rules
- Presenters own a ViewModel instance and populate/mutate it.
- Presenters call services, run processes, read/write files.
- Presenters are constructed with their dependencies (config, services) and the ViewModel.
- Presenters are not referenced from XAML.
- Keep Presenters focused. When a Presenter grows beyond ~200 lines, extract helper classes for distinct concerns (scanning, repair scripts, etc.).

### File size and decomposition
- Inherits all rules from `SparseLattice\CONTRIBUTING.md`.
- Keep file sizes under ~300 lines. When a class exceeds this, extract cohesive sub-concerns into separate files.
- GuideItem, ViewModel, Presenter, and scanner/builder helpers each get their own file.

### C# style
- Inherits all C# style rules from `SparseLattice\CONTRIBUTING.md`.
- All text displayed to users in WPF must be selectable/copyable. Use `TextBox` with `IsReadOnly="True"` styled to look like `TextBlock`, or use the `SelectableText` resource style.
