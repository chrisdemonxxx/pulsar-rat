using System;
using System.Diagnostics;
using System.IO;

class FirefoxLaunchTest
{
    static void Main()
    {
        Console.WriteLine("=== Firefox Launch Diagnostic Test ===\n");
        
        // Test 1: Firefox executable exists
        Console.WriteLine("1. Firefox Executable Check:");
        string firefoxPath = $"{Environment.GetEnvironmentVariable("PROGRAMFILES")}\\Mozilla Firefox\\firefox.exe";
        Console.WriteLine($"   Path: {firefoxPath}");
        Console.WriteLine($"   Exists: {File.Exists(firefoxPath)}");
        
        if (File.Exists(firefoxPath))
        {
            var info = new FileInfo(firefoxPath);
            Console.WriteLine($"   Size: {info.Length / 1024} KB");
            var version = FileVersionInfo.GetVersionInfo(firefoxPath);
            Console.WriteLine($"   Version: {version.ProductVersion}");
        }
        
        // Test 2: Profile path
        Console.WriteLine("\n2. Firefox Profile Location:");
        string profileSource = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mozilla", "Firefox"
        );
        Console.WriteLine($"   Path: {profileSource}");
        Console.WriteLine($"   Exists: {Directory.Exists(profileSource)}");
        if (Directory.Exists(profileSource))
        {
            var profiles = Directory.GetDirectories(profileSource);
            Console.WriteLine($"   Profiles found: {profiles.Length}");
        }
        
        // Test 3: Try to list Firefox processes
        Console.WriteLine("\n3. Firefox Process Check:");
        try
        {
            var firefoxProcesses = Process.GetProcessesByName("firefox");
            Console.WriteLine($"   Running processes: {firefoxProcesses.Length}");
            foreach (var proc in firefoxProcesses)
            {
                Console.WriteLine($"     - PID {proc.Id}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Error: {ex.Message}");
        }
        
        Console.WriteLine("\nTest complete.");
    }
}
