using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Permissions;
using System.Security.Principal;

namespace MSSQLand.Services.Credentials
{
    [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
    internal class WindowsIdentityImpersonation : IDisposable
    {
        private readonly SafeTokenHandle _handle;
        private readonly WindowsImpersonationContext _context;

        const int Logon32LogonNewCredentials = 9;

        internal WindowsIdentityImpersonation(string domain, string username, string password)
        {
            bool ok = LogonUser(username, domain, password, Logon32LogonNewCredentials, 0, out this._handle);
            if (!ok)
            {
                int errorCode = Marshal.GetLastWin32Error();
                throw new ApplicationException($"Could not impersonate the user. LogonUser returned error code {errorCode}.");
            }

            this._context = WindowsIdentity.Impersonate(this._handle.DangerousGetHandle());
        }

        public void Dispose()
        {
            this._context.Dispose();
            this._handle.Dispose();
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(
            string lpszUsername,
            string lpszDomain,
            string lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out SafeTokenHandle phToken);

        private sealed class SafeTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private SafeTokenHandle()
                : base(true) { }

            [DllImport("kernel32.dll")]
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            [SuppressUnmanagedCodeSecurity]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CloseHandle(IntPtr handle);

            protected override bool ReleaseHandle()
            {
                return CloseHandle(handle);
            }
        }
    }
}
