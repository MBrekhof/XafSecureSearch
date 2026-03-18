# TODO — XafSecureSearch

## Testing — Verify Existing Functionality

### String Filtering
- [ ] **Contains (default)** — type a partial name, verify `LIKE '%value%'` behavior
- [ ] **Wildcards** — type `A*` or `?ob`, verify `*` → `%` and `?` → `_` translation
- [ ] **Exact match** — mark a field as `UseExactMatch`, verify `=` instead of `Contains`
- [ ] **Empty string** — leave string field blank, verify it's skipped (no filter applied)

### Range Filtering
- [ ] **Date range** — mark a date field as Range Filter, verify From/To fields appear and produce `>= fromDate AND < toDate+1`
- [ ] **Numeric range** — mark an int/decimal field as Range Filter, verify `>= from AND <= to`
- [ ] **Partial range** — fill only From or only To, verify single-sided filter
- [ ] **Both empty** — leave both From and To blank, verify field is skipped

### Other Types
- [ ] **Boolean** — set IsActive to true/false, verify exact match filter
- [ ] **Integer** — enter Age value, verify exact match (non-range mode)
- [ ] **DateTime single** — enter a date without range mode, verify day-boundary filter (`>= startOfDay AND < endOfDay`)

### Reference Properties
- [ ] **Persistent reference lookup** — if a reference field is included (e.g., Customer on Order), verify the lookup editor works and filters by key
- [ ] **CompositeObjectSpace** — verify reference lookups work for non-admin users (additional persistent ObjectSpace is added)

### Security
- [ ] **Admin user** — full search panel functionality
- [ ] **Default role user** — search panel appears, fields are editable, values bind, filter applies (requires read access to the target entity)

### Edge Cases
- [ ] **No included fields** — all fields unchecked, verify no DTO is compiled (or empty DTO is handled gracefully)
- [ ] **Multiple search configs** — two different entities with search panels, verify both work independently
- [ ] **Config without fields** — SearchConfiguration with zero SearchField rows, verify graceful skip

## Potential Enhancements

### Search Capabilities
- [ ] **Enum filtering** — `PropertyEligibility` allows enums, but `SearchDtoCompiler.GenerateSource` doesn't handle enum types in `GetNullableTypeName` (falls through to `normalized + "?"` which may not compile). Need to emit the full enum type name.
- [ ] **OR mode** — currently all criteria are combined with AND. Add an option (per-config or per-field) to use OR between certain fields.
- [ ] **Negation** — "NOT contains" or "!= value" option per field. Useful for exclusion filters.
- [ ] **Null filtering** — explicit "Is Null" / "Is Not Null" option per field. Currently null values are skipped, but sometimes you want to find records where a field IS null.
- [ ] **Multi-value / IN operator** — allow comma-separated values for a field, produce `IN (val1, val2, val3)` criteria.
- [ ] **Collection/nested property search** — filter on properties of child collections (e.g., "Orders where any OrderLine has ProductName = X"). Would need `ContainsOperator` support in CriteriaBuilder.
- [ ] **DateOnly / TimeOnly support** — `PropertyEligibility` and `GetNullableTypeName` don't handle `DateOnly` or `TimeOnly` (used in newer .NET projects). WLNCentral's `MonsterZoeker` uses `DateOnly`.
- [ ] **Guid search** — currently eligible in `PropertyEligibility` but `GetNullableTypeName` maps it to `Guid?`. Verify this compiles and filters correctly.

### UX Improvements
- [ ] **Clear filter action** — add a "Clear Search" button on the ListView to remove the active search criteria without re-opening the popup.
- [ ] **Saved searches** — let users save a filled search form and recall it later. Would require persisting DTO property values.
- [ ] **Filter indicator** — show active filter count or summary in the ListView toolbar so users know a filter is applied.
- [ ] **Default values** — allow SearchField to specify a default value that pre-fills when the popup opens.
- [ ] **Field grouping / layout** — control how fields are arranged in the popup DetailView (tabs, groups).

### Technical
- [ ] **Trim Roslyn packages** — only `Microsoft.CodeAnalysis.CSharp` is needed; remove `Microsoft.CodeAnalysis.CSharp.Workspaces` and `Microsoft.CodeAnalysis.Workspaces.MSBuild` to save ~10MB in output.
- [ ] **Compilation caching** — cache the compiled assembly bytes on disk so restart doesn't recompile from source every time. Invalidate cache when SearchConfiguration is modified.
- [ ] **EasyTest connection string** — `Module.Setup` reads `ConnectionString` from IConfiguration, but doesn't handle `EASYTEST` config. Match the pattern from Startup.cs.
- [ ] **DemoDataController scoping** — `TargetViewType = ViewType.Any` shows the action everywhere. Scope to SampleCustomer ListView only, or gate behind a config flag.
