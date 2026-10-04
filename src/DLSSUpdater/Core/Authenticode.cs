using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DLSSUpdater.Core;

public enum SignatureStatus { NvidiaSigned, Unverified }

/// <summary>
/// Checks a DLL's embedded Authenticode signature: a chain Windows trusts (WinVerifyTrust) and NVIDIA Corporation as
/// the signer. No hash lists: anything else, including a modified NVIDIA binary, is reported as unverified.
/// </summary>
public static class Authenticode
{
    public const string NvidiaSigner = "NVIDIA Corporation";

    public static SignatureStatus Check(string path)
    {
        if (!File.Exists(path) || !IsTrusted(path)) return SignatureStatus.Unverified;
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return IsNvidiaSigner(cert.GetNameInfo(X509NameType.SimpleName, false))
                ? SignatureStatus.NvidiaSigned
                : SignatureStatus.Unverified;
        }
        catch (CryptographicException) { return SignatureStatus.Unverified; }
    }

    internal static bool IsNvidiaSigner(string? commonName) => string.Equals(commonName, NvidiaSigner, StringComparison.Ordinal);

    public static string Label(SignatureStatus? status) => status switch
    {
        SignatureStatus.NvidiaSigned => "NVIDIA-signed",
        SignatureStatus.Unverified => "unverified",
        _ => "",
    };

    // ---------- WinVerifyTrust ----------

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private const uint UiNone = 2, RevokeNone = 0, ChoiceFile = 1, StateVerify = 1, StateClose = 2;
    // Offline-safe: no network fetches for revocation lists during the check.
    private const uint CacheOnlyUrlRetrieval = 0x1000;

    /// <summary>The embedded signature is valid and chains to a root Windows trusts (any signer).</summary>
    internal static bool IsTrusted(string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var file = new FileInfoStruct { Size = (uint)Marshal.SizeOf<FileInfoStruct>(), FilePath = pathPtr };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoStruct>());
        try
        {
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(),
                UiChoice = UiNone,
                RevocationChecks = RevokeNone,
                UnionChoice = ChoiceFile,
                File = filePtr,
                StateAction = StateVerify,
                ProvFlags = CacheOnlyUrlRetrieval,
            };
            var result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
            data.StateAction = StateClose;
            WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
            return result == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(filePtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfoStruct
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr File;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref TrustData data);
}
