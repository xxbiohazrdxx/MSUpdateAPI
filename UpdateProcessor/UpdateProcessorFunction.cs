using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata.Content;
using Microsoft.PackageGraph.MicrosoftUpdate.Source;
using Microsoft.PackageGraph.Storage;
using UpdateLib.Configuration;
using UpdateLib.Data;
using UpdateLib.Models;
using File = UpdateLib.Models.File;

namespace UpdateProcessor
{
	public class UpdateProcessorFunction
	{
		private readonly ILogger logger;
		private readonly IDbContextFactory<DatabaseContext> dbContextFactory;

		private readonly FunctionConfiguration configuration;

		public Status Status { get; private set; } = new Status();

		private string logMessagePrefix = string.Empty;

		public UpdateProcessorFunction(ILoggerFactory loggerFactory, IDbContextFactory<DatabaseContext> DbContextFactory, IOptions<FunctionConfiguration> Configuration)
		{
			logger = loggerFactory.CreateLogger<UpdateProcessorFunction>();
			dbContextFactory = DbContextFactory;
			configuration = Configuration.Value;
		}

		[Function("UpdateProcessorFunction")]
		public async Task Run([TimerTrigger("0 0 0/12 * * *", RunOnStartup = true)] MyInfo myTimer, CancellationToken Token)
		{
			logger.LogInformation("Timer trigger function executed at: {CurrentTime}", DateTime.Now);
			logger.LogInformation("Next timer schedule at: {FutureTime}", myTimer.ScheduleStatus.Next);

			using (var dbContext = await dbContextFactory.CreateDbContextAsync(Token))
			{
				await dbContext.Database.EnsureCreatedAsync(Token);
			}

			await LoadMetadata(Token);
		}

		// Activates the trottle timer and begins metadata downloads from the upstream source
		internal async Task LoadMetadata(CancellationToken Token)
		{
			Status.State = Status.LoadingMetadata;
			Status.AddLogMessage("Beginning metadata refresh");
			logger.LogInformation("Beginning metadata refresh");

			await LoadClassificationMetadata(Token);
			await LoadUpdateMetadata(Token);

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

			UpstreamCategoriesSource categoriesSource = new(Endpoint.Default);
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

			UpstreamUpdatesSource updatesSource = new(Endpoint.Default, updatesFilter);
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
						.Select(y => new File()
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
					.Select(x => new File()
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

		private void CopyProgress(object? sender, PackageStoreEventArgs e)
		{
			Status.AddLogMessage(string.Format("{0}: {1}/{2}", logMessagePrefix, e.Current, e.Total));
			logger.LogInformation("{LogMessagePrefix}: {Current}/{Total}", logMessagePrefix, e.Current, e.Total);
		}
	}

	public class MyInfo
	{
		public MyScheduleStatus ScheduleStatus { get; set; }

		public bool IsPastDue { get; set; }
	}

	public class MyScheduleStatus
	{
		public DateTime Last { get; set; }

		public DateTime Next { get; set; }

		public DateTime LastUpdated { get; set; }
	}
}
