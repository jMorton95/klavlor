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
    public virtual DbSet<LuckLeaderboardEntry> LuckLeaderboardEntries => Set<LuckLeaderboardEntry>();
    public virtual DbSet<LuckLeaderboardMeta> LuckLeaderboardMeta => Set<LuckLeaderboardMeta>();
    public virtual DbSet<LeaderboardSourceExclusion> LeaderboardSourceExclusions => Set<LeaderboardSourceExclusion>();
    public virtual DbSet<LeaderboardItemExclusion> LeaderboardItemExclusions => Set<LeaderboardItemExclusion>();
    public virtual DbSet<SourceRateModifier> SourceRateModifiers => Set<SourceRateModifier>();
    public virtual DbSet<ItemValueOverride> ItemValueOverrides => Set<ItemValueOverride>();
    public virtual DbSet<CollectionLogCategory> CollectionLogCategories => Set<CollectionLogCategory>();
    public virtual DbSet<CollectionLogCategoryItem> CollectionLogCategoryItems => Set<CollectionLogCategoryItem>();
    public virtual DbSet<CharacterCollectionLogEntry> CharacterCollectionLogEntries => Set<CharacterCollectionLogEntry>();
    public virtual DbSet<CharacterCollectionLogState> CharacterCollectionLogStates => Set<CharacterCollectionLogState>();
    public virtual DbSet<JobRun> JobRuns => Set<JobRun>();
    public virtual DbSet<JobSchedule> JobSchedules => Set<JobSchedule>();
    public virtual DbSet<CharacterSourceBaseline> CharacterSourceBaselines => Set<CharacterSourceBaseline>();
    public virtual DbSet<CharacterDelveDepth> CharacterDelveDepths => Set<CharacterDelveDepth>();
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

        // Supports the per-(character, source) chronological "kill ordinal" COUNT used by the loot
        // feed and source pages. Without OccurredAt/Id in the index that correlated count scans the
        // whole character+source partition for every candidate row — pathologically slow on large
        // datasets (measured ~12.7s → ~0.26s per feed tier with this index).
        modelBuilder.Entity<LootRecord>()
            .HasIndex(l => new { l.GameCharacterId, l.SourceName, l.OccurredAt, l.Id })
            .HasDatabaseName("IX_LootRecords_GameCharacterId_SourceName_OccurredAt_Id");

        // Newest-first ordering for the global loot feed. Without it, each tier's
        // "most recent records above a value threshold" does a full table seq-scan + top-N sort
        // (O(table size), worsening as years of data accrue). A descending OccurredAt index lets
        // the query walk newest-first and stop at the limit (O(results)).
        modelBuilder.Entity<LootRecord>()
            .HasIndex(l => l.OccurredAt)
            .IsDescending()
            .HasDatabaseName("IX_LootRecords_OccurredAt_Desc");

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

        // LuckLeaderboard configuration (precomputed hourly; Board stored as text).
        modelBuilder.Entity<LuckLeaderboardEntry>()
            .Property(e => e.Board)
            .HasConversion<string>();

        // Backs the board query: filter by (Generation, Board) then order by Score desc.
        modelBuilder.Entity<LuckLeaderboardEntry>()
            .HasIndex(e => new { e.Generation, e.Board, e.Score });

        // Admin blacklist of sources excluded from the luck leaderboards (SourceName is the key).
        modelBuilder.Entity<LeaderboardSourceExclusion>()
            .HasIndex(e => e.SourceName)
            .IsUnique();

        // Admin blacklist of items excluded from the luck leaderboards (ItemName is the key).
        modelBuilder.Entity<LeaderboardItemExclusion>()
            .HasIndex(e => e.ItemName)
            .IsUnique();

        // Admin rate multipliers, keyed by (source, item); empty item = source-wide.
        modelBuilder.Entity<SourceRateModifier>()
            .HasIndex(e => new { e.SourceName, e.ItemName })
            .IsUnique();

        // ---------------------------------------------------------------- Collection log (Temple)

        // Category membership is many-to-many: an item can sit under several categories, so the
        // pair is the natural key and the surrogate Id exists only to keep EF happy.
        modelBuilder.Entity<CollectionLogCategoryItem>()
            .HasIndex(e => new { e.CategorySlug, e.ItemId })
            .IsUnique();

        // The reverse lookup — "which categories is this item in" — backs the per-item comparison page.
        modelBuilder.Entity<CollectionLogCategoryItem>()
            .HasIndex(e => e.ItemId);

        modelBuilder.Entity<CollectionLogCategory>()
            .HasIndex(e => new { e.GroupName, e.SortOrder });

        // Composite key, character first: every read is "this character's log", so the leading
        // column matches the access pattern and the PK index serves it without a second index.
        modelBuilder.Entity<CharacterCollectionLogEntry>()
            .HasKey(e => new { e.GameCharacterId, e.ItemId });

        // The cross-character direction — "who owns this item" — for the per-item comparison.
        modelBuilder.Entity<CharacterCollectionLogEntry>()
            .HasIndex(e => e.ItemId);

        // Losing the character loses its log; there is nothing to keep.
        modelBuilder.Entity<CharacterCollectionLogEntry>()
            .HasOne<GameCharacter>()
            .WithMany()
            .HasForeignKey(e => e.GameCharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CharacterCollectionLogState>()
            .HasOne(e => e.GameCharacter)
            .WithMany()
            .HasForeignKey(e => e.GameCharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CharacterCollectionLogState>()
            .Property(e => e.LastOutcome)
            .HasConversion<string>();

        // HasData is a computed property, not a column.
        modelBuilder.Entity<CharacterCollectionLogState>()
            .Ignore(e => e.HasData);

        // Ranking the clan board is an ordered scan of this one column.
        modelBuilder.Entity<CharacterCollectionLogState>()
            .HasIndex(e => e.TotalObtained);

        // Admin intrinsic item values, one row per item (see ItemValueOverride).
        modelBuilder.Entity<ItemValueOverride>()
            .HasIndex(e => e.ItemId)
            .IsUnique();

        // Background-job run log (append-only operational history; not an Entity, no audit stamp).
        modelBuilder.Entity<JobRun>()
            .Property(e => e.Outcome)
            .HasConversion<string>();

        modelBuilder.Entity<JobRun>()
            .Property(e => e.StartedAt)
            .HasColumnType("timestamp with time zone");

        modelBuilder.Entity<JobRun>()
            .Property(e => e.FinishedAt)
            .HasColumnType("timestamp with time zone");

        modelBuilder.Entity<JobRun>()
            .Property(e => e.Detail)
            .HasMaxLength(1000);

        // Latest-per-job (max Id per JobName) and the per-job history both key off (JobName, Id).
        modelBuilder.Entity<JobRun>()
            .HasIndex(e => new { e.JobName, e.Id });

        // Retention prune scans by StartedAt.
        modelBuilder.Entity<JobRun>()
            .HasIndex(e => e.StartedAt);

        // Per-job scheduling state (JobName is the key; polled by recurring services).
        modelBuilder.Entity<JobSchedule>()
            .HasKey(e => e.JobName);

        modelBuilder.Entity<JobSchedule>()
            .Property(e => e.LastRunAt)
            .HasColumnType("timestamp with time zone");

        modelBuilder.Entity<JobSchedule>()
            .Property(e => e.ManualRequestedAt)
            .HasColumnType("timestamp with time zone");

        // Admin baseline kill counts, keyed by (character, source).
        modelBuilder.Entity<CharacterSourceBaseline>()
            .HasKey(e => new { e.GameCharacterId, e.SourceName });

        // Admin average delve depths, keyed the same way.
        modelBuilder.Entity<CharacterDelveDepth>()
            .HasKey(e => new { e.GameCharacterId, e.SourceName });

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
