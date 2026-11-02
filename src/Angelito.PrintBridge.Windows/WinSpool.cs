// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Angelito.PrintBridge.Windows;

/// <summary>Minimal winspool.drv interop (documented Win32 Print Spooler API).</summary>
internal static class WinSpool
{
    private const int PRINTER_ENUM_LOCAL = 0x2;
    private const int PRINTER_ENUM_CONNECTIONS = 0x4;
    private const int JOB_CONTROL_DELETE = 5;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    public const uint JOB_STATUS_ERROR = 0x2, JOB_STATUS_DELETING = 0x4, JOB_STATUS_PRINTING = 0x10, JOB_STATUS_OFFLINE = 0x20,
        JOB_STATUS_PAPEROUT = 0x40, JOB_STATUS_PRINTED = 0x80, JOB_STATUS_DELETED = 0x100, JOB_STATUS_BLOCKED_DEVQ = 0x200,
        JOB_STATUS_USER_INTERVENTION = 0x400;

    internal sealed class PrinterHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => ClosePrinter(handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOC_INFO_1
    {
        public string pDocName;
        public string? pOutputFile;
        public string pDatatype;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PRINTER_INFO_2
    {
        public string? pServerName, pPrinterName, pShareName, pPortName, pDriverName, pComment, pLocation;
        public IntPtr pDevMode;
        public string? pSepFile, pPrintProcessor, pDatatype, pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, cJobs, AveragePPM;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct JOB_INFO_1
    {
        public uint JobId;
        public string? pPrinterName, pMachineName, pUserName, pDocument, pDatatype, pStatus;
        public uint Status, Priority, Position, TotalPages, PagesPrinted;
        public SYSTEMTIME Submitted;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEMTIME { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string name, out PrinterHandle handle, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDocPrinter(PrinterHandle h, int level, ref DOC_INFO_1 info);

    [DllImport("winspool.drv", SetLastError = true)] private static extern bool StartPagePrinter(PrinterHandle h);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndPagePrinter(PrinterHandle h);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndDocPrinter(PrinterHandle h);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool AbortPrinter(PrinterHandle h);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern unsafe bool WritePrinter(PrinterHandle h, byte* buf, int count, out int written);

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(int flags, string? name, int level, IntPtr buf, int cb, out int needed, out int returned);

    [DllImport("winspool.drv", EntryPoint = "EnumJobsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumJobs(PrinterHandle h, int first, int count, int level, IntPtr buf, int cb, out int needed, out int returned);

    [DllImport("winspool.drv", EntryPoint = "SetJobW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetJob(PrinterHandle h, uint jobId, int level, IntPtr job, int command);

    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? buf, ref int size);

    [DllImport("winspool.drv", EntryPoint = "SetDefaultPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDefaultPrinterNative(string name);

    /// <summary>Sets the current user's default printer (per-user setting, no admin rights needed).</summary>
    public static void SetDefaultPrinter(string name)
    {
        if (!SetDefaultPrinterNative(name)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static PrinterHandle? TryOpen(string printerName) =>
        OpenPrinter(printerName, out var h, IntPtr.Zero) ? h : null;

    public static string? DefaultPrinter()
    {
        var size = 0;
        GetDefaultPrinter(null, ref size);
        if (size == 0) return null;
        var sb = new StringBuilder(size);
        return GetDefaultPrinter(sb, ref size) ? sb.ToString() : null;
    }

    public static List<PRINTER_INFO_2> ListPrinters() =>
        Enumerate<PRINTER_INFO_2>((IntPtr b, int cb, out int n, out int r) =>
            EnumPrinters(PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS, null, 2, b, cb, out n, out r));

    public static List<JOB_INFO_1> ListJobs(PrinterHandle h) =>
        Enumerate<JOB_INFO_1>((IntPtr b, int cb, out int n, out int r) => EnumJobs(h, 0, 1000, 1, b, cb, out n, out r));

    public static bool DeleteJob(PrinterHandle h, uint jobId) => SetJob(h, jobId, 0, IntPtr.Zero, JOB_CONTROL_DELETE);

    private delegate bool EnumCall(IntPtr buf, int cb, out int needed, out int returned);

    private static List<T> Enumerate<T>(EnumCall call) where T : struct
    {
        call(IntPtr.Zero, 0, out var needed, out _);
        var err = Marshal.GetLastWin32Error();
        if (needed == 0) return [];
        if (err != ERROR_INSUFFICIENT_BUFFER && err != 0) throw new Win32Exception(err);
        var buf = Marshal.AllocHGlobal(needed);
        try
        {
            if (!call(buf, needed, out _, out var count)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var size = Marshal.SizeOf<T>();
            var list = new List<T>(count);
            for (var i = 0; i < count; i++) list.Add(Marshal.PtrToStructure<T>(buf + i * size));
            return list;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>Sends bytes as a RAW job. <paramref name="outputFile"/> redirects spooler output to a file (testing/virtual).</summary>
    public static unsafe void WriteRaw(string printerName, string docName, ReadOnlySpan<byte> data, int copies, string? outputFile,
        Action onSpoolStarting, CancellationToken ct)
    {
        using var h = TryOpen(printerName) ?? throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new DOC_INFO_1 { pDocName = docName, pOutputFile = outputFile, pDatatype = "RAW" };
        onSpoolStarting();
        if (StartDocPrinter(h, 1, ref info) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var ok = false;
        try
        {
            if (!StartPagePrinter(h)) throw new Win32Exception(Marshal.GetLastWin32Error());
            for (var copy = 0; copy < copies; copy++)
            {
                for (var offset = 0; offset < data.Length;)
                {
                    ct.ThrowIfCancellationRequested();
                    var chunk = Math.Min(64 * 1024, data.Length - offset);
                    fixed (byte* p = data.Slice(offset, chunk))
                        if (!WritePrinter(h, p, chunk, out var written) || written <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                        else offset += written;
                }
            }
            EndPagePrinter(h);
            ok = EndDocPrinter(h);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            // Abort deletes the partial job so nothing half-written reaches the device.
            if (!ok) AbortPrinter(h);
        }
    }
}
