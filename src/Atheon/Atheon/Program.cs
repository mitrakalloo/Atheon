﻿using Atheon.Extensions;
using Atheon.Options;
using Atheon.Services;
using Atheon.Services.BungieApi;
using Atheon.Services.Db.Sqlite;
using Atheon.Services.Hosted;
using Atheon.Services.Interfaces;
using Atheon.Services.Scanners.DestinyProfileScanner;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    ConfigureServices(builder);

    var application = builder.Build();

    ConfigureApplication(application);

    await application.RunAsync();
}
catch (Exception exception)
{
    Log.Logger.Error(exception, "Failed to start app");
}

void ConfigureServices(WebApplicationBuilder applicationBuilder)
{
    applicationBuilder.Services.AddControllersWithViews();
    applicationBuilder.Services.AddSwaggerGen();

    applicationBuilder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services.GetService<IServiceProvider>())
            .Enrich.FromLogContext()
            .WriteTo.Console();
    });

    applicationBuilder.Services.AddDiscordServices();

    applicationBuilder.Services.Configure<DatabaseOptions>((settings) =>
    {
        applicationBuilder.Configuration.GetSection("Database").Bind(settings);
    });

    switch (applicationBuilder.Configuration.GetSection("Database:CurrentMode").Value)
    {
        case DatabaseOptions.SqliteKey:
            {
                applicationBuilder.Services.AddSingleton<IDbConnectionFactory, SqliteDbConnectionFactory>();
                applicationBuilder.Services.AddSingleton<IDbBootstrap, SqliteDbBootstrap>();
                applicationBuilder.Services.AddSingleton<ISettingsStorage, SqliteSettingsStorage>();
                applicationBuilder.Services.AddSingleton<IDbAccess, SqliteDbAccess>();
                applicationBuilder.Services.AddSingleton<IDbDataValidator, SqliteDbDataValidator>();
                break;
            }
    }

    applicationBuilder.Services.AddSingleton<IBungieClientProvider, BungieClientProvider>();
    applicationBuilder.Services.AddSingleton<BungieNetApiCallLogger>();
    applicationBuilder.Services.AddSingleton<DestinyProfileScanner>();

    applicationBuilder.Services.AddHostedService<ApplicationStartup>();
}

void ConfigureApplication(WebApplication app)
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();


    app.MapControllerRoute(
        name: "default",
        pattern: "{controller}/{action=Index}/{id?}");

    app.MapFallbackToFile("index.html");
}