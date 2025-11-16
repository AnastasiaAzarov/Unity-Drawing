using UnityEngine;
using System.IO.Ports;
using System;
using System.Threading;

public class FeatherSenseReader : MonoBehaviour
{
    [Header("Serial Port Settings")]
    public string portName = "/dev/cu.usbmodem1201"; 
    public int baudRate = 9600;

    [Header("Raw Analog Ranges (based on Arduino sketch)")]
    // A0 (UpDown) now controls Y Position
    private const float A0_MIN = 560f; // Highest point (will map to Y=1.0)
    private const float A0_MAX = 930f; // Resting point (will map to Y=0.0)
    // A3 (LeftRight) now controls X Position
    private const float A3_MIN = 0f;
    private const float A3_MAX = 920f; 

    // --- Public Normalized Sensor Data ---
    [Header("Normalized Brush Position (0.0 to 1.0)")]
    [Tooltip("X-coordinate: A3 (Left/Right)")]
    [Range(0.0f, 1.0f)]
    public float normalizedX = 0.5f; 
    
    [Tooltip("Y-coordinate: A0 (Up/Down)")]
    [Range(0.0f, 1.0f)]
    public float normalizedY = 0.5f; 
    
    // Z is no longer a factor in movement or trigger.
    [Tooltip("Z/Pressure: Unused for drawing logic (0.0)")]
    [Range(0.0f, 1.0f)]
    public float normalizedZ = 0.0f; 

    [Header("Ultrasonic Distance")]
    [Tooltip("Distance from the SR04 sensor in centimeters.")]
    public int distance_cm = 999; // New variable for the fourth value

    // --- Internal Serial Communication ---
    private SerialPort serialPort;
    private bool isRunning = false;
    private Thread readThread;

    private volatile string latestDataPacket = null;
    
    private void Start()
    {
        OpenSerialPort();
        
        isRunning = true;
        readThread = new Thread(ReadSerialData);
        readThread.IsBackground = true; 
        readThread.Start();
    }
    
    private void Update()
    {
        // Check for new data packet from the background thread
        if (latestDataPacket != null)
        {
            string dataToProcess = latestDataPacket;
            latestDataPacket = null; 
            ProcessData(dataToProcess);
        }
    }

    private void OpenSerialPort()
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.ReadTimeout = 1; 
            serialPort.Open();
            Debug.Log($"Serial Port {portName} opened successfully at {baudRate} baud.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Could not open serial port {portName}: {e.Message}");
            enabled = false;
        }
    }

    private void ReadSerialData()
    {
        while (isRunning && serialPort != null && serialPort.IsOpen)
        {
            try
            {
                string data = serialPort.ReadLine();
                // Check for the "UNITY_READY" message sent at setup
                if (data.StartsWith("UNITY_READY")) 
                {
                    Debug.Log($"Arduino Ready: {data}");
                    continue;
                }
                
                latestDataPacket = data; 
            }
            catch (TimeoutException)
            {
                // Expected when no data is available
            }
            catch (Exception)
            {
                // Skip non-critical logging in the background thread
            }
            Thread.Sleep(5); 
        }
    }

    private void ProcessData(string data)
    {
        string[] values = data.Split(',');
        
        // Arduino now sends A0, A2, A3, Distance_cm (4 values)
        if (values.Length == 4)
        {
            if (int.TryParse(values[0].Trim(), out int rawA0) &&         // 0: UpDown
                int.TryParse(values[1].Trim(), out int rawA2) &&         // 1: FrontBack (Ignored for position)
                int.TryParse(values[2].Trim(), out int rawA3) &&         // 2: LeftRight
                int.TryParse(values[3].Trim(), out int rawDistance))     // 3: Distance
            {
                // 1. A3 (LeftRight) maps to normalizedX (Horizontal Position)
                normalizedX = Mathf.InverseLerp(A3_MIN, A3_MAX, rawA3);
                
                // 2. A0 (UpDown) maps to normalizedY (Vertical Position)
                normalizedY = Mathf.InverseLerp(A0_MIN, A0_MAX, rawA0);
                
                // 3. A2 (FrontBack/Z) is ignored for position/trigger, set to neutral 0.0
                normalizedZ = 0.0f; 
                
                // 4. Update Distance value
                distance_cm = rawDistance;
            }
        }
        else
        {
            // Log an error if the data packet length is unexpected
            Debug.LogWarning($"Received malformed data packet (Expected 4 values, got {values.Length}): {data}");
        }
    }

    private void OnApplicationQuit()
    {
        isRunning = false;
        
        if (readThread != null && readThread.IsAlive)
        {
            readThread.Join();
        }

        if (serialPort != null && serialPort.IsOpen)
        {
            serialPort.Close();
            serialPort.Dispose();
            Debug.Log("Serial Port closed.");
        }
    }
}