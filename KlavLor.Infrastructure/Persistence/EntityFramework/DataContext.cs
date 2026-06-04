using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Shared;

namespace KlavLor.Infrastructure.Persistence.EntityFramework;

internal class DataContext(DbContextOptions<DataContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public virtual DbSet<User> Users => Set<User>();
    public virtual DbSet<Role> Roles => Set<Role>();
    public virtual DbSet<UserRole> UserRoles => Set<UserRole>();
    public virtual DbSet<Template> Templates => Set<Template>();
    public virtual DbSet<TemplateNode> TemplateNodes => Set<TemplateNode>();
    public virtual DbSet<TemplateEdge> TemplateEdges => Set<TemplateEdge>();
    public virtual DbSet<GearItem> GearItems => Set<GearItem>();
    public virtual DbSet<TemplateNodeGroup> TemplateNodeGroups => Set<TemplateNodeGroup>();
    public virtual DbSet<UserNodeCompletion> UserNodeCompletions => Set<UserNodeCompletion>();
    public virtual DbSet<CachedImage> CachedImages => Set<CachedImage>();
    public virtual DbSet<LayoutSnapshot> LayoutSnapshots => Set<LayoutSnapshot>();
    public virtual DbSet<CanvasAnnotation> CanvasAnnotations => Set<CanvasAnnotation>();
    public virtual DbSet<CanvasRegion> CanvasRegions => Set<CanvasRegion>();
    public virtual DbSet<LootRecord> LootRecords => Set<LootRecord>();
    public virtual DbSet<LootDropRow> LootDrops => Set<LootDropRow>();
    public virtual DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public virtual DbSet<ItemIcon> ItemIcons => Set<ItemIcon>();
    public virtual DbSet<SourceIcon> SourceIcons => Set<SourceIcon>();
    public virtual DbSet<GameCharacter> GameCharacters => Set<GameCharacter>();
    public virtual DbSet<CollectionLogItem> CollectionLogItems => Set<CollectionLogItem>();
    public virtual DbSet<CollectionLogExclusion> CollectionLogExclusions => Set<CollectionLogExclusion>();
    public virtual DbSet<DropRate> DropRates => Set<DropRate>();
    public virtual DbSet<DropRateMiss> DropRateMisses => Set<DropRateMiss>();
    public virtual DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // UserRole composite key
        modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });

        modelBuilder.Entity<UserRole>()
            .HasOne(ur => ur.User)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserRole>()
            .HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        // User configuration
        modelBuilder.Entity<User>()
            .Navigation(u => u.UserRoles)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<UserRole>()
            .Navigation(ur => ur.Role)
            .AutoInclude();

        // Template configuration
        modelBuilder.Entity<Template>()
            .HasOne(t => t.CreatedBy)
            .WithMany()
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Template>()
            .Navigation(t => t.Nodes)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<Template>()
            .Navigation(t => t.Edges)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<Template>()
            .Navigation(t => t.Groups)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // TemplateNodeGroup configuration
        modelBuilder.Entity<TemplateNodeGroup>()
            .HasOne(g => g.Template)
            .WithMany(t => t.Groups)
            .HasForeignKey(g => g.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TemplateNode>()
            .HasOne(n => n.Group)
            .WithMany()
            .HasForeignKey(n => n.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        // TemplateNode configuration
        modelBuilder.Entity<TemplateNode>()
            .HasOne(n => n.Template)
            .WithMany(t => t.Nodes)
            .HasForeignKey(n => n.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TemplateNode>()
            .Property(n => n.NodeType)
            .HasConversion<string>();

        modelBuilder.Entity<TemplateNode>()
            .Property(n => n.Color)
            .HasDefaultValue("amber");

        // TemplateEdge configuration
        modelBuilder.Entity<TemplateEdge>()
            .HasOne(e => e.Template)
            .WithMany(t => t.Edges)
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TemplateEdge>()
            .HasOne(e => e.FromNode)
            .WithMany()
            .HasForeignKey(e => e.FromNodeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TemplateEdge>()
            .HasOne(e => e.ToNode)
            .WithMany()
            .HasForeignKey(e => e.ToNodeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TemplateEdge>()
            .HasIndex(e => new { e.FromNodeId, e.ToNodeId })
            .IsUnique();

        // GearItem configuration
        modelBuilder.Entity<GearItem>()
            .HasIndex(g => g.Name)
            .IsUnique();

        modelBuilder.Entity<GearItem>()
            .Property(g => g.ItemType)
            .HasConversion<string>();

        // UserNodeCompletion composite key
        modelBuilder.Entity<UserNodeCompletion>()
            .HasKey(unc => new { unc.UserId, unc.TemplateNodeId });

        modelBuilder.Entity<UserNodeCompletion>()
            .HasOne(unc => unc.User)
            .WithMany()
            .HasForeignKey(unc => unc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserNodeCompletion>()
            .HasOne(unc => unc.TemplateNode)
            .WithMany()
            .HasForeignKey(unc => unc.TemplateNodeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Role enum conversion and seed data
        modelBuilder.Entity<Role>()
            .Property(r => r.Name)
            .HasConversion<string>();

        modelBuilder.Entity<Role>()
            .HasData([
                new Role { Id = 1, Name = RoleName.Admin },
                new Role { Id = 2, Name = RoleName.User },
                new Role { Id = 3, Name = RoleName.Auditor }
            ]);

        // LayoutSnapshot configuration
        modelBuilder.Entity<Template>()
            .Navigation(t => t.LayoutSnapshots)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<LayoutSnapshot>()
            .HasOne(s => s.Template)
            .WithMany(t => t.LayoutSnapshots)
            .HasForeignKey(s => s.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        // CanvasAnnotation configuration
        modelBuilder.Entity<Template>()
            .Navigation(t => t.Annotations)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<CanvasAnnotation>()
            .HasOne(a => a.Template)
            .WithMany(t => t.Annotations)
            .HasForeignKey(a => a.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        // CanvasRegion configuration
        modelBuilder.Entity<Template>()
            .Navigation(t => t.Regions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<CanvasRegion>()
            .HasOne(r => r.Template)
            .WithMany(t => t.Regions)
            .HasForeignKey(r => r.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CanvasRegion>()
            .Property(r => r.Color)
            .HasDefaultValue("slate");

        modelBuilder.Entity<CanvasRegion>()
            .Property(r => r.Opacity)
            .HasDefaultValue(0.15);

        // CachedImage configuration
        modelBuilder.Entity<CachedImage>()
            .HasIndex(c => c.SourceUrl)
            .IsUnique();

        modelBuilder.Entity<CachedImage>()
            .Property(c => c.CachedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() AT TIME ZONE 'UTC'");

        // ItemIcon configuration
        modelBuilder.Entity<ItemIcon>()
            .HasIndex(i => i.ItemName)
            .IsUnique();

        modelBuilder.Entity<ItemIcon>()
            .HasOne(i => i.CachedImage)
            .WithMany()
            .HasForeignKey(i => i.CachedImageId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ItemIcon>()
            .Property(i => i.LastAttemptAt)
            .HasColumnType("timestamp with time zone");

        // SourceIcon configuration
        modelBuilder.Entity<SourceIcon>()
            .HasIndex(s => s.SourceName)
            .IsUnique();

        modelBuilder.Entity<SourceIcon>()
            .HasOne(s => s.CachedImage)
            .WithMany()
            .HasForeignKey(s => s.CachedImageId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<SourceIcon>()
            .Property(s => s.LastAttemptAt)
            .HasColumnType("timestamp with time zone");

        // LootRecord configuration
        modelBuilder.Entity<LootRecord>()
            .HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LootRecord>()
            .Property(l => l.SourceType)
            .HasConversion<string>();

        modelBuilder.Entity<LootRecord>()
            .Property(l => l.OccurredAt)
            .HasColumnType("timestamp with time zone");

        modelBuilder.Entity<LootRecord>()
            .HasIndex(l => new { l.UserId, l.SourceName });

        modelBuilder.Entity<LootRecord>()
            .HasIndex(l => new { l.UserId, l.SourceType });

        modelBuilder.Entity<LootRecord>()
            .HasIndex(l => new { l.UserId, l.OccurredAt });

        modelBuilder.Entity<LootRecord>()
            .HasIndex(l => new { l.UserId, l.ContentHash })
            .IsUnique()
            .HasFilter("\"ContentHash\" IS NOT NULL");

        modelBuilder.Entity<LootRecord>()
            .HasOne(l => l.GameCharacter)
            .WithMany()
            .HasForeignKey(l => l.GameCharacterId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<LootRecord>()
            .HasIndex(l => new { l.GameCharacterId, l.SourceName });

        modelBuilder.Entity<LootRecord>()
            .HasIndex(l => new { l.GameCharacterId, l.OccurredAt });

        modelBuilder.Entity<LootRecord>()
            .HasIndex(l => new { l.GameCharacterId, l.TotalValue, l.OccurredAt })
            .HasDatabaseName("IX_LootRecords_GameCharacterId_TotalValue_OccurredAt");

        // LootDropRow configuration — normalised, rebuildable projection of DropsJson.
        // Owned by its LootRecord (cascade delete); DropsJson remains the canonical source.
        // The gin_trgm index on Name (for item-name ILIKE search) is added in the migration
        // as raw SQL since EF's fluent API can't express operator-class indexes.
        modelBuilder.Entity<LootRecord>()
            .Navigation(l => l.Drops)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<LootDropRow>()
            .HasOne(d => d.LootRecord)
            .WithMany(l => l.Drops)
            .HasForeignKey(d => d.LootRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LootDropRow>()
            .HasIndex(d => d.ItemId);

        // Partial index backs the first-time collection-log feeds.
        modelBuilder.Entity<LootDropRow>()
            .HasIndex(d => d.IsFirstTime)
            .HasFilter("\"IsFirstTime\"");

        // GameCharacter configuration
        modelBuilder.Entity<GameCharacter>()
            .HasOne(gc => gc.User)
            .WithMany()
            .HasForeignKey(gc => gc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GameCharacter>()
            .HasIndex(gc => new { gc.UserId, gc.RuneLiteId })
            .IsUnique();

        modelBuilder.Entity<GameCharacter>()
            .HasIndex(gc => gc.DisplayName)
            .IsUnique()
            .HasFilter("\"DisplayName\" IS NOT NULL");

        // Lets the feed-tier query prune by scope + visibility before joining loot rows.
        modelBuilder.Entity<GameCharacter>()
            .HasIndex(gc => new { gc.IsLeagues, gc.IsVisible, gc.IsAdminHidden });

        // CollectionLogItem configuration (wiki-synced reference table; ItemId is the natural key)
        modelBuilder.Entity<CollectionLogItem>()
            .Property(c => c.ItemId)
            .ValueGeneratedNever();

        modelBuilder.Entity<CollectionLogItem>()
            .Property(c => c.SyncedAt)
            .HasColumnType("timestamp with time zone");

        // CollectionLogExclusion configuration (admin blacklist; ItemId is the business key).
        modelBuilder.Entity<CollectionLogExclusion>()
            .HasIndex(e => e.ItemId)
            .IsUnique();

        // DropRateMiss configuration (sources confirmed to have no wiki drop-rate data).
        modelBuilder.Entity<DropRateMiss>()
            .HasIndex(d => d.SourceName)
            .IsUnique();

        // DropRate configuration (wiki-synced per (source, item); joined into source-detail
        // and feed-popover queries). Unique index lets ReplaceForSource use a transactional
        // delete+insert without worrying about duplicates leaking between sync cycles.
        modelBuilder.Entity<DropRate>()
            .HasIndex(d => new { d.SourceName, d.ItemName })
            .IsUnique();

        modelBuilder.Entity<DropRate>()
            .HasIndex(d => d.SourceName);

        modelBuilder.Entity<DropRate>()
            .Property(d => d.SyncedAt)
            .HasColumnType("timestamp with time zone");

        modelBuilder.Entity<DropRate>()
            .Property(d => d.Rolls)
            .HasDefaultValue(1);

        // ApiKey configuration
        modelBuilder.Entity<ApiKey>()
            .HasOne(k => k.User)
            .WithMany()
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ApiKey>()
            .HasIndex(k => k.KeyHash)
            .IsUnique();

        modelBuilder.Entity<ApiKey>()
            .Property(k => k.CreatedAt)
            .HasColumnType("timestamp with time zone");

        modelBuilder.Entity<ApiKey>()
            .Property(k => k.LastUsedAt)
            .HasColumnType("timestamp with time zone");

        // Entity base class configuration (timestamps and row versions)
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(Entity).IsAssignableFrom(entityType.ClrType))
                continue;

            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(Entity.SavedAt))
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("now() AT TIME ZONE 'UTC'");

            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(Entity.RowVersion))
                .IsRowVersion();
        }
    }
}
