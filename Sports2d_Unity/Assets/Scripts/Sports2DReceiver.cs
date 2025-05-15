using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System;

// Define data structures to match JSON
[Serializable]
public class PersonData
{
    public List<float> keypoints_x;
    public List<float> keypoints_y;
    public List<float> scores;
    public List<float> angles;
}

[Serializable]
public class FrameData
{
    public int frame;
    public float time;
    public List<PersonData> persons;
}

public enum SkeletonDetailLevel
{
    Barebones, // Body + V-Face
    Mid,       // Body + V-Face + 3 Fingers per hand
    Full       // Body + All Points + All Fingers
}

public class Sports2DReceiver : MonoBehaviour
{
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isConnected = false;
    private string host = "localhost";
    private int port = 12345;
    
    // Store the latest parsed FrameData
    private FrameData latestFrameData;
    private object dataLock = new object();

    [Header("Visualization Settings")]
    public GameObject lineRendererPrefab; // Assign a prefab with a LineRenderer component
    public GameObject keypointMarkerPrefab; // Assign a small sphere/cube prefab for keypoints
    public float keypointScale = 0.01f;  // Adjust to fit your scene
    public Vector2 videoInputSize = new Vector2(1280, 720); // Match Sports2D input resolution
    public float skeletonZ = 0f;         // Z-depth for the 2D skeleton
    public Material skeletonMaterial; // Optional: Assign a material for the lines
    public SkeletonDetailLevel skeletonDetailPreset = SkeletonDetailLevel.Full;

    private List<List<LineRenderer>> personSkeletons = new List<List<LineRenderer>>();
    private List<List<GameObject>> personKeypointVisuals = new List<List<GameObject>>(); // For keypoint markers
    private const int MAX_KEYPOINTS = 133; // Max possible keypoints (COCO-133)

    // Define bone connections using keypoint indices
    private struct Bone { public int start; public int end; }
    private Bone[] bones; 
    #region BONE PRESET DEFINITIONS
    private static readonly Bone[] BAREBONES_SKELETON_BONES = new Bone[] {
        // Body & Basic Face (14 bones)
        new Bone {start=0, end=1},   // Nose to LEye
        new Bone {start=0, end=2},   // Nose to REye
        new Bone {start=5, end=6},   // LShoulder to RShoulder
        new Bone {start=5, end=7},   // LShoulder to LElbow
        new Bone {start=7, end=9},   // LElbow to LWrist
        new Bone {start=6, end=8},   // RShoulder to RElbow
        new Bone {start=8, end=10},  // RElbow to RWrist
        new Bone {start=5, end=11},  // LShoulder to LHip
        new Bone {start=6, end=12},  // RShoulder to RHip
        new Bone {start=11, end=12}, // LHip to RHip
        new Bone {start=11, end=13}, // LHip to LKnee
        new Bone {start=13, end=15}, // LKnee to LAnkle
        new Bone {start=12, end=14}, // RHip to RKnee
        new Bone {start=14, end=16}  // RKnee to RAnkle
    };

    private static readonly Bone[] MID_SKELETON_BONES = new Bone[] {
        // Body & Basic Face (14 bones)
        new Bone {start=0, end=1},   new Bone {start=0, end=2},   new Bone {start=5, end=6},
        new Bone {start=5, end=7},   new Bone {start=7, end=9},   new Bone {start=6, end=8},
        new Bone {start=8, end=10},  new Bone {start=5, end=11},  new Bone {start=6, end=12},
        new Bone {start=11, end=12}, new Bone {start=11, end=13}, new Bone {start=13, end=15},
        new Bone {start=12, end=14}, new Bone {start=14, end=16},
        // Left Hand - Thumb, Index, Middle (12 bones)
        new Bone {start=9, end=92},   new Bone {start=92, end=93},  new Bone {start=93, end=94},  new Bone {start=94, end=95}, // LThumb
        new Bone {start=9, end=96},   new Bone {start=96, end=97},  new Bone {start=97, end=98},  new Bone {start=98, end=99}, // LIndex
        new Bone {start=9, end=100},  new Bone {start=100, end=101},new Bone {start=101, end=102},new Bone {start=102, end=103},// LMiddle
        // Right Hand - Thumb, Index, Middle (12 bones)
        new Bone {start=10, end=113}, new Bone {start=113, end=114},new Bone {start=114, end=115},new Bone {start=115, end=116},// RThumb
        new Bone {start=10, end=117}, new Bone {start=117, end=118},new Bone {start=118, end=119},new Bone {start=119, end=120},// RIndex
        new Bone {start=10, end=121}, new Bone {start=121, end=122},new Bone {start=122, end=123},new Bone {start=123, end=124} // RMiddle
    }; // Total 14 + 12 + 12 = 38 bones

    private static readonly Bone[] FULL_SKELETON_BONES = new Bone[] {
        // Detailed Face (5 bones) - Assuming 0:Nose, 1:LEye, 2:REye, 3:LEar, 4:REar
        // new Bone {start=0, end=1},   // Nose to LEye
        // new Bone {start=0, end=2},   // Nose to REye
        // new Bone {start=1, end=2},   // LEye to REye
        // new Bone {start=1, end=3},   // LEye to LEar (Requires keypoint 3 = LEar)
        // new Bone {start=2, end=4},   // REye to REar (Requires keypoint 4 = REar)
        // Body (12 bones)
        new Bone {start=5, end=6},   new Bone {start=5, end=7},   new Bone {start=7, end=9},
        new Bone {start=6, end=8},   new Bone {start=8, end=10},  new Bone {start=5, end=11},
        new Bone {start=6, end=12},  new Bone {start=11, end=12}, new Bone {start=11, end=13},
        new Bone {start=13, end=15}, new Bone {start=12, end=14}, new Bone {start=14, end=16},
        // Left Hand (20 bones)
        new Bone {start=9, end=92},   new Bone {start=92, end=93},  new Bone {start=93, end=94},  new Bone {start=94, end=95},   // LThumb
        new Bone {start=9, end=96},   new Bone {start=96, end=97},  new Bone {start=97, end=98},  new Bone {start=98, end=99},   // LIndex
        new Bone {start=9, end=100},  new Bone {start=100, end=101},new Bone {start=101, end=102},new Bone {start=102, end=103},// LMiddle
        new Bone {start=9, end=104},  new Bone {start=104, end=105},new Bone {start=105, end=106},new Bone {start=106, end=107},// LRing
        new Bone {start=9, end=108},  new Bone {start=108, end=109},new Bone {start=109, end=110},new Bone {start=110, end=111},// LPinky
        // Right Hand (20 bones)
        new Bone {start=10, end=113}, new Bone {start=113, end=114},new Bone {start=114, end=115},new Bone {start=115, end=116},// RThumb
        new Bone {start=10, end=117}, new Bone {start=117, end=118},new Bone {start=118, end=119},new Bone {start=119, end=120},// RIndex
        new Bone {start=10, end=121}, new Bone {start=121, end=122},new Bone {start=122, end=123},new Bone {start=123, end=124},// RMiddle
        new Bone {start=10, end=125}, new Bone {start=125, end=126},new Bone {start=126, end=127},new Bone {start=127, end=128},// RRing
        new Bone {start=10, end=129}, new Bone {start=129, end=130},new Bone {start=130, end=131},new Bone {start=131, end=132} // RPinky
    }; // Total 5 + 12 + 20 + 20 = 57 bones
    #endregion
    private int numBones; // Will be bones.Length

    void Start()
    {
        ConnectToServer();
    }

    void ConnectToServer()
    {
        try
        {
            client = new TcpClient(host, port); // Corrected constructor usage
            stream = client.GetStream();
            isConnected = true;
            
            receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            
            Debug.Log("Connected to Sports2D server");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to connect: {e.Message}");
        }
    }

    void ReceiveData()
    {
        byte[] buffer = new byte[8192]; // Increased buffer size for potentially larger JSONs
        StringBuilder sb = new StringBuilder();
        
        while (isConnected)
        {
            try
            {
                if (stream.DataAvailable) // Check if data is available to read
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string receivedChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        sb.Append(receivedChunk);

                        string allData = sb.ToString();
                        int messageEndIndex;
                        
                        while ((messageEndIndex = allData.IndexOf('\n')) != -1)
                        {
                            string jsonDataString = allData.Substring(0, messageEndIndex);
                            allData = allData.Substring(messageEndIndex + 1);

                            // Debug.Log($"Attempting to parse JSON: {jsonDataString}"); // Optional: log before parsing
                            try
                            {
                                FrameData parsedData = JsonUtility.FromJson<FrameData>(jsonDataString);
                                if (parsedData != null)
                                {
                                    lock (dataLock)
                                    {
                                        latestFrameData = parsedData;
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning($"JsonUtility.FromJson returned null for: {jsonDataString}");
                                }
                            }
                            catch (System.Exception deserEx)
                            {
                                Debug.LogError($"JSON Deserialization Error: {deserEx.Message} for data: {jsonDataString}");
                            }
                        }
                        sb.Clear().Append(allData); // Keep any incomplete message for next read
                    }
                    else if (bytesRead == 0) // Connection closed by server
                    {
                        Debug.LogWarning("Server closed the connection.");
                        isConnected = false;
                        break;
                    }
                }
                else
                {
                    Thread.Sleep(10); // Avoid busy-waiting if no data
                }
            }
            catch (System.IO.IOException ex) 
            {
                Debug.LogError($"IO Exception reading from stream: {ex.Message}");
                isConnected = false;
                break;
            }
            catch (SocketException ex)
            {
                Debug.LogError($"SocketException reading from stream: {ex.Message}");
                isConnected = false;
                break;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error receiving data: {e.Message}");
                isConnected = false;
                break;
            }
        }
        Debug.Log("ReceiveData thread finished.");
    }

    void Awake() // Use Awake for initialization that doesn't depend on other Start() methods
    {
        if (lineRendererPrefab == null)
        {
            Debug.LogError("LineRenderer prefab is not assigned in Sports2DReceiver!");
        }
        if (keypointMarkerPrefab == null)
        {
            Debug.LogWarning("KeypointMarker prefab is not assigned in Sports2DReceiver! Keypoints will not be drawn.");
        }

        // Initialize bones based on preset
        switch (skeletonDetailPreset)
        {
            case SkeletonDetailLevel.Barebones:
                bones = BAREBONES_SKELETON_BONES;
                break;
            case SkeletonDetailLevel.Mid:
                bones = MID_SKELETON_BONES;
                break;
            case SkeletonDetailLevel.Full:
            default: // Default to Full if something unexpected happens
                bones = FULL_SKELETON_BONES;
                break;
        }
        numBones = bones.Length; // Set numBones after bones array is assigned
    }

    void Update()
    {
        FrameData currentFrameData = null;
        lock (dataLock)
        {
            if (latestFrameData != null)
            {
                currentFrameData = latestFrameData;
                // latestFrameData = null; // Consume the data // Keep data for potential re-processing if needed by other systems, or clear if strictly single-consumer
            }
        }

        if (currentFrameData != null)
        {
            // Debug.Log($"Processing Frame: {currentFrameData.frame}, Time: {currentFrameData.time}, Persons: {currentFrameData.persons.Count}");

            // Ensure we have enough parent GameObjects for persons
            while (personSkeletons.Count < currentFrameData.persons.Count)
            {
                GameObject personGO = new GameObject($"Person_{personSkeletons.Count}_Skeleton");
                personGO.transform.SetParent(this.transform); // Optional: make it a child of this receiver
                
                List<LineRenderer> skeletonRenderers = new List<LineRenderer>();
                for (int i = 0; i < numBones; i++) // numBones is set in Awake based on preset
                {
                    if (lineRendererPrefab != null)
                    {
                        GameObject boneGO = Instantiate(lineRendererPrefab, personGO.transform);
                        boneGO.name = $"Bone_{i}";
                        LineRenderer lr = boneGO.GetComponent<LineRenderer>();
                        if (skeletonMaterial != null) lr.material = skeletonMaterial;
                        lr.positionCount = 2;
                        lr.enabled = false; // Start disabled
                        skeletonRenderers.Add(lr);
                    }
                }
                personSkeletons.Add(skeletonRenderers);

                // Also prepare keypoint visuals for this new person
                List<GameObject> keypointMarkers = new List<GameObject>();
                if (keypointMarkerPrefab != null)
                {
                    for (int i = 0; i < MAX_KEYPOINTS; i++)
                    {
                        GameObject markerGO = Instantiate(keypointMarkerPrefab, personGO.transform);
                        markerGO.name = $"Keypoint_{i}";
                        markerGO.SetActive(false); // Start disabled
                        keypointMarkers.Add(markerGO);
                    }
                }
                personKeypointVisuals.Add(keypointMarkers);
            }

            // Update existing skeletons and keypoints
            for (int pIdx = 0; pIdx < personSkeletons.Count; pIdx++)
            {
                if (pIdx < currentFrameData.persons.Count)
                {
                    // Activate this person's skeleton game object (parent of lines)
                    if(personSkeletons[pIdx].Count > 0 && personSkeletons[pIdx][0] != null)
                         personSkeletons[pIdx][0].transform.parent.gameObject.SetActive(true);

                    PersonData personData = currentFrameData.persons[pIdx];
                    if (personData.keypoints_x == null || personData.keypoints_y == null) continue;

                    // --- START: Added Detailed Logging for Key Body Points ---
                    if (personData.keypoints_x.Count > 12) // Ensure enough keypoints for torso
                    {
                        int[] KPT_INDICES_TO_LOG = {0, 5, 6, 11, 12, 16}; // Nose, LShoulder, RShoulder, LHip, RHip, RAnkle
                        string[] KPT_NAMES = {"Nose", "LShoulder", "RShoulder", "LHip", "RHip", "RAnkle"};

                        // Log only once per few frames to avoid spamming console too much, e.g. every 30 frames
                        if (currentFrameData.frame % 30 == 0) 
                        {
                            for(int i=0; i < KPT_INDICES_TO_LOG.Length; i++)
                            {
                                int kptIdx = KPT_INDICES_TO_LOG[i];
                                if (kptIdx < personData.keypoints_x.Count && kptIdx < personData.keypoints_y.Count && !float.IsNaN(personData.keypoints_x[kptIdx]) && !float.IsNaN(personData.keypoints_y[kptIdx]))
                                {
                                    float px = personData.keypoints_x[kptIdx];
                                    float py = personData.keypoints_y[kptIdx];
                                    Vector3 worldPos = new Vector3(
                                        (px / videoInputSize.x - 0.5f) * keypointScale,
                                        (0.5f - (py / videoInputSize.y)) * keypointScale,
                                        skeletonZ
                                    );
                                    Debug.Log($"Frame {currentFrameData.frame} P{pIdx}: {KPT_NAMES[i]} (Idx {kptIdx}) - Pixel: ({px:F2},{py:F2}), World: {worldPos}");
                                }
                                else 
                                {
                                    Debug.Log($"Frame {currentFrameData.frame} P{pIdx}: {KPT_NAMES[i]} (Idx {kptIdx}) - NaN or out of bounds");
                                }
                            }
                        }
                    }
                    // --- END: Added Detailed Logging ---

                    // Update Bones
                    for (int boneIdx = 0; boneIdx < numBones; boneIdx++) // numBones is correct for current preset
                    {
                        LineRenderer lr = personSkeletons[pIdx][boneIdx];
                        Bone bone = bones[boneIdx];

                        // Check if keypoint indices are valid
                        if (bone.start < personData.keypoints_x.Count && bone.start < personData.keypoints_y.Count &&
                            bone.end < personData.keypoints_x.Count && bone.end < personData.keypoints_y.Count)
                        {
                            float x1 = personData.keypoints_x[bone.start];
                            float y1 = personData.keypoints_y[bone.start];
                            float x2 = personData.keypoints_x[bone.end];
                            float y2 = personData.keypoints_y[bone.end];

                            // Check for NaN values (undetected keypoints)
                            if (float.IsNaN(x1) || float.IsNaN(y1) || float.IsNaN(x2) || float.IsNaN(y2))
                            {
                                lr.enabled = false; // Hide this bone
                            }
                            else
                            {
                                Vector3 startPos = new Vector3(
                                    (x1 / videoInputSize.x - 0.5f) * keypointScale,
                                    (0.5f - (y1 / videoInputSize.y)) * keypointScale, 
                                    skeletonZ
                                );
                                Vector3 endPos = new Vector3(
                                    (x2 / videoInputSize.x - 0.5f) * keypointScale,
                                    (0.5f - (y2 / videoInputSize.y)) * keypointScale, 
                                    skeletonZ
                                );

                                lr.SetPosition(0, startPos);
                                lr.SetPosition(1, endPos);
                                lr.enabled = true;
                            }
                        }
                        else
                        {
                            lr.enabled = false; // Keypoint index out of bounds for this person
                        }
                    }
                     // Deactivate unused bone LineRenderers for this person if skeleton got smaller
                    for (int boneIdx = numBones; boneIdx < personSkeletons[pIdx].Count; boneIdx++)
                    {
                        personSkeletons[pIdx][boneIdx].enabled = false;
                    }

                    // Update Keypoint Markers (only if Full preset is active)
                    if (keypointMarkerPrefab != null && pIdx < personKeypointVisuals.Count) // Check if visuals list exists for this person
                    {
                        List<GameObject> currentPersonMarkers = personKeypointVisuals[pIdx];
                        if (skeletonDetailPreset == SkeletonDetailLevel.Full)
                        {
                            for (int kptIdx = 0; kptIdx < MAX_KEYPOINTS; kptIdx++)
                            {
                                if (kptIdx < currentPersonMarkers.Count) // Safety check
                                {
                                    GameObject marker = currentPersonMarkers[kptIdx];
                                    if (kptIdx < personData.keypoints_x.Count && kptIdx < personData.keypoints_y.Count &&
                                        !float.IsNaN(personData.keypoints_x[kptIdx]) && !float.IsNaN(personData.keypoints_y[kptIdx]))
                                    {
                                        float px = personData.keypoints_x[kptIdx];
                                        float py = personData.keypoints_y[kptIdx];
                                        marker.transform.position = new Vector3(
                                            (px / videoInputSize.x - 0.5f) * keypointScale,
                                            (0.5f - (py / videoInputSize.y)) * keypointScale,
                                            skeletonZ - 0.001f // Slightly offset Z to avoid z-fighting with lines
                                        );
                                        marker.SetActive(true);
                                    }
                                    else
                                    {
                                        marker.SetActive(false); // Hide if keypoint is NaN or out of bounds for this frame
                                    }
                                }
                            }
                        }
                        else // Not Full preset, so hide all keypoint markers for this person
                        {
                            foreach (GameObject marker in currentPersonMarkers)
                            {
                                marker.SetActive(false);
                            }
                        }
                    }
                }
                else
                {
                    // This person is no longer tracked, disable their skeleton and keypoints
                    if(personSkeletons[pIdx].Count > 0 && personSkeletons[pIdx][0] != null)
                         personSkeletons[pIdx][0].transform.parent.gameObject.SetActive(false); // Disable parent GO

                    if (keypointMarkerPrefab != null && pIdx < personKeypointVisuals.Count)
                    {
                        foreach (GameObject marker in personKeypointVisuals[pIdx])
                        {
                            marker.SetActive(false);
                        }
                    }
                }
            }
            latestFrameData = null; // Consume the data now that it has been processed
        }
    }

    void OnDestroy()
    {
        Debug.Log("Sports2DReceiver OnDestroy called.");
        isConnected = false;
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(500); // Give thread a bit of time to exit
            if(receiveThread.IsAlive) receiveThread.Abort(); // Force abort if still alive
        }
        if (stream != null)
        {
            stream.Close();
            stream = null;
        }
        if (client != null)
        {
            client.Close();
            client = null;
        }
    }
}
