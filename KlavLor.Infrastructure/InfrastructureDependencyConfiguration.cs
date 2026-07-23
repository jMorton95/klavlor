using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using KlavLor.Application.Common.DependencyInjection;
using KlavLor.Application.Common.Settings;
using KlavLor.Domain.Entities;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Infrastructure.ExternalServices.OsrsWiki;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using KlavLor.Infrastructure.Persistence.EntityFramework.Interceptors;
using KlavLor.Infrastructure.Security;
using KlavLor.Infrastructure.Services;

namespace KlavLor.Infrastructure;

public static class InfrastructureDependencyConfiguration
{
    extension(IServiceCollection services)
    {
        public void AddInfrastructure(IConfiguration configuration)
        {
            services.AddDbContext<DataContext>((serviceProvider, options) =>
            {
                var dbSettings = serviceProvider
                    .GetRequiredService<IOptions<DatabaseSettings>>()
                    .Value;

                var connectionString = dbSettings.ToConnectionString();

                if (string.IsNullOrWhiteSpace(connectionString)) {
                    throw new InvalidOperationException("Database connection string is not set.");
                }

                options.UseNpgsql(connectionString);

                var interceptors = serviceProvider.GetServices<IAuditInterceptor>()
                    .OfType<SaveChangesInterceptor>()
                    .ToArray<IInterceptor>();

                if (interceptors.Length != 0)
                    options.AddInterceptors(interceptors);
            });

            // Data Protection keys (which protect the auth + antiforgery cookies) are persisted
            // to the database so they survive restarts/deploys. This lives here, in the assembly
            // that owns DataContext, so DataContext can stay internal — no InternalsVisibleTo needed.
            // The application name is fixed so payloads remain decryptable across deployments.
            services.AddDataProtection()
                .SetApplicationName("KlavLor.Web")
                .PersistKeysToDbContext<DataContext>();

            // Encrypt the key ring at rest with a key derived from AuthKey (injected via env in
            // production, so it is not present in DB dumps/backups). Runs after AddDataProtection,
            // so this XmlEncryptor wins over the (absent) default.
            var dataProtectionKey = AuthKeyDerivation.DeriveKey(configuration);
            services.Configure<KeyManagementOptions>(options =>
                options.XmlEncryptor = new AuthKeyXmlEncryptor(dataProtectionKey));

            services.AddDomainRepositories();
            services.AddApplicationRepositories();

            services.AddSingleton<PasswordHasher<User>>();
            services.AddScoped<KlavLor.Domain.Interfaces.Services.IPasswordService, AspNetPasswordService>();

            services.AddScoped<IDatabaseConnector, EntityFrameworkDatabaseConnector>();
            services.AddScoped<IMigrationService, EntityFrameworkMigrator>();

            services.AddSingleton<ILootFeedHighlightTracker, LootFeedHighlightTracker>();
            services.AddSingleton<ILootFeedService, LootFeedService>();
            services.AddHostedService<LootFeedSeederService>();

            // Records every background-service cycle into the JobRuns log for the admin health panel.
            services.AddSingleton<KlavLor.Application.Interfaces.Services.IJobRunRecorder, JobRunRecorder>();

            services.AddSingleton<ICollectionLogCache, CollectionLogCache>();
            services.AddSingleton<ISystemSettingsCache, SystemSettingsCache>();
            services.AddSingleton<ISourceRateModifierCache, SourceRateModifierCache>();

            services.AddScoped<IDropRateSyncRunner, DropRateSyncRunner>();
            services.AddScoped<ICollectionLogSyncRunner, CollectionLogSyncRunner>();

            services.AddTransient<OsrsWikiRateLimitHandler>();
            services.AddOsrsWikiClient();
            services.AddImageCacheService();
        }

        private void AddDomainRepositories()
        {
            var domainAssembly = typeof(Entity).Assembly;
            var infrastructureAssembly = typeof(InfrastructureDependencyConfiguration).Assembly;

            var types = infrastructureAssembly.DefinedTypes
                .Where(t => t is { IsClass: true, IsAbstract: false })
                .Select(t => new
                {
                    Impl = t.AsType(),
                    Interface = t.ImplementedInterfaces.FirstOrDefault(i => i.Assembly == domainAssembly && i.Name.EndsWith("Repository"))
                });

            foreach (var type in types.Where(x => x.Interface != null).ToList())
            {
                services.AddScoped(type.Interface!, type.Impl);
            }
        }

        private void AddOsrsWikiClient()
        {
            services.AddHttpClient<IOsrsWikiClient, OsrsWikiClient>((_, client) =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("KlavLor/1.0 (OSRS Gear Progression Builder)");
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<OsrsWikiRateLimitHandler>();
        }

        private void AddImageCacheService()
        {
            services.AddHttpClient<IImageCacheService, ImageCacheService>((_, client) =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("KlavLor/1.0 (OSRS Gear Progression Builder)");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<OsrsWikiRateLimitHandler>();
        }

        private void AddApplicationRepositories()
        {
            var applicationAssembly = typeof(ApplicationDependencyConfiguration).Assembly;
            var infrastructureAssembly = typeof(InfrastructureDependencyConfiguration).Assembly;

            var types = infrastructureAssembly.DefinedTypes
                .Where(t => t is { IsClass: true, IsAbstract: false })
                .Select(t => new
                {
                    Impl = t.AsType(),
                    Interface = t.ImplementedInterfaces.FirstOrDefault(i => i.Assembly == applicationAssembly && i.Name.EndsWith("Repository"))
                });

            foreach (var type in types.Where(x => x.Interface != null).ToList())
            {
                services.AddScoped(type.Interface!, type.Impl);
            }
        }
    }
}
