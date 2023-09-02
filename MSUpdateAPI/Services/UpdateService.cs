using Microsoft.EntityFrameworkCore;
using UpdateLib.Data;
using UpdateLib.Models;

namespace UpdateAPI.Services
{
	public class UpdateService
	{
		// Read only injected w/ DI
		private readonly ILogger logger;
		private readonly IDbContextFactory<DatabaseContext> dbContextFactory;

		public UpdateService(ILogger<UpdateService> Logger, IDbContextFactory<DatabaseContext> DbContextFactory)
		{
			logger = Logger;
			dbContextFactory = DbContextFactory;
		}

		internal async Task<bool> IsInitialSyncCompleted(CancellationToken Token)
		{
			var status = await GetStatus(Token);
			return status.InitialSyncComplete;
		}

		internal async Task<Status> GetStatus(CancellationToken Token)
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync(Token);
			return await dbContext.Status.FirstAsync(Token);
		}

		#region Updates
		internal async Task<IEnumerable<Update>> GetUpdates(Guid? Category, Guid? Product, string? SearchString, CancellationToken Token)
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync(Token);

			IEnumerable<Update> allUpdates = await dbContext.Updates
					// This is effectively ".Where(x => x.Title.Contains(SearchString, StringComparison.InvariantCultureIgnoreCase)"
					// However, the EF Core provider for Cosmos does not yet translate case-insensitive "Contains()"
					.FromSqlRaw(SearchString == null ?
						"SELECT * FROM x" :	
						"SELECT * FROM x WHERE CONTAINS(x.Title, {0}, true)", SearchString!)
					// x.Classification can be null, which would generally throw a null reference exception. However, since this is 
					// translated and evaluated by the database, it effectively becomes "null == Classification.Value" in that scenario
					.Where(x => !Category.HasValue || x.Classification!.Id == Category.Value)
					.OrderByDescending(x => x.CreationDate)
					.ToListAsync(Token);

			// Evaluate the product filtering client side, as Owned types (in this case, Product) cannot be evaluated server side using the Cosmos provider
			// This could potentially be converted into raw SQL so the query could be run server side, but the client side overhead seems reasonable
			allUpdates = allUpdates
					.Where(x => !Product.HasValue || (x.Products?.Any(y => y.Id == Product.Value) ?? false));

			return allUpdates;
		}

		internal async Task<Update?> GetUpdate(Guid Id, CancellationToken Token)
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync(Token);
			var update = await dbContext.Updates
				.Where(x => x.Id == Id)
				.SingleOrDefaultAsync(Token);

			return update;
		}

		internal async Task<Update?> GetSupersedingUpdate(Guid Id, CancellationToken Token)
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync(Token);

			// Finds the update with the newest CreationDate where the list of superseded updates contains the provided Id
			// Not perfect, as some superseded metadata is missing/incorrect directly from the source, but probably good enough
			var supersedingUpdate = await dbContext.Updates
				.FromSqlRaw("SELECT * FROM x WHERE ARRAY_CONTAINS(x.Superseded, {0})", Id)
				.OrderByDescending(x => x.CreationDate)
				.FirstOrDefaultAsync(Token);

			return supersedingUpdate;
		}

		#endregion

		#region Categories
		internal async Task<List<Category>> GetCategories(CancellationToken Token)
		{
			var allCategories = await GetAllCategories(Token);

			return allCategories.Where(x => x.Enabled).ToList();
		}

		internal async Task<List<Category>> GetAllCategories(CancellationToken Token)
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync(Token);
			var allCategories = await dbContext.Categories.ToListAsync(Token);

			return allCategories;
		}
		#endregion

		#region Products
		internal async Task<Product> GetProducts(CancellationToken Token)
		{
			var allProducts = await GetAllProducts(Token);

			RemoveDisabledSubproducts(allProducts);
			return allProducts;
		}

		internal async Task<Product> GetAllProducts(CancellationToken Token)
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync(Token);
			var allProducts = await dbContext.Products.ToListAsync(Token);

			var rootProduct = allProducts.Where(x => !x.Categories.Any()).Single();

			rootProduct.Enabled = false;

			var allSubproducts = allProducts.Where(x => x.Categories.Any()).ToList();
			rootProduct.Subproducts.AddRange(GetSubproducts(allSubproducts, rootProduct));

			return rootProduct;
		}

		private IEnumerable<Product> GetSubproducts(IEnumerable<Product> AllProducts, Product Parent)
		{
			var subproducts = AllProducts
			  .Where(x => new Guid(x.Categories.First()) == Parent.Id)
			  .Select(x => new Product()
			  {
				  Id = x.Id,
				  Name = x.Name,
				  Enabled = x.Enabled
			  })
			  .ToList();

			foreach (var currentSubproduct in subproducts)
			{
				currentSubproduct.Subproducts.AddRange(GetSubproducts(AllProducts, currentSubproduct));
			}

			return subproducts;
		}

		private static void RemoveDisabledSubproducts(Product Parent)
		{
			Parent.Subproducts.RemoveAll(x => !x.Enabled);
			foreach (var currentSubproduct in Parent.Subproducts)
			{
				RemoveDisabledSubproducts(currentSubproduct);
			}
		}
		#endregion
	}
}
