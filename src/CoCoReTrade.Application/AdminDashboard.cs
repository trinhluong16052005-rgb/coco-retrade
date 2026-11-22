namespace CoCoReTrade.Application;

public sealed record DashboardFilter(string Period = "30d", DateOnly? From = null, DateOnly? Through = null);
public sealed record DashboardRange(string Period, DateOnly From, DateOnly Through, DateOnly PreviousFrom,
    DateOnly PreviousThrough, DateOnly Today)
{
    public int Days => Through.DayNumber - From.DayNumber + 1;
    public DateTime FromUtc => Utc(From);
    public DateTime ToUtc => Utc(Through.AddDays(1));
    public DateTime PreviousFromUtc => Utc(PreviousFrom);
    public static DateTime Utc(DateOnly date) => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
    public static DashboardRange Create(DashboardFilter filter, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var period = filter.Period?.Trim().ToLowerInvariant() ?? "30d";
        var through = period == "custom" ? filter.Through ?? throw new ArgumentException("Chọn ngày kết thúc.") : today;
        var from = period switch
        {
            "7d" => today.AddDays(-6), "30d" => today.AddDays(-29),
            "3m" => today.AddMonths(-3).AddDays(1), "12m" => today.AddMonths(-12).AddDays(1),
            "custom" => filter.From ?? throw new ArgumentException("Chọn ngày bắt đầu."),
            _ => throw new ArgumentException("Chọn 7 ngày, 30 ngày, 3 tháng, 12 tháng hoặc khoảng ngày tùy chọn.")
        };
        if (from > through || through > today || from.Year < 2000 || through.DayNumber - from.DayNumber > 3660)
            throw new ArgumentException("Khoảng ngày không hợp lệ. Ngày kết thúc không được sau hôm nay; tối đa 10 năm.");
        var days = through.DayNumber - from.DayNumber + 1;
        return new(period, from, through, from.AddDays(-days), from.AddDays(-1), today);
    }
}

public sealed record DashboardMeasure(string Key, string Label, decimal? Value, decimal? Previous,
    bool Currency = false, bool IncreaseIsGood = true)
{
    public decimal? Difference => Value - Previous;
    public decimal? Percent => Previous > 0 ? decimal.Round((Value!.Value - Previous.Value) * 100 / Previous.Value, 1) : null;
    public string Trend => Value > Previous ? "up" : Value < Previous ? "down" : "flat";
    public string ChangeLabel => Value is null ? "Chưa được cấp quyền" : Previous == 0 && Value > 0 ? "Mới phát sinh" :
        Difference == 0 ? "Không đổi" : Percent.HasValue ? $"{(Percent > 0 ? "+" : "")}{Percent:0.#}%" : "Không đổi";
    public bool Favorable => Difference != 0 && (IncreaseIsGood ? Difference > 0 : Difference < 0);
}
public sealed record DashboardBucket(string Label, DateOnly From, DateOnly Through,
    decimal? Revenue, decimal? Commission, int? NewOrders, int SoldProducts);
public sealed record DashboardDistribution(string Key, string Label, int Count);
public sealed record AdminDashboardOverview(DashboardRange Range, IReadOnlyList<DashboardMeasure> Metrics,
    IReadOnlyList<DashboardMeasure> Business, IReadOnlyList<DashboardBucket> Series,
    IReadOnlyList<DashboardDistribution> OrderStatuses, IReadOnlyList<DashboardDistribution> ProductStatuses,
    bool CanViewOrders, bool CanViewFinancials, DateTime UpdatedAtUtc);

public sealed record DashboardProductDay(DateOnly Day, int Pending, int Active);
public sealed record DashboardOrderDay(DateOnly Day, int Pending, int Shipping, int Ready, int Completed,
    int Cancelled, int HeldPickups, int ShippingWork)
{
    public int Total => Pending + Shipping + Ready + Completed + Cancelled;
}
public sealed record DashboardSalesDay(DateOnly Day, int SoldProducts, decimal Revenue, decimal Commission);
public sealed record DashboardWithdrawalDay(DateOnly Day, int Pending);
public sealed record DashboardSource(IReadOnlyList<DashboardProductDay> Products,
    IReadOnlyList<DashboardOrderDay> Orders, IReadOnlyList<DashboardSalesDay> Sales,
    IReadOnlyList<DashboardWithdrawalDay> Withdrawals);
public interface IAdminDashboardRepository
{
    Task<DashboardSource> ReadAsync(DashboardRange range, DateTime nowUtc, bool includeOrders,
        bool includeFinancials, CancellationToken ct);
}
public interface IAdminDashboardService
{
    Task<AdminDashboardOverview> GetAsync(string actorId, DashboardFilter filter, CancellationToken ct);
}

public sealed class AdminDashboardService(IAdminDashboardRepository repository,
    IStaffPermissionChecker permissions, TimeProvider clock) : IAdminDashboardService
{
    public async Task<AdminDashboardOverview> GetAsync(string actorId, DashboardFilter filter, CancellationToken ct)
    {
        await permissions.EnsureAsync(actorId, StaffPermissions.ReadPending, ct);
        var range = DashboardRange.Create(filter, clock.GetUtcNow());
        var orders = await Allowed(actorId, StaffPermissions.ManageOrders, ct);
        var money = await Allowed(actorId, StaffPermissions.ManageWallets, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var source = await repository.ReadAsync(range, now, orders, money, ct);
        bool Current(DateOnly day) => day >= range.From && day <= range.Through;
        bool Previous(DateOnly day) => day >= range.PreviousFrom && day <= range.PreviousThrough;
        decimal Product(Func<DashboardProductDay, int> selector, bool previous) => source.Products.Where(x => previous ? Previous(x.Day) : Current(x.Day)).Sum(selector);
        decimal Order(Func<DashboardOrderDay, int> selector, bool previous) => source.Orders.Where(x => previous ? Previous(x.Day) : Current(x.Day)).Sum(selector);
        decimal Sale(Func<DashboardSalesDay, decimal> selector, bool previous) => source.Sales.Where(x => previous ? Previous(x.Day) : Current(x.Day)).Sum(selector);
        decimal Withdrawal(bool previous) => source.Withdrawals.Where(x => previous ? Previous(x.Day) : Current(x.Day)).Sum(x => x.Pending);
        DashboardMeasure FromProduct(string key, string label, Func<DashboardProductDay, int> selector, bool good = true) =>
            new(key, label, Product(selector, false), Product(selector, true), IncreaseIsGood: good);
        var metrics = new DashboardMeasure[]
        {
            FromProduct("pending", "Ký gửi chờ xử lý", x => x.Pending, false),
            FromProduct("active", "Đang mở bán", x => x.Active),
            new("held", "Cần giữ cho khách", orders ? Order(x => x.HeldPickups, false) : null,
                orders ? Order(x => x.HeldPickups, true) : null, IncreaseIsGood: false),
            new("shipping", "Đang giao hàng", orders ? Order(x => x.ShippingWork, false) : null,
                orders ? Order(x => x.ShippingWork, true) : null, IncreaseIsGood: false),
            new("sold", "Đã bán thành công", Sale(x => x.SoldProducts, false), Sale(x => x.SoldProducts, true)),
            new("withdrawals", "Yêu cầu rút tiền chờ duyệt", money ? Withdrawal(false) : null,
                money ? Withdrawal(true) : null, IncreaseIsGood: false)
        };
        var business = new List<DashboardMeasure>();
        if (money)
        {
            business.Add(new("revenue", "Doanh thu tiền hàng", Sale(x => x.Revenue, false), Sale(x => x.Revenue, true), true));
            business.Add(new("commission", "Hoa hồng CoCo", Sale(x => x.Commission, false), Sale(x => x.Commission, true), true));
        }
        if (orders) business.Add(new("orders", "Đơn hàng mới", Order(x => x.Total, false), Order(x => x.Total, true)));
        var series = new List<DashboardBucket>();
        for (var start = range.From; start <= range.Through;)
        {
            var next = range.Days <= 45 ? start.AddDays(1) : range.Days <= 120 ? start.AddDays(7) : new DateOnly(start.Year, start.Month, 1).AddMonths(1);
            var end = next.AddDays(-1) < range.Through ? next.AddDays(-1) : range.Through;
            var sales = source.Sales.Where(x => x.Day >= start && x.Day <= end).ToArray();
            var newOrders = source.Orders.Where(x => x.Day >= start && x.Day <= end).Sum(x => x.Total);
            var label = range.Days <= 45 ? start.ToString("dd/MM") : range.Days <= 120 ? $"{start:dd/MM}–{end:dd/MM}" : start.ToString("MM/yyyy");
            series.Add(new(label, start, end, money ? sales.Sum(x => x.Revenue) : null,
                money ? sales.Sum(x => x.Commission) : null, orders ? newOrders : null, sales.Sum(x => x.SoldProducts)));
            start = next;
        }
        var currentOrders = source.Orders.Where(x => Current(x.Day)).ToArray();
        var states = orders ? new DashboardDistribution[] {
            new("PendingProcessing", "Chờ xử lý", currentOrders.Sum(x => x.Pending)),
            new("Shipping", "Đang giao", currentOrders.Sum(x => x.Shipping)),
            new("ReadyForPickup", "Chờ khách nhận", currentOrders.Sum(x => x.Ready)),
            new("Completed", "Hoàn tất", currentOrders.Sum(x => x.Completed)),
            new("Cancelled", "Đã hủy", currentOrders.Sum(x => x.Cancelled)) } : [];
        var productStates = new DashboardDistribution[] {
            new("Active", "Đang bán", (int)metrics[1].Value!),
            new("Sold", "Đã bán", (int)metrics[4].Value!),
            new("PendingApproval", "Chờ duyệt", (int)metrics[0].Value!) };
        return new(range, metrics, business, series, states, productStates, orders, money, now);
    }
    private async Task<bool> Allowed(string actorId, string permission, CancellationToken ct)
    {
        try { await permissions.EnsureAsync(actorId, permission, ct); return true; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
