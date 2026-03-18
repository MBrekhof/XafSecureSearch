# How to Implement XafSecureSearch in Your XAF Application

This guide walks you through adding database-driven, runtime-compiled search panels to an existing XAF Blazor Server application with EF Core. It assumes you're familiar with XAF modules, business objects, controllers, and the security system.

## Prerequisites

- XAF Blazor Server application with EF Core (.NET 8+)
- DevExpress v25.2+ (earlier versions may work but are untested)
- SQL Server (LocalDB or full instance)

## Step 1: Add the Roslyn NuGet Packages

Add these to your **Module** project (`.csproj`):

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.*" />
<PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="4.*" />
```

These are needed for runtime C# compilation. They add ~15MB to your output but have zero runtime overhead until compilation is triggered.

## Step 2: Create the Business Objects

You need two entities to store search panel definitions, plus a base class.

### BaseObjectInt

If you don't already have an integer-key base class:

```csharp
using System.ComponentModel;
using DevExpress.Persistent.Base;

namespace YourApp.Module.BusinessObjects;

public abstract class BaseObjectInt
{
    [Browsable(false)]
    [VisibleInDetailView(false), VisibleInListView(false)]
    public virtual int ID { get; set; }
}
```

### SearchConfiguration

```csharp
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using System.ComponentModel;

namespace YourApp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
[XafDisplayName("Search Configuration")]
public class SearchConfiguration : BaseObjectInt
{
    [XafDisplayName("Name")]
    public virtual string Name { get; set; }

    [XafDisplayName("Target Entity Type")]
    public virtual string TargetEntityType { get; set; }

    [XafDisplayName("Active")]
    public virtual bool IsActive { get; set; } = true;

    public virtual IList<SearchField> Fields { get; set; } = new List<SearchField>();

    public override string ToString() => Name ?? "New Configuration";
}
```

### SearchField

```csharp
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using System.ComponentModel;

namespace YourApp.Module.BusinessObjects;

[DefaultProperty(nameof(PropertyName))]
[XafDisplayName("Search Field")]
public class SearchField : BaseObjectInt
{
    public virtual int? SearchConfigurationId { get; set; }
    public virtual SearchConfiguration SearchConfiguration { get; set; }

    [XafDisplayName("Include")]
    public virtual bool IsIncluded { get; set; }

    [XafDisplayName("Property Name")]
    [ImmediatePostData]
    public virtual string PropertyName { get; set; }

    [XafDisplayName("Property Type")]
    [VisibleInDetailView(false)]
    public virtual string PropertyTypeName { get; set; }

    [XafDisplayName("Display Name")]
    public virtual string DisplayName { get; set; }

    [XafDisplayName("Exact Match")]
    public virtual bool UseExactMatch { get; set; }

    [XafDisplayName("Range Filter")]
    public virtual bool UseRangeFilter { get; set; }

    [XafDisplayName("Sort Order")]
    public virtual int SortOrder { get; set; }

    [VisibleInDetailView(false), VisibleInListView(false)]
    public virtual bool IsReferenceProperty { get; set; }

    [VisibleInDetailView(false), VisibleInListView(false)]
    public virtual string ReferencedTypeName { get; set; }
}
```

### Register in DbContext

Add both entities to your `DbContext`:

```csharp
public DbSet<SearchConfiguration> SearchConfigurations { get; set; }
public DbSet<SearchField> SearchFields { get; set; }
```

And register them in your module's `AdditionalExportedTypes`:

```csharp
AdditionalExportedTypes.Add(typeof(SearchConfiguration));
AdditionalExportedTypes.Add(typeof(SearchField));
```

## Step 3: Add the UseExactMatch Attribute

A simple marker attribute used by the code generator and criteria builder:

```csharp
namespace YourApp.Module.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class UseExactMatchAttribute : Attribute { }
```

## Step 4: Add the Services

### SearchDtoCompiler

This is the core — it generates C# source from a `SearchConfiguration` and compiles it via Roslyn.

Key points:
- Generated types live in a `YourApp.RuntimeSearch` namespace
- Each type is marked `[DomainComponent]` — this is critical for XAF to treat it as non-persistent
- Each type has a `Guid Oid` property with `[Key]` — required for XAF's object identity
- Only fields with `IsIncluded = true` are emitted
- The assembly is loaded into `AssemblyLoadContext.Default`

Copy `SearchDtoCompiler.cs` from this project and update the namespace constant:

```csharp
internal const string RuntimeNamespace = "YourApp.RuntimeSearch";
```

### SearchDtoRegistry

Singleton that manages the compilation lifecycle:

- `CompileFromDatabase(connectionString, module)` — called once at startup, loads configs via ADO.NET, compiles each one, registers types with `XafTypesInfo` and `AdditionalExportedTypes`
- `CompileAndRegister(config, module)` — compiles a single config at runtime (e.g., from a UI action)
- `GetDtoType(entityTypeName)` — returns the compiled Type for an entity, or null

**Why ADO.NET instead of EF Core?** The compilation happens in `Module.Setup()` before `base.Setup()` — at this point, EF Core's `DbContext` may not be fully initialized. ADO.NET with a raw connection string avoids this dependency.

**Why compile only once?** In Blazor Server, `Module.Setup(XafApplication)` is called for each user session. If you recompile each time, you get different `Type` instances from different in-memory assemblies. The XAF model was built from the first Type, so subsequent Types cause a mismatch — property editors bind to the wrong Type and values appear null. The registry guards against this with an early return if entries already exist.

Copy `SearchDtoRegistry.cs` and update:
- The ADO.NET query column names to match your schema
- The namespace references

### CriteriaBuilder

Reflects over a filled search DTO and builds `CriteriaOperator` expressions:

- Null/empty values are skipped (no filter for that property)
- Strings: `LIKE` with wildcard translation (`*` → `%`, `?` → `_`), or exact match if `[UseExactMatch]` is present
- Range fields: properties ending in `From`/`To` produce `>=` / `<=` operators on the base property name
- Reference properties: matched by the reference object's default property
- Boolean: exact match
- All criteria are combined with `AND`

Copy `CriteriaBuilder.cs` from this project.

### PropertyEligibility

Determines which entity properties are eligible for search panel inclusion. Filters out navigation collections, key properties, system fields, etc.

Copy `PropertyEligibility.cs` from this project.

## Step 5: Add the Controllers

### SearchPanelController

This is the user-facing controller — it adds the "Advanced Search" button to ListViews.

```csharp
public class SearchPanelController : ViewController<ListView>
```

Key implementation details:

- **OnActivated**: Looks up `_dtoType` from `SearchDtoRegistry.Instance.GetDtoType(entityType.FullName)`. If no DTO exists for this entity, deactivates via `Active["HasSearchConfig"] = false`.

- **CustomizePopupWindowParams**: Creates a `NonPersistentObjectSpace` via `Application.CreateObjectSpace(_dtoType)`. **Critical step**: if the returned ObjectSpace is a `CompositeObjectSpace`, you must add an additional persistent ObjectSpace for the target entity type. Without this, reference/lookup property editors won't be able to resolve persistent entities:

```csharp
if (os is CompositeObjectSpace compositeOs)
{
    var persistentOs = Application.CreateObjectSpace(entityType);
    compositeOs.AdditionalObjectSpaces.Add(persistentOs);
    os.Disposed += (_, _) => persistentOs.Dispose();
}
```

- **Execute**: Reads the filled DTO from `e.PopupWindowViewCurrentObject`, passes it to `CriteriaBuilder.BuildCriteria()`, and applies the result to `View.CollectionSource.Criteria`.

### SearchConfigurationController

Admin-facing controller on the `SearchConfiguration` DetailView:

- **Populate Properties** — Reads `XafTypesInfo` for the target entity type and creates `SearchField` entries for all eligible properties (with `IsIncluded = false` by default)
- **Compile & Activate** — Calls `SearchDtoRegistry.Instance.CompileAndRegister()` for immediate testing (requires app restart for full activation)
- **Export C# Source** — Shows the generated source code in a modal for inspection

### SearchConfigurationListController

ListView controller with a "Compile All" batch action.

Copy both controllers from this project and update namespaces.

## Step 6: Wire Up Module.Setup()

This is the most important step — and where most bugs hide. In your module class:

```csharp
public override void Setup(XafApplication application)
{
    // Compile BEFORE base.Setup() — types must be registered
    // before XAF builds its model.
    var connectionString = application.ServiceProvider
        ?.GetService<IConfiguration>()
        ?.GetConnectionString("ConnectionString");

    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        SearchDtoRegistry.Instance.CompileFromDatabase(connectionString, this);
    }

    base.Setup(application);
}
```

**The order matters.** If you call `base.Setup(application)` first:
- XAF builds the model without knowing about your DTO types
- The DetailView has no proper editor configuration
- Property editors won't bind values (everything appears null)

## Step 7: Ensure NonPersistent ObjectSpace Provider

In your `Startup.cs` / `ConfigureServices`, make sure `.AddNonPersistent()` is present on the ObjectSpace providers:

```csharp
builder.ObjectSpaceProviders
    .AddSecuredEFCore(options => { options.PreFetchReferenceProperties(); })
    .WithDbContext<YourDbContext>((sp, options) => { /* ... */ })
    .AddNonPersistent();  // ← Required for [DomainComponent] types
```

## Gotchas and Things We Learned the Hard Way

### 1. `[DomainComponent]` is Non-Negotiable

The generated DTO class **must** have `[DomainComponent]`. Without it:
- `NonPersistentTypeInfoSource` won't claim the type
- `EFCoreTypeInfoSource` might claim it instead, setting `IsPersistent = true`
- The security system will then require explicit permissions for the type
- `HasRightsToModifyMemberController` will set `AllowEdit = false` for non-admin users

With `[DomainComponent]`:
- `IsPersistent = false` → `IsSecuredType() = false` → security ignores it entirely
- All users can edit the search form regardless of their role permissions

### 2. Compile Once, Not Per Session

Blazor Server creates a new `XafApplication` instance per user session. Each one calls `Module.Setup()`. If you compile each time:
- Each compilation creates a new assembly with a unique name
- Each assembly contains a different `Type` instance (same name, different identity)
- The XAF model was built from Type A, but the registry now points to Type C
- Property editors bind to Type A's model, the object is Type C → null values

Guard with a simple check at the top of `CompileFromDatabase`:

```csharp
if (_entries.Count > 0) return existing entries;
```

### 3. CompositeObjectSpace Needs a Persistent Companion

`Application.CreateObjectSpace(nonPersistentType)` returns a `CompositeObjectSpace`. By default, it can only manage non-persistent objects. If your search DTO has reference properties to persistent entities (e.g., a Customer lookup), the lookup editor needs a persistent ObjectSpace to query the database. Add one:

```csharp
if (os is CompositeObjectSpace compositeOs)
{
    var persistentOs = Application.CreateObjectSpace(typeof(SomePersistentEntity));
    compositeOs.AdditionalObjectSpaces.Add(persistentOs);
    os.Disposed += (_, _) => persistentOs.Dispose();
}
```

### 4. ADO.NET Column Alignment

The `LoadConfigsViaAdoNet` method reads fields by ordinal position (`reader.GetInt32(0)`, `reader.GetBoolean(5)`, etc.). If you add a column to `SearchField`, you must update both:
- The SQL `SELECT` statement to include the new column
- The `reader.Get*()` calls with the correct ordinal index

This bit us when we added `IsIncluded` — the query didn't select it, so all fields loaded as `false` and the DTO had zero properties.

### 5. Connection String Availability

`application.ConnectionString` may be `null` before `base.Setup()` in projects with integrated security. Use `IConfiguration` via `ServiceProvider` instead:

```csharp
var connectionString = application.ServiceProvider
    ?.GetService<IConfiguration>()
    ?.GetConnectionString("ConnectionString");
```

`ServiceProvider` is available because DI container setup completes before `Module.Setup()` is called.

## Testing Your Implementation

1. Run the app, log in as Admin
2. Navigate to Search Configurations, create a new record
3. Set the Target Entity Type (full CLR type name, e.g., `YourApp.Module.BusinessObjects.Customer`)
4. Click "Populate Properties" — all eligible properties appear as SearchField rows
5. Check "Include" on the fields you want in the search panel
6. Save the record, restart the app
7. Navigate to the target entity's ListView — "Advanced Search" button should appear
8. Fill in values, click OK — the list should filter
9. Test with a non-admin user to verify security doesn't interfere

## File Checklist

```
Module/
  Attributes/
    UseExactMatchAttribute.cs
  BusinessObjects/
    BaseObjectInt.cs           (if needed)
    SearchConfiguration.cs
    SearchField.cs
    SourceExportView.cs        (optional, for Export Source action)
  Controllers/
    SearchPanelController.cs
    SearchConfigurationController.cs
    SearchFieldLookupController.cs  (optional, for TargetEntityType dropdown)
  Services/
    SearchDtoCompiler.cs
    SearchDtoRegistry.cs
    CriteriaBuilder.cs
    PropertyEligibility.cs
```
