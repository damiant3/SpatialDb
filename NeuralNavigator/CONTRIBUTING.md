# Contributing to NeuralNavigator

This project follows `SparseLattice/CONTRIBUTING.md` as the canonical style guide.
Key rules and project-specific additions:

## Style
- Explicit types — no `var`
- File-scoped namespaces with `///` separator matching namespace length
- No comments that describe algorithm steps — expressive names only
- No `/* */` block comments or `#region`
- Single-statement `if`/`else` without braces
- `""` not `string.Empty`
- Empty collection initializer `[]` on the right-hand side
- Remove default accessibility modifiers (class members default to `private`)

## Naming
- Instance fields: `m_` prefix, camelCase
- Static fields: `s_` prefix, camelCase
- `const` fields: PascalCase, no prefix
- Base class: `ObservableObject` (not `ViewModelBase`)
- Command class: `RelayCommand`

## Architecture
- Prefer composition over inheritance
- Prefer returning null/false over throwing for expected failures
- No empty `catch {}` blocks
- `ConcurrentDictionary` for shared/cached/long-lived dictionaries
- Method-local lookup tables and hot-path numerics are exempt
- O(n²) algorithms must include `Slow` in the method name
- Large view models split into partial classes by logical concern:
  `.Loading.cs`, `.Rendering.cs`, `.Selection.cs`, `.Trace.cs`, `.Weights.cs`
- No file should exceed ~500 lines

## Usings
- Omit what `ImplicitUsings` provides
- Sort: System.*, Microsoft.*, External.*, NeuralNavigator.*
- Each group alphabetically

## Project-Specific
- HelixToolkit 3.x types: `System.Numerics.Vector3`, `HelixToolkit.Maths.Color4`
- `Color4Collection` from `HelixToolkit` namespace
- No stale tool/probe projects inside the NeuralNavigator directory
