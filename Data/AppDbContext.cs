using FruitBilling.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace FruitBilling.Web.Data
{
    // 👈 تغییر اصلی: از IdentityDbContext ارث‌بری می‌کند
    public class AppDbContext : IdentityDbContext<IdentityUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        ////////////////////public DbSet<FruitRecord> FruitRecords { get; set; } = null!;
        public DbSet<Farm> Farms { get; set; }
        public DbSet<Plot> Plots { get; set; }
        public DbSet<TreeRow> TreeRows { get; set; }
        public DbSet<Tree> Trees { get; set; }
        public DbSet<Road> Roads => Set<Road>();
        public DbSet<FruitExitPermit> FruitExitPermits => Set<FruitExitPermit>();
        public DbSet<FruitExitPermitItem> FruitExitPermitItems => Set<FruitExitPermitItem>();
        public DbSet<BasculTicket> BasculTickets => Set<BasculTicket>();
        public DbSet<SortingExitPermit> SortingExitPermits => Set<SortingExitPermit>();
        public DbSet<SortingExitPermitItem> SortingExitPermitItems => Set<SortingExitPermitItem>();
        public DbSet<SortingBasculTicket> SortingBasculTickets => Set<SortingBasculTicket>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<RowCondition> RowConditions => Set<RowCondition>();
        public DbSet<FieldLock> FieldLocks => Set<FieldLock>();
        public DbSet<UserImportPermission> UserImportPermissions => Set<UserImportPermission>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<ProductVariety> ProductVarieties => Set<ProductVariety>();
        public DbSet<HarvestCycle> HarvestCycles => Set<HarvestCycle>();
        public DbSet<HarvestCycleCategory> HarvestCycleCategories => Set<HarvestCycleCategory>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<YieldEstimate> YieldEstimates => Set<YieldEstimate>();
        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);

            // برآورد تولید
            mb.Entity<YieldEstimate>().Property(e => e.EstimatedKg).HasPrecision(18, 2);
            mb.Entity<YieldEstimate>().HasOne(e => e.HarvestCycle).WithMany().HasForeignKey(e => e.HarvestCycleId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<YieldEstimate>().HasOne(e => e.Variety).WithMany().HasForeignKey(e => e.VarietyId).OnDelete(DeleteBehavior.NoAction);
            mb.Entity<YieldEstimate>().HasOne(e => e.Plot).WithMany().HasForeignKey(e => e.PlotId).OnDelete(DeleteBehavior.NoAction);

            mb.Entity<Farm>().Property(p => p.AreaHectare).HasPrecision(18, 2);
            mb.Entity<Plot>().Property(p => p.AreaHectare).HasPrecision(18, 2);
            // 👇 Foreign Key برای Farm — SetNull را به NoAction تغییر دهید
            mb.Entity<Road>()
              .HasOne(r => r.Farm)
              .WithMany()
              .HasForeignKey(r => r.FarmId)
              .OnDelete(DeleteBehavior.NoAction);   // 👈 از SetNull به NoAction

            // 👇 Foreign Key برای Plot — SetNull را به NoAction تغییر دهید
            mb.Entity<Road>()
              .HasOne(r => r.Plot)
              .WithMany()
              .HasForeignKey(r => r.PlotId)
              .OnDelete(DeleteBehavior.NoAction);   // 👈 از SetNull به NoAction

            // 🆕 یکتا بودن شماره برگ خروج در هر مزرعه
            mb.Entity<FruitExitPermit>()
              .HasIndex(p => new { p.FarmId, p.PermitNumber })
              .IsUnique();

            // تنظیمات RolePermission
            mb.Entity<RolePermission>()
                .HasIndex(rp => new { rp.RoleId, rp.ResourceName, rp.FieldName })
                .IsUnique();

            // تنظیمات RowCondition
            mb.Entity<RowCondition>()
                .HasOne(rc => rc.RolePermission)
                .WithMany(rp => rp.RowConditions)
                .HasForeignKey(rc => rc.RolePermissionId)
                .OnDelete(DeleteBehavior.Cascade);

            mb.Entity<UserImportPermission>()
                .HasIndex(p => new { p.UserId, p.ResourceName })
                .IsUnique();

            // 🆕 تنظیمات جداول محصولات و دوره‌ها
            // 🆕 تنظیمات گونه‌ها
            mb.Entity<Category>()
                .HasIndex(c => c.Name)
                .IsUnique();

            mb.Entity<Product>()
                .HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            mb.Entity<HarvestCycle>()
                .HasOne(h => h.Category)
                .WithMany(c => c.HarvestCycles)
                .HasForeignKey(h => h.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            mb.Entity<ProductVariety>()
                .HasOne(v => v.Product)
                .WithMany(p => p.Varieties)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // گونه‌های متعدد برای هر دوره برداشت
            mb.Entity<HarvestCycleCategory>()
                .HasKey(x => new { x.HarvestCycleId, x.CategoryId });
            mb.Entity<HarvestCycleCategory>()
                .HasOne(x => x.HarvestCycle)
                .WithMany(x => x.HarvestCycleCategories)
                .HasForeignKey(x => x.HarvestCycleId)
                .OnDelete(DeleteBehavior.Cascade);
            mb.Entity<HarvestCycleCategory>()
                .HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // جزئیات چندرقمی برگ خروج مزرعه
            mb.Entity<FruitExitPermitItem>()
                .HasOne(x => x.FruitExitPermit)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.FruitExitPermitId)
                .OnDelete(DeleteBehavior.Cascade);
            mb.Entity<FruitExitPermitItem>()
                .HasOne(x => x.Variety)
                .WithMany()
                .HasForeignKey(x => x.VarietyId)
                .OnDelete(DeleteBehavior.Restrict);
            mb.Entity<BasculTicket>()
                .HasOne(x => x.FruitExitPermitItem)
                .WithMany(x => x.BasculTickets)
                .HasForeignKey(x => x.FruitExitPermitItemId)
                .OnDelete(DeleteBehavior.NoAction);

            // جزئیات چندرقمی/چنددرجه برگ خروج سورتینگ
            mb.Entity<SortingExitPermitItem>()
                .HasOne(x => x.SortingExitPermit)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.SortingExitPermitId)
                .OnDelete(DeleteBehavior.Cascade);
            mb.Entity<SortingExitPermitItem>()
                .HasOne(x => x.Variety)
                .WithMany()
                .HasForeignKey(x => x.VarietyId)
                .OnDelete(DeleteBehavior.Restrict);
            mb.Entity<SortingBasculTicket>()
                .HasOne(x => x.SortingExitPermitItem)
                .WithMany(x => x.BasculTickets)
                .HasForeignKey(x => x.SortingExitPermitItemId)
                .OnDelete(DeleteBehavior.NoAction);

            // جلوگیری از حذف محصول/رقم/دوره‌ای که در برگه‌ها استفاده شده است (NoAction)
            mb.Entity<FruitExitPermit>()
                .HasOne(p => p.Product).WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.NoAction);
            mb.Entity<FruitExitPermit>()
                .HasOne(p => p.Variety).WithMany(v => v.FruitExitPermits).HasForeignKey(p => p.VarietyId).OnDelete(DeleteBehavior.NoAction);
            mb.Entity<FruitExitPermit>()
                .HasOne(p => p.HarvestCycle).WithMany(c => c.FruitExitPermits).HasForeignKey(p => p.HarvestCycleId).OnDelete(DeleteBehavior.NoAction);

            mb.Entity<SortingExitPermit>()
                .HasOne(p => p.Product).WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.NoAction);
            mb.Entity<SortingExitPermit>()
                .HasOne(p => p.Variety).WithMany(v => v.SortingExitPermits).HasForeignKey(p => p.VarietyId).OnDelete(DeleteBehavior.NoAction);
            mb.Entity<SortingExitPermit>()
                .HasOne(p => p.HarvestCycle).WithMany(c => c.SortingExitPermits).HasForeignKey(p => p.HarvestCycleId).OnDelete(DeleteBehavior.NoAction);
        }

    }
}