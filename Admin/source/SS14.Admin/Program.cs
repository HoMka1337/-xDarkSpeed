using Serilog;

namespace SS14.Admin
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, builder) =>
                {
                    var env = context.HostingEnvironment;
                    builder.AddYamlFile("appsettings.yml", false, true);
                    builder.AddYamlFile($"appsettings.{env.EnvironmentName}.yml", true, true);
                    builder.AddYamlFile("appsettings.Secret.yml", true, true);
                })
                .UseSerilog((ctx, cfg) =>
                {
                    cfg.ReadFrom.Configuration(ctx.Configuration);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    // В установленной панели ресурсы находятся рядом с exe, а конфиг — уровнем выше.
                    var publishedWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
                    if (Directory.Exists(publishedWebRoot))
                        webBuilder.UseWebRoot(publishedWebRoot);
                    webBuilder.UseStartup<Startup>();
                })
                .UseSystemd();
    }
}
