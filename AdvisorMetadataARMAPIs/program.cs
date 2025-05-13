// See https://aka.ms/new-console-template for more information
using Azure.Core;
using Azure.Identity;
using System.Net.Http.Headers;
using System.Text.Json;


internal class Program
{
    static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    const string tenantId = "<your tenant id>";
    const string subscriptions = "<sub id 1>;<sub id 2>";

    const string armAPIVersion = "2020-01-01";
    const string armEndpoint = "https://management.azure.com";
    const string metadataFilename = "metadata.json";
    const string recommendationsFilename = "recommendations.json";
    const int itemCountperRequest = 100;

    static readonly Dictionary<string, RType> rTypes = [];
    static readonly List<RInstance> rInstances = [];

    static async Task Main(string[] args)
    {
        Console.WriteLine("Advisor Metadata");

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions() { TenantId = tenantId });
        var token = await credential.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]));

        using HttpClient client = new();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        await FetchMetadataAsync(client);
        await FetchRecommendationsAsync(client);
    }

    static async Task<bool> FetchMetadataAsync(HttpClient client)
    {
        var metadataUrl = $"/providers/Microsoft.Advisor/metadata?api-version={armAPIVersion}&$expand=ibiza";
        var metadataEndpoint = $"{armEndpoint}{metadataUrl}";

        var response = await client.GetAsync(metadataEndpoint);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Error calling metadata API: {response.StatusCode}");
            return false;
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        await File.WriteAllTextAsync(metadataFilename, responseBody);

        var metadata = JsonSerializer.Deserialize<MetadataResponse>(responseBody, DefaultJsonSerializerOptions);
        if (metadata == null)
        {
            Console.WriteLine("Error deserializing metadata");
            return false;
        }

        foreach (var item in metadata.Value)
        {
            if (item.Name == "recommendationType")
            {
                foreach (var rtype in item.Properties.SupportedValues)
                {
                    rTypes.TryAdd(rtype.Id, new RType()
                    {
                        DetailedDescription = rtype.DetailedDescription,
                        LearnMoreLink = rtype.LearnMoreLink,
                        PotentialBenefits = rtype.PotentialBenefits
                    });
                }
                break;
            }
        }

        return true;
    }

    static async Task<bool> FetchRecommendationsAsync(HttpClient client)
    {
        foreach (var subscriptionId in subscriptions.Split(";"))
        {
            await FetchSubscriptionRecommendationsAsync(client, subscriptionId);
        }

        return true;
    }

    static async Task<bool> FetchSubscriptionRecommendationsAsync(HttpClient client, string subscriptionId)
    {
        var recommendationListUrl = $"/subscriptions/{subscriptionId}/providers/Microsoft.Advisor/recommendations?api-version={armAPIVersion}";
        var recommendationsEndpoint = $"{armEndpoint}{recommendationListUrl}&$top={itemCountperRequest}";

        while (true)
        {
            var response = await client.GetAsync(recommendationsEndpoint);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error calling recommendation LIST API: {response.StatusCode}");
                return false;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            //await File.WriteAllTextAsync(recommendationsFilename, responseBody);
            var instancesResponse = JsonSerializer.Deserialize<InstancesResponse>(responseBody, DefaultJsonSerializerOptions);

            if (instancesResponse == null)
            {
                Console.WriteLine("Error deserializing instances");
                return false;
            }

            foreach (var instance in instancesResponse.Value)
            {
                if (!rTypes.TryGetValue(instance.Properties.RecommendationTypeId, out var rtype))
                {
                    continue;
                }

                rInstances.Add(new RInstance()
                {
                    Name = instance.Name,
                    Id = instance.Id,
                    Category = instance.Properties.Category,
                    LearnMoreLink = rtype.LearnMoreLink,
                    DetailedDescription = rtype.DetailedDescription,
                });
            }

            if (string.IsNullOrEmpty(instancesResponse.NextLink))
            {
                break;
            }

            recommendationsEndpoint = instancesResponse.NextLink;
        }

        using var writer = new FileStream(recommendationsFilename, FileMode.Create, FileAccess.Write);
        await JsonSerializer.SerializeAsync(writer, rInstances);
        await writer.FlushAsync();
        writer.Close();

        return true;
    }
}

class MetadataResponse
{
    public List<MetadataValue> Value { get; set; } = [];
}

class MetadataValue
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public MetadataProperties Properties { get; set; } = new();
}

class MetadataProperties
{
    public string DisplayName { get; set; } = string.Empty;
    public List<Metadata> SupportedValues { get; set; } = [];
}

class Metadata
{
    public string DetailedDescription { get; set; } = string.Empty;
    public string PotentialBenefits { get; set; } = string.Empty;
    public string LearnMoreLink { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

class RType
{
    public string DetailedDescription { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string PotentialBenefits { get; set; } = string.Empty;
    public string LearnMoreLink { get; set; } = string.Empty;
}

class InstancesResponse
{
    public string NextLink { get; set; } = string.Empty;
    public List<InstanceResponse> Value { get; set; } = [];
}

class InstanceResponse
{
    public string Id { get; set; } = string.Empty ;
    public string Name { get; set; } = string.Empty;
    public InstanceProperties Properties { get; set; } = new InstanceProperties();
}

public class InstanceProperties
{
    public string Category { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public string ImpactedField { get; set; } = string.Empty;
    public string ImpactedValue { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
    public string RecommendationTypeId { get; set; } = string.Empty;
    public ShortDescription ShortDescription { get; set; } = new ShortDescription();
}

public class ShortDescription
{
    public string Problem { get; set; } = string.Empty;
    public string Solution { get; set; } = string.Empty;
}

class RInstance
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string LearnMoreLink { get; set; } = string.Empty; 
    public string DetailedDescription {  get; set; } = string.Empty;
}
