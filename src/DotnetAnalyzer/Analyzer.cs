using System.Globalization;
using CsvHelper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application;

public class Analyzer(IAzureDevOpsClient client, IConfiguration configuration, IHostApplicationLifetime hostApplicationLifetime, ILogger<Analyzer> logger) : BackgroundService
{
    private readonly IAzureDevOpsClient client = client;
    private readonly IConfiguration configuration = configuration;
    private readonly IHostApplicationLifetime hostApplicationLifetime = hostApplicationLifetime;
    private readonly ILogger<Analyzer> logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
        logger.LogInformation("Organization: {organization}", configuration["AzureDevOps:Organization"]);

        var organization = configuration["AzureDevOps:Organization"]!;

        using var projectsWriter = new StreamWriter("projects.csv", append: false);
        using var projectsCsv = new CsvWriter(projectsWriter, CultureInfo.InvariantCulture, leaveOpen: false);

        using var itemsWriter = new StreamWriter("items.csv", append: false);
        using var itemsCsv = new CsvWriter(itemsWriter, CultureInfo.InvariantCulture, leaveOpen: false);

        using var frameworksFoundWriter = new StreamWriter("frameworks-found.csv", append: false);
        using var frameworksFoundCsv = new CsvWriter(frameworksFoundWriter, CultureInfo.InvariantCulture, leaveOpen: false);

        var projects = await client.GetProjects(organization, stoppingToken);
        projectsCsv.WriteRecords(projects);

        // Loop through projects, get all repositories
        List<AnalyzerItem> targetFrameworksFound = [];
        foreach (var project in projects)
        {
            logger.LogInformation("Project: {project}", project.Name);
            var repositories = await client.GetRepositories(organization, project.Id, stoppingToken);

            // Log repositories
            foreach (var repositoryItem in repositories)
            {
                logger.LogInformation("Project: {project} > Repository: {repository}", project.Name, repositoryItem.Name);

                var repository = await client.GetRepository(repositoryItem.Url, stoppingToken);

                if (repository == null) continue;

                var items = await client.GetItems(repository.Links.Items.Href, stoppingToken);
                itemsCsv.WriteRecords(items);

                foreach (var item in items)
                {
                    var frameworksFound = await client.ExploreItemTree(repository, item.Url, stoppingToken);
                    targetFrameworksFound.AddRange(frameworksFound);
                }
            }
        }

        frameworksFoundCsv.WriteRecords(targetFrameworksFound);

        await projectsWriter.FlushAsync(stoppingToken);
        await itemsWriter.FlushAsync(stoppingToken);
        await frameworksFoundWriter.FlushAsync(stoppingToken);

        hostApplicationLifetime.StopApplication();
    }
}