using System.Text.Json;

namespace CoCoReTrade.Web.Demo;

public sealed record DemoProduct(
    string Sku, string Name, string Brand, string Category, string Size, string Color,
    int ConditionPercent, string Description, decimal ReferencePriceGbp, decimal PriceVnd,
    string SourceTitle, string SourceUrl, string ImageCredit, IReadOnlyList<string> ImageSources);

public sealed class DemoCatalog
{
    private readonly IReadOnlyDictionary<string, DemoProduct> _products;
    public bool Enabled { get; }

    public DemoCatalog(IConfiguration configuration, IWebHostEnvironment environment)
    {
        Enabled = configuration.GetValue<bool>("Demo:Enabled");
        if (!Enabled)
        {
            _products = new Dictionary<string, DemoProduct>();
            return;
        }
        var path = Path.Combine(environment.ContentRootPath, "demo-products.json");
        var items = JsonSerializer.Deserialize<List<DemoProduct>>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        _products = items.ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
    }

    public DemoProduct? Find(string? sku) => sku is not null && _products.TryGetValue(sku, out var product)
        ? product : null;

    public static bool IsLocalHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1" or "[::1]";
}
