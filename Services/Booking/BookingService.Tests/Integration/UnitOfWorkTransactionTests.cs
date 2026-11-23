using System.Data;
using BookingService.Dal;
using BookingService.Domain.Exceptions;
using BookingService.Domain.Models;

namespace BookingService.Tests.Integration;

/// <summary>DAL + Unit of Work проти справжньої бази: транзакційний відкат і параметризація.</summary>
public sealed class UnitOfWorkTransactionTests
{
    private static UnitOfWork NewUow() => new(TestDatabase.ConnectionString!);

    [DbFact]
    public async Task Error_in_second_repository_rolls_back_changes_of_the_first()
    {
        // Бронювання #3 із seed — у статусі Submitted, тому перехід у Paid недопустимий (THROW 50002)
        int before;
        await using (var check = NewUow())
            before = (await check.Payments.GetByBookingAsync(3)).Count;

        await using (var uow = NewUow())
        {
            await uow.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                // репозиторій 1: вставка оплати (у межах транзакції)
                await uow.Payments.AddAsync(new Payment { BookingId = 3, Amount = 1m, Provider = "Card", CreatedBy = "test" });
                // репозиторій 2: процедура зміни статусу — штучна помилка (недопустимий перехід)
                await Assert.ThrowsAsync<BusinessConflictException>(() =>
                    uow.Bookings.ChangeStatusAsync(3, BookingStatus.Paid, "test", null, null));
            }
            finally
            {
                await uow.RollbackAsync();
            }
        }

        await using var after = NewUow();
        Assert.Equal(before, (await after.Payments.GetByBookingAsync(3)).Count);   // оплата відкотилась
    }

    [DbFact]
    public async Task Occupied_seat_violates_unique_index_and_whole_booking_is_rolled_back()
    {
        long bookingId;
        await using (var uow = NewUow())
        {
            await uow.BeginTransactionAsync();
            bookingId = await uow.Bookings.AddAsync(new Booking
            {
                CustomerId = Guid.Parse("6f1c2a10-0000-4000-8000-000000000002"),
                Status = BookingStatus.Submitted,
                CreatedBy = "test"
            });
            // місце ряд 7 / місце 10 на сеансі #1 уже продане (seed, бронювання #1) → 2601 → 409
            await Assert.ThrowsAsync<BusinessConflictException>(() => uow.Bookings.AddTicketsAsync(
            [
                new Ticket { BookingId = bookingId, ScreeningId = 1, SeatRow = 7, SeatNumber = 10,
                             MovieTitle = "test", HallName = "IMAX", StartsAt = DateTime.UtcNow, UnitPrice = 1m }
            ]));
            await uow.RollbackAsync();
        }

        await using var check = NewUow();
        Assert.Null(await check.Bookings.GetByIdAsync(bookingId));   // бронювання з першого кроку теж зникло
    }

    [DbFact]
    public async Task Values_with_quotes_do_not_break_sql()
    {
        var email = $"obrien.{Guid.NewGuid():N}@example.com";
        await using var uow = NewUow();

        var id = await uow.Customers.AddAsync(new Customer { Email = email, FullName = "Seán O'Brien", CreatedBy = "test" });
        var (items, _) = await uow.Customers.GetPageAsync("O'Brien", 0, 100);

        Assert.Contains(items, c => c.Id == id && c.FullName == "Seán O'Brien");
        Assert.True(await uow.Customers.SoftDeleteAsync(id, "test"));
    }

    [DbFact]
    public async Task Customer_update_with_stale_rowversion_is_conflict()
    {
        await using var uow = NewUow();
        var id = await uow.Customers.AddAsync(new Customer
        {
            Email = $"rv.{Guid.NewGuid():N}@example.com", FullName = "Версія Тест", CreatedBy = "test"
        });
        var customer = (await uow.Customers.GetByIdAsync(id))!;
        var stale = customer.RowVersion;

        customer.FullName = "Перша зміна";
        await uow.Customers.UpdateAsync(customer, stale);

        customer.FullName = "Друга зміна зі старою версією";
        await Assert.ThrowsAsync<BusinessConflictException>(() => uow.Customers.UpdateAsync(customer, stale));
        await uow.Customers.SoftDeleteAsync(id, "test");
    }
}
