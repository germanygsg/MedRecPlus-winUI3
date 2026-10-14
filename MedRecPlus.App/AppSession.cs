// Holds the unlocked Database and shared services for the running app.
// Set once after setup/unlock succeeds; MainWindow builds its pages from it.

using MedRecPlus.Core.Data;
using MedRecPlus.Core.Services;

namespace MedRecPlus.App;

public static class AppSession
{
    public static Database Db { get; private set; } = null!;
    public static string Actor { get; private set; } = "operator";

    public static PatientRepository Patients { get; private set; } = null!;
    public static OperatorRepository Operators { get; private set; } = null!;
    public static TreatmentRepository Treatments { get; private set; } = null!;
    public static CustomExaminationRepository CustomExams { get; private set; } = null!;
    public static VisitService Visits { get; private set; } = null!;
    public static InvoiceService Invoices { get; private set; } = null!;
    public static AppointmentRepository Appointments { get; private set; } = null!;
    public static InvoiceRepository InvoiceRecords { get; private set; } = null!;
    public static PaymentRepository Payments { get; private set; } = null!;
    public static AttachmentService Attachments { get; private set; } = null!;
    public static BackupService Backups { get; private set; } = null!;
    public static AiInsightsService Ai { get; private set; } = null!;
    public static IcdService Icd { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static NumberingService Numbering { get; private set; } = null!;
    public static AuditService Audit { get; private set; } = null!;

    public static bool IsReady { get; private set; }

    public static void Start(Database db, string actor)
    {
        Db = db;
        Actor = actor;
        Patients = new PatientRepository(db);
        Operators = new OperatorRepository(db);
        Treatments = new TreatmentRepository(db);
        CustomExams = new CustomExaminationRepository(db);
        Visits = new VisitService(db);
        Invoices = new InvoiceService(db);
        Appointments = new AppointmentRepository(db);
        InvoiceRecords = new InvoiceRepository(db);
        Payments = new PaymentRepository(db);
        Attachments = new AttachmentService(db, AppData.AttachmentsDir);
        Backups = new BackupService(db, AppData.BackupsDir);
        Ai = new AiInsightsService(db);
        Icd = new IcdService(AppData.FindIcdDb());
        Settings = new SettingsService(db);
        Numbering = new NumberingService(db);
        Audit = new AuditService(db);
        IsReady = true;
    }

    public static void Rekey(Database newDb)
    {
        Start(newDb, Actor);
    }
}
