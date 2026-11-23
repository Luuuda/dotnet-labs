using System.ComponentModel.DataAnnotations;

namespace BookingService.Bll.Validation;

/// <summary>[Required] не ловить Guid.Empty — цей атрибут ловить.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class NotEmptyGuidAttribute() : ValidationAttribute("Поле {0} обов'язкове")
{
    public override bool IsValid(object? value) => value is Guid g && g != Guid.Empty;
}
