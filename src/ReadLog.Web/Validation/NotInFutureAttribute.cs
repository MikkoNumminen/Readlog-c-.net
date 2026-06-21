using System.ComponentModel.DataAnnotations;

namespace ReadLog.Web.Validation;

/// <summary>
/// Validation that a <see cref="DateOnly"/> (or <see cref="DateTime"/>) is not after
/// today (UTC) — a book can't be finished in the future. Null passes (let
/// <c>[Required]</c> own emptiness).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotInFutureAttribute : ValidationAttribute
{
    public NotInFutureAttribute()
        : base("{0} cannot be in the future.")
    {
    }

    public override bool IsValid(object? value)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return value switch
        {
            DateOnly date => date <= today,
            DateTime dateTime => DateOnly.FromDateTime(dateTime) <= today,
            _ => true,
        };
    }
}
