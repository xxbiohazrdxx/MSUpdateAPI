using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata.Content;
using Microsoft.PackageGraph.MicrosoftUpdate.Source;
using Microsoft.PackageGraph.Storage;
using MSUpdateAPI.Configuration;
using MSUpdateAPI.Data;
using MSUpdateAPI.Models;

namespace MSUpdateAPI.Services
{
	public class UpdateService
	{
		private readonly ILogger logger;
		private readonly IDbContextFactory<MSUpdateAPIContext> dbContextFactory;

		private readonly ServiceConfiguration configuration;

		private string logMessagePrefix = string.Empty;

		public bool MetadataLoaded { get; private set; } = false;

		public UpdateService(ILogger<UpdateService> Logger, IDbContextFactory<MSUpdateAPIContext> DbContextFactory, IOptions<ServiceConfiguration> Configuration)
		{
			logger = Logger;
			dbContextFactory = DbContextFactory;
			configuration = Configuration.Value;
		}

		private void CopyProgress(object? sender, PackageStoreEventArgs e)
		{
			logger.LogInformation("{LogMessagePrefix}: {Current}/{Total}", logMessagePrefix, e.Current, e.Total);
		}

#region Metadata
		internal async Task LoadMetadata(CancellationToken Token)
		{
			logger.LogInformation("Beginning metadata refresh");
			MetadataLoaded = false;

			await LoadProductAndCategoryMetadata(Token);
			await LoadUpdateMetadata(Token);

			MetadataLoaded = true;
			logger.LogInformation("Metadata refresh complete");
		}

		private async Task LoadProductAndCategoryMetadata(CancellationToken Token)
		{
			logMessagePrefix = "Loading product and category metadata";

			using var dbContext = await dbContextFactory.CreateDbContextAsync();
			var allExistingCategories = await dbContext.Categories.ToListAsync();
			var allExistingProducts = await dbContext.Products.ToListAsync();
			var allExistingDetectoids = await dbContext.Detectoids.ToListAsync();

			UpstreamCategoriesSource categoriesSource = new(Microsoft.PackageGraph.MicrosoftUpdate.Source.Endpoint.Default);
			categoriesSource.MetadataCopyProgress += CopyProgress;

			var allClassificationCategories = categoriesSource
				.GetCategories(Token, 
					 allExistingCategories.Select(x => x.Id)
					.Concat(allExistingProducts.Select(x => x.Id))
					.Concat(allExistingDetectoids.Select(x => x.Id))
					.ToList());

			logger.LogInformation("Processing product and category metadata");

			var allCategories = allClassificationCategories
				.OfType<ClassificationCategory>()
				.Select(x => new Category()
				{
					Id = x.Id.ID,
					Name = x.Title
				})
				.ToList();

			logger.LogInformation("Existing db category count: {existingCategoryCount}", allExistingCategories.Count);
			logger.LogInformation("New categories to add: {newCategoryCount}", allCategories.Count);

			await dbContext.Categories.AddRangeAsync(allCategories);
			await dbContext.SaveChangesAsync();

			// There are a very small number of duplicate ProductCategory items with identical GUIDs.
			// This selects the ProductCategory with the GUID that has the highest revision
			var allProductCategories = allClassificationCategories
				.OfType<ProductCategory>()
				.GroupBy(x => x.Id.ID)
				.Select(x => x.OrderBy(y => y.Id.Revision).Last());

			var allProducts = allProductCategories
				.Select(x => new Product()
				{
					Id = x.Id.ID,
					Name = x.Title,
					Categories = (x.Categories?
						.Select(y => y.ToString()) ?? Array.Empty<string>())
						.ToList()
				})
				.ToList();

			logger.LogInformation("Existing db product count: {existingProductCount}", allExistingProducts.Count);
			logger.LogInformation("New products to add: {newProductCount}", allProducts.Count);

			await dbContext.Products.AddRangeAsync(allProducts);
			await dbContext.SaveChangesAsync();

			// While we don't actually use detectoids, storing them in the database reduces delta metadata sync time
			var allDetectoids = allClassificationCategories
				.OfType<DetectoidCategory>()
				.GroupBy(x => x.Id.ID)
				.Select(x => x.OrderBy(y => y.Id.Revision).Last())
				.Select(x => new Detectoid(x.Id.ID, x.Title))
				.ToList();

			logger.LogInformation("Existing db detectoid count: {existingDetectoidCount}", allExistingDetectoids.Count);
			logger.LogInformation("New detectoids to add: {newDetectoidCount}", allDetectoids.Count);

			await dbContext.Detectoids.AddRangeAsync(allDetectoids);
			await dbContext.SaveChangesAsync();
		}

		private async Task LoadUpdateMetadata(CancellationToken Token)
		{
			logMessagePrefix = "Loading update metadata";

			using var dbContext = await dbContextFactory.CreateDbContextAsync();
			var allExistingUpdates = dbContext.Updates.ToList();
			var allProducts = dbContext.Products.ToList();
			var allCategories = dbContext.Categories.ToList();

			var updatesFilter = new UpstreamSourceFilter();
			updatesFilter
				.ProductsFilter
				.AddRange(configuration.EnabledProducts);
			updatesFilter
				.ClassificationsFilter
				.AddRange(configuration.EnabledCategories);

			UpstreamUpdatesSource updatesSource = new(Microsoft.PackageGraph.MicrosoftUpdate.Source.Endpoint.Default, updatesFilter);
			updatesSource.MetadataCopyProgress += CopyProgress;

			var allUpdates = updatesSource
				.GetUpdates(Token, allExistingUpdates.Select(x => x.Id), true)
				.OfType<SoftwareUpdate>()
				.ToList();

			logger.LogInformation("Processing update metadata");

			var processedUpdates = allUpdates
				.OfType<SoftwareUpdate>()
				.Select(x => new Update()
				{
					Id = x.Id.ID,
					Title = x.Title,
					Description = x.Description,
					CreationDate = DateTime.Parse(x.CreationDate),
					KBArticleId = x.KBArticleId,
					Products = allProducts
								.Where(y => x.Categories?.Any(z => y.Id == z) ?? false)
								.Select(z => new OwnedProduct()
								{
									Id = z.Id,
									Name = z.Name
								})
								.ToList(),
					Classification = allCategories
								.Where(y => x.Categories?.Any(z => y.Id == z) ?? false)
								.Select(z => new OwnedCategory()
								{
									Id = z.Id,
									Name = z.Name
								})
								.SingleOrDefault(),
					Superseded = x.SupersededUpdates
								.Select(x => x.ToString())
								.ToList(),
					Files = (x.Files.Any() ? x.Files : allUpdates
									.OfType<SoftwareUpdate>()
									.Where(y => x.BundledUpdates?.Any(z => z.ID == y.Id.ID) ?? false)
									.SelectMany(y => y.Files))
								.OfType<UpdateFile>()
								.Select(y => new Models.File()
								{
									FileName = y.FileName,
									Source = y.Source,
									ModifiedDate = y.ModifiedDate,
									Digest = new FileDigest()
									{
										Algorithm = y.Digest.Algorithm,
										Value = y.Digest.HexString
									},
									Size = y.Size
								})
								.ToList() ?? new List<Models.File>()
				});

			logger.LogInformation("Existing db update count: {existingUpdateCount}", allExistingUpdates.Count);
			logger.LogInformation("New updates to add: {newCategoryCount}", allUpdates.Count);

			await dbContext.Updates.AddRangeAsync(processedUpdates);
			await dbContext.SaveChangesAsync();
		}
#endregion Metadata

		#region Updates
		internal async Task<IEnumerable<Update>> GetUpdates(Guid? Classification, Guid? Product, string? SearchString)
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync();

			IEnumerable<Update> allUpdates = await dbContext.Updates
					// This is effectively ".Where(x => x.Title.Contains(SearchString, StringComparison.InvariantCultureIgnoreCase)"
					// However, the EF Core provider for Cosmos does not yet translate case-insensitive "Contains()"
					.FromSqlRaw(SearchString == null ?
						"SELECT * FROM x" :	
						"SELECT * FROM x WHERE CONTAINS(x.Title, {0}, true)", SearchString!)
					// x.Classification can be null, which would generally throw a null reference exception. However, since this is 
					// translated and evaluated by the database, it effectively becomes "null == Classification.Value" in that scenario
					.Where(x => !Classification.HasValue || x.Classification!.Id == Classification.Value)
					.OrderByDescending(x => x.CreationDate)
					.ToListAsync();

			// Evaluate the product filtering client side, as Owned types (in this case, Product) cannot be evaluated server side using the Cosmos provider
			// This could potentially be converted into raw SQL so the query could be run server side, but the client side overhead seems reasonable
			allUpdates = allUpdates
					.Where(x => !Product.HasValue || (x.Products?.Any(y => y.Id == Product.Value) ?? false));

			return allUpdates;
		}

		internal async Task<Update?> GetUpdate(Guid Id)
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync();
			var update = await dbContext.Updates
				.Where(x => x.Id == Id)
				.SingleOrDefaultAsync();

			return update;
		}

		internal async Task<Update?> GetSupersedingUpdate(Guid Id)
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync();

			// Finds the update with the newest CreationDate where the list of superseded updates contains the provided Id
			// Not perfect, as some superseded metadata is missing/incorrect directly from the source, but probably good enough
			var supersedingUpdate = await dbContext.Updates
				.FromSqlRaw("SELECT * FROM x WHERE ARRAY_CONTAINS(x.Superseded, {0})", Id)
				.OrderByDescending(x => x.CreationDate)
				.FirstOrDefaultAsync();

			return supersedingUpdate;
		}

		#endregion

		#region Categories
		internal async Task<List<Category>> GetCategories()
		{
			var allCategories = await GetAllCategories();

			return allCategories.Where(x => configuration.EnabledCategories.Contains(x.Id)).ToList();
		}

		internal async Task<List<Category>> GetAllCategories()
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync();
			var allCategories = await dbContext.Categories.ToListAsync();

			return allCategories;
		}
		#endregion

		#region Products
		internal async Task<Product> GetProducts()
		{
			var allProducts = await GetAllProducts();

			RemoveDisabledSubproducts(allProducts);
			return allProducts;
		}

		internal async Task<Product> GetAllProducts()
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync();
			var allProducts = await dbContext.Products.ToListAsync();

			var rootProduct = allProducts.Where(x => !x.Categories.Any()).Single();
			var allSubproducts = allProducts.Where(x => x.Categories.Any()).ToList();

			rootProduct.Enabled = false;

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
				  Enabled = configuration.EnabledProducts.Contains(x.Id)
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
