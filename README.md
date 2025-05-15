[demo.webm](https://github.com/user-attachments/assets/eb53614c-9542-4e1c-8c6a-2d7ac7214060)

# Sports2D-Unity-Integration

Send real-time 2D pose and angle data from the [Sports2D](https://github.com/davidpagnon/sports2d) package to a Unity scene. This project provides the necessary bridge scripts and a sample Unity setup to visualize the tracking data.

## Features

- Launches the Sports2D Python process automatically when entering Play Mode in Unity.
- Terminates the Sports2D process when exiting Play Mode or when Unity Editor quits.
- Streams 2D keypoints (X, Y coordinates) and calculated joint/segment angles per frame over a local TCP socket.
- Includes a C# receiver script in Unity to parse the incoming data.
- Draws skeletons of detected persons using LineRenderers.
- Modified Sports2D package with a callback for sending data per frame.

## Prerequisites

- **Unity Hub** and **Unity Editor** (e.g., 2021.3 LTS or newer recommended).
- **Python** (>= 3.7). Anaconda or Miniconda is recommended for managing Python environments.
- **Git** and **Git LFS** (if you plan to add large assets to your Unity project, though not strictly required by this core integration).

## Setup Instructions

### 1. Clone the Repository

This project uses a Git submodule for the modified Sports2D package. Clone the repository recursively:

```bash
git clone --recursive https://github.com/arghhhhh/Sports2D-Unity-Integration.git
cd Sports2D-Unity-Integration
```

If you have already cloned the repository without `--recursive`, navigate into the project directory and run:

```bash
git submodule update --init --recursive
```

This will pull down the `sports2d_fork` submodule which contains the modified Python code.

### 2. Set Up Python Environment & Install Modified Sports2D

It is highly recommended to use a dedicated Python virtual environment (e.g., Conda).

```bash
# Create a new Conda environment (e.g., named 'sports2d-unity-env')
conda create -n sports2d-unity-env python=3.10 -y

# Activate the environment
conda activate sports2d-unity-env

# Navigate to the submodule directory
cd sports2d_fork

# Install the modified Sports2D package and its dependencies in editable mode
# This may take a few minutes as it installs PyTorch, OpenCV, etc.
pip install -e .

# Navigate back to the main project root
cd ..
```

**Note:** The original Sports2D package has several dependencies. If `pip install -e .` encounters issues, you might need to install some dependencies manually or ensure your system has the necessary build tools (especially for packages like `numpy` or `scipy` if they need to be compiled). Refer to the original [Sports2D installation instructions](https://github.com/davidpagnon/Sports2D#installation) if needed.

### 3. Configure Unity Project

1.  Open **Unity Hub**.
2.  Click "Open" or "Add project from disk" and navigate to the `Sports2D-Unity-Integration/Sports2D_Unity` folder (this is your Unity project folder within the main repository).
3.  Once the project is open in the Unity Editor, locate the `Sports2DLauncher.cs` script. You can find it in the Project window, typically under `Assets/Editor/Sports2DLauncher.cs`.
4.  Open `Sports2DLauncher.cs` in your script editor (e.g., Visual Studio, Rider).
5.  **Modify the following path variables at the top of the script:**
    - `pythonExecutablePath`: Update this to the **absolute path** of the `python.exe` (or `python` on macOS/Linux) executable _within the Conda environment you just created and activated in Step 2_.
      - Example Windows: `C:\Users\YourUser\miniconda3\envs\sports2d-unity-env\python.exe`
      - Example macOS/Linux: `/Users/YourUser/miniconda3/envs/sports2d-unity-env/bin/python`
      - You can find this path by activating your conda environment and typing `where python` (Windows CMD), `gcm python` (Windows PowerShell), or `which python` (macOS/Linux).
    - `sports2dProjectPath`: Update this to the **absolute path** of the project root (where `Sports2DUnityBridge.py` is)
      - Example Windows: `C:\Path\To\Your\Sports2D_Unity_Integration\Sports2D_Package`

### 4. Running the Integration

1.  Ensure your webcam is connected and available (if using webcam input for Sports2D, which is the default for the bridge).
2.  In the Unity Editor, open the sample scene (e.g., `Assets/Scenes/SampleScene.unity`).
3.  Make sure the GameObject in the scene that has the `Sports2DReceiver.cs` script attached (e.g., "Sports2D Manager") is active.
4.  Press the **Play** button in the Unity Editor.

**Expected Behavior:**

- The `Sports2DLauncher` script will automatically start the Python bridge (`Sports2DUnityBridge.py`) in the background.
- Your webcam should activate (you might see its indicator light turn on).
- The Unity Console should show logs:
  - From `Sports2DLauncher` indicating the Python process has started.
  - From `Sports2DReceiver` indicating a successful connection to the server.
  - Parsed tracking data (e.g., "Frame X: Person 0, Keypoint 0 Coords...").
- Tracked bodies should be drawn on the screen using LineRenderers.
- When you stop Play Mode in Unity, the Python process should automatically terminate, and your webcam should turn off.

## How It Works

1.  **`Sports2DLauncher.cs` (Unity Editor Script):**

    - Runs when the Unity Editor loads and on Play Mode state changes.
    - When entering Play Mode, it starts the `Sports2DUnityBridge.py` Python script using the configured Python executable and project path.
    - It stores the Python process ID (PID) in `SessionState` to keep track of it across potential domain reloads.
    - When exiting Play Mode (and `EditorApplication.isPlaying` is false), it retrieves the PID and terminates the Python process.
    - It also attempts to clean up any lingering processes on editor startup if not entering play mode.

2.  **`Sports2DUnityBridge.py` (Python Script in `sports2d_fork`):**

    - Uses the `Sports2D` library (our modified version).
    - Starts a TCP socket server.
    - Configures Sports2D using a deep copy of `DEFAULT_CONFIG` and applies overrides. One crucial override is setting `'show_realtime_results': False` and passing a callback function (`process_callback_for_unity`).
    - Runs `Sports2D.process()` in a separate thread.
    - The callback function is triggered by the modified `Sports2D.process_fun` after each frame is processed.
    - This callback sends the frame's pose/angle data (converted to JSON and lists) to any connected Unity client.

3.  **Modified `Sports2D/Sports2D/process.py` (in `sports2d_fork`):**

    - The core `process_fun` has been slightly modified to check for `config_dict['process_callback_for_unity']`.
    - If the callback exists, it's called after each frame's data (keypoints, angles) is computed, passing this data to the callback.
    - Current time for each frame is calculated directly within the processing loop for real-time transmission.

4.  **`Sports2DReceiver.cs` (Unity Script):**
    - Attached to a GameObject in the Unity scene.
    - In its `Start()` method, it attempts to connect to the TCP server started by `Sports2DUnityBridge.py`.
    - It runs a separate thread to continuously listen for and read data from the socket (messages are expected to be newline-terminated JSON).
    - Received JSON data is parsed into C# classes (`FrameData`, `PersonData`) using `JsonUtility`.
    - The `Update()` method can then access this `latestFrameData` to visualize or use the tracking information.

## Troubleshooting

- **"Failed to connect... target machine actively refused it"**:
  - Ensure the Python script started correctly. Check the Unity console for errors from `[Sports2DBridge Error]:`.
  - Manually run `python Sports2DUnityBridge.py` from an activated Conda environment within the `sports2d_fork` directory to see if it runs and waits for a client.
  - Check firewall settings if running manually works but Unity connection fails.
- **Camera doesn't turn off / Python process doesn't stop**:
  - Verify the logs from `Sports2DLauncher` in the Unity console when you stop Play Mode. It should indicate it's attempting to kill the process based on `EditorApplication.isPlaying` being `false`.
  - The static constructor for `Sports2DLauncher` should also clean up lingering processes on editor load/recompile, but only if not entering play mode.
- **JSON Deserialization Errors in Unity**:
  - Ensure the C# classes (`FrameData`, `PersonData`) in `Sports2DReceiver.cs` exactly match the structure of the JSON being sent from `Sports2DUnityBridge.py` (via the callback in `process.py`).
  - Unity's `JsonUtility` has limitations (e.g., does not directly deserialize dictionaries at the root). For more complex JSON, consider using a library like Newtonsoft.Json (Json.NET).
- **Path Configuration Errors in `Sports2DLauncher.cs`**: Double-check that `pythonExecutablePath` and `sports2dProjectPath` are correct absolute paths.
- **`UnboundLocalError` or other Python errors from the bridge**: Ensure the `our_config_overrides` in `Sports2DUnityBridge.py` provides all necessary keys that `Sports2D.process` might expect if they are not in `DEFAULT_CONFIG` or if `DEFAULT_CONFIG` itself is minimal.

## Contributing / Future Improvements

- **Upstream Callback to Sports2D:** The ideal solution for the modification to `Sports2D/Sports2D/process.py` is to propose a generalized callback/hook mechanism to the official `Sports2D` repository ([davidpagnon/sports2d](https://github.com/davidpagnon/sports2d)). This would eliminate the need for users to rely on this specific fork for the callback functionality.
- **Example Visualizations:** Add more advanced examples in the Unity scene for visualizing the skeleton using Line Renderers or driving a 2D/3D model.
- **Configuration from Unity:** Allow more Sports2D settings to be configured directly from the Unity Editor interface. Currently, many settings can be adjusted on the fly by modifying `config_unity_bridge.toml`, and some are set in `Sports2DUnityBridge.py`.
- **Error Handling & Resilience:** Improve error reporting from the Python bridge back to Unity.

---

_This integration setup was developed with assistance from an AI programming partner. YEAH THAT'S RIGHT THIS WAS VIBE CODED MOTHAFUCKAAAAA_
