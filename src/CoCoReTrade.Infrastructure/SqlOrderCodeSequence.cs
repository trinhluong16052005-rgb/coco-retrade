using CoCoReTrade.Domain;
using CoCoReTrade.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoCoReTrade.Infrastructure;

public sealed class OrderCodeCounter
{
    public DateOnly Day { get; set; }
    public long LastNumber { get; set; }
}

internal static class SqlOrderCodeSequence
{
    public static async Task AssignAsync(AppDbContext db, Order order, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Cấp mã cần giao dịch SQL.");
        var day = OrderCodeRules.VietnamDate(order.CreatedAtUtc);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int, @next bigint;
            EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=15000;
            IF @result < 0 THROW 51001, N'Không thể cấp mã đơn hàng. Vui lòng thử lại.', 1;
            SELECT @next=LastNumber FROM dbo.OrderCodeCounters WITH (UPDLOCK,HOLDLOCK) WHERE [Day]=@day;
            IF @next IS NULL
            BEGIN
                SET @next=1;
                INSERT dbo.OrderCodeCounters ([Day],LastNumber) VALUES (@day,@next);
            END
            ELSE
            BEGIN
                SET @next=@next+1;
                UPDATE dbo.OrderCodeCounters SET LastNumber=@next WHERE [Day]=@day;
            END;
            SELECT @next;
            """;
        var date = command.CreateParameter(); date.ParameterName = "@day"; date.DbType = System.Data.DbType.Date;
        date.Value = day.ToDateTime(TimeOnly.MinValue); command.Parameters.Add(date);
        var resource = command.CreateParameter(); resource.ParameterName = "@resource";
        resource.Value = "CoCo.OrderCode." + day.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        command.Parameters.Add(resource);
        try { order.AssignOrderCode(Convert.ToInt64(await command.ExecuteScalarAsync(ct))); }
        catch (SqlException ex) when (ex.Number == 51001)
        { throw new OrderConcurrencyException("Có xung đột khi đặt hàng. Hãy thử lại với cùng yêu cầu."); }
    }
}
