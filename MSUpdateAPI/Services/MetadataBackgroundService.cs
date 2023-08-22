using Microsoft.Extensions.Options;
using MSUpdateAPI.Configuration;

namespace MSUpdateAPI.Services
{
	public class MetadataBackgroundService : BackgroundService
	{
		private readonly ILogger logger;
		private readonly UpdateService service;
		private readonly TimeSpan RefreshInterval; 

		public MetadataBackgroundService(ILogger<MetadataBackgroundService> Logger, IOptions<MSUpdateAPIConfiguration> Configuration, UpdateService Service)
		{
			logger = Logger;
			service = Service;
			RefreshInterval = new(Configuration.Value.RefreshIntervalHours, Configuration.Value.RefreshIntervalMinutes, 0);

			logger.LogInformation("Initialized with refresh interval: {RefreshInterval}", RefreshInterval);
		}

		protected override async Task ExecuteAsync(CancellationToken Token)
		{
			using PeriodicTimer timer = new(RefreshInterval);
			do
			{
				logger.LogInformation("Starting scheduled metadata refresh");
				await service.LoadMetadata(Token);
			}
			while (!Token.IsCancellationRequested && await timer.WaitForNextTickAsync(Token));
		}
	}
}
