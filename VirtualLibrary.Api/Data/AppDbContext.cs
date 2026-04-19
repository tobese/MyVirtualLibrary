using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VirtualLibrary.Api.Models;

namespace VirtualLibrary.Api.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Work> Works => Set<Work>();
    public DbSet<Edition> Editions => Set<Edition>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<EditionAuthor> EditionAuthors => Set<EditionAuthor>();
    public DbSet<Cover> Covers => Set<Cover>();
    public DbSet<UserBook> UserBooks => Set<UserBook>();
    public DbSet<ReadRecord> ReadRecords => Set<ReadRecord>();
    public DbSet<Shelf> Shelves => Set<Shelf>();
    public DbSet<ShelfPlacement> ShelfPlacements => Set<ShelfPlacement>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(entity =>
        {
            entity.Property(u => u.Role).HasConversion<string>();
            entity.Property(u => u.Status).HasConversion<string>();
        });

        var stringListComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
            v => v.ToList());

        builder.Entity<Work>(entity =>
        {
            entity.Property(w => w.Subjects)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(stringListComparer);
            entity.HasIndex(w => w.OpenLibraryId).IsUnique();
        });

        builder.Entity<Edition>(entity =>
        {
            entity.HasIndex(e => e.Isbn13);
            entity.HasIndex(e => e.Isbn10);
            entity.HasIndex(e => e.OpenLibraryId).IsUnique();
            entity.HasOne(e => e.Work)
                  .WithMany(w => w.Editions)
                  .HasForeignKey(e => e.WorkId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Author>(entity =>
        {
            entity.HasIndex(a => a.OpenLibraryId).IsUnique();
        });

        builder.Entity<EditionAuthor>(entity =>
        {
            entity.HasKey(ea => new { ea.EditionId, ea.AuthorId });
            entity.HasOne(ea => ea.Edition)
                  .WithMany(e => e.EditionAuthors)
                  .HasForeignKey(ea => ea.EditionId);
            entity.HasOne(ea => ea.Author)
                  .WithMany(a => a.EditionAuthors)
                  .HasForeignKey(ea => ea.AuthorId);
        });

        builder.Entity<Cover>(entity =>
        {
            entity.HasOne(c => c.Edition)
                  .WithMany(e => e.Covers)
                  .HasForeignKey(c => c.EditionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserBook>(entity =>
        {
            entity.Property(ub => ub.Status).HasConversion<string>();
            entity.HasIndex(ub => new { ub.UserId, ub.EditionId }).IsUnique();
            entity.HasOne(ub => ub.User)
                  .WithMany()
                  .HasForeignKey(ub => ub.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(ub => ub.Edition)
                  .WithMany()
                  .HasForeignKey(ub => ub.EditionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(ub => ub.ReadRecords)
                  .WithOne(r => r.UserBook)
                  .HasForeignKey(r => r.UserBookId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ReadRecord>(entity =>
        {
            entity.HasIndex(r => r.UserBookId);
        });

        builder.Entity<Shelf>(entity =>
        {
            entity.HasOne(s => s.Owner)
                  .WithMany()
                  .HasForeignKey(s => s.OwnerUserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ShelfPlacement>(entity =>
        {
            entity.HasOne(sp => sp.Shelf)
                  .WithMany(s => s.Placements)
                  .HasForeignKey(sp => sp.ShelfId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(sp => sp.UserBook)
                  .WithMany()
                  .HasForeignKey(sp => sp.UserBookId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
