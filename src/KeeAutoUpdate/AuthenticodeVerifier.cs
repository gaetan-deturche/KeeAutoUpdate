using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace KeeAutoUpdate
{
    /// <summary>
    /// Verifies that a downloaded file carries a valid, chain-trusted Authenticode signature
    /// before it is ever executed. This is the plugin's core defense against a compromised
    /// download mirror or a man-in-the-middle: a cryptographically valid signature check via
    /// WinVerifyTrust (the real OS trust decision), plus a check that the signer's subject
    /// matches the expected KeePass publisher.
    /// </summary>
    public static class AuthenticodeVerifier
    {
        public sealed class Result
        {
            public bool IsTrusted { get; set; }
            public string SubjectName { get; set; }
            public string FailureReason { get; set; }
        }

        public static Result Verify(string filePath, string expectedSubjectSubstring)
        {
            var result = new Result { IsTrusted = false };

            bool wvtOk = WinVerifyTrustFile(filePath, out uint wvtCode);
            if (!wvtOk)
            {
                result.FailureReason = $"WinVerifyTrust rejected the signature (0x{wvtCode:X8}).";
                return result;
            }

            string subject;
            try
            {
                using (var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath)))
                {
                    subject = cert.Subject;
                }
            }
            catch (Exception ex)
            {
                result.FailureReason = "Could not read the embedded certificate: " + ex.Message;
                return result;
            }

            result.SubjectName = subject;

            if (!string.IsNullOrEmpty(expectedSubjectSubstring) &&
                subject.IndexOf(expectedSubjectSubstring, StringComparison.OrdinalIgnoreCase) < 0)
            {
                result.FailureReason = $"Signer \"{subject}\" does not match the expected publisher (\"{expectedSubjectSubstring}\").";
                return result;
            }

            result.IsTrusted = true;
            return result;
        }

        private static bool WinVerifyTrustFile(string filePath, out uint returnCode)
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            IntPtr pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            try
            {
                Marshal.StructureToPtr(fileInfo, pFileInfo, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFileInfo,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = IntPtr.Zero,
                    dwProvFlags = WTD_SAFER_FLAG,
                    dwUIContext = 0,
                    pSignatureSettings = IntPtr.Zero
                };

                Guid action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                returnCode = WinVerifyTrust(IntPtr.Zero, action, ref data);

                // Release the state WinVerifyTrust allocated during the verify call.
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(IntPtr.Zero, action, ref data);

                return returnCode == 0; // ERROR_SUCCESS
            }
            finally
            {
                Marshal.FreeHGlobal(pFileInfo);
            }
        }

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_WHOLECHAIN = 1;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;
        private const uint WTD_SAFER_FLAG = 0x100;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);
    }
}
