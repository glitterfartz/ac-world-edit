using System.Runtime.InteropServices;
namespace ACWorldEdit.Assets;

/// <summary>Optional Windows x64 read-only test oracle, never a production dependency.</summary>
public sealed class StormLibOracle : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    delegate bool OpenArchive([MarshalAs(UnmanagedType.LPWStr)] string path, uint priority, uint flags, out IntPtr archive);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    delegate bool OpenFile(IntPtr archive, [MarshalAs(UnmanagedType.LPStr)] string name, uint scope, out IntPtr file);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate uint FileSize(IntPtr file, out uint high);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    delegate bool ReadFile(IntPtr file, [Out] byte[] bytes, uint length, out uint read, IntPtr overlapped);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    delegate bool Close(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint SetLocale(uint locale);
    readonly IntPtr library;
    readonly List<IntPtr> archives = [];
    readonly OpenFile openFile;
    readonly FileSize size;
    readonly ReadFile read;
    readonly Close closeFile, closeArchive;
    bool disposed;
    public StormLibOracle(string dllPath, string directory)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new NotSupportedException("Oracle expects Windows x64 DLL.");
        library = NativeLibrary.Load(Path.GetFullPath(dllPath));
        T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        try
        {
            var open = Function<OpenArchive>("SFileOpenArchive");
            openFile = Function<OpenFile>("SFileOpenFileEx"); size = Function<FileSize>("SFileGetFileSize");
            read = Function<ReadFile>("SFileReadFile"); closeFile = Function<Close>("SFileCloseFile"); closeArchive = Function<Close>("SFileCloseArchive");
            Function<SetLocale>("SFileSetLocale")(0x409);
            foreach (string path in ClientMountProfile.StockEnUs.Resolve(directory))
            {
                if (!open(path, 0, 0x100, out var archive)) throw new IOException("StormLib could not open archive read-only: " + path);
                archives.Add(archive);
            }
        }
        catch { Dispose(); throw; }
    }
    public byte[] Read(string path)
    {
        foreach (var archive in archives.AsEnumerable().Reverse())
        {
            if (!openFile(archive, path.Replace('/', '\\'), 0, out var file)) continue;
            try
            {
                uint length = size(file, out uint high);
                if (high != 0 || length > 128 * 1024 * 1024) throw new InvalidDataException("Oracle fixture exceeds read limit.");
                byte[] bytes = new byte[checked((int)length)];
                if (!read(file, bytes, length, out uint readBytes, IntPtr.Zero) || readBytes != length) throw new IOException("StormLib short/failed read: " + path);
                return bytes;
            }
            finally { closeFile(file); }
        }
        throw new FileNotFoundException("StormLib asset missing: " + path);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var archive in archives) closeArchive?.Invoke(archive);
        archives.Clear(); NativeLibrary.Free(library);
    }
}
