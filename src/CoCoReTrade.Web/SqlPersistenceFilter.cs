using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CoCoReTrade.Web;

public sealed class SqlPersistenceFilter(ILogger<SqlPersistenceFilter> logger) : IExceptionFilter, IResultFilter
{
    public void OnException(ExceptionContext context)
    {
        var exception = context.Exception;
        if (exception is not (DbUpdateException or SqlException)) return;
        var sql = exception.GetBaseException() as SqlException;
        var conflict = exception is DbUpdateConcurrencyException || sql?.Number is 1205 or 2601 or 2627 or 547;
        var status = conflict ? StatusCodes.Status409Conflict : StatusCodes.Status503ServiceUnavailable;
        var message = conflict
            ? "Dữ liệu đã thay đổi hoặc đang có giao dịch liên quan. Hãy tải lại trang để kiểm tra trước khi thử lại."
            : "Chưa xác nhận được kết quả từ cơ sở dữ liệu. Hãy tải lại trang để kiểm tra trước khi thử lại.";
        // Never expose SQL text, bank details, connection strings or provider errors to clients.
        logger.LogWarning("SQL request failed. Type={Type}; SQL number={Number}; Trace={Trace}",
            exception.GetType().Name, sql?.Number, context.HttpContext.TraceIdentifier);
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = context.HttpContext.Request.Path.StartsWithSegments("/api")
            ? new ObjectResult(new { error = message, traceId = context.HttpContext.TraceIdentifier }) { StatusCode = status }
            : new ContentResult { StatusCode = status, ContentType = "text/plain; charset=utf-8", Content = message };
        context.ExceptionHandled = true;
    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not FileResult && (context.HttpContext.User.Identity?.IsAuthenticated == true ||
            context.RouteData.Values["area"]?.ToString() == "Admin"))
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store, no-cache";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
        }
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}
