using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RoonRelay
{
    /// <summary>
    /// Entry point for the Roon Relay application.
    /// Handles configuration loading and service initialization.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Application entry point.
        /// Loads configuration from JSON file and starts the relay service.
        /// </summary>
        /// <param name="args">Command-line arguments. First argument can specify config file path.</param>
        public static void Main(string[] args)
        {
            // Use first argument as config path, or default to appsettings.json
            string configPath = args.Length > 0 ? args[0] : "appsettings.json";

            // Check if config file exists
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Config file not found: {configPath}");
                Console.WriteLine("Creating example appsettings.json...");
                CreateExampleConfig(configPath);
                return;
            }

            // Load and deserialize configuration
            string json = File.ReadAllText(configPath);
            RelayConfig config = JsonSerializer.Deserialize<RelayConfig>(json);

            // Create and start relay service
            RoonRelay relay = new RoonRelay(config);
            relay.Start();
        }

        /// <summary>
        /// Creates an example configuration file with default settings.
        /// Used when no config file exists.
        /// </summary>
        /// <param name="path">Path where the example config file will be created.</param>
        public static void CreateExampleConfig(string path)
        {
            // Create example configuration with typical settings
            // This shows a common setup with one LAN interface and two unicast targets
            RelayConfig example = new RelayConfig
            {
                SiteName = "SiteA",
                TunnelPort = 9004,
                RemoteRelayIp = "192.168.30.40",
                LocalInterfaces = new List<InterfaceConfig>
            {
                new InterfaceConfig
                {
                    LocalIp = "172.16.0.120",
                    BroadcastAddress = "172.16.0.255",
                    SubnetMask = "255.255.255.0"
                }
            },
                UnicastTargets = new List<string>
            {
                "10.0.0.50",
                "10.0.0.51"
            }
            };

            // Serialize with indentation for readability
            JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(example, options);
            File.WriteAllText(path, json);
            Console.WriteLine($"Example config written to {path}");
        }
    }
}