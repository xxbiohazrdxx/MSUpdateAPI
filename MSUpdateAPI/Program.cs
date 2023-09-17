using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UpdateAPI.Services;
using UpdateLib.Configuration;
using UpdateLib.Data;

var builder = WebApplication.CreateBuilder(args);

// Set up logging
builder.Logging.AddConsole();
builder.Logging.AddAzureWebAppDiagnostics();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<DatabaseConfiguration>(builder.Configuration.GetSection("DatabaseConfiguration"));

builder.Services.AddSingleton<UpdateService>();

builder.Services.AddDbContextFactory<DatabaseContext>((IServiceProvider serviceProvider, DbContextOptionsBuilder options) =>
{
	var databaseConfiguration = serviceProvider.GetRequiredService<IOptions<DatabaseConfiguration>>().Value;

#if !DEBUG
	options.UseCosmos(databaseConfiguration.Uri, databaseConfiguration.PrimaryKey, databaseConfiguration.DatabaseName);
#endif

#if DEBUG
	options.UseCosmos(databaseConfiguration.Uri, databaseConfiguration.PrimaryKey, databaseConfiguration.DatabaseName, clientOptions =>
	{
		clientOptions.HttpClientFactory(() =>
		{
			HttpMessageHandler httpMessageHandler = new HttpClientHandler()
			{
				ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
			};

			return new HttpClient(httpMessageHandler);
		});
		clientOptions.ConnectionMode(ConnectionMode.Gateway);
	});

	options.EnableSensitiveDataLogging();
#endif
});

builder.Services.AddRateLimiter(options =>
{
	options.AddSlidingWindowLimiter("RateLimiter", opt =>
	{
		opt.Window = TimeSpan.FromHours(1);
		opt.PermitLimit = 100;
		opt.SegmentsPerWindow = 5;
	});
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();

app.UseHttpLogging();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
