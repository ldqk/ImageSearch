using System.Runtime.InteropServices;

namespace 以图搜图;

public class RecycleBinHelper
{
    private const int FO_DELETE = 3;
    private const uint FOF_ALLOWUNDO = 0x40;
    private const uint FOF_NOCONFIRMATION = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public int wFunc;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pTo;

        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    public static void Delete(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("Path not found: " + path);

        SHFILEOPSTRUCT fileOp = new SHFILEOPSTRUCT();
        fileOp.wFunc = FO_DELETE;
        fileOp.pFrom = path + '\0' + '\0'; // 必须双null结尾
        fileOp.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION);

        int result = SHFileOperation(ref fileOp);
        if (result != 0)
            throw new IOException($"Failed to move to recycle bin. Error code: {result}");
    }
}