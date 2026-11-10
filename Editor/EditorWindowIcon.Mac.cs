#if UNITY_EDITOR_OSX
using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BiteMe.EditorWindowIcon
{
    /// <summary>
    /// macOS: sets the editor's Dock icon, which the app switcher (Cmd-Tab) also shows. Every editor
    /// already has a Dock icon of its own, so there is no grouping to undo as on Windows. The icon
    /// is drawn on the macOS app icon grid: a rounded square with a margin around it. Unity resets
    /// the Dock icon after it shows progress there, so the icon is put back when that happens.
    /// </summary>
    [InitializeOnLoad]
    static partial class EditorWindowIcon
    {
        const int IconSize = 512;                     // pixels: a 256 pt Dock icon on a Retina display
        const float BodySize = 824f / 1024;           // the rounded square, as a fraction of the icon edge
        const float CornerRadius = 185.4f / 824;      // as a fraction of the rounded square's edge
        const float BadgeSize = 0.5f;                 // badge edge as a fraction of the icon edge
        const string SessionKey = "BiteMe.EditorWindowIcon.Dock";

        static double nextCheck;
        static string iconKey;
        static IntPtr dockImage;   // our NSImage, retained; zero while the Dock shows Unity's own icon
        static Pixels badge;

        static EditorWindowIcon()
        {
            if (Application.isBatchMode)
                return;

            // The Dock keeps showing the image set before a domain reload: adopt it, so it's
            // neither rebuilt for nothing nor leaked.
            string saved = SessionState.GetString(SessionKey, "");
            int separator = saved.IndexOf('|');
            if (separator > 0 && long.TryParse(saved.Substring(0, separator), out long image))
            {
                dockImage = new IntPtr(image);
                iconKey = saved.Substring(separator + 1);
            }
            EditorApplication.update += Update;
        }

        static void Update()
        {
            if (EditorApplication.timeSinceStartup < nextCheck)
                return;
            nextCheck = EditorApplication.timeSinceStartup + CheckInterval;

            var texture = DefaultIcon();
            string key = texture == null ? "" : $"{AssetDatabase.GetAssetPath(texture)}|{texture.imageContentsHash}";
            if (key != iconKey)
            {
                iconKey = key;
                Rebuild(texture);
                return;
            }

            // Unity draws a progress pie on its own icon while it compiles or imports, and puts its
            // plain icon back after: then put ours back. Leaving the Dock alone while Unity is busy
            // keeps that progress visible.
            if (dockImage != IntPtr.Zero && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !InDock(dockImage))
                SetDockIcon(dockImage);
        }

        /// <summary>
        /// Whether the Dock shows <paramref name="image"/>. AppKit hands out a fresh snapshot of the
        /// Dock icon rather than the image that was set, so this goes by size: Unity's own icon and
        /// its progress image are 128 pt, ours is <see cref="IconSize"/>.
        /// </summary>
        static bool InDock(IntPtr image)
        {
            IntPtr pool = Send(Send(Class("NSAutoreleasePool"), Sel("alloc")), Sel("init"));
            try
            {
                var dock = SendSize(Send(NSApp(), Sel("applicationIconImage")), Sel("size"));
                var ours = SendSize(image, Sel("size"));
                return dock.width == ours.width && dock.height == ours.height;
            }
            finally
            {
                Send(pool, Sel("drain"));
            }
        }

        static void Rebuild(Texture2D texture)
        {
            IntPtr old = dockImage;
            dockImage = IntPtr.Zero;

            if (texture != null)
            {
                try
                {
                    badge ??= UnityLogo(Mathf.RoundToInt(IconSize * BadgeSize));
                    dockImage = ToImage(DockIcon(Pixels.FromTexture(texture)).ToPng());
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Editor Window Icon: could not build the icon. {e.Message}");
                }
            }

            // Nil hands the Dock back to Unity's own icon.
            if (dockImage != IntPtr.Zero || old != IntPtr.Zero)
                SetDockIcon(dockImage);
            if (old != IntPtr.Zero)
                Send(old, Sel("release"));
            SessionState.SetString(SessionKey, $"{dockImage.ToInt64()}|{iconKey}");
        }

        static Pixels DockIcon(Pixels source)
        {
            var icon = new Pixels(IconSize, IconSize);
            int body = Mathf.RoundToInt(IconSize * BodySize), margin = (IconSize - body) / 2;
            var face = source.Resize(body, body);
            face.RoundCorners(body * CornerRadius);
            icon.Over(face, margin, margin);
            if (badge != null)
            {
                int badgeSize = Mathf.RoundToInt(IconSize * BadgeSize);
                icon.Over(badge.Width == badgeSize && badge.Height == badgeSize ? badge : badge.Resize(badgeSize, badgeSize),
                    IconSize - badgeSize, IconSize - badgeSize);
            }
            return icon;
        }

        static void SetDockIcon(IntPtr image) => Send(NSApp(), Sel("setApplicationIconImage:"), image);

        /// <summary>
        /// The Unity logo of this editor version: the icon of its app bundle, as the Finder draws
        /// it. Already on the app icon grid, so it needs no rounding of its own.
        /// </summary>
        static Pixels UnityLogo(int size)
        {
            IntPtr pool = Send(Send(Class("NSAutoreleasePool"), Sel("alloc")), Sel("init"));
            try
            {
                IntPtr path = Send(Class("NSString"), Sel("stringWithUTF8String:"), Encoding.UTF8.GetBytes(EditorApplication.applicationPath + "\0"));
                IntPtr icon = Send(Send(Class("NSWorkspace"), Sel("sharedWorkspace")), Sel("iconForFile:"), path);
                var rect = new CGRect { width = size, height = size };
                IntPtr cgImage = Send(icon, Sel("CGImageForProposedRect:context:hints:"), ref rect, IntPtr.Zero, IntPtr.Zero);
                if (cgImage == IntPtr.Zero)
                    return null;
                IntPtr rep = Send(Send(Send(Class("NSBitmapImageRep"), Sel("alloc")), Sel("initWithCGImage:"), cgImage), Sel("autorelease"));
                IntPtr png = Send(rep, Sel("representationUsingType:properties:"), (IntPtr)NSBitmapImageFileTypePNG, Send(Class("NSDictionary"), Sel("dictionary")));
                if (png == IntPtr.Zero)
                    return null;
                var bytes = new byte[(long)Send(png, Sel("length"))];
                Marshal.Copy(Send(png, Sel("bytes")), bytes, 0, bytes.Length);
                return Pixels.FromPng(bytes);
            }
            finally
            {
                Send(pool, Sel("drain"));
            }
        }

        /// <summary>A retained NSImage of the PNG, or zero.</summary>
        static IntPtr ToImage(byte[] png)
        {
            IntPtr data = Send(Send(Class("NSData"), Sel("alloc")), Sel("initWithBytes:length:"), png, (IntPtr)png.Length);
            try
            {
                return Send(Send(Class("NSImage"), Sel("alloc")), Sel("initWithData:"), data);
            }
            finally
            {
                Send(data, Sel("release"));
            }
        }

        partial class Pixels
        {
            /// <summary>Clips to a square with rounded corners and anti-aliased edges.</summary>
            public void RoundCorners(float radius)
            {
                float halfWidth = Width / 2f, halfHeight = Height / 2f;
                for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    // Signed distance from the pixel centre to the rounded square's edge.
                    float qx = Mathf.Abs(x + 0.5f - halfWidth) - (halfWidth - radius);
                    float qy = Mathf.Abs(y + 0.5f - halfHeight) - (halfHeight - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;
                    float coverage = Mathf.Clamp01(0.5f - outside);
                    if (coverage < 1)
                        Data[y * Width + x].a = (byte)Mathf.Round(Data[y * Width + x].a * coverage);
                }
            }
        }

        // Objective-C runtime ---------------------------------------------------------------------

        const int NSBitmapImageFileTypePNG = 4;

        [StructLayout(LayoutKind.Sequential)]
        struct CGRect
        {
            public double x, y, width, height;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CGSize
        {
            public double width, height;
        }

        static IntPtr NSApp() => Send(Class("NSApplication"), Sel("sharedApplication"));

        const string ObjC = "/usr/lib/libobjc.A.dylib";
        [DllImport(ObjC, EntryPoint = "objc_getClass")] static extern IntPtr Class(string name);
        [DllImport(ObjC, EntryPoint = "sel_registerName")] static extern IntPtr Sel(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, byte[] bytes);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, byte[] bytes, IntPtr length);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, ref CGRect rect, IntPtr arg1, IntPtr arg2);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern CGSize SendSize(IntPtr receiver, IntPtr selector);
    }
}
#endif
