using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Reflection;
using VideoTranslate.DataAccess.Repositories;
using VideoTranslate.FFmpegProcessor.Wrappers;
using VideoTranslate.Service.Services;
using VideoTranslate.Shared.Abstractions.Repositories;
using VideoTranslate.Shared.Abstractions.Services;
using VideoTranslate.Shared.DTO.Configuration;

namespace VideoTranslate.FFmpegProcessor
{
    public class Program
    {        static async Task Main(string[] args)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            Console.WriteLine($"environment: {environment}");

            var baseDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);

            var configurationBuilder = new ConfigurationBuilder();
            var configuration = BuildConfig(configurationBuilder, baseDirectory, environment);

            // Specifying the configuration for serilog
            Log.Logger = new LoggerConfiguration() // initiate the logger configuration
                            .ReadFrom.Configuration(configuration) // connect serilog to our configuration folder
                            .Enrich.FromLogContext() //Adds more information to our logs from built in Serilog
                            .CreateLogger(); //initialise the logger

            Log.Logger.Information("Application Starting");

            try
            {
                // Setup Host
                //var host = CreateDefaultBuilder(configuration, baseDirectory, environment).Build();

                // Invoke Worker
                //using IServiceScope serviceScope = host.Services.CreateScope();
                //IServiceProvider provider = serviceScope.ServiceProvider;

                //var application = provider.GetRequiredService<Application>();
                //application.Start();

                await CreateDefaultBuilder(configuration, baseDirectory, environment)
                    .RunConsoleAsync();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Error while Starting Application");
            }
        }
        static IHostBuilder CreateDefaultBuilder(IConfigurationRoot configuration, string? baseDirectory, string? environment)
        {
            return Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureAppConfiguration(app =>
                {
                    BuildConfig(app, baseDirectory, environment);
                })
                .ConfigureServices(services =>
                {
                    var connectionStrings = configuration.GetSection("ConnectionStrings").Get<ConnectionStringConfiguration>();
                    services.AddSingleton(connectionStrings);
                    var rabbitMQConfiguration = configuration.GetSection("RabbitMQ").Get<RabbitMQConfiguration>();
                    services.AddSingleton(rabbitMQConfiguration);
                    var azureSubscriptionConfiguration = configuration.GetSection("SpeechRecognitionAzure").Get<Models.Configurations.AzureSubscriptionConfiguration>();
                    services.AddSingleton(azureSubscriptionConfiguration);

                    services.AddSingleton<FFmpegWrapper>();

                    services.AddHostedService<HostedServices.ConvertVideoRecognizeQueueService>();

                    services.AddScoped<IFileRepository, FileRepository>();
                    services.AddScoped<IFileServerRepository, FileServerRepository>();
                    services.AddScoped<IVideoFileRepository, VideoFileRepository>();
                    services.AddScoped<IVideoInfoRepository, VideoInfoRepository>();

                    services.AddScoped<IFileService, FileService>();
                    services.AddScoped<IVideoFileService, VideoFileService>();
                    services.AddScoped<IVideoInfoService, VideoInfoService>();
                });
        }
        static IConfigurationRoot BuildConfig(IConfigurationBuilder builder, string? baseDirectory, string? environment)
        {
            // Check the current directory that the application is running on 
            // Then once the file 'appsetting.json' is found, we are adding it.
            // We add env variables, which can override the configs in appsettings.json
            return builder.SetBasePath(Directory.GetCurrentDirectory())
                .SetBasePath(baseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
                .Build();
        }

    }
}