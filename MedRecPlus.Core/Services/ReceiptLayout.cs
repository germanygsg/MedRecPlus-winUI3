using System.Text.Json;

namespace MedRecPlus.Core.Services;

/// <summary>Receipt appearance, saved locally and pinned when an invoice is issued.</summary>
public sealed record ReceiptLayout
{
    public double MarginTopMm { get; init; } = 3.6;
    public double MarginRightMm { get; init; } = 3.6;
    public double MarginBottomMm { get; init; } = 3.6;
    public double MarginLeftMm { get; init; } = 3.6;
    public string Font { get; init; } = "inter";
    public double ClinicHeaderSize { get; init; } = 18;
    public double ReceiptTitleSize { get; init; } = 14;
    public double InfoSize { get; init; } = 14;
    public double TreatmentSize { get; init; } = 14;
    public double TotalSize { get; init; } = 16;
    public double FooterSize { get; init; } = 14;
    public double LineSpacing { get; init; } = 1;
    public bool ShowClinicHeader { get; init; } = true;
    public bool ShowReceiptTitle { get; init; } = true;
    public bool ShowPatient { get; init; } = true;
    public bool ShowInvoiceInfo { get; init; } = true;
    public bool ShowTreatments { get; init; } = true;
    public bool ShowTreatmentNotes { get; init; } = true;
    public bool ShowTotals { get; init; } = true;
    public bool ShowTax { get; init; } = true;
    public bool ShowOperator { get; init; } = true;
    public bool ShowFooter { get; init; } = true;
    public bool HeaderDivider { get; init; }
    public bool DetailsDivider { get; init; }
    public bool TreatmentsDivider { get; init; }
    public bool TotalDivider { get; init; }
    public bool FooterDivider { get; init; }

    // Bound imported/older settings as well as UI input, so malformed numbers
    // cannot create unprintable geometry or prevent an invoice from opening.
    public ReceiptLayout Normalize() => this with
    {
        MarginTopMm = Bound(MarginTopMm, 0, 25, 3.6),
        MarginRightMm = Bound(MarginRightMm, 0, 25, 3.6),
        MarginBottomMm = Bound(MarginBottomMm, 0, 25, 3.6),
        MarginLeftMm = Bound(MarginLeftMm, 0, 25, 3.6),
        Font = Font is "inter" or "vt323" or "courier" ? Font : "inter",
        ClinicHeaderSize = Bound(ClinicHeaderSize, 8, 36, 18),
        ReceiptTitleSize = Bound(ReceiptTitleSize, 8, 36, 14),
        InfoSize = Bound(InfoSize, 8, 36, 14),
        TreatmentSize = Bound(TreatmentSize, 8, 36, 14),
        TotalSize = Bound(TotalSize, 8, 36, 16),
        FooterSize = Bound(FooterSize, 8, 36, 14),
        LineSpacing = Bound(LineSpacing, 0.8, 2, 1),
    };

    private static double Bound(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public static ReceiptLayout Load(SettingsService settings)
    {
        try { return settings.GetOrDefault(SettingKeys.ReceiptDocument, new ReceiptLayout()).Normalize(); }
        catch (JsonException) { return new ReceiptLayout(); }
    }
}
