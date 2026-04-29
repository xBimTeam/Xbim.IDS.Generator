using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Xbim.Common.Configuration;
using Xbim.IDS.Generator.Common;
using Xbim.IDS.Generator.Dfe;
using Xbim.IDS.Validator.Core;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var imrVersion = args.Contains("--s21", StringComparer.OrdinalIgnoreCase)
            ? ImrVersion.S21
            : ImrVersion.S25;

        var status = args.FirstOrDefault(a => a.StartsWith("--status=", StringComparison.OrdinalIgnoreCase))?.Split('=')[1] ?? "Sn";
        var revision = args.FirstOrDefault(a => a.StartsWith("--revision=", StringComparison.OrdinalIgnoreCase))?.Split('=')[1] ?? "Pnn";

        // bootstrap an app
        var host = CreateHostBuilder(imrVersion, status, revision).Build();

        var generator = host.Services.GetRequiredService<IIdsSchemaGenerator>();
        var modelGenerator = host.Services.GetRequiredService<IModelGenerator>();

        await generator.PublishIDS();
        await modelGenerator.GenerateTestModels();
    }

    static HostApplicationBuilder CreateHostBuilder(ImrVersion imrVersion, string status, string revision)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u4}] {Scope} {Message:lj} {NewLine}{Exception}")
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Xbim.IDS.Validator.Core", Serilog.Events.LogEventLevel.Warning)
            .CreateLogger();

        var hostBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });

        hostBuilder.Services
            .AddXbimToolkit()
            .AddLogging(o => o.AddSerilog(Log.Logger).SetMinimumLevel(LogLevel.Debug))
            .AddSingleton(new DfeOptions { Version = imrVersion, Status = status, Revision = revision })
            .AddTransient<IIdsSchemaGenerator, DfeGenerator>()
            .AddTransient<IModelGenerator, DfeGenerator>()
            .AddIdsValidation()
            ;

        XbimServices.Current.UseExternalServiceProvider(hostBuilder.Services.BuildServiceProvider());

        return hostBuilder;
    }
}