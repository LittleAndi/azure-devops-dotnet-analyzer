using System.Text;

await Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        // Infrastructure
        services.AddMemoryCache();
        services.AddTransient<ApiVersionHandler>();
        services.AddTransient<CachedDevOpsHandler>();
        services.AddSingleton<IAzureDevOpsClient, AzureDevOpsClient>();
        services.AddHttpClient("jsonclient", client =>
        {
            client.BaseAddress = new Uri("https://dev.azure.com/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes(hostContext.Configuration["AzureDevOps:Username"] + ":" + hostContext.Configuration["AzureDevOps:PersonalAccessToken"]))
            );
        })
        .AddHttpMessageHandler<ApiVersionHandler>()
        .AddHttpMessageHandler<CachedDevOpsHandler>();

        services.AddHttpClient("downloadclient", client =>
        {
            client.BaseAddress = new Uri("https://dev.azure.com/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes(hostContext.Configuration["AzureDevOps:Username"] + ":" + hostContext.Configuration["AzureDevOps:PersonalAccessToken"]))
            );
        });
        // Add worker
        services.AddHostedService<Analyzer>();
    })
    .UseConsoleLifetime()
    .Build()
    .RunAsync();
