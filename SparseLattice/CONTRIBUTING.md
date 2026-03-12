root = true

[*.cs]
charset = utf-8
indent_style = space
indent_size = 4
insert_final_newline = true

# PRIORITY DOCUMENT AUTHORITY
Any conflict between this document and automated style tools (such as .editorconfig or IDE suggestions) is resolved in favor of this document. This document is the source of truth for style and architectural decisions. Update tool configuration to match these rules rather than changing these rules to match the tool configuration. This document defines architectural intent; .editorconfig exists only to mechanically enforce these rules where possible.

## Engineering Guidance

### Architectural and design principles
- Scan and use existing core solutions like IStore<T> for local file storage and avoid repetition of a pattern or common practice. If similar code already exists, abstract it into a common reusable solution rather than cloning the mechanism into other models and services.
- Rely on abstractions, not concretions.
- Use SOLID principles of OOD wherever applicable.
- Prefer composition over inheritance. Avoid deep inheritance hierarchies and favor composition of smaller, focused classes that can be easily tested and maintained.
- Obey Liskov Substitution Principle. Ensure that derived classes can be substituted for their base classes without altering the correctness of the program.
- Prefer returning named DTO types rather than anonymous types, dynamics, duck-types, or tuples when returning more than two values.

### Prefer small, focused files and types
- Keep file sizes small and focused. Avoid allowing any single file to grow much beyond ~500 lines.
- When a class or view model begins to accumulate unrelated responsibilities or exceeds a manageable size, split it into partial classes or extract cohesive components into separate types.
- Favor decomposition over monolithic files. Large files slow down navigation, increase cognitive load, and degrade AI‑assisted editing performance.
- When adding new functionality to an already large type, prefer creating a new type to isolate the functionality or create a partial type in its own file, rather than expanding an already large file.

### Dependency discipline
- Do not guess about external dependencies — inspect the source.
- When working with dependencies that exist in the public GitHub repository for this project or its referenced libraries, do not infer behavior or guess API details.
- Always inspect the actual source code, including commit history, implementation details, and version differences between local and remote copies.
- Prefer reading the real implementation over relying on memory, heuristics, probes, or assumptions.
- When uncertain about a dependency’s behavior, consult the source directly before proposing code changes or abstractions.

### C# code style and conventions
- Use "" (empty string literal) in code instead of `String.Empty` or `string.Empty` when representing an empty string constant.
- Use collection initializers when constructing collections where applicable (e.g., `new List<int> { 1, 2 }`).
- When declaring an empty collection value prefer the concise empty initializer `[]` on the right-hand side. Example: `ObservableCollection<AIModel> models = [];`. Do not duplicate the concrete type on both sides when declaring an empty collection. All new empty collection declarations should follow the pattern `Type m_name = [];`.
- Construct collections directly from call results when possible. Avoid creating unnecessary intermediate variables just to pass them to collection constructors. Example: prefer `HashSet<string> activeSet = new HashSet<string>([.. await client.GetActiveAsync(ct) ?? []]);` over separate `active` and `activeList` temporaries.
- Avoid creating redundant temporaries that only serve to be forwarded into another collection. Build the collection from the source value in place.
- Prefer performing discovery and hydration inside a single try/catch and materialize only the resulting hydrated domain objects. Do not create an intermediate names list if it is only used to feed a "get details" call; instead iterate the filtered/distinct names sequence directly from the API result.
- When hydrating DTOs from a remote API, prefer pattern-matching checks to validate the DTO shape rather than multi-step null/empty fallbacks. Example: `if (info is { Name.Length: > 0 })`.
- Use explicit typing for locals and fields. Do not use `var` in new code. Update use of var in existing code wherever you see it.
- Never include braces for single-statement `if`/`else` blocks. Use the project's established style for concise single-line blocks.
- Never swallow exceptions silently. Empty `catch { }` blocks are forbidden. Do not catch exceptions only to ignore them. If a catch is required, handle it explicitly by logging and returning a concrete error value or object. Avoid unnecessary try/catch wrappers that only hide errors; prefer Try-patterns (e.g., `int.TryParse`) and explicit error returns for expected failures.
- Avoid adding try/catch wrappers around code solely to suppress or translate exceptions. Do not add a try/catch block unless you will handle the exception (log, map to an error result, retry/backoff, or rethrow).
- Never add comments that describe the steps of an algorithm or function. Use expressive type, value, and method names so the code reads like English. Only comment non-obvious behavior (bit-shift magic, interop contracts, unexpected dependency expectations).
- Prefer pattern matching instead of unnecessary null checks. Avoid fallbacks like `string x = maybeNull ?? ""` followed by `if (!string.IsNullOrWhiteSpace(x))` — prefer matching the original value where applicable.
- Prefer returning null/false or a concrete error result object for expected failure conditions. Reserve throwing exceptions for truly exceptional conditions (OOM, corrupted memory, catastrophic failures).
- Do not add defensive fallback and excessive null-checking when the project is built with nullable enabled. If a parameter or field may be null, declare it as nullable in the API (e.g., `string?`) and handle it there. Otherwise assume non-null and allow natural exceptions at the point of use.
- Do not re-throw different exceptions to intercept eventual null reference errors. Let the natural exception occur at the appropriate domain level.
- Field naming convention: prefix instance fields with m_ (e.g., m_names), static fields with s_ (e.g., s_singleton), thread-static with t_ (e.g., t_context). Do not use a bare _ prefix. const fields are exempt from the s_ prefix and should use PascalCase (e.g., DefaultFracBits), matching standard C# convention for compile-time constants.
- Do not add `using` declarations that are provided by the project's implicit/global usings. Rely on the project's `ImplicitUsings` setting. When in doubt, perform a test build first without the using import.
- Sort `using` statements into groups in this order: `System.*`, `Microsoft.*`, `OtherExternalPackageName.*`, `LocalVisualStudioCopilot.*` (or the app's root namespace). Within each group sort alphabetically.
- Use file-scoped namespaces (e.g., `namespace LocalVisualStudioCopilot.Services.AI;`) for new files and update for consistency when editing existing files.
- A single comment line consisting of a repeated comment character / between the usings/imports section and the file-scoped namespace declaration for readability. This separator should be the same number of characters as the namespace declaration on the following line.
- Never use block comments `/* */` or `#regions`.
- Use expressive names, never abbreviations or initialisms unless they are widely recognized (e.g., `Id`). Avoid generic names like `data`, `info`, `temp` in domain models.

### NO GOTCHAS
- Pay attention to runtimes. O(n^2) algorithms must include Slow in the method name and be highlighted if unavoidable. For example "SortSlow()" if you must implement nested iterative loops like selection or insertion sorts. Expect O(n log n) for sorting, O(log n) for searching. Be judicious using hash tables. Use ConcurrentDictionary for shared/cached/long-lived dictionaries rather than plain Dictionary, even when current callers are single-threaded. Method-local lookup tables and hot-path numerics are exempt — use the simplest collection that fits.

These rules are requirements and are stylistic / syntactic / architectural invariants, and should be followed closely when modifying or adding code. They will be enforced via user code reviews.

# Scope of this document
This `CONTRIBUTING.md` is a style, coding, process, and architecture guide for contributions to the `SparseLattice` project.
