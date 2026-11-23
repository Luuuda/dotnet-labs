using AutoMapper;
using BookingService.Bll.Dtos;
using BookingService.Bll.Validation;
using BookingService.Dal.Abstractions;
using BookingService.Domain.Exceptions;
using BookingService.Domain.Models;
using Microsoft.Extensions.Logging;

namespace BookingService.Bll.Services;

public sealed class CustomerService(IUnitOfWork uow, IMapper mapper, ILogger<CustomerService> logger) : ICustomerService
{
    private const string Actor = "booking-api";

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken ct)
    {
        var customer = await uow.Customers.GetByIdAsync(id, ct)
                       ?? throw new NotFoundException($"Клієнта {id} не знайдено");
        return mapper.Map<CustomerDto>(customer);
    }

    public async Task<PagedResult<CustomerDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        Guards.Paging(page, pageSize);
        var (items, total) = await uow.Customers.GetPageAsync(search, (page - 1) * pageSize, pageSize, ct);
        return new PagedResult<CustomerDto>
        {
            Items = mapper.Map<List<CustomerDto>>(items),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken ct)
    {
        var email = input.Email.Trim();
        // Бізнес-правило, якому потрібна БД: email унікальний серед активних клієнтів
        if (await uow.Customers.EmailExistsAsync(email, null, ct))
            throw new BusinessConflictException($"Клієнт з email {email} уже існує");

        var id = await uow.Customers.AddAsync(new Customer
        {
            Email = email,
            FullName = input.FullName.Trim(),
            Phone = input.Phone,
            CreatedBy = Actor
        }, ct);

        logger.LogInformation("Створено клієнта {CustomerId}", id);   // без персональних даних у лозі
        return await GetAsync(id, ct);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerDto input, CancellationToken ct)
    {
        var expected = Guards.RowVersion(input.RowVersion);
        var customer = await uow.Customers.GetByIdAsync(id, ct)
                       ?? throw new NotFoundException($"Клієнта {id} не знайдено");

        var email = input.Email.Trim();
        if (await uow.Customers.EmailExistsAsync(email, id, ct))
            throw new BusinessConflictException($"Клієнт з email {email} уже існує");

        customer.Email = email;
        customer.FullName = input.FullName.Trim();
        customer.Phone = input.Phone;
        customer.UpdatedBy = Actor;

        await uow.Customers.UpdateAsync(customer, expected, ct);    // конфлікт версій → 409
        logger.LogInformation("Оновлено клієнта {CustomerId}", id);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        // Soft delete: історія бронювань клієнта зберігається
        if (!await uow.Customers.SoftDeleteAsync(id, Actor, ct))
            throw new NotFoundException($"Клієнта {id} не знайдено");
        logger.LogInformation("Клієнта {CustomerId} позначено видаленим", id);
    }
}
