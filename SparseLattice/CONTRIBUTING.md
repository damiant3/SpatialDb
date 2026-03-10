root = true

[*.cs]
charset = utf-8
indent_style = space
indent_size = 4
insert_final_newline = true

# Naming and project conventions
dotnet_naming_style.store_suffix.required = true
dotnet_naming_rule.store_types_should_end_with_store.severity = suggestion
dotnet_naming_rule.store_types_should_end_with_store.symbols = all_types

## New coding and style preferences
- Scan and use existing core solutions like IStore<T> for local file storage and avoid repetition of a pattern or common practice. If similar code already exists, abstract it into a common reusable solution rather than cloning the mechanism into other models and services.
- Rely on abstractions, not concretions.
- Use SOLID principles of OOD wherever applicable.
- Prefer composition over inheritance. Avoid deep inheritance hierarchies and favor composition of smaller, focused classes that can be easily tested and maintained.
- Obey Liskov Substitution Principle. Ensure that derived classes can be substituted for their base classes without altering the correctness of the program.
- Prefer returning named DTO types rather than tuples when returning more than two values.
- Use "" (empty string literal) in code instead of `String.Empty` or `string.Empty` when representing an empty string constant.
- Use collection initializers when constructing collections where applicable (e.g., `new List<int> { 1, 2 }`).
- When declaring an empty collection value prefer the concise empty initializer `[]` on the right-hand side. Example: `ObservableCollection<AIModel> models = [];`. Do not duplicate the concrete type on both sides when declaring an empty collection. All new empty collection declarations should follow the pattern `Type m_name = [];`.
- Construct collections directly from call results when possible. Avoid creating unnecessary intermediate variables just to pass them to collection constructors. Example: prefer `HashSet<string> activeSet = new HashSet<string>([.. await client.GetActiveAsync(ct) ?? []]);` over separate `active` and `activeList` temporaries.
- Avoid creating redundant temporaries that only serve to be forwarded into another collection. Build the collection from the source value in place.
- Prefer performing discovery and hydration inside a single try/catch and materialize only the resulting hydrated domain objects. Do not create an intermediate names list if it is only used to feed a "get details" call; instead iterate the filtered/distinct names sequence directly from the API result.
- When hydrating DTOs from a remote API, prefer pattern-matching checks to validate the DTO shape rather than multi-step null/empty fallbacks. Example: `if (info is { Name.Length: > 0 })`.
- Use explicit typing for locals and fields. Do not use `var` in new code.  update use of var in existing code where ever you see it.
- Never include braces for single-statement `if`/`else` blocks. Use the project's established style for concise single-line blocks.
- Never swallow exceptions silently. Empty `catch { }` blocks are forbidden. Do not catch exceptions only to ignore them. If a catch is required, handle it explicitly by logging and returning a concrete error value or object. Avoid unnecessary try/catch wrappers that only hide errors; prefer Try-patterns (e.g., `int.TryParse`) and explicit error returns for expected failures.
- Avoid adding try/catch wrappers around code solely to suppress or translate exceptions. Do not add a try/catch block unless you will handle the exception (log, map to an error result, retry/backoff, or rethrow).
- Never add comments that describe the steps of an algorithm or function. Use expressive type, value, and method names so the code reads like English. Only comment non-obvious behavior (bit-shift magic, interop contracts, unexpected dependency expectations).
- Prefer pattern matching instead of unnecessary null checks. Avoid fallbacks like `string x = maybeNull ?? ""` followed by `if (!string.IsNullOrWhiteSpace(x))` — prefer matching the original value where applicable.
- Prefer returning null/false or a concrete error result object for expected failure conditions. Reserve throwing exceptions for truly exceptional conditions (OOM, corrupted memory, catastrophic failures).
- Do not add defensive fallback and excessive null-checking when the project is built with nullable enabled. If a parameter or field may be null, declare it as nullable in the API (e.g., `string?`) and handle it there. Otherwise assume non-null and allow natural exceptions at the point of use.
- Do not re-throw different exceptions to intercept eventual null reference errors. Let the natural exception occur at the appropriate domain level.
- Field naming convention: prefix instance fields with `m` (e.g., `m_names`), static fields with `s` (e.g., `s_singleton`), thread-static with `t` (e.g., `t_context`). Do not use bare `_` prefix.
- Do not add `using` declarations that are provided by the project's implicit/global usings. Rely on the project's `ImplicitUsings` setting. When in doubt, perform a test build first without the using import.
- Sort `using` statements into groups in this order: `System.*`, `Microsoft.*`, `OtherExternalPackageName.*`, `LocalVisualStudioCopilot.*` (or the app's root namespace). Within each group sort alphabetically.
- Use file-scoped namespaces (e.g., `namespace LocalVisualStudioCopilot.Services.AI;`) for new files and update for consistency when editing existing files.
- A single comment line consisting of a repeated comment character / between the usings/imports section and the file-scoped namespace declaration for readability.  This seperator should be the same number of characters as the namespace declaration on the following line.
- Never use block comments `/* */` or `#regions`.
- Use expressive names, never abbreviations or initialisms unless they are widely recognized (e.g., `Id`). Avoid generic names like `data`, `info`, `temp` in domain models.
- Pay attention to runtimes... O(n^2) algorithms must include Slow in the method name and be highlighted if unavoidable.  For example "SortSlow()" if you must implement nested iterative loops like selection or insertion sorts.  Expect O(n log n) for sorting, O(log n) for searching.  Be judicious using hash tables.  Use ConcurrentDictionary in all scenarios, even when a HashSet or single threaded dictionary would work.

These rules are requirements and stylistic / syntactic and sometimes architectural invariants, and should be followed closely when modifying or adding code. They will be enforced via user code reviews. Any conflict or significant vagueness or ambiguity discovered in these rules should be resolved immediatly in discussion with the user.  When discovered, please notify the user and propose a rule change that best resolves the conflict before continuing. In such cases, consider the nature of the other rules, and the potential impact on the overall codebase.  Think deeply about the best rule you can propose in one shot that the user can review and decide.

# Note — scope of this document

This `CONTRIBUTING.md` is a style and architecture guide for the `SparseLattice` project. It focuses on naming, formatting, API design conventions, and architectural principles to be followed by contributors.

Operational runbooks, automated-agent instructions, CI job run plans, and step‑by‑step project plans are intentionally kept out of this file. See `AGENT_PLAN.md` in the project root for the detailed agent run plan, test matrices, and procedural onboarding steps.