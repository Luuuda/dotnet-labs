namespace BookingService.Domain.Models;

/// <summary>Глядач (таблиця dbo.Customers). PK — GUID, бо на клієнта посилаються інші сервіси.</summary>
public sealed class Customer
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>rowversion — оптимістична конкурентність під час редагування профілю.</summary>
    public byte[] RowVersion { get; set; } = [];
}
