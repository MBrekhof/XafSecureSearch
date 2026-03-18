using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Editors;
using DevExpress.Persistent.Base;
using XafSecureSearch.Module.BusinessObjects;
using XafSecureSearch.Module.Services;

namespace XafSecureSearch.Module.Controllers;

/// <summary>
/// DetailView controller: Populate, Generate Source, Export actions.
/// </summary>
public class SearchConfigurationController : ObjectViewController<DetailView, SearchConfiguration>
{
    private SimpleAction populateAction;
    private SimpleAction compileAction;
    private SimpleAction exportAction;

    public SearchConfigurationController()
    {
        populateAction = new SimpleAction(this, "PopulateProperties", PredefinedCategory.View)
        {
            Caption = "Populate Properties",
            ImageName = "Action_Reload",
            ToolTip = "Read properties from the target entity and populate the fields list",
            PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage
        };
        populateAction.Execute += PopulateAction_Execute;

        compileAction = new SimpleAction(this, "CompileAndActivate", PredefinedCategory.View)
        {
            Caption = "Generate Source",
            ImageName = "Action_Grant",
            ToolTip = "Generate the search panel C# source files (rebuild required)",
            PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage
        };
        compileAction.Execute += CompileAction_Execute;

        exportAction = new SimpleAction(this, "ExportCSharpSource", PredefinedCategory.View)
        {
            Caption = "Export C# Source",
            ImageName = "Action_Export",
            ToolTip = "Generate and display the C# source code for this search panel",
            PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage
        };
        exportAction.Execute += ExportAction_Execute;
    }

    private void PopulateAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var config = ViewCurrentObject;
        if (string.IsNullOrWhiteSpace(config.TargetEntityType))
        {
            Application.ShowViewStrategy.ShowMessage(
                "Please set the Target Entity Type first.",
                InformationType.Warning, 3000, InformationPosition.Top);
            return;
        }

        var typeInfo = XafTypesInfo.Instance.FindTypeInfo(config.TargetEntityType);
        if (typeInfo == null)
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Type '{config.TargetEntityType}' not found in XAF type system.",
                InformationType.Error, 5000, InformationPosition.Top);
            return;
        }

        while (config.Fields.Count > 0)
        {
            ObjectSpace.Delete(config.Fields[0]);
        }

        int sortOrder = 0;
        foreach (var member in typeInfo.Members.OrderBy(m => m.Name))
        {
            if (!PropertyEligibility.IsEligibleProperty(member)) continue;

            var field = ObjectSpace.CreateObject<SearchField>();
            var underlyingType = Nullable.GetUnderlyingType(member.MemberType) ?? member.MemberType;
            field.PropertyName = member.Name;
            field.PropertyTypeName = underlyingType.FullName;
            field.DisplayName = member.DisplayName ?? member.Name;
            field.SortOrder = sortOrder++;
            field.IsReferenceProperty = member.MemberTypeInfo?.IsPersistent == true;

            if (field.IsReferenceProperty)
            {
                field.ReferencedTypeName = member.MemberType.FullName;
            }

            config.Fields.Add(field);
        }

        ObjectSpace.SetModified(config);

        Application.ShowViewStrategy.ShowMessage(
            $"Populated {config.Fields.Count} properties from {typeInfo.Name}.",
            InformationType.Success, 3000, InformationPosition.Top);
    }

    private void CompileAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var config = ViewCurrentObject;

        if (string.IsNullOrWhiteSpace(config.TargetEntityType) || config.Fields.Count == 0)
        {
            Application.ShowViewStrategy.ShowMessage(
                "Configure target entity and fields before generating source.",
                InformationType.Warning, 3000, InformationPosition.Top);
            return;
        }

        if (ObjectSpace.IsModified)
        {
            ObjectSpace.CommitChanges();
        }

        var moduleDir = FindModuleProjectDir();
        if (moduleDir == null)
        {
            Application.ShowViewStrategy.ShowMessage(
                "Cannot locate XafSecureSearch.Module project directory.",
                InformationType.Error, 5000, InformationPosition.Top);
            return;
        }

        try
        {
            var compiler = new SearchDtoCompiler();
            var targetShortName = config.TargetEntityType.Split('.').Last();

            var dtoDir = Path.Combine(moduleDir, "BusinessObjects", "Generated");
            Directory.CreateDirectory(dtoDir);
            var dtoPath = Path.Combine(dtoDir, $"{targetShortName}SearchDTO.cs");
            File.WriteAllText(dtoPath, compiler.GenerateExportSource(config));

            var controllerDir = Path.Combine(moduleDir, "Controllers", "Generated");
            Directory.CreateDirectory(controllerDir);
            var controllerPath = Path.Combine(controllerDir, $"{targetShortName}SearchController.cs");
            File.WriteAllText(controllerPath, compiler.GenerateControllerSource(config));

            Application.ShowViewStrategy.ShowMessage(
                $"Generated {config.Name} files. Rebuild the project to activate.",
                InformationType.Success, 5000, InformationPosition.Top);
        }
        catch (Exception ex)
        {
            Application.ShowViewStrategy.ShowMessage(
                $"File generation failed: {ex.Message}",
                InformationType.Error, 10000, InformationPosition.Top);
        }
    }

    private void ExportAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var config = ViewCurrentObject;
        var compiler = new SearchDtoCompiler();
        var source = compiler.GenerateExportSource(config);

        if (string.IsNullOrWhiteSpace(source))
        {
            Application.ShowViewStrategy.ShowMessage(
                "No source available.",
                InformationType.Warning, 3000, InformationPosition.Top);
            return;
        }

        var os = Application.CreateObjectSpace(typeof(SourceExportView));
        var exportView = os.CreateObject<SourceExportView>();
        exportView.SourceCode = source;
        exportView.FileName = $"{config.TargetEntityType.Split('.').Last()}Search.cs";

        var detailView = Application.CreateDetailView(os, exportView);
        detailView.ViewEditMode = ViewEditMode.View;

        e.ShowViewParameters.CreatedView = detailView;
        e.ShowViewParameters.TargetWindow = TargetWindow.NewModalWindow;
    }

    /// <summary>
    /// Walks up from AppDomain.CurrentDomain.BaseDirectory looking for XafSecureSearch.Module.csproj.
    /// Returns the directory containing the .csproj, or null if not found.
    /// </summary>
    internal static string FindModuleProjectDir()
    {
        // Walk up from bin output directory, checking each ancestor and its children
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            // Check this directory itself
            if (File.Exists(Path.Combine(dir.FullName, "XafSecureSearch.Module.csproj")))
                return dir.FullName;

            // Check immediate subdirectories (Module project is sibling to Blazor.Server)
            try
            {
                foreach (var sub in dir.GetDirectories())
                {
                    if (File.Exists(Path.Combine(sub.FullName, "XafSecureSearch.Module.csproj")))
                        return sub.FullName;
                }
            }
            catch { }

            dir = dir.Parent;
        }
        return null;
    }
}

/// <summary>
/// ListView controller: "Generate All" action to batch-generate source for all active configurations.
/// </summary>
public class SearchConfigurationListController : ObjectViewController<ListView, SearchConfiguration>
{
    private SimpleAction compileAllAction;

    public SearchConfigurationListController()
    {
        compileAllAction = new SimpleAction(this, "CompileAllSearchPanels", PredefinedCategory.View)
        {
            Caption = "Generate All",
            ImageName = "Action_Grant",
            ToolTip = "Generate the search panel C# source files for all active configurations (rebuild required)",
            PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage,
            SelectionDependencyType = SelectionDependencyType.Independent
        };
        compileAllAction.Execute += CompileAllAction_Execute;
    }

    private void CompileAllAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var moduleDir = SearchConfigurationController.FindModuleProjectDir();
        if (moduleDir == null)
        {
            Application.ShowViewStrategy.ShowMessage(
                "Cannot locate XafSecureSearch.Module project directory.",
                InformationType.Error, 5000, InformationPosition.Top);
            return;
        }

        var configs = ObjectSpace.GetObjectsQuery<SearchConfiguration>()
            .Where(c => c.IsActive && c.TargetEntityType != null)
            .ToList();

        int success = 0;
        int failed = 0;
        var compiler = new SearchDtoCompiler();

        foreach (var config in configs)
        {
            if (config.Fields.Count == 0) continue;

            try
            {
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

                success++;
            }
            catch
            {
                failed++;
            }
        }

        var message = $"Generated {success} search panel(s). Rebuild the project to activate.";
        if (failed > 0)
            message += $" {failed} failed.";

        Application.ShowViewStrategy.ShowMessage(
            message,
            failed > 0 ? InformationType.Warning : InformationType.Success,
            3000, InformationPosition.Top);
    }
}
