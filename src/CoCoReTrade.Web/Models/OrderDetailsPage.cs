using CoCoReTrade.Application;

namespace CoCoReTrade.Web.Models;

public sealed record OrderDetailsPage(OrderDto Order, int? ConditionPercent, bool HasProductImage);
