using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using SkiaSharp;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace 以图搜图.Helpers;

internal static class VirtualFileDrop
{
    public static string? SaveFirstImage(System.Windows.IDataObject dataObject)
    {
        if (dataObject.GetDataPresent("FileGroupDescriptorW", false) &&
            dataObject.GetDataPresent("FileContents", false) &&
            dataObject is ComDataObject comDataObject)
        {
            try
            {
                var image = SaveVirtualImage(comDataObject);
                if (image != null)
                {
                    return image;
                }
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidCastException or System.ComponentModel.Win32Exception)
            {
                Debug.WriteLine($"读取拖放的虚拟文件失败，将尝试其他格式：{ex}");
            }
        }

        foreach (var imageFormat in new[] {"image/png", "image/jpeg"}.Where(imageFormat => dataObject.GetDataPresent(imageFormat, false)))
        {
            try
            {
                if (dataObject.GetData(imageFormat, false) is Stream stream)
                {
                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    var image = SaveImage(stream.CopyTo);
                    if (image != null)
                    {
                        return image;
                    }
                }
                else
                {
                    Debug.WriteLine($"拖放格式 {imageFormat} 未提供图片流。");
                }
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Debug.WriteLine($"读取拖放格式 {imageFormat} 失败：{ex}");
            }
        }

        return null;
    }

    private static string SaveVirtualImage(ComDataObject comDataObject)
    {
        var format = new FORMATETC
        {
            cfFormat = unchecked((short) DataFormats.GetDataFormat("FileContents").Id),
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = 0,
            tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL
        };

        comDataObject.GetData(ref format, out var medium);
        try
        {
            return SaveImage(output =>
            {
                switch (medium.tymed)
                {
                    case TYMED.TYMED_ISTREAM:
                        CopyComStream(medium.unionmember, output);
                        break;
                    case TYMED.TYMED_HGLOBAL:
                        CopyGlobalMemory(medium.unionmember, output);
                        break;
                    default:
                        throw new NotSupportedException($"不支持的虚拟文件存储类型：{medium.tymed}");
                }
            });
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static string? SaveImage(Action<Stream> copy)
    {
        var filename = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tmp");
        var keepFile = false;
        try
        {
            using (var output = new FileStream(filename, FileMode.CreateNew, FileAccess.Write))
            {
                copy(output);
            }

            SKEncodedImageFormat imageFormat;
            using (var codec = SKCodec.Create(filename))
            {
                if (codec == null)
                {
                    Debug.WriteLine("拖放内容不是受支持的图片，尝试其他拖放格式。");
                    return null;
                }

                imageFormat = codec.EncodedFormat;
            }

            // 搜索服务按扩展名识别 GIF 动图。
            if (imageFormat == SKEncodedImageFormat.Gif)
            {
                var gifFilename = Path.ChangeExtension(filename, ".gif");
                File.Move(filename, gifFilename);
                filename = gifFilename;
            }

            keepFile = true;
            return filename;
        }
        finally
        {
            if (!keepFile && File.Exists(filename))
            {
                File.Delete(filename);
            }
        }
    }

    private static void CopyComStream(IntPtr pointer, Stream output)
    {
        if (pointer == IntPtr.Zero)
        {
            throw new InvalidDataException("虚拟文件流为空。");
        }

        var stream = (IStream) Marshal.GetObjectForIUnknown(pointer);
        var bytesReadPointer = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            var buffer = new byte[81920];
            int bytesRead;
            do
            {
                Marshal.WriteInt32(bytesReadPointer, 0);
                stream.Read(buffer, buffer.Length, bytesReadPointer);
                bytesRead = Marshal.ReadInt32(bytesReadPointer);
                if (bytesRead < 0 || bytesRead > buffer.Length)
                {
                    throw new InvalidDataException("虚拟文件流返回了无效的读取长度。");
                }

                output.Write(buffer, 0, bytesRead);
            } while (bytesRead > 0);
        }
        finally
        {
            Marshal.FreeCoTaskMem(bytesReadPointer);
            if (Marshal.IsComObject(stream))
            {
                Marshal.ReleaseComObject(stream);
            }
        }
    }

    private static void CopyGlobalMemory(IntPtr handle, Stream output)
    {
        var length = GlobalSize(handle).ToUInt64();
        if (length == 0)
        {
            throw new InvalidDataException("虚拟文件内存为空。");
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var buffer = new byte[81920];
            ulong offset = 0;
            while (offset < length)
            {
                var count = (int) Math.Min((ulong) buffer.Length, length - offset);
                Marshal.Copy(new IntPtr(checked(pointer.ToInt64() + (long) offset)), buffer, 0, count);
                output.Write(buffer, 0, count);
                offset += (uint) count;
            }
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr memory);
}