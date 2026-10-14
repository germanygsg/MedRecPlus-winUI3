// Shared UI formatting helpers.

using System.Globalization;
using MedRecPlus.Core.Models;
using Microsoft.Data.Sqlite;

namespace MedRecPlus.App;

public static class UiHelpers
{
    private static readonly CultureInfo Id = new("id-ID");

    public static IReadOnlyList<IcdCode> SearchIcd(string query, int limit, out string? error)
    {
        error = null;
        if (!AppSession.Icd.IsAvailable)
        {
            error = "The ICD-11 database is not available.";
            return Array.Empty<IcdCode>();
        }
        try
        {
            return AppSession.Icd.Search(query, "mms", limit).ToList();
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            error = "ICD-11 search is unavailable. Please try again or restart the app.";
            return Array.Empty<IcdCode>();
        }
    }

    public static string Money(long cents)
    {
        // IDR: no decimals, dot thousands separator.
        return "Rp " + (cents / 100m).ToString("N0", Id);
    }

    public static string Date(string iso)
    {
        if (DateTime.TryParse(iso, out var dt))
            return dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Id);
        return iso;
    }

    public static string DateShort(string iso)
    {
        if (DateTime.TryParse(iso, out var dt))
            return dt.ToLocalTime().ToString("dd/MM/yyyy", Id);
        return iso;
    }

    public static string TodayIsoStart() => DateTime.UtcNow.Date.ToString("o");
    public static string TodayIsoEnd() => DateTime.UtcNow.Date.AddDays(1).ToString("o");
    public static string MonthIsoStart()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("o");
    }
}
