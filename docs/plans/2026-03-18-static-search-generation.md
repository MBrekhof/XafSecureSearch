# Static Search Panel Generation — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace runtime Roslyn compilation of search DTOs with static .cs file generation that gets compiled with the project.

**Architecture:** The "Compile & Activate" action writes a `.cs` file containing the DTO class + controller into `Module/Controllers/Generated/`. The generated controller inherits from `SearchControllerBase<TEntity, TSearchDTO>`, which already uses `Application.CreateObjectSpace` — the proven working pattern. Runtime Roslyn compilation is removed entirely. The `SearchPanelController` (runtime) is removed. The `SearchDtoRegistry` is simplified to a source generator only.

**Tech Stack:** DevExpress XAF 25.2, EF Core, Blazor Server, .NET 8

**Root cause:** XAF Blazor property editors cannot write values to types loaded from dynamically-compiled assemblies. Static types compiled with the project work correctly.

---

### Task 1: Update `GenerateExportSource` to produce project-ready source

**Files:**
- Modify: `XafSecureSearch/XafSecureSearch.Module/Services/SearchDtoCompiler.cs`

The current `GenerateExportSource` puts both DTO and controller in `XafSecureSearch.RuntimeSearch` namespace. Change the DTO namespace to `XafSecureSearch.Module.BusinessObjects` and the controller namespace to `XafSecureSearch.Module.Controllers` so they align with the project structure and get picked up naturally by XAF.

**Step 1: Update `GenerateExportSource` in `SearchDtoCompiler.cs`**

Replace lines 135-159 with:

```csharp
public string GenerateExportSource(SearchConfiguration config)
{
    var targetShortName = config.TargetEntityType.Split('.').Last();
    var dtoName = $"{targetShortName}SearchDTO";
    var controllerName = $"{targetShortName}SearchController";

    var sb = new StringBuilder();
    sb.AppendLine("// Auto-generated search panel — exported from XafSecureSearch configuration");
    sb.AppendLine($"// Configuration: {config.Name}");
    sb.AppendLine($"// Target Entity: {config.TargetEntityType}");
    sb.AppendLine($"// Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
    sb.AppendLine("//");
    sb.AppendLine("// To regenerate: open the SearchConfiguration in the app and click 'Compile & Activate',");
    sb.AppendLine("// then rebuild the project.");
    sb.AppendLine();

    // Emit the DTO in the Module.BusinessObjects namespace
    sb.AppendLine("using System;");
    sb.AppendLine("using System.ComponentModel;");
    sb.AppendLine("using DevExpress.ExpressApp.DC;");
    sb.AppendLine("using DevExpress.Persistent.Base;");
    sb.AppendLine("using XafSecureSearch.Module.Attributes;");
    sb.AppendLine();

    var fields = config.Fields
        .Where(f => !string.IsNullOrWhiteSpace(f.PropertyName) && !string.IsNullOrWhiteSpace(f.PropertyTypeName))
        .OrderBy(f => f.SortOrder)
        .ToList();

    sb.AppendLine("namespace XafSecureSearch.Module.BusinessObjects;");
    sb.AppendLine();
    sb.AppendLine("[DomainComponent]");
    sb.AppendLine($"[XafDisplayName(\"Search {EscapeString(config.Name)}\")]");
    sb.AppendLine($"public class {dtoName}");
    sb.AppendLine("{");
    sb.AppendLine("    [System.ComponentModel.DataAnnotations.Key]");
    sb.AppendLine("    [Browsable(false)]");
    sb.AppendLine("    public Guid Oid { get; set; } = Guid.NewGuid();");
    sb.AppendLine();

    foreach (var field in fields)
    {
        var displayName = field.DisplayName ?? field.PropertyName;

        if (field.IsReferenceProperty && !string.IsNullOrWhiteSpace(field.ReferencedTypeName))
        {
            sb.AppendLine($"    [XafDisplayName(\"{EscapeString(displayName)}\")]");
            sb.AppendLine($"    public {field.ReferencedTypeName} {field.PropertyName} {{ get; set; }}");
        }
        else if (field.UseRangeFilter && IsRangeEligibleType(field.PropertyTypeName))
        {
            var clrType = GetNullableTypeName(field.PropertyTypeName);
            sb.AppendLine($"    [XafDisplayName(\"{EscapeString(displayName)} (From)\")]");
            sb.AppendLine($"    public {clrType} {field.PropertyName}From {{ get; set; }}");
            sb.AppendLine();
            sb.AppendLine($"    [XafDisplayName(\"{EscapeString(displayName)} (To)\")]");
            sb.AppendLine($"    public {clrType} {field.PropertyName}To {{ get; set; }}");
        }
        else
        {
            var clrType = GetNullableTypeName(field.PropertyTypeName);
            var normalizedType = NormalizeTypeName(field.PropertyTypeName);
            sb.AppendLine($"    [XafDisplayName(\"{EscapeString(displayName)}\")]");
            if (normalizedType == "System.String" && field.UseExactMatch)
                sb.AppendLine("    [UseExactMatch]");
            if (normalizedType == "System.String" && !field.UseExactMatch)
                sb.AppendLine("    [ToolTip(\"Supports wildcards: * (any chars), ? (single char)\")]");
            sb.AppendLine($"    public {clrType} {field.PropertyName} {{ get; set; }}");
        }
        sb.AppendLine();
    }

    sb.AppendLine($"    public override string ToString() => \"Search {EscapeString(config.Name)}\";");
    sb.AppendLine("}");

    return sb.ToString();
}
```

Note: The controller is no longer emitted in the same file. It goes in a separate file (Task 2).

**Step 2: Add `GenerateControllerSource` method**

Add this new method to `SearchDtoCompiler`:

```csharp
public string GenerateControllerSource(SearchConfiguration config)
{
    var targetShortName = config.TargetEntityType.Split('.').Last();
    var dtoName = $"{targetShortName}SearchDTO";
    var controllerName = $"{targetShortName}SearchController";

    var sb = new StringBuilder();
    sb.AppendLine("// Auto-generated search controller");
    sb.AppendLine($"// Configuration: {config.Name}");
    sb.AppendLine($"// Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
    sb.AppendLine();
    sb.AppendLine("using XafSecureSearch.Module.BusinessObjects;");
    sb.AppendLine();
    sb.AppendLine("namespace XafSecureSearch.Module.Controllers;");
    sb.AppendLine();
    sb.AppendLine($"public class {controllerName} : SearchControllerBase<{config.TargetEntityType}, {dtoName}>");
    sb.AppendLine("{");
    sb.AppendLine("}");

    return sb.ToString();
}
```

**Step 3: Build and verify**

Run: `dotnet build XafSecureSearch/XafSecureSearch.Module/XafSecureSearch.Module.csproj`
Expected: Build succeeded

---

### Task 2: Rewrite "Compile & Activate" to write files to disk

**Files:**
- Modify: `XafSecureSearch/XafSecureSearch.Module/Controllers/SearchConfigurationController.cs`

**Step 1: Replace `CompileAction_Execute`**

Replace the `CompileAction_Execute` method (lines 145-188) with:

```csharp
private void CompileAction_Execute(object sender, SimpleActionExecuteEventArgs e)
{
    var config = ViewCurrentObject;

    if (string.IsNullOrWhiteSpace(config.TargetEntityType) || config.Fields.Count == 0)
    {
        Application.ShowViewStrategy.ShowMessage(
            "Configure target entity and fields before generating.",
            InformationType.Warning, 3000, InformationPosition.Top);
        return;
    }

    if (ObjectSpace.IsModified)
    {
        ObjectSpace.CommitChanges();
    }

    var compiler = new SearchDtoCompiler();
    var targetShortName = config.TargetEntityType.Split('.').Last();

    // Determine the Module project root (navigate up from bin output)
    var moduleDir = FindModuleProjectDir();
    if (moduleDir == null)
    {
        Application.ShowViewStrategy.ShowMessage(
            "Could not locate Module project directory. Use 'Export C# Source' instead.",
            InformationType.Error, 5000, InformationPosition.Top);
        return;
    }

    // Generate and write the DTO file
    var dtoSource = compiler.GenerateExportSource(config);
    var dtoDir = Path.Combine(moduleDir, "BusinessObjects", "Generated");
    Directory.CreateDirectory(dtoDir);
    var dtoPath = Path.Combine(dtoDir, $"{targetShortName}SearchDTO.cs");
    File.WriteAllText(dtoPath, dtoSource);

    // Generate and write the Controller file
    var controllerSource = compiler.GenerateControllerSource(config);
    var controllerDir = Path.Combine(moduleDir, "Controllers", "Generated");
    Directory.CreateDirectory(controllerDir);
    var controllerPath = Path.Combine(controllerDir, $"{targetShortName}SearchController.cs");
    File.WriteAllText(controllerPath, controllerSource);

    Application.ShowViewStrategy.ShowMessage(
        $"Generated {targetShortName}SearchDTO.cs and {targetShortName}SearchController.cs. Rebuild the project to activate.",
        InformationType.Success, 5000, InformationPosition.Top);
}

private static string FindModuleProjectDir()
{
    // Walk up from AppDomain base directory to find the Module .csproj
    var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
    while (dir != null)
    {
        if (dir.GetFiles("XafSecureSearch.Module.csproj").Length > 0)
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}
```

**Step 2: Rename the action caption**

In the constructor, change the compile action caption and tooltip:

```csharp
compileAction = new SimpleAction(this, "CompileAndActivate", PredefinedCategory.View)
{
    Caption = "Generate Source",
    ImageName = "Action_Grant",
    ToolTip = "Generate the search panel C# source files (rebuild required)",
    PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage
};
```

**Step 3: Remove auto-compile on save**

Delete or comment out the `ObjectSpace_Committed` handler (lines 63-90) and remove the event subscription from `OnActivated`/`OnDeactivated`. The auto-compile on save was for runtime compilation; it doesn't make sense for file generation.

**Step 4: Update the `SearchConfigurationListController.CompileAllAction_Execute`**

Replace it with a version that generates files for all active configs:

```csharp
private void CompileAllAction_Execute(object sender, SimpleActionExecuteEventArgs e)
{
    var compiler = new SearchDtoCompiler();
    var moduleDir = FindModuleProjectDir();

    if (moduleDir == null)
    {
        Application.ShowViewStrategy.ShowMessage(
            "Could not locate Module project directory.",
            InformationType.Error, 5000, InformationPosition.Top);
        return;
    }

    var configs = ObjectSpace.GetObjectsQuery<SearchConfiguration>()
        .Where(c => c.IsActive && c.TargetEntityType != null)
        .ToList();

    int generated = 0;
    foreach (var config in configs)
    {
        if (config.Fields.Count == 0) continue;
        var targetShortName = config.TargetEntityType.Split('.').Last();

        var dtoDir = Path.Combine(moduleDir, "BusinessObjects", "Generated");
        Directory.CreateDirectory(dtoDir);
        File.WriteAllText(
            Path.Combine(dtoDir, $"{targetShortName}SearchDTO.cs"),
            compiler.GenerateExportSource(config));

        var controllerDir = Path.Combine(moduleDir, "Controllers", "Generated");
        Directory.CreateDirectory(controllerDir);
        File.WriteAllText(
            Path.Combine(controllerDir, $"{targetShortName}SearchController.cs"),
            compiler.GenerateControllerSource(config));

        generated++;
    }

    Application.ShowViewStrategy.ShowMessage(
        $"Generated {generated} search panel(s). Rebuild the project to activate.",
        generated > 0 ? InformationType.Success : InformationType.Warning,
        3000, InformationPosition.Top);
}
```

Move `FindModuleProjectDir` to be accessible by both controllers (make it a static method in `SearchConfigurationController` or extract to a helper).

**Step 5: Build and verify**

Run: `dotnet build XafSecureSearch/XafSecureSearch.Blazor.Server/XafSecureSearch.Blazor.Server.csproj`
Expected: Build succeeded

---

### Task 3: Remove runtime compilation from `Module.Setup()`

**Files:**
- Modify: `XafSecureSearch/XafSecureSearch.Module/Module.cs`

**Step 1: Strip the runtime compilation from `Setup(XafApplication)`**

Replace the `Setup(XafApplication)` override (lines 62-83) with:

```csharp
public override void Setup(XafApplication application)
{
    base.Setup(application);
}
```

**Step 2: Remove `CleanOrphanedModelDiffs`**

Delete the `CleanOrphanedModelDiffs` and `CleanModelFile` methods (lines 89-167). These cleaned model diffs for runtime-compiled types in the `XafSecureSearch.RuntimeSearch` namespace, which no longer exists.

**Step 3: Remove the `using` for `SearchDtoRegistry` service and related imports**

Remove unused usings: `System.Xml.Linq`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection`, `Serilog`, `XafSecureSearch.Module.Services` — but only if they're truly unused after the changes.

**Step 4: Build and verify**

Run: `dotnet build XafSecureSearch/XafSecureSearch.Blazor.Server/XafSecureSearch.Blazor.Server.csproj`
Expected: Build succeeded

---

### Task 4: Clean up temp test artifacts

**Files:**
- Delete: `XafSecureSearch/XafSecureSearch.Module/BusinessObjects/SampleCustomerSearchDTO.cs` (the static test DTO)
- Modify: `XafSecureSearch/XafSecureSearch.Module/Controllers/SearchPanelController.cs` (remove temp hack + diagnostic logging)
- Modify: `XafSecureSearch/XafSecureSearch.Module/Controllers/SearchDtoEditBypassController.cs` (simplify or remove)

**Step 1: Delete the temp static DTO**

Delete `XafSecureSearch/XafSecureSearch.Module/BusinessObjects/SampleCustomerSearchDTO.cs` (the `StaticCustomerSearchDTO` test file).

**Step 2: Clean up `SearchPanelController`**

This controller was for runtime DTOs. Since generated controllers now inherit `SearchControllerBase`, `SearchPanelController` is no longer the primary path. However, keep it intact as it still provides a fallback mechanism for any entity with a runtime registry entry. Remove:
- The temporary static DTO hack (`typeof(StaticCustomerSearchDTO)`)
- All diagnostic logging (`_log.Information("[Execute] reflection..."`, editor logging, memberInfo logging)
- The `using System.Reflection;` import if no longer needed

Revert `CustomizePopupWindowParams` to use the `SearchDtoRegistry` lookup, but this is now a dead code path for entities that have generated controllers.

**Step 3: Simplify `SearchDtoEditBypassController`**

With generated controllers using `SearchControllerBase`, the DTO is in `Module.BusinessObjects` namespace (not `RuntimeSearch`). The bypass controller's namespace check `StartsWith(SearchDtoCompiler.RuntimeNamespace)` won't match static DTOs. Since the generated DTOs use `Application.CreateObjectSpace` (which returns a proper `CompositeObjectSpace`), `HasRightsToModifyMemberController` may not block them.

Test first, but this controller can likely be deleted. If needed, update the namespace check to also match `XafSecureSearch.Module.BusinessObjects` for DTO types ending in `SearchDTO`.

**Step 4: Build and verify**

Run: `dotnet build XafSecureSearch/XafSecureSearch.Blazor.Server/XafSecureSearch.Blazor.Server.csproj`
Expected: Build succeeded

---

### Task 5: End-to-end test

**Step 1: Run the app**

Start from Visual Studio.

**Step 2: Generate the search panel**

1. Log in as Admin
2. Navigate to SearchConfiguration list
3. Open the SampleCustomer config
4. Click "Generate Source" (formerly "Compile & Activate")
5. Verify the files were created:
   - `Module/BusinessObjects/Generated/SampleCustomerSearchDTO.cs`
   - `Module/Controllers/Generated/SampleCustomerSearchController.cs`

**Step 3: Stop and rebuild**

Stop the app, rebuild the solution.

**Step 4: Test the search panel**

1. Start the app
2. Log in as **User** (empty password)
3. Navigate to SampleCustomer list
4. Click "Advanced Search"
5. Type a value in the Name field
6. Click OK
7. Verify the list is filtered

**Step 5: Verify criteria works**

Check the log for `[Execute]` entries — property values should no longer be null.

---

### Task 6: Clean up dead code (optional)

**Files:**
- Evaluate: `XafSecureSearch/XafSecureSearch.Module/Services/SearchDtoRegistry.cs`
- Evaluate: `XafSecureSearch/XafSecureSearch.Module/Services/SearchDtoCompiler.cs`
- Evaluate: `XafSecureSearch/XafSecureSearch.Module/Controllers/SearchPanelController.cs`

After confirming end-to-end works:

1. **SearchDtoRegistry**: Remove `CompileFromDatabase`, `CompileAndRegister`, `Unregister`, `GetDtoType`, `HasSearchPanel`, `GetAll`, `GetSource`, and the `RegistryEntry` class. Keep `GetExportSource` (rename to just `GenerateSource`). Consider whether the singleton pattern is still needed — the compiler can be used directly.

2. **SearchDtoCompiler**: Remove `Compile` method and `CompilationResult` class (Roslyn compilation). Remove `GenerateSource` (the old runtime-namespace version). Keep `GenerateExportSource` (rename to `GenerateSource`), `GenerateControllerSource`, and the helper methods. Remove Roslyn `using` statements and `GetMetadataReferences`.

3. **SearchPanelController**: Can be deleted entirely if all search panels use generated controllers. Or keep as a minimal fallback.

4. **SearchDtoEditBypassController**: Delete if the User account can edit generated search forms without it.

5. **Module.csproj**: Remove `Microsoft.CodeAnalysis.CSharp.Workspaces` and `Microsoft.CodeAnalysis.Workspaces.MSBuild` package references if Roslyn is no longer used at runtime.
