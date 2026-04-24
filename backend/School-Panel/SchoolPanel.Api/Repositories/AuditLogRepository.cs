using System.Data;
using System.Data.SqlClient;
using Dapper;

namespace SchoolPanel.Api.Repositories;

public interface IAuditLogRepository
{
    Task InsertAsync(
        Guid? userId,
        string? userEmail,
        string action,
        string module,
        string? recordId,
        string? oldValue,
        string? newValue,
        string? description,
        string ipAddress,
        string? userAgent,
        bool isSuccess = true,
        string? errorMessage = null,
        CancellationToken ct = default);
}

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly string _connectionString;

    public AuditLogRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string is missing.");
    }

    public async Task InsertAsync(
        Guid? userId,
        string? userEmail,
        string action,
        string module,
        string? recordId,
        string? oldValue,
        string? newValue,
        string? description,
        string ipAddress,
        string? userAgent,
        bool isSuccess = true,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.ExecuteAsync(
            "dbo.sp_InsertAuditLog",
            new
            {
                UserId = userId,
                UserEmail = userEmail,
                Action = action,
                Module = module,
                RecordId = recordId,
                OldValue = oldValue,
                NewValue = newValue,
                Description = description,
                IPAddress = ipAddress,
                UserAgent = userAgent,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage
            },
            commandType: CommandType.StoredProcedure);
    }
}