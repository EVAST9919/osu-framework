// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using osu.Framework.Input;
using osu.Framework.Input.Handlers.Mouse;
using osu.Framework.Platform.SDL3;
using osu.Framework.Platform.Windows.Native;
using osuTK;
using SDL;
using Icon = osu.Framework.Platform.Windows.Native.Icon;
using static SDL.SDL3;

namespace osu.Framework.Platform.Windows
{
    [SupportedOSPlatform("windows")]
    internal class SDL3WindowsWindow : SDL3DesktopWindow, IWindowsWindow
    {
        private const int seticon_message = 0x0080;
        private const int icon_big = 1;
        private const int icon_small = 0;

        private const int large_icon_size = 256;
        private const int small_icon_size = 16;

        private Icon? smallIcon;
        private Icon? largeIcon;

        /// <summary>
        /// Whether to apply the <see cref="windows_borderless_width_hack"/>.
        /// </summary>
        private readonly bool applyBorderlessWindowHack;

        public SDL3WindowsWindow(GraphicsSurfaceType surfaceType, string appName)
            : base(surfaceType, appName)
        {
            switch (surfaceType)
            {
                case GraphicsSurfaceType.OpenGL:
                case GraphicsSurfaceType.Vulkan:
                    applyBorderlessWindowHack = true;
                    break;

                case GraphicsSurfaceType.Direct3D11:
                    applyBorderlessWindowHack = false;
                    break;
            }
        }

        public override void Create()
        {
            base.Create();

            /*uint backdropType = (uint)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW; // For Acrylic blur
            DwmSetWindowAttribute(WindowHandle, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(uint));*/
            /*uint t = 0x00000001;
            DwmSetWindowAttribute(WindowHandle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref t, sizeof(uint));*/
            /*uint t2 = 0xFFFFFFFE;
            DwmSetWindowAttribute(WindowHandle, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, ref t2, sizeof(uint));*/
            //SetWindowDisplayAffinity(WindowHandle, 0x00000011);

            // disable all pen and touch feedback as this causes issues when running "optimised" fullscreen under Direct3D11.
            foreach (var feedbackType in Enum.GetValues<FeedbackType>())
                Native.Input.SetWindowFeedbackSetting(WindowHandle, feedbackType, false);
        }

        protected override bool HandleEventFromFilter(SDL_Event e)
        {
            switch (e.Type)
            {
                case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                    warpCursorFromFocusLoss();
                    break;
            }

            return base.HandleEventFromFilter(e);
        }

        public Vector2? LastMousePosition { get; set; }

        /// <summary>
        /// If required, warps the OS cursor to match the framework cursor position.
        /// </summary>
        /// <remarks>
        /// The normal warp in <see cref="MouseHandler.transferLastPositionToHostCursor"/> doesn't work in fullscreen,
        /// as it is called when the window has already lost focus and is minimized.
        /// So we do an out-of-band warp, immediately after receiving the <see cref="SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST"/> message.
        /// </remarks>
        private void warpCursorFromFocusLoss()
        {
            if (LastMousePosition.HasValue
                && WindowMode.Value == Configuration.WindowMode.Fullscreen
                && RelativeMouseMode)
            {
                var pt = PointToScreen(new Point((int)LastMousePosition.Value.X, (int)LastMousePosition.Value.Y));
                SDL_WarpMouseGlobal(pt.X, pt.Y).LogErrorIfFailed(); // this directly calls the SetCursorPos win32 API
            }
        }

        public override void StartTextInput(TextInputProperties properties)
        {
            base.StartTextInput(properties);
            ScheduleCommand(() => Imm.SetImeAllowed(WindowHandle, properties.Type.SupportsIme() && properties.AllowIme));
        }

        public override void ResetIme() => ScheduleCommand(() => Imm.CancelComposition(WindowHandle));

        public override Size Size
        {
            protected set
            {
                // trick the game into thinking the borderless window has normal size so that it doesn't render into the extra space.
                if (applyBorderlessWindowHack && WindowState == WindowState.FullscreenBorderless)
                    value.Width -= windows_borderless_width_hack;

                base.Size = value;
            }
        }

        /// <summary>
        /// Amount of extra width added to window size when in borderless mode on Windows.
        /// Some drivers require this to avoid the window switching to exclusive fullscreen automatically.
        /// </summary>
        /// <remarks>Used on <see cref="GraphicsSurfaceType.OpenGL"/> and <see cref="GraphicsSurfaceType.Vulkan"/>.</remarks>
        private const int windows_borderless_width_hack = 1;

        protected override unsafe Size SetBorderless(Display display)
        {
            SDL_SetWindowBordered(SDLWindowHandle, false).LogErrorIfFailed();

            var newSize = display.Bounds.Size;

            if (applyBorderlessWindowHack)
                // use the 1px hack we've always used, but only expand the width.
                // we also trick the game into thinking the window has normal size: see Size setter override
                newSize += new Size(windows_borderless_width_hack, 0);

            SDL_SetWindowSize(SDLWindowHandle, newSize.Width, newSize.Height).LogErrorIfFailed();
            Position = display.Bounds.Location;

            return newSize;
        }

        /// <summary>
        /// On Windows, SDL will use the same image for both large and small icons (scaled as necessary).
        /// This can look bad if scaling down a large image, so we use the Windows API directly so as
        /// to get a cleaner icon set than SDL can provide.
        /// If called before the window has been created, or we do not find two separate icon sizes, we fall back to the base method.
        /// </summary>
        internal override void SetIconFromGroup(IconGroup iconGroup)
        {
            smallIcon = iconGroup.CreateIcon(small_icon_size, small_icon_size);
            largeIcon = iconGroup.CreateIcon(large_icon_size, large_icon_size);

            IntPtr windowHandle = WindowHandle;

            if (windowHandle == IntPtr.Zero || largeIcon == null || smallIcon == null)
                base.SetIconFromGroup(iconGroup);
            else
            {
                SendMessage(windowHandle, seticon_message, icon_small, smallIcon.Handle);
                SendMessage(windowHandle, seticon_message, icon_big, largeIcon.Handle);
            }
        }

        public override Point PointToClient(Point point)
        {
            ScreenToClient(WindowHandle, ref point);
            return point;
        }

        public override Point PointToScreen(Point point)
        {
            ClientToScreen(WindowHandle, ref point);
            return point;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool ScreenToClient(IntPtr hWnd, ref Point point);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        public enum DWMWINDOWATTRIBUTE : uint
        {
            DWMWA_NCRENDERING_ENABLED,
            DWMWA_NCRENDERING_POLICY,
            DWMWA_TRANSITIONS_FORCEDISABLED,
            DWMWA_ALLOW_NCPAINT,
            DWMWA_CAPTION_BUTTON_BOUNDS,
            DWMWA_NONCLIENT_RTL_LAYOUT,
            DWMWA_FORCE_ICONIC_REPRESENTATION,
            DWMWA_FLIP3D_POLICY,
            DWMWA_EXTENDED_FRAME_BOUNDS,
            DWMWA_HAS_ICONIC_BITMAP,
            DWMWA_DISALLOW_PEEK,
            DWMWA_EXCLUDED_FROM_PEEK,
            DWMWA_CLOAK,
            DWMWA_CLOAKED,
            DWMWA_FREEZE_REPRESENTATION,
            DWMWA_PASSIVE_UPDATE_MODE,
            DWMWA_USE_HOSTBACKDROPBRUSH,
            DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
            DWMWA_WINDOW_CORNER_PREFERENCE = 33,
            DWMWA_BORDER_COLOR,
            DWMWA_CAPTION_COLOR,
            DWMWA_TEXT_COLOR,
            DWMWA_VISIBLE_FRAME_BORDER_THICKNESS,
            DWMWA_SYSTEMBACKDROP_TYPE,
            DWMWA_REDIRECTIONBITMAP_ALPHA,
            DWMWA_BORDER_MARGINS,
            DWMWA_LAST
        }

        public enum DWM_SYSTEMBACKDROP_TYPE : uint
        {
            DWMSBT_AUTO,
            DWMSBT_NONE,
            DWMSBT_MAINWINDOW,
            DWMSBT_TRANSIENTWINDOW,
            DWMSBT_TABBEDWINDOW
        }

        [DllImport("Dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE dwAttribute, ref uint pvAttribute, uint cbAttribute);
    }
}
