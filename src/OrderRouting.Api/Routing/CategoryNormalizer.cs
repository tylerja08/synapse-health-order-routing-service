namespace OrderRouting.Api.Routing;

public static class CategoryNormalizer
{
    public static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
