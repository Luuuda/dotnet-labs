using BookingService.Bll.Dtos;
using BookingService.Bll.Services;
using BookingService.Domain.Exceptions;
using BookingService.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BookingService.Tests.Unit;

public sealed class CustomerServiceTests
{
    private readonly UnitOfWorkMock _m = new();
    private readonly CustomerService _service;

    public CustomerServiceTests() =>
        _service = new CustomerService(_m.Uow.Object, MappingConfigurationTests.CreateMapper(), NullLogger<CustomerService>.Instance);

    [Fact]
    public async Task Get_missing_customer_throws_NotFound()
    {
        _m.Customers.Setup(c => c.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Customer?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Create_with_existing_email_throws_conflict()
    {
        _m.Customers.Setup(c => c.EmailExistsAsync("olena@example.com", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<BusinessConflictException>(() => _service.CreateAsync(
            new CreateCustomerDto { Email = "olena@example.com", FullName = "Олена" }, CancellationToken.None));

        _m.Customers.Verify(c => c.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_with_malformed_rowversion_throws_validation()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _service.UpdateAsync(Guid.NewGuid(),
            new UpdateCustomerDto { Email = "a@b.ua", FullName = "Ім'я", RowVersion = "not-base64!" }, CancellationToken.None));
    }

    [Fact]
    public async Task List_with_too_big_page_throws_validation()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _service.ListAsync(null, 1, 1000, CancellationToken.None));
    }
}
