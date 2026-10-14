// Invoice money math — a verbatim port of computeReviewedInvoiceTotals from
// server/src/mcp/invoiceTotals.ts. ALL arithmetic is integer cents; percentages
// use integer basis points to avoid binary half-cent errors.
// Order of operations (frozen by the old app, do not "improve"):
//   1. discount applied FIRST (percent clamped 0–100, fixed capped at subtotal)
//   2. tax computed on the DISCOUNTED subtotal
//   3. total = subtotal − discount + tax
// NOTE: the source clamps the DISCOUNT percent to 100 but does NOT clamp the
// TAX percent — ported exactly, with the same asymmetry.

using MedRecPlus.Core.Models;

namespace MedRecPlus.Core.Services;

public sealed record InvoiceTotals(
    long SubtotalCents,
    long DiscountCents,
    long DiscountedSubtotalCents,
    long TaxCents,
    long TotalCents);

public static class MoneyMath
{
    private static long ToCents(decimal value) =>
        value <= 0 ? 0 : (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);

    /// <summary>percentOf(amount, percent): floor((amount * round(percent*100) + 5000) / 10000)</summary>
    private static long PercentOf(long amountCents, decimal percent)
    {
        var basisPoints = (long)Math.Round(percent * 100m, MidpointRounding.AwayFromZero);
        return (amountCents * basisPoints + 5000) / 10000;
    }

    /// <param name="discountValue">
    /// Percent as whole number (e.g. 10 = 10%), or fixed amount in MAJOR units.</param>
    /// <param name="taxValue">Percent as whole number, or fixed amount in MAJOR units.</param>
    public static InvoiceTotals Compute(
        long subtotalCents,
        string discountType, decimal discountValue,
        string taxType, decimal taxValue)
    {
        var base_ = Math.Max(0, subtotalCents);
        var value = discountValue > 0 ? discountValue : 0;

        long discount = discountType switch
        {
            DiscountTypes.Percent => PercentOf(base_, Math.Min(100m, value)),
            DiscountTypes.Fixed => Math.Min(base_, ToCents(value)),
            _ => 0,
        };

        var discounted = base_ - discount;
        var taxVal = taxValue > 0 ? taxValue : 0;

        long tax = taxType switch
        {
            // NOTE: not clamped to 100 — matches the source exactly.
            TaxTypes.Percent => PercentOf(discounted, taxVal),
            TaxTypes.Fixed => ToCents(taxVal),
            _ => 0,
        };

        return new InvoiceTotals(base_, discount, discounted, tax, discounted + tax);
    }

    /// <summary>Convenience overload taking major-unit decimals (e.g. 150000.00).</summary>
    public static InvoiceTotals Compute(
        decimal subtotal,
        string discountType, decimal discountValue,
        string taxType, decimal taxValue) =>
        Compute(ToCents(subtotal), discountType, discountValue, taxType, taxValue);

    public static string? DescribeTax(string? taxLabel, string? taxType, decimal taxValue, long taxCents)
    {
        if (taxCents <= 0) return null;
        var label = string.IsNullOrWhiteSpace(taxLabel) ? "VAT" : taxLabel.Trim();
        return taxType == TaxTypes.Percent ? $"{label} ({taxValue}%)" : label;
    }

    public static string? DescribeDiscount(string? discountType, decimal discountValue, long discountCents, string label = "Discount")
    {
        if (discountCents <= 0) return null;
        return discountType == DiscountTypes.Percent ? $"{label} ({discountValue}%)" : label;
    }
}
