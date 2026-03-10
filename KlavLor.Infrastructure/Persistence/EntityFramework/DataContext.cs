using Microsoft.EntityFrameworkCore;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Shared;

namespace KlavLor.Infrastructure.Persistence.EntityFramework;

internal class DataContext(DbContextOptions<DataContext> options) : DbContext(options)
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
            .HasIndex(t => t.ShareToken)
            .IsUnique();

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
                new Role { Id = 2, Name = RoleName.User }
            ]);

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
