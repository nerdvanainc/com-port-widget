using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ComPortWidget.Core.Storage;

/// <summary>파일 교체. 임시 파일에 쓴 뒤 대상으로 바꿔서 반쯤 쓰인 파일이 남지 않게 한다.</summary>
public static class FileUtil
{
    private const uint MoveFileReplaceExisting = 0x1;
    private const uint MoveFileWriteThrough = 0x8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string existing, string target, uint flags);

    /// <summary>
    /// temp를 dest로 바꾼다. MoveFileEx 한 번(덮어쓰기 + 쓰기 완료 대기)이므로 대상이 있든 없든
    /// 대상이 사라지는 순간이 없다. 실패하면 IOException 계열을 던진다.
    /// </summary>
    public static void ReplaceAtomic(string temp, string dest)
    {
        if (MoveFileExW(temp, dest, MoveFileReplaceExisting | MoveFileWriteThrough)) return;
        var error = Marshal.GetLastWin32Error();
        var message = new Win32Exception(error).Message;
        switch (error)
        {
            case 5: // ERROR_ACCESS_DENIED
                throw new UnauthorizedAccessException(message);
            case 2: // ERROR_FILE_NOT_FOUND
            case 3: // ERROR_PATH_NOT_FOUND
                throw new FileNotFoundException(message, temp);
            default:
                throw new IOException(message, error);
        }
    }
}
