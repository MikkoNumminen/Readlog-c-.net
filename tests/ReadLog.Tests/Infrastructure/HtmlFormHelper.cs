using System.Text.RegularExpressions;

namespace ReadLog.Tests.Infrastructure;

/// <summary>Helpers for driving antiforgery-protected Razor Pages forms from tests.</summary>
public static partial class HtmlFormHelper
{
    /// <summary>Pulls the hidden antiforgery field value out of a rendered page.</summary>
    public static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryField().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException("No antiforgery token found in the page.");
        }

        return match.Groups[1].Value;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}
