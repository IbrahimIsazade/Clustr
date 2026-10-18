using Microsoft.EntityFrameworkCore;

namespace Clustr.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Membership>().HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();
        b.Entity<Product>().HasIndex(x => new { x.WorkspaceId, x.Sku }).IsUnique();
        b.Entity<Sale>().HasIndex(x => new { x.WorkspaceId, x.RequestKey }).IsUnique();
        b.Entity<Sale>().HasIndex(x => new { x.WorkspaceId, x.CreatedAt });
        b.Entity<Product>().ToTable(t => t.HasCheckConstraint("CK_Product_Values", "Stock >= 0 AND PriceCents >= 0 AND LowStockThreshold >= 0"));
        b.Entity<Membership>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<Membership>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId);
        b.Entity<Product>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId);
        b.Entity<Customer>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId);
        b.Entity<Sale>().HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId);
        b.Entity<Sale>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        b.Entity<SaleLine>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        b.Entity<StockMovement>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        b.Entity<StockMovement>().HasOne<User>().WithMany().HasForeignKey(x => x.ActorId);
    }
}
