using System.Globalization;
using System.Text.RegularExpressions;

namespace MedRecPlus.Core.Services;

/// <summary>Converts whole rupiah entered by a user to the database's integer cents.</summary>
public static class RupiahAmount
{
    public static bool TryParseCents(string text, out long cents)
    {
        cents = 0;
        var amount = text.Trim();
        if (amount.StartsWith("Rp", StringComparison.OrdinalIgnoreCase))
            amount = amount[2..].Trim();

        // Accept ungrouped digits or consistent three-digit thousands groups.
        // Reject decimal fractions and malformed grouping rather than changing their value.
        if (!Regex.IsMatch(amount, @"\A(?:[0-9]+|[0-9]{1,3}(?:\.[0-9]{3})+|[0-9]{1,3}(?:,[0-9]{3})+)\z"))
            return false;
        var digits = amount.Replace(".", "").Replace(",", "");
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var rupiah)
            || rupiah < 1 || rupiah > long.MaxValue / 100)
            return false;

        cents = rupiah * 100;
        return true;
    }
}
