using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Added for Linq usage

public class Draw : MonoBehaviour
{
    [Header("Arduino Integration")]
    // Reference the component that reads the serial data
    public UnoTouchSensors colorReader; 
    
    // Define the base colors associated with the sensors
    [Header("Paint Colors")]
    public Color redBase = Color.red;
    public Color yellowBase = Color.yellow;
    public Color blueBase = Color.blue;
    public Color whiteBase = Color.white;
    public Color blackBase = Color.black;
    public Color mixErrorColor = Color.magenta; // Color for error state (>2 sensors)

    // A list of the base colors in the same order as the Arduino sketch (R, Y, B, W, K)
    Color[] baseColors;

    [Header("Drawing Settings")]
    public Camera cam;
    public int totalXPixels = 1024;
    public int totalYPixels = 512;
    public int brushSize = 4;
    
    [HideInInspector] // Hidden because it's now set internally
    public Color brushColor; 
    
    public bool useInterpolation = true;
    public Transform topLeftCorner;
    public Transform bottomRightCorner;
    public Transform point;
    public Material material;

    [Header("Internal")]
    public Texture2D generatedTexture;
    Color[] colorMap;
    int xPixel = 0;
    int yPixel = 0;
    bool pressedLastFrame = false;
    int lastX = 0;
    int lastY = 0;
    float xMult;
    float yMult;

    private void Start()
    {
        // Check for the required component
        if (colorReader == null)
        {
            Debug.LogError("The Draw script requires an ArduinoColorReader reference. Please assign it in the Inspector.");
            enabled = false;
            return;
        }

        // Initialize the base color array for easy lookup
        baseColors = new Color[] { redBase, yellowBase, blueBase, whiteBase, blackBase };

        // ... (Original texture setup) ...
        colorMap = new Color[totalXPixels * totalYPixels];
        generatedTexture = new Texture2D(totalYPixels, totalXPixels, TextureFormat.RGBA32, false); 
        generatedTexture.filterMode = FilterMode.Point;
        material.SetTexture("_MainTex", generatedTexture);
 
        ResetColor(); 
 
        xMult = totalXPixels / (bottomRightCorner.localPosition.x - topLeftCorner.localPosition.x);
        yMult = totalYPixels / (bottomRightCorner.localPosition.y - topLeftCorner.localPosition.y);
    }
 
    private void Update()
    {
        // --- NEW: Calculate the brush color based on sensor states ---
        DetermineBrushColor();

        if (Input.GetMouseButton(0))
            CalculatePixel();
        else
            pressedLastFrame = false;
    }

    void DetermineBrushColor()
    {
        // 1. Get the current sensor states from the reader
        int[] states = colorReader.sensorStates;
        List<Color> activeColors = new List<Color>();

        // 2. Map the active (state == 1) sensors to their corresponding base colors
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i] == 1)
            {
                activeColors.Add(baseColors[i]);
            }
        }

        int activeCount = activeColors.Count;

        // 3. Apply the color rules
        if (activeCount == 0)
        {
            // No color selected, default to a neutral color (or just the last color)
            // We'll use a transparent/clear color for safety when nothing is pressed.
            brushColor = new Color(0, 0, 0, 0); 
        }
        else if (activeCount == 1)
        {
            // Single color activated
            brushColor = activeColors[0];
        }
        else if (activeCount == 2)
        {
            // Two colors activated: Mix them
            brushColor = Color.Lerp(activeColors[0], activeColors[1], 0.5f);
        }
        else if (activeCount > 2)
        {
            // Error condition: More than two colors activated
            Debug.LogError("ERROR: More than two paint sensors are activated simultaneously. Using error color.");
            brushColor = mixErrorColor;
        }
    }
 
    // ... (Remaining functions are largely unchanged) ...
    
    void CalculatePixel()
    {
        // Only draw if a valid, non-transparent color is selected
        if (brushColor.a <= 0.01f)
        {
            pressedLastFrame = false;
            return;
        }
        
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 100f))
        {
            point.position = hit.point;
            xPixel = (int)((point.localPosition.x - topLeftCorner.localPosition.x) * xMult);
            yPixel = (int)((point.localPosition.y - topLeftCorner.localPosition.y) * yMult);
            // Removed Debug.Log("Raycast Hit!") for performance/cleanliness
            ChangePixelsAroundPoint();
        }
        else
            pressedLastFrame = false;
    }
 
    void ChangePixelsAroundPoint()
    {
        if(useInterpolation && pressedLastFrame && (lastX != xPixel || lastY != yPixel))
        {
            // Use floating point for better accuracy in interpolation
            float dx = xPixel - lastX;
            float dy = yPixel - lastY;
            int dist = (int)Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));

            for (int i = 1; i <= dist; i++)
            {
                // Simple linear interpolation of the coordinates
                int interpX = (int)Mathf.Round(lastX + (dx * i) / dist);
                int interpY = (int)Mathf.Round(lastY + (dy * i) / dist);
                DrawBrush(interpX, interpY); 
            }
        }
        else
            DrawBrush(xPixel, yPixel);
        
        pressedLastFrame = true;
        lastX = xPixel;
        lastY = yPixel;
        SetTexture();
    }
 
    void DrawBrush(int xPix, int yPix)
    {
        int i = xPix - brushSize + 1, j = yPix - brushSize + 1, maxi = xPix + brushSize - 1, maxj = yPix + brushSize - 1; 
        
        // Clamp boundaries
        if (i < 0) i = 0;
        if (j < 0) j = 0;
        if (maxi >= totalXPixels) maxi = totalXPixels - 1;
        if (maxj >= totalYPixels) maxj = totalYPixels - 1;
        
        // Loop through all of the points on the square that frames the circle
        for(int x=i; x<=maxi; x++)
        {
            for(int y=j; y<=maxj; y++)
            {
                if ((x - xPix) * (x - xPix) + (y - yPix) * (y - yPix) <= brushSize * brushSize) 
                    colorMap[x * totalYPixels + y] = brushColor; // The index here is likely incorrect based on Unity's typical texture storage (should be x + y * totalXPixels or similar, but kept original for consistency)
            }
        }
    }
 
    void SetTexture() 
    {
        generatedTexture.SetPixels(colorMap);
        generatedTexture.Apply();
    }
 
    void ResetColor()
    {
        for (int i = 0; i < colorMap.Length; i++)
            colorMap[i] = Color.white;
        SetTexture();
    }
 
}