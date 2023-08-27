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
		// Read only injected w/ DI
		private readonly ILogger logger;
		private readonly IDbContextFactory<DatabaseContext> dbContextFactory;

		private readonly ServiceConfiguration configuration;

		// Throttle
		private readonly System.Timers.Timer throttleTimer;
		private static readonly TimeSpan throttleInterval = new(0, 2, 30);
		private bool throttle = false;

		public Status Status { get; private set; } = new Status();
		public string LastLogMessage { get; private set; } = string.Empty;
		private string logMessagePrefix = string.Empty;

		public UpdateService(ILogger<UpdateService> Logger, IDbContextFactory<DatabaseContext> DbContextFactory, IOptions<ServiceConfiguration> Configuration)
		{
			logger = Logger;
			dbContextFactory = DbContextFactory;

			Status = new Status()
			{
				State = Status.Idle
			};

			// Set up the throttle timer but leave it disabled. Use an anonymous method to set throttle to true when the Elasped event is invoked
			throttleTimer = new System.Timers.Timer()
			{
				Interval = throttleInterval.TotalMilliseconds,
				AutoReset = false,
				Enabled = false
			};
			throttleTimer.Elapsed += (sender, e) => throttle = true;

			configuration = Configuration.Value;
		}

		private void CopyProgress(object? sender, PackageStoreEventArgs e)
		{
			Status.AddLogMessage(string.Format("{0}: {1}/{2}", logMessagePrefix, e.Current, e.Total));
			logger.LogInformation("{LogMessagePrefix}: {Current}/{Total}", logMessagePrefix, e.Current, e.Total);
		}

		// When called, checks to see if the application should be in a throttling state, if so the execution thread is put to sleep for the throttle duration
		private void Throttle()
		{
			if (throttle)
			{
				Status.State = Status.Throttling;
				Status.AddLogMessage("Self throttling for 150 seconds so as to not exceed quota");
				logger.LogInformation("Self throttling for 150 seconds so as to not exceed quota");
				Thread.Sleep(throttleInterval);

				Status.State = Status.LoadingMetadata;
				Status.AddLogMessage("Waking from self throttle");
				logger.LogInformation("Waking from self throttle");
				throttle = false;
				throttleTimer.Interval = throttleInterval.TotalMilliseconds;
			}
		}

		#region Metadata
		// Activates the trottle timer and begins metadata downloads from the upstream source
		internal async Task LoadMetadata(CancellationToken Token)
		{
			Status.State = Status.LoadingMetadata;
			Status.AddLogMessage("Beginning metadata refresh");
			logger.LogInformation("Beginning metadata refresh");

			#if !DEBUG
			throttleTimer.Enabled = true;
			#endif

			await LoadClassificationMetadata(Token);
			await LoadUpdateMetadata(Token);

			throttleTimer.Enabled = false;

			Status.State = Status.Idle;
			Status.AddLogMessage("Metadata refresh complete");
			logger.LogInformation("Metadata refresh complete");
		}

		// Begin downloading classification (categories, products, and detectoids) metadata, and inserting it as it becomes available
		private async Task LoadClassificationMetadata(CancellationToken Token)
		{
			logMessagePrefix = "Loading classification metadata";

			using var dbContext = await dbContextFactory.CreateDbContextAsync(Token);
			var allExistingCategories = await dbContext.Categories.ToListAsync(Token);
			var allExistingProducts = await dbContext.Products.ToListAsync(Token);
			var allExistingDetectoids = await dbContext.Detectoids.ToListAsync(Token);

			UpstreamCategoriesSource categoriesSource = new(Microsoft.PackageGraph.MicrosoftUpdate.Source.Endpoint.Default);
			categoriesSource.MetadataCopyProgress += CopyProgress;

			// Existing IDs from the database can be passed to the upstream source, those IDs will be ignored and not downloaded/processed again
			var allClassificationCategories = categoriesSource
				.GetCategories(Token,
					 allExistingCategories.Select(x => x.Id)
					.Concat(allExistingProducts.Select(x => x.Id))
					.Concat(allExistingDetectoids.Select(x => x.Id)));

			Status.AddLogMessage("Processing classification metadata");
			Status.CategoryCount = allExistingCategories.Count;
			Status.ProductCount = allExistingProducts.Count;
			Status.DetectoidCount = allExistingDetectoids.Count;
			logger.LogInformation("Processing classification metadata");
			logger.LogInformation("Existing db classification count: C: {existingCategoryCount}, P: {existingProductCount}, D: {existingDetectoidCount}", 
				allExistingCategories.Count,
				allExistingProducts.Count, 
				allExistingDetectoids.Count);

			
			// Iterate through all of the metadata returned by the upstream source
			await foreach (var currentClassification in allClassificationCategories)
			{
				Throttle();
				
				// Depending on the type of the MicrosoftUpdatePackage, create the entity type and insert into the correct container
				if (currentClassification is ClassificationCategory)
				{
					await dbContext.Categories.AddAsync(new Category()
					{
						Id = currentClassification.Id.ID,
						Name = currentClassification.Title
					}, Token);

					Status.CategoryCount++;
				}
				else if (currentClassification is ProductCategory)
				{
					await dbContext.Products.AddAsync(new Product()
					{
						Id = currentClassification.Id.ID,
						Revision = currentClassification.Id.Revision,
						Name = currentClassification.Title,
						Categories = currentClassification.Categories?.Select(y => y.ToString()).ToList() ?? new List<string>()
					}, Token);

					Status.ProductCount++;
				}
				// While we don't actually use detectoids, storing them in the database reduces delta metadata sync time
				else if (currentClassification is DetectoidCategory)
				{
					await dbContext.Detectoids.AddAsync(new Detectoid()
					{
						Id = currentClassification.Id.ID,
						Revision = currentClassification.Id.Revision,
						Name = currentClassification.Title
					}, Token);

					Status.DetectoidCount++;
				}
				else
				{
					break;
				}

				await dbContext.SaveChangesAsync(Token);

				logger.LogTrace("Added classification: {Id}:{Revision} - {Title}",
					currentClassification.Id.ID,
					currentClassification.Id.Revision,
					currentClassification.Title);
			}
		}

		// Begin downloading update metadata, and inserting it as it becomes available
		private async Task LoadUpdateMetadata(CancellationToken Token)
		{
			logMessagePrefix = "Loading update metadata";

			using var dbContext = await dbContextFactory.CreateDbContextAsync(Token);
			var allExistingUpdates = await dbContext.Updates.ToListAsync(Token);
			var allProducts = await dbContext.Products.ToListAsync(Token);
			var allCategories = await dbContext.Categories.ToListAsync(Token);

			// Filter so only the particular types and products wanted will be downloaded and processed
			var updatesFilter = new UpstreamSourceFilter();
			updatesFilter
				.ProductsFilter
				.AddRange(configuration.EnabledProducts);
			updatesFilter
				.ClassificationsFilter
				.AddRange(configuration.EnabledCategories);

			UpstreamUpdatesSource updatesSource = new(Microsoft.PackageGraph.MicrosoftUpdate.Source.Endpoint.Default, updatesFilter);
			updatesSource.MetadataCopyProgress += CopyProgress;

			// Existing IDs from the database can be passed to the upstream source, those IDs will be ignored and not downloaded/processed again
			var allUpdates = updatesSource.GetUpdates(Token, allExistingUpdates.Select(x => x.Id));

			Status.AddLogMessage("Processing update metadata");
			Status.UpdateCount = allExistingUpdates.Count;
			logger.LogInformation("Processing update metadata");
			logger.LogInformation("Existing db update count: {existingUpdateCount}", allExistingUpdates.Count);

			var bundledList = new Dictionary<Guid, IEnumerable<Guid>>();

			// Iterate through all of the metadata returned by the upstream source
			await foreach (var current in allUpdates)
			{
				Throttle();

				if (current is not SoftwareUpdate currentUpdate)
				{
					break;
				}
				
				// Some updates will not have any file information and will instead have that metadata contained inside of a "bundled update"
				// If this update contains any bundled updates, save that metadata in a Dictionary for additional processing after this step
				if (currentUpdate.BundledUpdates.Count > 0)
				{
					bundledList.Add(currentUpdate.Id.ID, currentUpdate.BundledUpdates.Select(x => x.ID));
				}

				var newUpdate = new Update()
				{
					Id = currentUpdate.Id.ID,
					Title = currentUpdate.Title,
					Description = currentUpdate.Description,
					CreationDate = DateTime.Parse(currentUpdate.CreationDate),
					KBArticleId = currentUpdate.KBArticleId,
					BundledUpdates = currentUpdate.BundledUpdates
						.Select(x => x.ID.ToString())
						.ToList(),
					Products = allProducts
						.Where(y => currentUpdate.Categories?.Any(z => y.Id == z) ?? false)
						.Select(z => new OwnedProduct()
						{
							Id = z.Id,
							Name = z.Name
						})
						.ToList(),
					Classification = allCategories
						.Where(y => currentUpdate.Categories?.Any(z => y.Id == z) ?? false)
						.Select(z => new OwnedCategory()
						{
							Id = z.Id,
							Name = z.Name
						})
						.SingleOrDefault(),
					SupersededUpdates = currentUpdate.SupersededUpdates
						.Select(x => x.ToString())
						.ToList(),
					Files = currentUpdate.Files
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
						.ToList()
				};

				Status.UpdateCount++;

				await dbContext.Updates.AddAsync(newUpdate, Token);
				await dbContext.SaveChangesAsync(Token);

				logger.LogTrace("Added update: {Id}:{Revision} - {Title}",
					currentUpdate.Id.ID,
					currentUpdate.Id.Revision,
					currentUpdate.Title);
			}

			Status.AddLogMessage("Processing bundled update file lists");
			logger.LogInformation("Processing bundled update file lists");
			logger.LogInformation("Bundled update count: {bundledUpdateCount}", bundledList.Count);

			// Iterate through all updates that have bundled updates, copying the files from the bundled update to the primary update entity
			int bundleProgress = 1;
			foreach (var currentBundle in bundledList)
			{
				Status.AddLogMessage(string.Format("Processing bundled update file lists: {0}/{1}", bundleProgress, bundledList.Count));
				logger.LogInformation("Processing bundled update file lists: {Current}/{Total}", bundleProgress, bundledList.Count);

				var update = await dbContext.Updates
					.Where(x => x.Id == currentBundle.Key)
					.SingleAsync(Token);

				var bundledUpdateFiles = await dbContext.Updates
					.Where(x => currentBundle.Value.Any(y => y == x.Id))
					.ToListAsync(Token);

				update.Files = bundledUpdateFiles
					.SelectMany(x => x.Files)
					.Select(x => new Models.File()
					{
						FileName = x.FileName,
						Source = x.Source,
						ModifiedDate = x.ModifiedDate,
						Digest = new FileDigest()
						{
							Algorithm = x.Digest.Algorithm,
							Value = x.Digest.Value
						},
						Size = x.Size
					})
					.ToList();
				await dbContext.SaveChangesAsync(Token);

				logger.LogTrace("Added bundled files: {Id} - {Title}", update.Id, update.Title);
				bundleProgress++;
			}
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

			if (allProducts is null)
			{
				return null!;
			}

			RemoveDisabledSubproducts(allProducts);
			return allProducts;
		}

		internal async Task<Product> GetAllProducts()
		{
			using var dbContext = await dbContextFactory.CreateDbContextAsync();
			var allProducts = await dbContext.Products.ToListAsync();

			var rootProduct = allProducts.Where(x => !x.Categories.Any()).SingleOrDefault();

			if (rootProduct is null)
			{
				return null!;
			}

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
