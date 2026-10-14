// SQLite DDL for MedRecPlus Offline. Mirrors PRD §3.1.
// Conventions: INTEGER PK AUTOINCREMENT, ISO-8601 TEXT dates, soft delete via
// deleted_at, money as INTEGER cents, JSON payloads as TEXT.

namespace MedRecPlus.Core.Data;

public static class Schema
{
    /// <summary>Individual statements; executed one-by-one inside a transaction.</summary>
    public static readonly string[] Statements =
    {
        """
        CREATE TABLE IF NOT EXISTS patients (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          record_number TEXT NOT NULL UNIQUE,
          name TEXT NOT NULL,
          age INTEGER NOT NULL,
          gender TEXT,
          phone_number TEXT,
          address TEXT,
          initial_diagnosis TEXT,
          icd_codes TEXT,
          consent_basis TEXT,
          consent_date TEXT,
          consent_recorded_by TEXT,
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL,
          deleted_at TEXT
        )
        """,
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_patients_record_number ON patients(record_number)",
        "CREATE INDEX IF NOT EXISTS idx_patients_name ON patients(name)",
        """
        CREATE TABLE IF NOT EXISTS operators (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          name TEXT NOT NULL,
          role TEXT,
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL,
          deleted_at TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS treatments (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          name TEXT NOT NULL,
          description TEXT,
          price_cents INTEGER NOT NULL,
          notes TEXT,
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL,
          deleted_at TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS custom_examinations (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          name TEXT NOT NULL,
          unit TEXT NOT NULL,
          created_at TEXT NOT NULL DEFAULT '',
          updated_at TEXT NOT NULL DEFAULT '',
          deleted_at TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS appointments (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
          patient_name TEXT NOT NULL,
          operator_id INTEGER NOT NULL REFERENCES operators(id),
          operator_name TEXT NOT NULL,
          date TEXT NOT NULL,
          vital_signs TEXT NOT NULL DEFAULT '{}',
          treatments TEXT NOT NULL DEFAULT '[]',
          total_price_cents INTEGER NOT NULL DEFAULT 0,
          discount_type TEXT NOT NULL DEFAULT 'none',
          discount_value_cents INTEGER NOT NULL DEFAULT 0,
          notes TEXT,
          description TEXT,
          diagnosis_codes TEXT,
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL,
          deleted_at TEXT
        )
        """,
        "CREATE INDEX IF NOT EXISTS idx_appointments_patient ON appointments(patient_id, date)",
        "CREATE INDEX IF NOT EXISTS idx_appointments_date ON appointments(date)",
        """
        CREATE TABLE IF NOT EXISTS invoices (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          invoice_number TEXT NOT NULL UNIQUE,
          appointment_id INTEGER NOT NULL UNIQUE REFERENCES appointments(id) ON DELETE CASCADE,
          patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
          patient_name TEXT NOT NULL,
          operator_id INTEGER NOT NULL REFERENCES operators(id),
          operator_name TEXT NOT NULL,
          date TEXT NOT NULL,
          appointment_date TEXT NOT NULL,
          vital_signs TEXT NOT NULL DEFAULT '{}',
          treatments TEXT NOT NULL DEFAULT '[]',
          subtotal_cents INTEGER NOT NULL DEFAULT 0,
          discount_type TEXT NOT NULL DEFAULT 'none',
          discount_value_cents INTEGER NOT NULL DEFAULT 0,
          discount_cents INTEGER NOT NULL DEFAULT 0,
          tax_label TEXT,
          tax_type TEXT,
          tax_value_cents INTEGER,
          tax_cents INTEGER,
          total_cents INTEGER NOT NULL DEFAULT 0,
          paid_cents INTEGER NOT NULL DEFAULT 0,
          status TEXT NOT NULL DEFAULT 'unpaid',
          payment_method TEXT,
          payment_note TEXT,
          document_profile TEXT,
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL,
          deleted_at TEXT
        )
        """,
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_invoices_number ON invoices(invoice_number)",
        "CREATE INDEX IF NOT EXISTS idx_invoices_date ON invoices(date)",
        "CREATE INDEX IF NOT EXISTS idx_invoices_patient ON invoices(patient_id)",
        """
        CREATE TABLE IF NOT EXISTS invoice_payments (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          invoice_id INTEGER NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
          amount_cents INTEGER NOT NULL,
          method TEXT NOT NULL,
          note TEXT,
          paid_at TEXT NOT NULL,
          created_at TEXT NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS idx_payments_invoice ON invoice_payments(invoice_id)",
        """
        CREATE TABLE IF NOT EXISTS attachments (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          owner_type TEXT NOT NULL,
          owner_id INTEGER NOT NULL,
          file_name TEXT NOT NULL,
          mime_type TEXT NOT NULL,
          size_bytes INTEGER NOT NULL,
          sha256 TEXT NOT NULL,
          thumbnail BLOB,
          description TEXT,
          created_at TEXT NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS idx_attachments_owner ON attachments(owner_type, owner_id)",
        """
        CREATE TABLE IF NOT EXISTS audit_events (
          id TEXT PRIMARY KEY,
          at TEXT NOT NULL,
          event TEXT NOT NULL,
          entity_type TEXT,
          entity_id TEXT,
          actor TEXT NOT NULL,
          meta TEXT NOT NULL DEFAULT '{}',
          chain_hash TEXT NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS idx_audit_at ON audit_events(at)",
        "CREATE INDEX IF NOT EXISTS idx_audit_event ON audit_events(event)",
        """
        CREATE TABLE IF NOT EXISTS settings (
          key TEXT PRIMARY KEY,
          value TEXT NOT NULL,
          updated_at TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS sequences (
          name TEXT NOT NULL,
          period TEXT NOT NULL,
          next_value INTEGER NOT NULL,
          PRIMARY KEY (name, period)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS ai_logs (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          at TEXT NOT NULL,
          question TEXT NOT NULL,
          intent TEXT NOT NULL,
          tools_used TEXT NOT NULL DEFAULT '[]',
          row_counts TEXT NOT NULL DEFAULT '{}',
          duration_ms INTEGER NOT NULL DEFAULT 0
        )
        """,
    };

    /// <summary>
    /// Idempotent column additions for databases created by older schema versions.
    /// Executed with "duplicate column" errors ignored.
    /// </summary>
    public static readonly string[] Migrations =
    {
        "ALTER TABLE custom_examinations ADD COLUMN created_at TEXT NOT NULL DEFAULT ''",
        "ALTER TABLE custom_examinations ADD COLUMN updated_at TEXT NOT NULL DEFAULT ''",
        "ALTER TABLE custom_examinations ADD COLUMN deleted_at TEXT",
    };
}
