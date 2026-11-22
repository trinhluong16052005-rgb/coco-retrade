namespace CoCoReTrade.Web.Models;

public sealed record ProductSizeInput(string Category, string Size, bool PreserveLegacyClothing = false);
