using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UpdateLib.Configuration;
using UpdateLib.Data;

var host = new HostBuilder()
	.ConfigureFunctionsWorkerDefaults()
	.ConfigureServices(services => {
		services.AddApplicationInsightsTelemetryWorkerService();
		services.ConfigureFunctionsApplicationInsights();

		services.AddOptions<DatabaseConfiguration>()
			.Configure<IConfiguration>((settings, configuration) =>
			{
				configuration.GetSection("DatabaseConfiguration").Bind(settings);
			});

		services.AddOptions<FunctionConfiguration>()
			.Configure<IConfiguration>((settings, configuration) =>
			{
				configuration.GetSection("Configuration").Bind(settings);
			});

		services.AddDbContextFactory<DatabaseContext>((IServiceProvider serviceProvider, DbContextOptionsBuilder options) =>
		{
			var databaseConfiguration = serviceProvider.GetRequiredService<IOptions<DatabaseConfiguration>>().Value;
			options.UseCosmos(databaseConfiguration.Uri, databaseConfiguration.PrimaryKey, databaseConfiguration.DatabaseName);
#if DEBUG
			options.EnableSensitiveDataLogging();
#endif
		});
	})
	.ConfigureLogging(logging =>
	{
		logging.AddConsole();

		logging.Services.Configure<LoggerFilterOptions>(options =>
		{
			LoggerFilterRule defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName
				== "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
			if (defaultRule is not null)
			{
				options.Rules.Remove(defaultRule);
			}
		});
	})
	.Build();

host.Run();
