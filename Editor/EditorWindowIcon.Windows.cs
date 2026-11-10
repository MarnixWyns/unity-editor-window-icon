#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BiteMe.EditorWindowIcon
{
    /// <summary>
    /// Windows: sets the icon of the editor's windows (title bar, Alt-Tab) and gives the editor its
    /// own taskbar group, drawn with the same icon.
    /// </summary>
    [InitializeOnLoad]
    static partial class EditorWindowIcon
    {
        const float BadgeSize = 0.5f;       // badge edge as a fraction of the icon edge
        const string SessionKey = "BiteMe.EditorWindowIcon.Handles";

        static readonly uint processId = (uint)Process.GetCurrentProcess().Id;
        static double nextCheck;
        static string iconKey;
        static IntPtr bigIcon, smallIcon;
        static string iconFile;
        static Pixels badge;

        static EditorWindowIcon()
        {
            // The icons made before a domain reload are still on the windows: adopt them so the
            // first rebuild replaces and destroys them instead of leaking them.
            var handles = SessionState.GetString(SessionKey, "").Split(',');
            if (handles.Length == 2 && long.TryParse(handles[0], out long big) && long.TryParse(handles[1], out long small))
            {
                bigIcon = new IntPtr(big);
                smallIcon = new IntPtr(small);
            }
            EditorApplication.update += Update;
        }

        static void Update()
        {
            if (EditorApplication.timeSinceStartup < nextCheck)
                return;
            nextCheck = EditorApplication.timeSinceStartup + CheckInterval;

            var windows = UnityWindows();
            if (windows.Count == 0)
                return;

            int dpi = Dpi(windows[0]);
            var texture = DefaultIcon();
            string key = texture == null ? "" : $"{AssetDatabase.GetAssetPath(texture)}|{texture.imageContentsHash}|{dpi}";
            if (key != iconKey)
            {
                iconKey = key;
                Rebuild(texture, dpi, windows);
                return;
            }

            // Unity can reset the icon (new windows, layout changes): put ours back.
            if (bigIcon == IntPtr.Zero)
                return;
            foreach (var hwnd in windows)
                if (SendMessage(hwnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero) != bigIcon)
                    Apply(hwnd);
        }

        static void Rebuild(Texture2D texture, int dpi, List<IntPtr> windows)
        {
            IntPtr oldBig = bigIcon, oldSmall = smallIcon;
            bigIcon = smallIcon = IntPtr.Zero;
            iconFile = null;

            if (texture != null)
            {
                try
                {
                    var source = Pixels.FromTexture(texture);
                    badge ??= Pixels.FromExeIcon(EditorApplication.applicationPath);
                    bigIcon = Badged(source, SystemMetric(SM_CXICON, dpi)).ToIcon();
                    smallIcon = Badged(source, SystemMetric(SM_CXSMICON, dpi)).ToIcon();
                    iconFile = WriteIconFile(source);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"Editor Window Icon: could not build the icon. {e.Message}");
                }
            }

            // Zero icons hand the windows back to Unity's own class icon.
            foreach (var hwnd in windows)
                Apply(hwnd);
            if (oldBig != IntPtr.Zero) DestroyIcon(oldBig);
            if (oldSmall != IntPtr.Zero) DestroyIcon(oldSmall);
            SessionState.SetString(SessionKey, $"{bigIcon.ToInt64()},{smallIcon.ToInt64()}");
        }

        static Pixels Badged(Pixels source, int size)
        {
            var icon = source.Resize(size, size);
            if (badge != null)
            {
                int badgeSize = Mathf.Max(1, Mathf.RoundToInt(size * BadgeSize));
                icon.Over(badge.Resize(badgeSize, badgeSize), size - badgeSize, size - badgeSize);
            }
            return icon;
        }

        /// <summary>
        /// Writes the badged icon as a multi-size .ico in Library and returns its path. The name
        /// carries a content hash, since the taskbar caches icons by path.
        /// </summary>
        static string WriteIconFile(Pixels source)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
                images.Add(Badged(source, size).ToPng());

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write((ushort)0);             // reserved
                writer.Write((ushort)1);             // type: icon
                writer.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    writer.Write((byte)0);           // palette size
                    writer.Write((byte)0);           // reserved
                    writer.Write((ushort)1);         // planes
                    writer.Write((ushort)32);        // bits per pixel
                    writer.Write(images[i].Length);
                    writer.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var png in images)
                    writer.Write(png);
            }
            var bytes = stream.ToArray();

            string folder = Path.Combine(ProjectRoot, "Library", "EditorWindowIcon");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, Hash128.Compute(bytes) + ".ico");
            foreach (var old in Directory.GetFiles(folder, "*.ico"))
                if (old != path)
                    File.Delete(old);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        static void Apply(IntPtr hwnd)
        {
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, bigIcon);
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, smallIcon);
            SetTaskbarIdentity(hwnd);
        }

        /// <summary>
        /// Windows 11 draws a taskbar button with the icon of its app group, and all editors share
        /// Unity's group. A per-project AppUserModelID gives this editor its own group, drawn with
        /// the relaunch icon; pinning it reopens this project. Without an icon the properties are
        /// cleared, which puts the window back in Unity's group.
        /// </summary>
        static void SetTaskbarIdentity(IntPtr hwnd)
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0)
                return;
            try
            {
                bool on = iconFile != null;
                SetProperty(store, PID_RelaunchIconResource, on ? iconFile + ",0" : null);
                SetProperty(store, PID_RelaunchCommand, on ? $"\"{EditorApplication.applicationPath.Replace('/', '\\')}\" -projectPath \"{ProjectRoot}\"" : null);
                SetProperty(store, PID_RelaunchDisplayNameResource, on ? Path.GetFileName(ProjectRoot) : null);
                SetProperty(store, PID_ID, on ? "BiteMe.UnityEditor." + Hash128.Compute(ProjectRoot.ToLowerInvariant()) : null);
                store.Commit();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Editor Window Icon: could not set the taskbar icon. {e.Message}");
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }

        static void SetProperty(IPropertyStore store, uint id, string text)
        {
            var key = new PROPERTYKEY { fmtid = AppUserModelProperties, pid = id };
            var value = new PROPVARIANT();
            if (text != null)
            {
                value.vt = VT_LPWSTR;
                value.pointer = Marshal.StringToCoTaskMemUni(text);
            }
            try { store.SetValue(ref key, ref value); }
            finally
            {
                if (value.pointer != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(value.pointer);
            }
        }

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>The editor's visible top-level windows: the main window and floating ones.</summary>
        static List<IntPtr> UnityWindows()
        {
            var windows = new List<IntPtr>();
            var className = new StringBuilder(64);
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == processId && IsWindowVisible(hwnd))
                {
                    className.Clear();
                    GetClassName(hwnd, className, className.Capacity);
                    if (className.ToString() == "UnityContainerWndClass")
                        windows.Add(hwnd);
                }
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        static int Dpi(IntPtr hwnd)
        {
            try { return (int)GetDpiForWindow(hwnd); }
            catch (EntryPointNotFoundException) { return 96; }
        }

        static int SystemMetric(int index, int dpi)
        {
            try { return GetSystemMetricsForDpi(index, (uint)dpi); }
            catch (EntryPointNotFoundException) { return GetSystemMetrics(index); }
        }

        partial class Pixels
        {
            /// <summary>The largest icon in the editor executable: the Unity logo of this version.</summary>
            public static Pixels FromExeIcon(string exePath)
            {
                var icons = new IntPtr[1];
                var ids = new int[1];
                if (PrivateExtractIcons(exePath.Replace('/', '\\'), 0, 256, 256, icons, ids, 1, 0) == 0 || icons[0] == IntPtr.Zero)
                    return null;
                try
                {
                    if (!GetIconInfo(icons[0], out var info))
                        return null;
                    try
                    {
                        if (info.hbmColor == IntPtr.Zero)
                            return null;
                        GetObject(info.hbmColor, Marshal.SizeOf<BITMAP>(), out BITMAP bitmap);
                        int w = bitmap.bmWidth, h = bitmap.bmHeight;
                        var header = Header(w, h);
                        var bgra = new byte[w * h * 4];
                        IntPtr dc = GetDC(IntPtr.Zero);
                        int lines = GetDIBits(dc, info.hbmColor, 0, (uint)h, bgra, ref header, DIB_RGB_COLORS);
                        ReleaseDC(IntPtr.Zero, dc);
                        if (lines == 0)
                            return null;

                        var pixels = new Pixels(w, h);
                        bool hasAlpha = false;
                        for (int i = 0; i < pixels.Data.Length; i++)
                        {
                            pixels.Data[i] = new Color32(bgra[i * 4 + 2], bgra[i * 4 + 1], bgra[i * 4], bgra[i * 4 + 3]);
                            hasAlpha |= bgra[i * 4 + 3] != 0;
                        }
                        if (!hasAlpha)
                            for (int i = 0; i < pixels.Data.Length; i++)
                                pixels.Data[i].a = 255;
                        return pixels;
                    }
                    finally
                    {
                        DeleteObject(info.hbmColor);
                        DeleteObject(info.hbmMask);
                    }
                }
                finally
                {
                    DestroyIcon(icons[0]);
                }
            }

            public IntPtr ToIcon()
            {
                var header = Header(Width, Height);
                IntPtr dc = GetDC(IntPtr.Zero);
                IntPtr color = CreateDIBSection(dc, ref header, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
                ReleaseDC(IntPtr.Zero, dc);
                var bgra = new byte[Data.Length * 4];
                for (int i = 0; i < Data.Length; i++)
                {
                    bgra[i * 4] = Data[i].b;
                    bgra[i * 4 + 1] = Data[i].g;
                    bgra[i * 4 + 2] = Data[i].r;
                    bgra[i * 4 + 3] = Data[i].a;
                }
                Marshal.Copy(bgra, 0, bits, bgra.Length);

                // All-zero AND mask: the alpha channel decides transparency. Rows are word-aligned.
                IntPtr mask = CreateBitmap(Width, Height, 1, 1, new byte[(Width + 15) / 16 * 2 * Height]);
                var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
                IntPtr icon = CreateIconIndirect(ref info);
                DeleteObject(color);
                DeleteObject(mask);
                return icon;
            }

            static BITMAPINFOHEADER Header(int width, int height) => new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,   // top-down rows
                biPlanes = 1,
                biBitCount = 32,
            };
        }

        // Win32 -----------------------------------------------------------------------------------

        const uint WM_SETICON = 0x0080, WM_GETICON = 0x007F;
        const int ICON_SMALL = 0, ICON_BIG = 1;
        const int SM_CXICON = 11, SM_CXSMICON = 49;
        const uint DIB_RGB_COLORS = 0;

        const ushort VT_LPWSTR = 31;
        static readonly Guid AppUserModelProperties = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        const uint PID_RelaunchCommand = 2, PID_RelaunchIconResource = 3, PID_RelaunchDisplayNameResource = 4, PID_ID = 5;

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROPVARIANT
        {
            public ushort vt, reserved1, reserved2, reserved3;
            public IntPtr pointer, padding;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            uint GetCount();
            PROPERTYKEY GetAt(uint index);
            PROPVARIANT GetValue(ref PROPERTYKEY key);
            void SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
            void Commit();
        }

        [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO
        {
            [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
            public int xHotspot, yHotspot;
            public IntPtr hbmMask, hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public ushort bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint count, uint flags);
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
        [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] static extern int GetObject(IntPtr obj, int size, out BITMAP bitmap);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER header, uint usage);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    }
}
#endif
