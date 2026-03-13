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
- When uncertain about a dependency's behavior, consult the source directly before proposing code changes or abstractions.

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
- Field naming convention: prefix instance fields with m_ (e.g., m_names), static fields with s_ (e.g., s_singleton), thread-static with t_ (e.g., t_context). Do not use a bare _ prefix. const fields are exempt from the s_ prefix and should use PascalCase (e.g., DefaultFracBits), matching standard C# convention for compile-time constants.
- Do not add `using` declarations that are provided by the project's implicit/global usings. Rely on the project's `ImplicitUsings` setting. When in doubt, perform a test build first without the using import.
- Sort `using` statements into groups in this order: `System.*`, `Microsoft.*`, `OtherExternalPackageName.*`, `LocalVisualStudioCopilot.*` (or the app's root namespace). Within each group sort alphabetically.
- Use file-scoped namespaces (e.g., `namespace LocalVisualStudioCopilot.Services.AI;`) for new files and update for consistency when editing existing files.
- A single comment line consisting of a repeated comment character / between the usings/imports section and the file-scoped namespace declaration for readability. This separator should be the same number of characters as the namespace declaration on the following line.
- Never use block comments `/* */` or `#regions`.
- Use expressive names, never abbreviations or initialisms unless they are widely recognized (e.g., `Id`). Avoid generic names like `data`, `info`, `temp` in domain models.
- Do not write `private` on class members that are already private by default. Omit the redundant access modifier on methods, fields, nested types, and other members where `private` is the language default.
- Do not add XML doc comments (`/// <summary>`). Code is read by agents and compilers, not humans browsing docs. Expressive names replace summaries. Remove XML doc comments when editing existing files.
- Do not add section-separator comments (`// --- section ---`, `// =====`, `// *****`). If a file needs section headers to be navigable, it needs to be split into smaller files instead.

### Disposable discipline
- Always acquire disposable resources with a `using` declaration (`using SlimSyncer s = new(...);`). Never use raw `try/finally` to manage disposable lifetimes in business logic. The `try/finally` pattern belongs exclusively inside `Dispose()` implementations and finalizers — nowhere else.
- If a method needs to acquire multiple disposables, stack `using` declarations. Do not accumulate disposables in a list with manual `try/finally` cleanup unless you are implementing a composite disposable (e.g., `MultiSyncerScope`).
- Disposable fields that outlive a single method should implement `IDisposable` on the owning type. Do not rely on callers to remember cleanup — the type owns the lifetime.

### Null contract discipline
- Nullable analysis is enabled project-wide. A non-nullable parameter or field is a contract: the caller guarantees it is not null. The callee must not second-guess that contract with defensive null checks.
- Never test for null then throw. If a value is contractually non-null, use it directly. A `NullReferenceException` at the point of use is the correct signal that a caller violated the contract. Wrapping it in `ArgumentNullException` or `InvalidOperationException` adds noise without information.
- Never intercept a null to throw a different exception. Do not write `x ?? throw new InvalidOperationException(...)` or `if (x == null) throw ...` for values the type system already declares non-null. Let the natural exception surface at the domain-appropriate call site.
- Use pattern matching for control flow, not null guards. Prefer `if (result is SomeType typed)` over `if (result != null && result is SomeType)`. Prefer `switch` expressions with type patterns over chains of `is`/`as` checks followed by null tests.
- Nullable return types and parameters (`T?`, `string?`) are the explicit opt-in for "this may be absent." When you see `T?`, handle the null case. When you see `T`, trust it and move on.

### NO GOTCHAS
- Pay attention to runtimes. O(n^2) algorithms must include Slow in the method name and be highlighted if unavoidable. For example "SortSlow()" if you must implement nested iterative loops like selection or insertion sorts. Expect O(n log n) for sorting, O(log n) for searching. Be judicious using hash tables. Use ConcurrentDictionary for shared/cached/long-lived dictionaries rather than plain Dictionary, even when current callers are single-threaded. Method-local lookup tables and hot-path numerics are exempt — use the simplest collection that fits.

These rules are requirements and are stylistic / syntactic / architectural invariants, and should be followed closely when modifying or adding code. They will be enforced via user code reviews.

# Scope of this document
This `CONTRIBUTING.md` is a style, coding, process, and architecture guide for contributions to the `SpatialDbLib` project.
