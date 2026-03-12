# Contributing to NeuralNavigator

This project follows the same conventions as `SparseLattice/CONTRIBUTING.md`.
Key rules summarized here for quick reference:

## Style
- Explicit types everywhere — no `var`
- File-scoped namespaces with `///` separator line matching namespace length
- No comments that describe algorithm steps — use expressive names instead
- No `/* */` block comments or `#region`
- Single-statement `if`/`else` without braces
- `""` not `string.Empty`
- Empty collection initializer `[]` on the right-hand side

## Naming
- Instance fields: `m_` prefix, camelCase (e.g., `m_viewport`)
- Static fields: `s_` prefix, camelCase (e.g., `s_singleton`)
- `const` fields: PascalCase, no prefix (e.g., `PitchClamp`)
- Expressive names — no abbreviations except widely recognized ones (`Id`)

## Architecture
- Prefer composition over inheritance
- Prefer returning null/false over throwing for expected failures
- No empty `catch {}` blocks
- Use `ConcurrentDictionary` for shared/cached/long-lived dictionaries
- Method-local lookup tables and hot-path numerics are exempt from above
- O(n^2) algorithms must include `Slow` in the method name

## Usings
- Omit what `ImplicitUsings` provides (System, System.Collections.Generic, etc.)
- Sort: System.*, Microsoft.*, External.*, NeuralNavigator.*
- Each group alphabetically

## Project-Specific
- HelixToolkit 3.x types: `System.Numerics.Vector3`, `HelixToolkit.Maths.Color4`
- `Color4Collection` from `HelixToolkit` namespace (in HelixToolkit.Maths assembly)
- No stale tool/probe projects inside the NeuralNavigator directory (WPF temp
  project picks up nested .csproj files and creates duplicate assembly attributes)
