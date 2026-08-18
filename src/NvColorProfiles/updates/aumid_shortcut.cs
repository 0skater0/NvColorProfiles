using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace nv_color_profiles.updates;

/// <summary>
/// Ensures a Start-Menu shortcut carrying our AUMID exists so Windows renders the app icon in
/// the toast header. Windows treats classic-desktop toasts without a matching AUMID shortcut as
/// icon-less; a portable EXE therefore needs to plant this shortcut before the first toast.
/// </summary>
internal static class aumid_shortcut
{
    private const string SHORTCUT_FILE_NAME = "NvColorProfiles.lnk";
    private const string ICON_FILE_NAME = "NvColorProfiles.ico";
    private const ushort VT_EMPTY = 0;
    private const ushort VT_LPWSTR = 31;

    // PKEY_AppUserModel_ID = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, PID 5
    private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5,
    };

    /// <summary>Creates or updates the Start-Menu shortcut so it points at the given EXE and
    /// carries the given AUMID. No-ops silently on failure; toasts then still fire without an icon.</summary>
    public static void ensure(string aumid, string display_name, string exe_path, ILogger log)
    {
        try
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (string.IsNullOrEmpty(programs) || string.IsNullOrEmpty(exe_path))
            {
                return;
            }
            Directory.CreateDirectory(programs);
            var shortcut_path = Path.Combine(programs, SHORTCUT_FILE_NAME);
            var icon_path = Path.Combine(programs, ICON_FILE_NAME);

            if (File.Exists(shortcut_path) && string.Equals(read_aumid(shortcut_path), aumid, StringComparison.Ordinal))
            {
                return;
            }

            write_icon_file(exe_path, icon_path, log);
            write_shortcut(shortcut_path, exe_path, icon_path, display_name, aumid);
            log.LogInformation("AUMID shortcut ensured at {path}", shortcut_path);
        }
        catch (Exception ex)
        {
            log.LogInformation(ex, "Ensuring AUMID shortcut failed; toast will render without app icon");
        }
    }

    private static void write_icon_file(string exe_path, string icon_path, ILogger log)
    {
        try
        {
            // Copy the whole embedded .ico stream (multi-resolution) instead of extracting a single
            // frame via Icon.ExtractAssociatedIcon — the extracted frame is often the wrong design
            // when the .ico bundles different artworks per size.
            using var stream = typeof(aumid_shortcut).Assembly.GetManifestResourceStream(ICON_RESOURCE);
            if (stream != null)
            {
                using var file = File.Create(icon_path);
                stream.CopyTo(file);
                return;
            }
            // Fallback: single-frame extraction if the resource was not embedded for some build.
            using var icon = Icon.ExtractAssociatedIcon(exe_path);
            if (icon == null)
            {
                return;
            }
            using var fallback = File.Create(icon_path);
            icon.Save(fallback);
        }
        catch (Exception ex)
        {
            log.LogInformation(ex, "Could not write shortcut icon; toast will fall back to a generic icon");
        }
    }

    private const string ICON_RESOURCE = "nvcolorprofiles.ico";

    private static string? read_aumid(string shortcut_path)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcut_path, 0);
            var store = (IPropertyStore)link;
            var key = PKEY_AppUserModel_ID;
            var pv = default(PROPVARIANT);
            try
            {
                store.GetValue(ref key, out pv);
                if (pv.vt == VT_LPWSTR && pv.data != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(pv.data);
                }
                return null;
            }
            finally
            {
                PropVariantClear(ref pv);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static void write_shortcut(string shortcut_path, string exe_path, string icon_path,
        string display_name, string aumid)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(exe_path);
            link.SetDescription(display_name);
            if (File.Exists(icon_path))
            {
                link.SetIconLocation(icon_path, 0);
            }
            else
            {
                // fall back to the EXE's own default icon (index 0)
                link.SetIconLocation(exe_path, 0);
            }

            var store = (IPropertyStore)link;
            var key = PKEY_AppUserModel_ID;
            var pv = new PROPVARIANT { vt = VT_LPWSTR, data = Marshal.StringToCoTaskMemUni(aumid) };
            try
            {
                store.SetValue(ref key, ref pv);
                store.Commit();
            }
            finally
            {
                PropVariantClear(ref pv);
            }

            ((IPersistFile)link).Save(shortcut_path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    // Sized for x64 (24 bytes: 8-byte header + two 8-byte union slots). The project pins x64
    // globally in Directory.Build.props, so no 32-bit padding needs to be considered.
    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr data;
        public IntPtr data2;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder psz_file,
            int cch_max_path, IntPtr pfd, int f_flags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder psz_name, int cch_max_name);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string psz_name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder psz_dir, int cch_max_path);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string psz_dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder psz_args, int cch_max_path);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string psz_args);
        void GetHotkey(out short pw_hotkey);
        void SetHotkey(short w_hotkey);
        void GetShowCmd(out int pi_show_cmd);
        void SetShowCmd(int i_show_cmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder psz_icon_path,
            int cch_icon_path, out int pi_icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string psz_icon_path, int i_icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string psz_path_rel, int dw_reserved);
        void Resolve(IntPtr hwnd, int f_flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string psz_file);
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid p_class_id);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string psz_file_name, int dw_mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string psz_file_name,
            [MarshalAs(UnmanagedType.Bool)] bool f_remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string psz_file_name);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppsz_file_name);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint c_props);
        void GetAt(uint i_prop, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        void Commit();
    }
}
