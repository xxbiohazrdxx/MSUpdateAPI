using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSUpdateAPI.Configuration;
using MSUpdateAPI.Data;
using MSUpdateAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Set up logging
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<MSUpdateAPIConfiguration>(builder.Configuration.GetSection("Configuration"));
builder.Services.Configure<MSUpdateAPIDBConfiguration>(builder.Configuration.GetSection("DatabaseConfiguration"));

builder.Services.AddSingleton<UpdateService>();
builder.Services.AddHostedService<MetadataBackgroundService>();

builder.Services.AddDbContextFactory<MSUpdateAPIContext>((IServiceProvider serviceProvider, DbContextOptionsBuilder options) =>
{
	var databaseConfiguration = serviceProvider.GetRequiredService<IOptions<MSUpdateAPIDBConfiguration>>().Value;
	options.UseCosmos(databaseConfiguration.Uri, databaseConfiguration.PrimaryKey, databaseConfiguration.DatabaseName);
#if DEBUG
	options.EnableSensitiveDataLogging();
#endif
});
builder.Services.AddDbContext<MSUpdateAPIContext>((IServiceProvider serviceProvider, DbContextOptionsBuilder options) =>
{
	var databaseConfiguration = serviceProvider.GetRequiredService<IOptions<MSUpdateAPIDBConfiguration>>().Value;
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

using (var scope = app.Services.CreateScope())
{
	try
	{
		var context = scope.ServiceProvider.GetRequiredService<MSUpdateAPIContext>();
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

app.UseRateLimiter();

app.UseHttpLogging();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
