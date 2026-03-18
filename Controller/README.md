# ESP32 Level 1 Manufacturing Test Rig

This project implements a **complete Level 1 hardware bring-up and manufacturing test rig** for an ESP32-based device.  
It consists of:

* **ESP32 firmware** (Arduino framework)
* **C# Console Application** (operator-facing test controller)
* **Interactive + automated test flow**
* **JSON logging + PASS/FAIL summaries**
* **Fully timestamped per‑run log folders**

Level 1 validates that the DUT (Device Under Test) is electrically alive and able to communicate.  
It is intentionally simple, **non-destructive**, and designed for early-stage production or manual bench testing.



## Features...



### Automatic Tests

* **Board ID Query**
* **GPIO output + readback**
* **ADC read test**
* **I²C bus probe**



### Interactive Tests

* **Onboard LED test (GPIO2)**  
Operator visually confirms LED illumination.
* **BOOT button test (GPIO0)**  
Operator presses BOOT, software detects the press.



### Logging \& Traceability

Each test run produces:


logs/YYYYMMDD\_HHMMSS\_SERIAL/
    testlog.json     ← detailed test results
    result.txt       ← simple PASS/FAIL summary


The log folder includes:

* Timestamp
* DUT serial number
* Full ordered test results
* All ESP32 responses



## Project Structure


/Firmware/
    esp32\_manufacturing\_test.ino     ← ESP32 test firmware

/Controller/
    Program.cs                       ← Full C# console test rig
    README.md                        ← You're reading it
/logs/                               ← Created automatically


## Running the Test Rig



### 1\. Flash the ESP32

Use Arduino IDE or PlatformIO to flash the provided firmware in `/Firmware`.

### 2\. Build \& run the C# Console App

From `/Controller`:


dotnet build
dotnet run


You will be prompted for:

1. DUT Serial Number
2. DUT connection
3. LED and BOOT button tests

All logs will be created automatically.



## Communication Protocol



The PC and ESP32 communicate over USB serial using simple text-based messages:

### Example Commands


ID?
GPIO\_SET:<pin>:<value>
GPIO\_READ:<pin>
ADC\_READ:<pin>
I2C\_PROBE


### Example Responses


RESP:OK:ESP32\_TEST\_RIG\_v1
RESP:OK:1
RESP:OK:1578
RESP:OK:3C,40,68,


## Level 1 Test Philosophy

Level 1 tests **only basic electrical responsiveness**:

* The board powers on
* The MCU responds to commands
* Basic I/O behaves correctly
* At least one button and one LED function
* I²C bus is alive

This level *does not* validate:

* Firmware features
* Sensors
* Communication stacks
* RF (WiFi/Bluetooth)
* Power‑rail thresholds
* Load testing

Those belong to Level 2 and Level 3.



## Maintainer Notes

* The serial reader thread is intentionally simple — **do not move logic into it**.
* Level 1 must remain minimal. Resist adding heavy logic.
* Serial timeouts return `RESP:ERR:TIMEOUT` but do not crash tests.
* Tests should only verify basic life, not strict tolerances.
* For fixture integration, create a Level 2 branch.



## Contributing

Pull requests are welcome for:

* Bug fixes
* Improved documentation
* Additional manufacturing utilities

Please avoid adding features that increase complexity.



## License

Copyright (c) \[2026] \[Ian Weston]



Permission is hereby granted, free of charge, to any person obtaining a copy

of this software and associated documentation files (the "Software"), to deal

in the Software without restriction, including without limitation the rights

to use, copy, modify, merge, publish, distribute, sublicense, and/or sell

copies of the Software, and to permit persons to whom the Software is

furnished to do so, subject to the following conditions:



The above copyright notice and this permission notice shall be included in all

copies or substantial portions of the Software.



THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR

IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,

FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE

AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER

LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,

OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE

SOFTWARE.

