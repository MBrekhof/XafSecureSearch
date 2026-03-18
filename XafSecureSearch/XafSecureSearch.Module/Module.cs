using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Updating;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using XafSecureSearch.Module.Services;

namespace XafSecureSearch.Module
{
    // For more typical usage scenarios, be sure to check out https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.ModuleBase.
    public sealed class XafSecureSearchModule : ModuleBase
    {
        private static readonly ILogger _log = Log.ForContext<XafSecureSearchModule>();

        public XafSecureSearchModule()
        {
            //
            // XafSecureSearchModule
            //
            AdditionalExportedTypes.Add(typeof(XafSecureSearch.Module.BusinessObjects.ApplicationUser));
            AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.PermissionPolicy.PermissionPolicyRole));
            AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.ModelDifference));
            AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.ModelDifferenceAspect));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.SystemModule.SystemModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Security.SecurityModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.ConditionalAppearance.ConditionalAppearanceModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Dashboards.DashboardsModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Notifications.NotificationsModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Office.OfficeModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.ReportsV2.ReportsModuleV2));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Scheduler.SchedulerModuleBase));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Validation.ValidationModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.ViewVariantsModule.ViewVariantsModule));
            DevExpress.ExpressApp.Security.SecurityModule.UsedExportedTypes = DevExpress.Persistent.Base.UsedExportedTypes.Custom;
            AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.FileData));
            AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.FileAttachment));
            AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.Event));
            AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.Resource));
            AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.HCategory));
            AdditionalExportedTypes.Add(typeof(XafSecureSearch.Module.BusinessObjects.SearchConfiguration));
            AdditionalExportedTypes.Add(typeof(XafSecureSearch.Module.BusinessObjects.SearchField));
            AdditionalExportedTypes.Add(typeof(XafSecureSearch.Module.BusinessObjects.SampleCustomer));
            AdditionalExportedTypes.Add(typeof(XafSecureSearch.Module.BusinessObjects.SampleOrder));
            AdditionalExportedTypes.Add(typeof(XafSecureSearch.Module.BusinessObjects.SourceExportView));
        }
        public override IEnumerable<ModuleUpdater> GetModuleUpdaters(IObjectSpace objectSpace, Version versionFromDB)
        {
            ModuleUpdater updater = new DatabaseUpdate.Updater(objectSpace, versionFromDB);
            return new ModuleUpdater[] { updater };
        }
        public override void Setup(XafApplication application)
        {
            // Compile search DTOs BEFORE base.Setup() so types are registered
            // before model generation. application.ConnectionString is null at this point
            // (DI/security lifecycle), so read from IConfiguration via ServiceProvider.
            var connectionString = application.ServiceProvider
                ?.GetService<IConfiguration>()
                ?.GetConnectionString("ConnectionString");

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                var compiledTypeNames = SearchDtoRegistry.Instance.CompileFromDatabase(connectionString, this);

                foreach (var entry in SearchDtoRegistry.Instance.GetAll())
                {
                    var typeInfo = XafTypesInfo.Instance.FindTypeInfo(entry.DtoType);
                    _log.Information(
                        "[Module.Setup] Compiled DTO {TypeName}: IsPersistent={IsPersistent}, IsDomainComponent={IsDomainComponent}",
                        entry.DtoType.FullName,
                        typeInfo?.IsPersistent,
                        typeInfo?.IsDomainComponent);
                }
            }

            base.Setup(application);
        }

        public override void Setup(ApplicationModulesManager moduleManager)
        {
            base.Setup(moduleManager);
        }
    }
}
