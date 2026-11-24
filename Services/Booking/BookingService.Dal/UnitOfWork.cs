using System.Data;
using BookingService.Dal.Abstractions;
using BookingService.Dal.AdoNet;
using BookingService.Dal.DapperRepositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Dal;

/// <summary>
/// Unit of Work на ADO.NET: ОДНЕ з'єднання на всю операцію (Scoped = один екземпляр на HTTP-запит),
/// транзакція з обраним рівнем ізоляції, СПІЛЬНА для всіх репозиторіїв, гарантований Commit або Rollback.
/// </summary>
/// <remarks>
/// <para><b>Рівень ізоляції.</b> За замовчуванням — <see cref="IsolationLevel.ReadCommitted"/>:
/// немає «брудних» читань, а блокування короткі, тож ризик взаємних блокувань (deadlock) мінімальний.
/// Від гонок за місця захищає не вищий рівень ізоляції, а унікальний фільтрований індекс
/// UX_Tickets_Screening_Seat_Active (друге бронювання того самого місця → 2601 → 409),
/// а від паралельної зміни статусу — UPDLOCK у процедурі usp_Booking_ChangeStatus.
/// Компроміс: Serializable/RepeatableRead дали б менше аномалій читання, але більше блокувань
/// і deadlock-ів під навантаженням — для цього домену це невиправдано.</para>
/// <para><b>Таймаути.</b> CommandTimeout задається в конфігурації (Database:CommandTimeoutSeconds,
/// за замовчуванням 30 с) і застосовується до кожної команди; таймаут з'єднання — у рядку підключення
/// (Connect Timeout). Скасування HTTP-запиту (CancellationToken) перериває запит до SQL Server.</para>
/// </remarks>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly SqlConnection _connection;
    private readonly ILogger<UnitOfWork> _logger;
    private SqlTransaction? _transaction;

    public UnitOfWork(string connectionString, int commandTimeoutSeconds = 30, ILogger<UnitOfWork>? logger = null)
    {
        _connection = new SqlConnection(connectionString);
        _logger = logger ?? NullLogger<UnitOfWork>.Instance;

        // Усі репозиторії отримують ОДНЕ з'єднання і доступ до ПОТОЧНОЇ транзакції (через делегат,
        // бо транзакція з'являється пізніше, ніж створюються репозиторії).
        Func<SqlTransaction?> current = () => _transaction;
        Customers = new CustomerRepository(_connection, current, commandTimeoutSeconds);   // ADO.NET
        Screenings = new ScreeningRepository(_connection, current, commandTimeoutSeconds); // Dapper
        Bookings = new BookingRepository(_connection, current, commandTimeoutSeconds);     // Dapper
        Payments = new PaymentRepository(_connection, current, commandTimeoutSeconds);     // Dapper
        Promotions = new PromotionRepository(_connection, current, commandTimeoutSeconds); // Dapper
        ProcessedEvents = new ProcessedEventRepository(_connection, current, commandTimeoutSeconds);   // Лаб. 08
    }

    public ICustomerRepository Customers { get; }
    public IScreeningRepository Screenings { get; }
    public IBookingRepository Bookings { get; }
    public IPaymentRepository Payments { get; }
    public IPromotionRepository Promotions { get; }
    public IProcessedEventRepository ProcessedEvents { get; }

    public bool HasActiveTransaction => _transaction is not null;

    public async Task BeginTransactionAsync(IsolationLevel level = IsolationLevel.ReadCommitted, CancellationToken ct = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Транзакцію вже розпочато в цьому Unit of Work");

        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync(ct);

        _transaction = (SqlTransaction)await _connection.BeginTransactionAsync(level, ct);
        _logger.LogDebug("Транзакцію розпочато ({IsolationLevel})", level);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("Немає активної транзакції для Commit");
        try
        {
            await _transaction.CommitAsync(ct);
            _logger.LogDebug("Транзакцію зафіксовано (Commit)");
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null) return;
        try
        {
            // Якщо процедура з Лаб. 01 вже виконала ROLLBACK у своєму CATCH, транзакція на сервері
            // завершена («zombie»): Connection == null. Тоді відкочувати вже нічого — лише звільнити об'єкт.
            if (_transaction.Connection is not null)
                await _transaction.RollbackAsync(ct);
            _logger.LogWarning("Транзакцію відкочено (Rollback)");
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Незафіксована транзакція при Dispose відкочується сервером автоматично
        if (_transaction is not null) await _transaction.DisposeAsync();
        await _connection.DisposeAsync();   // з'єднання повертається в пул SqlClient
    }
}
