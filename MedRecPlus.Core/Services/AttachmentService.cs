// Attachments: files live in the app-data attachments folder (NOT in the DB);
// the DB holds metadata + a small thumbnail. Mirrors the old app's
// "full image device-local, thumbnail in record" split.

using System.Security.Cryptography;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;

namespace MedRecPlus.Core.Services;

public sealed class AttachmentService
{
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxBytes = 20L * 1024 * 1024;

    private readonly Database _db;
    private readonly AttachmentRepository _repo;
    private readonly string _dir;
    private readonly AuditService _audit;

    public AttachmentService(Database db, string attachmentsDir)
    {
        _db = db;
        _repo = new AttachmentRepository(db);
        _dir = attachmentsDir;
        _audit = new AuditService(db);
        Directory.CreateDirectory(_dir);
    }

    public Attachment Add(string ownerType, long ownerId, string sourcePath,
        string? description, byte[]? thumbnail, string actor)
    {
        if (ownerType != "patient" && ownerType != "appointment")
            throw new ArgumentException("ownerType must be patient|appointment.", nameof(ownerType));
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException("Only JPG/PNG/WebP images are accepted.");
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new FileNotFoundException("Source file not found.", sourcePath);
        if (info.Length > MaxBytes)
            throw new InvalidOperationException("File exceeds the 20 MB limit.");
        if (info.Length == 0) throw new InvalidOperationException("File is empty.");

        var safeName = string.Concat(Path.GetFileNameWithoutExtension(info.Name)
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').Take(60)) + ext;
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "image" + ext;

        byte[] bytes;
        string sha;
        using (var sha256 = SHA256.Create())
        using (var fs = info.OpenRead())
        {
            bytes = new byte[info.Length];
            var read = 0;
            while (read < bytes.Length)
                read += fs.Read(bytes, read, bytes.Length - read);
            sha = Convert.ToHexString(sha256.ComputeHash(bytes)).ToLowerInvariant();
        }

        var meta = new Attachment
        {
            OwnerType = ownerType, OwnerId = ownerId,
            FileName = safeName,
            MimeType = ext switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" },
            SizeBytes = info.Length, Sha256 = sha,
            Thumbnail = thumbnail, Description = description,
        };
        var id = _repo.Add(meta);
        File.WriteAllBytes(Path.Combine(_dir, $"{id}_{safeName}"), bytes);
        meta.Id = id;

        _audit.Append(AuditEvents.AttachmentAdded, ownerType, ownerId.ToString(), actor,
            new Dictionary<string, object> { ["attachment_id"] = id });
        return meta;
    }

    public string GetFilePath(long attachmentId)
    {
        var meta = _repo.GetById(attachmentId)
            ?? throw new InvalidOperationException("Attachment not found.");
        var path = Path.Combine(_dir, $"{meta.Id}_{meta.FileName}");
        if (!File.Exists(path)) throw new FileNotFoundException("Attachment file is missing.", path);
        return path;
    }

    public IEnumerable<Attachment> ListByOwner(string ownerType, long ownerId) =>
        _repo.ListByOwner(ownerType, ownerId);

    public void Delete(long attachmentId, string actor)
    {
        var meta = _repo.GetById(attachmentId);
        if (meta == null) return;
        _repo.Delete(attachmentId);
        var path = Path.Combine(_dir, $"{meta.Id}_{meta.FileName}");
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        _audit.Append(AuditEvents.AttachmentDeleted, meta.OwnerType, meta.OwnerId.ToString(), actor,
            new Dictionary<string, object> { ["attachment_id"] = attachmentId });
    }
}
