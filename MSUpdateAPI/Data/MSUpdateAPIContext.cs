using Microsoft.EntityFrameworkCore;
using MSUpdateAPI.Models;

namespace MSUpdateAPI.Data
{
	public class MSUpdateAPIContext : DbContext
	{
		public MSUpdateAPIContext(DbContextOptions<MSUpdateAPIContext> options) : base(options) { }

		public DbSet<Product> Products { get; set; }
		public DbSet<Category> Categories { get; set; }
		public DbSet<Update> Updates { get; set; }
		public DbSet<Detectoid> Detectoids { get; set; }

		protected override void OnModelCreating(ModelBuilder builder)
		{
			builder.Entity<Product>()
				.Property(x => x.Id)
				.ToJsonProperty("id");
			builder.Entity<Category>()
				.Property(x => x.Id)
				.ToJsonProperty("id");
			builder.Entity<Update>()
				.Property(x => x.Id)
				.ToJsonProperty("id");
			builder.Entity<Detectoid>()
				.Property(x => x.Id)
				.ToJsonProperty("id");

			builder.Entity<Update>()
				.OwnsOne(x => x.Classification);
			builder.Entity<Update>()
				.OwnsMany(x => x.Products);

			builder.Entity<Product>()
				.Ignore(x => x.Subproducts)
				.ToContainer("Products")
				.HasNoDiscriminator()
				.HasKey(x => x.Id);
			builder.Entity<Category>()
				.ToContainer("Categories")
				.HasNoDiscriminator()
				.HasKey(x => x.Id);
			builder.Entity<Update>()
				.ToContainer("Updates")
				.HasNoDiscriminator()
				.HasKey(x => x.Id);
			builder.Entity<Detectoid>()
				.ToContainer("Detectoids")
				.HasNoDiscriminator()
				.HasKey(x => x.Id);

			base.OnModelCreating(builder);
		}
	}
}
