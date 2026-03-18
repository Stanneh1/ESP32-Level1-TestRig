#include <Arduino.h>
#include <Wire.h>

// -----------------------------------------------------------------------------
// CONFIG
// -----------------------------------------------------------------------------
#define SERIAL_BAUD 115200
#define MAX_CMD_LEN 128

// Example hardware pins
const int TEST_OUTPUT_PIN = 12;
const int TEST_INPUT_PIN  = 13;
const int ADC_PIN         = 34;

// -----------------------------------------------------------------------------
// Utility
// -----------------------------------------------------------------------------
String readSerialLine()
{
    static String buffer = "";
    while (Serial.available())
    {
        char c = Serial.read();
        if (c == '\n')
        {
            String out = buffer;
            buffer = "";
            return out;
        }
        buffer += c;
    }
    return "";
}

// Send structured response
void sendResponse(const String& status, const String& payload)
{
    Serial.print("RESP:");
    Serial.print(status);
    Serial.print(":");
    Serial.println(payload);
}

// -----------------------------------------------------------------------------
// Commands
// -----------------------------------------------------------------------------

void cmdGpioSet(int pin, int value)
{
    pinMode(pin, OUTPUT);
    digitalWrite(pin, value ? HIGH : LOW);
    sendResponse("OK", "GPIO_SET");
}

void cmdGpioRead(int pin)
{
    pinMode(pin, INPUT);
    int v = digitalRead(pin);
    sendResponse("OK", String(v));
}

void cmdAdcRead(int pin)
{
    int v = analogRead(pin);
    sendResponse("OK", String(v));
}

void cmdBoardId()
{
    sendResponse("OK", "ESP32_TEST_RIG_v1");
}

void cmdI2CProbe()
{
    Wire.begin();
    String found = "";

    for (uint8_t addr = 1; addr < 127; addr++)
    {
        Wire.beginTransmission(addr);
        if (Wire.endTransmission() == 0)
        {
            found += String(addr, HEX) + ",";
        }
    }

    if (found.length() == 0)
        found = "NONE";

    sendResponse("OK", found);
}

// -----------------------------------------------------------------------------
// Command Dispatcher
// -----------------------------------------------------------------------------

void processCommand(String cmd)
{
    cmd.trim();
    if (cmd.length() == 0)
        return;

    // Split
    int p1 = cmd.indexOf(':');
    int p2 = cmd.indexOf(':', p1 + 1);

    String op = (p1 > 0) ? cmd.substring(0, p1) : cmd;
    String arg1 = (p1 > 0) ? cmd.substring(p1 + 1, (p2 > 0 ? p2 : cmd.length())) : "";
    String arg2 = (p2 > 0) ? cmd.substring(p2 + 1) : "";

    op.toUpperCase();

    // ----------------------
    // Match Commands
    // ----------------------
    if (op == "GPIO_SET")
    {
        cmdGpioSet(arg1.toInt(), arg2.toInt());
    }
    else if (op == "GPIO_READ")
    {
        cmdGpioRead(arg1.toInt());
    }
    else if (op == "ADC_READ")
    {
        cmdAdcRead(arg1.toInt());
    }
    else if (op == "ID?")
    {
        cmdBoardId();
    }
    else if (op == "I2C_PROBE")
    {
        cmdI2CProbe();
    }
    else
    {
        sendResponse("ERR", "UNKNOWN_CMD");
    }
}

// -----------------------------------------------------------------------------
// Setup / Loop
// -----------------------------------------------------------------------------

void setup()
{
    Serial.begin(SERIAL_BAUD);
    pinMode(TEST_OUTPUT_PIN, OUTPUT);
    pinMode(TEST_INPUT_PIN, INPUT);

    sendResponse("OK", "BOOTED");
}

void loop()
{
    String line = readSerialLine();
    if (line.length() > 0)
    {
        processCommand(line);
    }
}