using Microsoft.EntityFrameworkCore;
using MSUpdateAPI.Models;

namespace MSUpdateAPI.Data
{
	public class DatabaseContext : DbContext
	{
		public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options) { }

		public DbSet<Product> Products { get; set; }
		public DbSet<Category> Categories { get; set; }
		public DbSet<Update> Updates { get; set; }
		public DbSet<Detectoid> Detectoids { get; set; }

		protected override void OnModelCreating(ModelBuilder builder)
		{
			builder.HasManualThroughput(1000);

			builder.Entity<Product>(entity =>
			{
				//entity.Property(x => x.Id)
				//	.ToJsonProperty("id");

				entity.Ignore(x => x.Subproducts);

				entity.ToContainer("Products")
					.HasNoDiscriminator()
					.HasKey(x => new { x.Id, x.Revision });
			});
				
			builder.Entity<Category>(entity =>
			{
				entity.Property(x => x.Id)
					.ToJsonProperty("id");

				entity.ToContainer("Categories")
					.HasNoDiscriminator()
					.HasKey(x => x.Id);
			});
				
			builder.Entity<Detectoid>(entity =>
			{
				//entity.Property(x => x.Id)
				//	.ToJsonProperty("id");

				entity.ToContainer("Detectoids")
					.HasNoDiscriminator()
					.HasKey(x => new { x.Id, x.Revision });
			});

			builder.Entity<Update>(entity =>
			{
				entity.Property(x => x.Id)
					.ToJsonProperty("id");

				entity.OwnsOne(x => x.Classification);
				entity.OwnsMany(x => x.Products);

				entity.ToContainer("Updates")
					.HasNoDiscriminator()
					.HasKey(x => x.Id);
			});

			base.OnModelCreating(builder);
		}
	}
}
