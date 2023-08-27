using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSUpdateAPI.Configuration;
using MSUpdateAPI.Data;
using MSUpdateAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Set up logging
builder.Logging.AddConsole();
builder.Logging.AddAzureWebAppDiagnostics();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<ServiceConfiguration>(builder.Configuration.GetSection("Configuration"));
builder.Services.Configure<DatabaseConfiguration>(builder.Configuration.GetSection("DatabaseConfiguration"));

builder.Services.AddSingleton<UpdateService>();
builder.Services.AddHostedService<MetadataBackgroundService>();

builder.Services.AddDbContextFactory<DatabaseContext>((IServiceProvider serviceProvider, DbContextOptionsBuilder options) =>
{
	var databaseConfiguration = serviceProvider.GetRequiredService<IOptions<DatabaseConfiguration>>().Value;
	options.UseCosmos(databaseConfiguration.Uri, databaseConfiguration.PrimaryKey, databaseConfiguration.DatabaseName);
#if DEBUG
	options.EnableSensitiveDataLogging();
#endif
});
builder.Services.AddDbContext<DatabaseContext>((IServiceProvider serviceProvider, DbContextOptionsBuilder options) =>
{
	var databaseConfiguration = serviceProvider.GetRequiredService<IOptions<DatabaseConfiguration>>().Value;
	options.UseCosmos(databaseConfiguration.Uri, databaseConfiguration.PrimaryKey, databaseConfiguration.DatabaseName);
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

// Get the database configuration, and create the database if it does not already exist
using (var scope = app.Services.CreateScope())
{
	try
	{
		var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
		context.Database.EnsureCreated();
	}
	catch (Exception ex)
	{
		var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
		logger.LogError(ex, "An error occured creating the database");
		throw;
	}
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseStaticFiles();

app.UseRateLimiter();

app.UseHttpLogging();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
