using System;
using System.Collections.Generic;
using Pulsar.Common.DNS;

class Program
{
    static void Main()
    {
        Console.WriteLine("Testing HostsConverter with DEBUG settings...\n");
        
        // Test the exact same host string as in DEBUG settings
        string hosts = "127.0.0.1:4783;";
        
        Console.WriteLine($"Input: \"{hosts}\"");
        Console.WriteLine($"Length: {hosts.Length}");
        Console.WriteLine("");
        
        // Test parsing
        var converter = new HostsConverter();
        var hostsList = converter.RawHostsToList(hosts);
        
        Console.WriteLine($"Parsed hosts count: {hostsList.Count}");
        Console.WriteLine("");
        
        for (int i = 0; i < hostsList.Count; i++)
        {
            var host = hostsList[i];
            Console.WriteLine($"Host [{i}]:");
            Console.WriteLine($"  Hostname: \"{host.Hostname}\"");
            Console.WriteLine($"  Port: {host.Port}");
            Console.WriteLine($"  IpAddress: {host.IpAddress}");
            Console.WriteLine("");
        }
        
        // Test name resolution
        if (hostsList.Count > 0)
        {
            Console.WriteLine("Testing IP resolution...");
            var testHost = hostsList[0];
            
            if (string.IsNullOrEmpty(testHost.Hostname))
            {
                Console.WriteLine("ERROR: Hostname is empty!");
            }
            else
            {
                try
                {
                    if (System.Net.IPAddress.TryParse(testHost.Hostname, out var ip))
                    {
                        Console.WriteLine($"Parsed IP successfully: {ip}");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to parse \"{testHost.Hostname}\" as IP");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception: {ex.Message}");
                }
            }
        }
        
        Console.WriteLine("\n\nTest complete. Press any key...");
        Console.ReadKey();
    }
}
