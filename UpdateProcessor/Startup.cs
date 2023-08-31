//using Microsoft.Azure.Functions.Extensions.DependencyInjection;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Options;
//using System.Xml.Linq;
//using UpdateLib.Configuration;
//using UpdateLib.Data;

//[assembly: FunctionsStartup(typeof(UpdateProcessor.Startup))]

//namespace UpdateProcessor
//{
//	public class Startup : FunctionsStartup
//	{
//		public override void Configure(IFunctionsHostBuilder builder)
//		{
//			builder.Services.AddOptions<DatabaseConfiguration>()
//				.Configure<IConfiguration>((settings, configuration) =>
//				{
//					configuration.GetSection("DatabaseConfiguration").Bind(settings);
//				});

//			builder.Services.AddOptions<FunctionConfiguration>()
//				.Configure<IConfiguration>((settings, configuration) =>
//				{
//					configuration.GetSection("Configuration").Bind(settings);
//				});

//			builder.Services.AddDbContextFactory<DatabaseContext>((IServiceProvider serviceProvider, DbContextOptionsBuilder options) =>
//			{
//				var databaseConfiguration = serviceProvider.GetRequiredService<IOptions<DatabaseConfiguration>>().Value;
//				options.UseCosmos(databaseConfiguration.Uri, databaseConfiguration.PrimaryKey, databaseConfiguration.DatabaseName);
//#if DEBUG
//				options.EnableSensitiveDataLogging();
//#endif
//			});
//		}
//	}
//}