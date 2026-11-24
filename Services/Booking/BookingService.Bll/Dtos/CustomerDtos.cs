using System.ComponentModel.DataAnnotations;

namespace BookingService.Bll.Dtos;

/// <summary>Клієнт у відповідях API.</summary>
public sealed record CustomerDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = "";
    public string FullName { get; init; } = "";
    public string? Phone { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    /// <summary>Версія рядка (base64) — передається назад у PUT для оптимістичної конкурентності.</summary>
    public string RowVersion { get; init; } = "";
}

/// <summary>Створення клієнта. Модельна валідація — атрибутами (відхиляється ще до виклику сервісу).</summary>
public sealed record CreateCustomerDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = "";

    [Required, StringLength(200, MinimumLength = 2)]
    public string FullName { get; init; } = "";

    [RegularExpression(@"^\+[0-9]{7,15}$", ErrorMessage = "Телефон у форматі +380XXXXXXXXX")]
    public string? Phone { get; init; }
}

/// <summary>Оновлення клієнта з перевіркою версії рядка.</summary>
public sealed record UpdateCustomerDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = "";

    [Required, StringLength(200, MinimumLength = 2)]
    public string FullName { get; init; } = "";

    [RegularExpression(@"^\+[0-9]{7,15}$", ErrorMessage = "Телефон у форматі +380XXXXXXXXX")]
    public string? Phone { get; init; }

    /// <summary>RowVersion, отриманий у GET (base64).</summary>
    [Required]
    public string RowVersion { get; init; } = "";
}
