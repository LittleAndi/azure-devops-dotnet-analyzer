using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.XPath;

namespace Infrastructure;

public interface IAzureDevOpsClient
{
    // Projects
    Task<IEnumerable<ProjectDto>> GetProjects(string organization, CancellationToken cancellationToken = default);

    // Repos
    Task<IEnumerable<RepositoryListItemDto>> GetRepositories(string organization, string project, CancellationToken cancellationToken = default);

    // Items
    Task<IEnumerable<ItemDto>> GetItems(string repositoryUrl, CancellationToken cancellationToken = default);

    // Path
    Task<GitObjectDto> GetGitObject(string organization, string project, string repository, string path, CancellationToken cancellationToken = default);
    Task<RepositoryDto> GetRepository(string repositoryUrl, CancellationToken stoppingToken);
    Task<IEnumerable<AnalyzerItem>> ExploreItemTree(RepositoryDto repositoryDto, string url, CancellationToken stoppingToken);
}

public class AzureDevOpsClient : IAzureDevOpsClient
{
    private readonly HttpClient httpJsonClient;
    private readonly HttpClient httpDownloadClient;

    public AzureDevOpsClient(IHttpClientFactory httpClientFactory)
    {
        this.httpJsonClient = httpClientFactory.CreateClient("jsonclient");
        this.httpDownloadClient = httpClientFactory.CreateClient("downloadclient");
    }

    public async Task<IEnumerable<ProjectDto>> GetProjects(string organization, CancellationToken cancellationToken = default)
    {
        var url = $"{organization}/_apis/projects";
        var projectsDto = await httpJsonClient.GetFromJsonAsync<ProjectsDto>(url, cancellationToken);
        if (projectsDto == null || projectsDto.Value == null) return [];
        return projectsDto.Value;
    }

    public async Task<IEnumerable<RepositoryListItemDto>> GetRepositories(string organization, string project, CancellationToken cancellationToken = default)
    {
        var url = $"{organization}/{project}/_apis/git/repositories";
        var repositoriesDto = await httpJsonClient.GetFromJsonAsync<RepositoriesDto>(url, cancellationToken);
        if (repositoriesDto == null || repositoriesDto.Value == null) return [];
        return repositoriesDto.Value;
    }

    public async Task<IEnumerable<ItemDto>> GetItems(string repositoryItemsUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var itemsDto = await httpJsonClient.GetFromJsonAsync<ItemsDto>(repositoryItemsUrl, cancellationToken);
            if (itemsDto == null || itemsDto.Value == null) return [];
            return itemsDto.Value;
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task<GitObjectDto?> GetGitObject(string organization, string project, string repository, string path, CancellationToken cancellationToken = default)
    {
        var escapedPath = Uri.EscapeDataString(path);
        var url = $"{organization}/{project}/_apis/git/repositories/{repository}/items?path={escapedPath}";
        try
        {
            var contentDto = await httpJsonClient.GetFromJsonAsync<GitObjectDto>(url, cancellationToken);
            return contentDto;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    public async Task<RepositoryDto?> GetRepository(string repositoryUrl, CancellationToken stoppingToken)
    {
        try
        {
            var repositoryDto = await httpJsonClient.GetFromJsonAsync<RepositoryDto>(repositoryUrl, stoppingToken);
            return repositoryDto;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    public async Task<IEnumerable<AnalyzerItem>> ExploreItemTree(RepositoryDto repositoryDto, string url, CancellationToken stoppingToken)
    {
        var gitObjectDto = await httpJsonClient.GetFromJsonAsync<GitObjectDto>(url, stoppingToken);

        if (gitObjectDto == null) return [];

        return await ProcessGitObjectDto(repositoryDto, gitObjectDto, stoppingToken);
    }

    private async Task<IEnumerable<AnalyzerItem>> ProcessGitObjectDto(RepositoryDto repositoryDto, GitObjectDto gitObjectDto, CancellationToken stoppingToken = default)
    {
        List<AnalyzerItem> targetFrameworksFound = [];

        switch (gitObjectDto.GitObjectType)
        {
            case GitObjectType.Tree:
                // Follow the tree
                var frameworksFound = await ProcessTreeUrl(repositoryDto, gitObjectDto.Links.Tree.Href, stoppingToken);
                targetFrameworksFound.AddRange(frameworksFound);
                break;
            case GitObjectType.Blob:
                // If it is a *.csproj file, fetch it and analyze its contents
                break;
            default:
                break;
        }

        return targetFrameworksFound;
    }

    private async Task<IEnumerable<AnalyzerItem>> ProcessTreeUrl(RepositoryDto repositoryDto, string url, CancellationToken cancellation = default)
    {
        List<AnalyzerItem> targetFrameworksFound = [];

        var treeDto = await httpJsonClient.GetFromJsonAsync<TreeDto>(url, cancellation);

        if (treeDto == null) return targetFrameworksFound;

        foreach (var item in treeDto.TreeEntries)
        {
            var frameworksFound = await ProcessItemDto(repositoryDto, item, cancellation);
            targetFrameworksFound.AddRange(frameworksFound);
        }

        return targetFrameworksFound;
    }

    private async Task<IEnumerable<AnalyzerItem>> ProcessItemDto(RepositoryDto repositoryDto, ItemDto item, CancellationToken cancellation = default)
    {
        List<AnalyzerItem> targetFrameworksFound = [];
        switch (item.GitObjectType)
        {
            case GitObjectType.Tree:
                var frameworksFound = await ProcessTreeUrl(repositoryDto, item.Url, cancellation);
                targetFrameworksFound.AddRange(frameworksFound);
                break;
            case GitObjectType.Blob:
                // If it is a *.csproj file, fetch it and analyze its contents
                // Modify accept header to application/octet-stream
                if (item.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    var content = await httpDownloadClient.GetStringAsync(item.Url, cancellation);
                    var stream = await httpDownloadClient.GetStreamAsync(item.Url, cancellation);
                    var xmlReader = XmlReader.Create(stream);
                    XPathDocument xPathDocument = new(xmlReader);
                    XPathNavigator xPathNavigator = xPathDocument.CreateNavigator();
                    string? targetFramework = xPathNavigator.SelectSingleNode("/Project/PropertyGroup/TargetFramework")?.Value;

                    if (targetFramework == null)
                    {
                        // Try with the older format, use namespace http://schemas.microsoft.com/developer/msbuild/2003 for Project
                        XmlNamespaceManager manager = new(xPathNavigator.NameTable);
                        manager.AddNamespace("ns", "http://schemas.microsoft.com/developer/msbuild/2003");

                        targetFramework = xPathNavigator.SelectSingleNode("/ns:Project/ns:PropertyGroup/ns:TargetFrameworkVersion", manager)?.Value;
                    }

                    if (targetFramework != null)
                    {
                        targetFrameworksFound.Add(
                            new AnalyzerItem(
                                Project: repositoryDto.Project.Name,
                                Repository: repositoryDto.Name,
                                RelativePath: item.RelativePath,
                                TargetFramework: targetFramework,
                                ObjectId: item.ObjectId,
                                RepositoryUrl: repositoryDto.WebUrl
                            )
                        );
                    }
                }
                break;
            default:
                break;
        }
        return targetFrameworksFound;
    }
}

public record ProjectsDto(int Count, IEnumerable<ProjectDto> Value);
public record ProjectDto(string Id, string Name, string Description, string Url, string State, int Revision, string Visibility, DateTimeOffset LastUpdateTime);

public record RepositoriesDto(int Count, IEnumerable<RepositoryListItemDto> Value);
public record RepositoryListItemDto(string Id, string Name, string Url);
public record RepositoryDto(
    string Id,
    string Name,
    string Url,
    ProjectDto Project,
    Uri WebUrl,
    [property: JsonPropertyName("_links")]
    RepositoryLinkDto Links
);
public record RepositoryLinkDto(UrlDto Items);

public record ItemDto(
    string ObjectId,
    string RelativePath,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    GitObjectType GitObjectType,
    string Path,
    bool IsFolder,
    string Url
);
public record ItemsDto(int Count, IEnumerable<ItemDto> Value);

public enum GitObjectType
{
    Unknown = 0,
    Blob = 1,
    Tree = 2,
}
public record GitObjectDto(
    string ObjectId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    GitObjectType GitObjectType,
    string Path,
    bool IsFolder,
    string Url,
    [property: JsonPropertyName("_links")]
    GitLinkDto Links
);
public record GitLinkDto(UrlDto Tree);
public record UrlDto(string Href);

public record TreeDto(
    string ObjectId,
    string Url,
    IEnumerable<ItemDto> TreeEntries,
    [property: JsonPropertyName("_links")]
    GitLinkDto Links
    );
public record TreeLinkDto(UrlDto Repository);

public record AnalyzerItem(string Project, string Repository, string RelativePath, string TargetFramework, string ObjectId, Uri RepositoryUrl);