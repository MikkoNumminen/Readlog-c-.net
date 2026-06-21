using System.ComponentModel.DataAnnotations;
using ReadLog.Web.Dtos;

namespace ReadLog.Tests.Validation;

public class DtoValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static bool HasErrorFor(object model, string member) =>
        Validate(model).Any(r => r.MemberNames.Contains(member));

    [Fact]
    public void LogBookRequest_rejects_a_future_finished_date()
    {
        var request = new LogBookRequest
        {
            OpenLibraryId = "ol:1",
            Title = "Dune",
            FinishedAt = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
        };

        Assert.True(HasErrorFor(request, nameof(LogBookRequest.FinishedAt)));
    }

    [Fact]
    public void LogBookRequest_accepts_a_finished_date_of_today()
    {
        var request = new LogBookRequest
        {
            OpenLibraryId = "ol:1",
            Title = "Dune",
            FinishedAt = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        Assert.False(HasErrorFor(request, nameof(LogBookRequest.FinishedAt)));
    }

    [Fact]
    public void LogBookRequest_rejects_a_rating_above_five()
    {
        var request = new LogBookRequest
        {
            OpenLibraryId = "ol:1",
            Title = "Dune",
            FinishedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            Rating = 6,
        };

        Assert.True(HasErrorFor(request, nameof(LogBookRequest.Rating)));
    }

    [Fact]
    public void LogBookRequest_requires_a_title()
    {
        var request = new LogBookRequest { OpenLibraryId = "ol:1", Title = "" };

        Assert.True(HasErrorFor(request, nameof(LogBookRequest.Title)));
    }
}
