﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata.Content;
using Microsoft.PackageGraph.MicrosoftUpdate.Source;
using Microsoft.PackageGraph.Storage;
using MSUpdateAPI.Configuration;
using MSUpdateAPI.Data;
using MSUpdateAPI.Models;
using System.Timers;

namespace MSUpdateAPI.Services
{
	public class UpdateService
	{
		// Read only injected w/ DI
		private readonly ILogger logger;
		private readonly IDbContextFactory<DatabaseContext> dbContextFactory;

		private readonly ServiceConfiguration configuration;

		// Throttle
		private System.Timers.Timer throttleTimer;
		private static readonly TimeSpan throttleInterval = new(0, 2, 0);
		private bool throttle { get; set; } = false;

		public bool MetadataLoaded { get; private set; } = false;
		public string LastLogMessage { get; private set; } = string.Empty;
		private string logMessagePrefix { get; set; } = string.Empty;

		public UpdateService(ILogger<UpdateService> Logger, IDbContextFactory<DatabaseContext> DbContextFactory, IOptions<ServiceConfiguration> Configuration)
		{
			logger = Logger;
			dbContextFactory = DbContextFactory;

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
			LastLogMessage = string.Format("{0}: {1}/{2}", logMessagePrefix, e.Current, e.Total);
			logger.LogInformation("{LogMessagePrefix}: {Current}/{Total}", logMessagePrefix, e.Current, e.Total);
		}

		#region Metadata
		internal async Task LoadMetadata(CancellationToken Token)
		{
			LastLogMessage = "Beginning metadata refresh";
			logger.LogInformation("Beginning metadata refresh");
			MetadataLoaded = false;

			#if !DEBUG
			throttleTimer.Enabled = true;
			#endif

			await LoadProductAndCategoryMetadata(Token);
			await LoadUpdateMetadata(Token);

			throttleTimer.Enabled = false;
			MetadataLoaded = true;
			LastLogMessage = "Metadata refresh complete";
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
					.Concat(allExistingDetectoids.Select(x => x.Id)));

			LastLogMessage = "Processing classification metadata";
			logger.LogInformation("Processing classification metadata");
			logger.LogInformation("Existing db category count: {existingCategoryCount}", allExistingCategories.Count);
			logger.LogInformation("Existing db product count: {existingProductCount}", allExistingProducts.Count);
			logger.LogInformation("Existing db detectoid count: {existingDetectoidCount}", allExistingDetectoids.Count);

			await foreach (var currentClassification in allClassificationCategories)
			{
				if (throttle)
				{
					LastLogMessage = "Self throttling for 2 minutes so as to not exceed quota";
					logger.LogInformation("Self throttling for 2 minutes so as to not exceed quota");
					Thread.Sleep(throttleInterval);

					throttle = false;
					throttleTimer.Interval = throttleInterval.TotalMilliseconds;
				}

				logger.LogInformation("Added classification: {Id}:{Revision} - {Title}", currentClassification.Id.ID, currentClassification.Id.Revision, currentClassification.Title);
				
				if (currentClassification is ClassificationCategory)
				{
					await dbContext.Categories.AddAsync(new Category()
					{
						Id = currentClassification.Id.ID,
						Name = currentClassification.Title
					});
				}
				else if (currentClassification is ProductCategory)
				{
					await dbContext.Products.AddAsync(new Product()
					{
						Id = currentClassification.Id.ID,
						Revision = currentClassification.Id.Revision,
						Name = currentClassification.Title,
						Categories = currentClassification.Categories?.Select(y => y.ToString()).ToList() ?? new List<string>()
					});
				}
				// While we don't actually use detectoids, storing them in the database reduces delta metadata sync time
				else if (currentClassification is DetectoidCategory)
				{
					await dbContext.Detectoids.AddAsync(new Detectoid()
					{
						Id = currentClassification.Id.ID,
						Revision = currentClassification.Id.Revision,
						Name = currentClassification.Title
					});
				}
				else
				{
					break;
				}

				await dbContext.SaveChangesAsync();
			}
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

			var allUpdates = updatesSource.GetUpdates(Token, allExistingUpdates.Select(x => x.Id));

			LastLogMessage = "Processing update metadata";
			logger.LogInformation("Processing update metadata");
			logger.LogInformation("Existing db update count: {existingUpdateCount}", allExistingUpdates.Count);

			await foreach (var current in allUpdates)
			{
				if (throttle)
				{
					logger.LogInformation("Self throttling for 2 minutes so as to not exceed quota");
					Thread.Sleep(throttleInterval);

					throttle = false;
					throttleTimer.Interval = throttleInterval.TotalMilliseconds;
				}

				if (current is not SoftwareUpdate currentUpdate)
				{
					break;
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

				await dbContext.Updates.AddAsync(newUpdate);
				await dbContext.SaveChangesAsync();
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
