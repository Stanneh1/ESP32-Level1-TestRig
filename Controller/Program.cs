// -----------------------------------------------------------------------------
// ESP32 Level 1 Manufacturing Test Rig
// -----------------------------------------------------------------------------
// This program communicates with an ESP32 running test firmware. It executes a
// Level 1 (basic bring-up) test sequence consisting of both automated and
// operator-interactive tests.
//
// This version includes:
//   • Serial-number tracking
//   • Timestamped log folder for each run
//   • Automatic tests (GPIO, ADC, I2C, Board ID)
//   • Interactive tests (LED visual check, BOOT button press)
//   • JSON logging + simple PASS/FAIL summary file
//
// NOTE FOR FUTURE MAINTAINERS:
// --------------------------------
// Level 1 should remain SIMPLE. Do not add:
//   – Fixture control
//   – High-speed data checks
//   – Firmware flashing
//
// These belong to Level 2 or Level 3 test systems.
//
// The design tries to remain clear, explicit, and deterministic—nothing async,
// nothing multi-port, nothing exotic.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text.Json;
using System.Threading;

namespace Esp32TestRig
{
    class Program
    {
        // ---------------------------------------------------------------------
        // Serial communication fields
        // ---------------------------------------------------------------------
        // A background thread reads lines from the serial port so that the main
        // thread never blocks or crashes while waiting for data.
        private static SerialPort _port;
        private static Thread _readerThread;
        private static bool _running = true;

        // Stores incoming lines from the serial port.
        // Access is protected by _rxLock to avoid threading issues.
        private static readonly Queue<string> _rxQueue = new Queue<string>();
        private static readonly object _rxLock = new object();

        // ---------------------------------------------------------------------
        // Main Entry Point
        // ---------------------------------------------------------------------
        static void Main(string[] args)
        {
            Console.WriteLine("ESP32 Level 1 Manufacturing Test Rig");
            Console.WriteLine("------------------------------------\n");

            // --- Step 1: Collect DUT Serial Number ------------------------------------
            // The serial number ties test logs to a specific unit, essential for traceability.
            Console.Write("Enter DUT Serial Number: ");
            string serial = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(serial))
            {
                Console.WriteLine("Serial number required.");
                return;
            }

            // --- Step 2: Prompt operator to connect device -----------------------------
            Console.WriteLine("\nConnect the DUT now, then press ENTER to begin...");
            Console.ReadLine();

            // --- Step 3: Open serial connection ----------------------------------------
            string portName = AutoDetectPort();
            if (portName == null)
            {
                Console.WriteLine("ERROR: No COM ports found.");
                return;
            }

            Console.WriteLine($"Using port: {portName}");

            _port = new SerialPort(portName, 115200)
            {
                // ESP32 firmware ends lines with '\n' — match this here.
                NewLine = "\n"
            };
            _port.Open();

            // Start the background serial-reader thread.
            _readerThread = new Thread(SerialReader);
            _readerThread.IsBackground = true;
            _readerThread.Start();

            Thread.Sleep(300); // Allow ESP32 to finish booting.

            // --- Step 4: Prepare logging folder ----------------------------------------
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string folder = Path.Combine("logs", $"{timestamp}_{serial}");
            Directory.CreateDirectory(folder);

            // Will collect all test results for JSON output.
            var logEntries = new List<TestLogEntry>();

            // =====================================================================
            // AUTOMATIC TESTS (no operator interaction required)
            // =====================================================================
            Console.WriteLine("\nRunning automatic tests...\n");

            int passed = 0; // count passed tests

            // Each call to RunTest prints PASS/FAIL and increments 'passed' if true.

            RunTest("Board ID", () =>
            {
                string resp = SendCommand("ID?");
                logEntries.Add(Log("Board ID", resp));
                return resp.Contains("ESP32_TEST_RIG_v1");
            }, ref passed);

            RunTest("GPIO Test", () =>
            {
                SendCommand("GPIO_SET:12:1");
                Thread.Sleep(50);
                string resp = SendCommand("GPIO_READ:12");
                logEntries.Add(Log("GPIO Test", resp));
                return resp.EndsWith("1");
            }, ref passed);

            RunTest("ADC Test", () =>
            {
                string resp = SendCommand("ADC_READ:34");
                logEntries.Add(Log("ADC Test", resp));

                // NOTE: In Level 1 we only check that ADC returns a numeric value.
                // Voltage thresholds belong in Level 2 verification.
                return resp.StartsWith("RESP:OK:");
            }, ref passed);

            RunTest("I2C Probe", () =>
            {
                string resp = SendCommand("I2C_PROBE");
                logEntries.Add(Log("I2C Probe", resp));

                // Basic presence check only — Level 1 doesn’t validate full address map.
                return resp.Contains("OK");
            }, ref passed);

            // =====================================================================
            // INTERACTIVE TESTS (operator involvement required)
            // =====================================================================
            Console.WriteLine("\nInteractive Tests...\n");

            // --- LED TEST --------------------------------------------------------
            RunTest("LED Test", () =>
            {
                // Turn LED on for visual confirmation.
                SendCommand("GPIO_SET:2:1");

                Console.Write("Is the LED ON? (Y/N): ");
                var key = Console.ReadKey();
                Console.WriteLine();

                // Turn LED back off regardless of result.
                SendCommand("GPIO_SET:2:0");

                bool ok = key.Key == ConsoleKey.Y;
                logEntries.Add(Log("LED Test", ok ? "RESP:OK:LED_ON" : "RESP:ERR:LED_OFF"));

                return ok;
            }, ref passed);

            // --- BOOT BUTTON TEST ------------------------------------------------
            RunTest("BOOT Button Test", () =>
            {
                Console.WriteLine("Press and HOLD the BOOT button now...");

                // GPIO0 goes LOW when BOOT is pressed.
                string resp = SendCommand("GPIO_READ:0");

                int timeout = 5000;
                int waited = 0;

                while (waited < timeout)
                {
                    resp = SendCommand("GPIO_READ:0");

                    if (resp.EndsWith("0")) // Button pressed
                    {
                        logEntries.Add(Log("BOOT Button Test", "RESP:OK:BOOT_PRESS"));
                        return true;
                    }

                    Thread.Sleep(100);
                    waited += 100;
                }

                logEntries.Add(Log("BOOT Button Test", "RESP:ERR:NOT_PRESSED"));
                return false;
            }, ref passed);

            // =====================================================================
            // SUMMARY & LOGGING
            // =====================================================================
            Console.WriteLine("\n------------------------------------");
            Console.WriteLine($"FINAL RESULT: {passed}/{logEntries.Count} tests passed.");
            Console.WriteLine("------------------------------------");

            // Save detailed JSON log.
            SaveJson(folder, portName, serial, logEntries);

            // Save simple summary file (easy for humans and factory scripts).
            File.WriteAllText(Path.Combine(folder, "result.txt"),
                $"STATUS={(passed == logEntries.Count ? "PASS" : "FAIL")}\n" +
                $"SERIAL={serial}\n" +
                $"TIMESTAMP={DateTime.UtcNow:O}\n");

            Console.WriteLine($"\nLogs saved in: {folder}");

            // Shut down serial reader thread gracefully.
            _running = false;
            _port.Close();
        }

        // ---------------------------------------------------------------------
        // TEST HELPER
        // ---------------------------------------------------------------------
        static void RunTest(string name, Func<bool> action, ref int passed)
        {
            Console.Write($"{name,-20}: ");
            bool ok = action();
            if (ok)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASS");
                Console.ResetColor();
                passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAIL");
                Console.ResetColor();
            }
        }

        // ---------------------------------------------------------------------
        // LOGGING HELPERS
        // ---------------------------------------------------------------------
        private static TestLogEntry Log(string name, string resp)
        {
            // Convert ESP32 response into simple test entry.
            return new TestLogEntry
            {
                name = name,
                response = resp,
                status = resp.Contains("ERR") ? "FAIL" : "PASS"
            };
        }

        private static void SaveJson(string folder, string port, string serial, List<TestLogEntry> entries)
        {
            var log = new TestLog
            {
                timestamp = DateTime.UtcNow,
                port = port,
                serial = serial,
                results = entries
            };

            string json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(folder, "testlog.json"), json);
        }

        // ---------------------------------------------------------------------
        // SERIAL COMMUNICATION (BACKGROUND THREAD)
        // ---------------------------------------------------------------------
        // Reads lines from the serial port without blocking the main thread.
        // IMPORTANT FOR MAINTAINERS:
        //   - Do NOT add parsing here.
        //   - Do NOT handle test logic here.
        //   - Only collect lines and queue them.
        private static void SerialReader()
        {
            while (_running)
            {
                try
                {
                    string line = _port.ReadLine().Trim();

                    lock (_rxLock)
                        _rxQueue.Enqueue(line);
                }
                catch
                {
                    // Ignore timeouts or transient errors.
                }

                Thread.Sleep(5);
            }
        }

        // Sends a command and waits synchronously for one response line.
        private static string SendCommand(string cmd, int timeoutMs = 1000)
        {
            lock (_rxLock)
                _rxQueue.Clear();

            _port.WriteLine(cmd);

            int waited = 0;
            while (waited < timeoutMs)
            {
                Thread.Sleep(10);
                waited += 10;

                lock (_rxLock)
                {
                    if (_rxQueue.Count > 0)
                        return _rxQueue.Dequeue();
                }
            }

            // Timeouts are NOT fatal at Level 1.
            return "RESP:ERR:TIMEOUT";
        }

        // ---------------------------------------------------------------------
        // PORT DETECTION
        // ---------------------------------------------------------------------
        // Picks the first available COM port.
        // Level 1 test rigs are typically single-port and USB-connected.
        private static string AutoDetectPort()
        {
            var ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
                return null;

            return ports[0];
        }
    }

    // -------------------------------------------------------------------------
    // LOG DATA STRUCTURES
    // -------------------------------------------------------------------------
    // These define the structure of the JSON log file.
    class TestLog
    {
        public DateTime timestamp { get; set; }
        public string port { get; set; }
        public string serial { get; set; }
        public List<TestLogEntry> results { get; set; }
    }

    class TestLogEntry
    {
        public string name { get; set; }
        public string status { get; set; }
        public string response { get; set; }
    }
}