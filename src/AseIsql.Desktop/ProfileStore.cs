using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AseIsql.Desktop;

// Each secret is kept only through its own explicit Remember checkbox, inside the DPAPI-protected file.
internal sealed record SavedLogin(string Name, ConnectionProfile Profile, string? Password = null, string? IdentityPassword = null)
{
    public bool HasPassword => !string.IsNullOrEmpty(Password);
    public bool HasIdentityPassword => !string.IsNullOrEmpty(IdentityPassword);
    public override string ToString() => Name;
}

// Saved logins live inside the app's data folder as one file encrypted for the current Windows user (DPAPI). It is
// not a document to open or share: another user or machine cannot decrypt it. Unlike user files, an overwrite keeps
// no backup, so a deleted login or a forgotten password does not survive in an old copy.
internal static class ProfileStore
{
    public const int MaxLogins = 200;
    private const int MaxBytes = 1024 * 1024;
    private static readonly byte[] Magic = "DBMONP1\n"u8.ToArray();
    private static readonly byte[] Entropy = "DBMON SQL saved logins v1"u8.ToArray();
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    // Smoke runs point this at an isolated file.
    public static string? OverridePath { get; set; }
    public static string PathName => OverridePath ?? Path.Combine(Path.GetDirectoryName(AppearanceStore.PathName)!, "profiles.dat");

    public static List<SavedLogin> Load(string path)
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > MaxBytes) throw new IOException("The saved profiles file exceeds 1 MiB.");
        byte[] sealedBytes = File.ReadAllBytes(path);
        if (sealedBytes.Length <= Magic.Length || !sealedBytes.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new IOException("The saved profiles file is not recognized.");
        byte[] plain = Unprotect(sealedBytes.AsSpan(Magic.Length).ToArray());
        try
        {
            var logins = JsonSerializer.Deserialize<List<SavedLogin>>(plain, Json) ?? throw new JsonException("Empty profiles file.");
            if (logins.Count > MaxLogins || logins.Any(l => l == null || string.IsNullOrWhiteSpace(l.Name) || l.Profile == null)) throw new JsonException("Invalid saved profiles.");
            foreach (var login in logins) login.Profile.Validate();
            return logins;
        }
        finally { Array.Clear(plain); }
    }

    public static void Save(string path, IReadOnlyList<SavedLogin> logins)
    {
        if (logins.Count > MaxLogins) throw new IOException("Up to 200 profiles can be saved.");
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(logins, Json);
        byte[] sealedBytes;
        try { sealedBytes = Protect(plain); } finally { Array.Clear(plain); }
        if (sealedBytes.Length + Magic.Length > MaxBytes) throw new IOException("The saved profiles exceed 1 MiB.");
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string staging = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(Magic); stream.Write(sealedBytes); stream.Flush(true); }
            if (File.Exists(full)) File.Replace(staging, full, null); else File.Move(staging, full);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    // Same name (case-insensitive) replaces the entry in place; otherwise the login is added at the end.
    public static List<SavedLogin> Upsert(IReadOnlyList<SavedLogin> logins, SavedLogin login)
    {
        var next = logins.ToList(); int index = next.FindIndex(l => l.Name.Equals(login.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) next[index] = login; else next.Add(login);
        return next;
    }
    public static List<SavedLogin> Remove(IReadOnlyList<SavedLogin> logins, string name) => logins.Where(l => !l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    private const int UiForbidden = 0x1;

    private static byte[] Protect(byte[] plain) => Transform(plain, true);
    private static byte[] Unprotect(byte[] sealedBytes) => Transform(sealedBytes, false);
    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = GCHandle.Alloc(data, GCHandleType.Pinned); var entropy = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            var inBlob = new DataBlob { Size = data.Length, Data = input.AddrOfPinnedObject() };
            var entropyBlob = new DataBlob { Size = Entropy.Length, Data = entropy.AddrOfPinnedObject() };
            bool ok = protect ? CryptProtectData(ref inBlob, "DBMON SQL profiles", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var outBlob)
                              : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out outBlob);
            if (!ok) throw new IOException(protect ? "Windows could not encrypt the saved profiles." : "The saved profiles belong to another Windows user or machine and cannot be read.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            try { var result = new byte[outBlob.Size]; Marshal.Copy(outBlob.Data, result, 0, outBlob.Size); return result; }
            finally { if (!protect) ZeroMemory(outBlob.Data, outBlob.Size); LocalFree(outBlob.Data); }
        }
        finally { input.Free(); entropy.Free(); }
    }
    private static void ZeroMemory(IntPtr memory, int size) { for (int i = 0; i < size; i++) Marshal.WriteByte(memory, i, 0); }
}
