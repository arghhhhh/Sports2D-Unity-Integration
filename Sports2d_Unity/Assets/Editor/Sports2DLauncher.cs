using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO; // Required for Path operations
using System.ComponentModel;

[InitializeOnLoad] // Ensures this script runs when the editor loads
public class Sports2DLauncher
{
    // We no longer rely on this static variable to persist *between* play/stop 
    // private static Process sports2dProcess;
    private const string ProcessIdKey = "Sports2D_PID"; // Key for SessionState

    // --- Configuration ---
    // Set the correct path to your Python executable (e.g., from your conda env)
    private static string pythonExecutablePath = @"C:\Users\YourUsername\miniconda3\envs\Sports2D\python.exe"; 
    // Set the correct path to your Sports2D project root (where Sports2DUnityBridge.py is)
    private static string sports2dProjectPath = @"C:\Path\To\Your\Sports2D_Unity_Integration\Sports2D_Package"; 
    // --- End Configuration ---

    private static string bridgeScriptName = "Sports2DUnityBridge.py";

    // Static constructor called when the editor loads
    static Sports2DLauncher()
    {
        // Only perform cleanup if not currently in or transitioning to Play Mode
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            UnityEngine.Debug.LogWarning("Sports2DLauncher: Static constructor called while isPlayingOrWillChangePlaymode is true. Skipping cleanup to avoid killing active/starting process.");
        }
        else
        {
            UnityEngine.Debug.Log("Sports2DLauncher: Static constructor cleanup. Checking for lingering PID.");
            KillProcessById(SessionState.GetInt(ProcessIdKey, -1));
            SessionState.EraseInt(ProcessIdKey); 
        }
            
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        UnityEngine.Debug.Log($"Sports2DLauncher: OnPlayModeStateChanged - Current state: {state}, EditorApplication.isPlaying: {EditorApplication.isPlaying}, EditorApplication.isPaused: {EditorApplication.isPaused}, EditorApplication.isCompiling: {EditorApplication.isCompiling}");

        string bridgeScriptFullPath = Path.Combine(sports2dProjectPath, bridgeScriptName);

        // Check if the configuration paths are valid
        if (!File.Exists(pythonExecutablePath))
        {
            UnityEngine.Debug.LogError($"Sports2DLauncher Error: Python executable not found at '{pythonExecutablePath}'. Please configure the path in Sports2DLauncher.cs.");
            // Prevent entering play mode if Python path is wrong
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode) 
            {
                EditorApplication.isPlaying = false;
                UnityEngine.Debug.LogWarning("Exiting play mode due to invalid Python path.");
            }
            return; // Stop further execution if path is invalid
        }

        if (!File.Exists(bridgeScriptFullPath))
        {
            UnityEngine.Debug.LogError($"Sports2DLauncher Error: Bridge script not found at '{bridgeScriptFullPath}'. Please configure the path in Sports2DLauncher.cs.");
                // Prevent entering play mode if script path is wrong
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.isPlaying = false;
                UnityEngine.Debug.LogWarning("Exiting play mode due to invalid bridge script path.");
            }
            return; // Stop further execution if path is invalid
        }


        if (state == PlayModeStateChange.ExitingEditMode) // About to enter Play Mode
        {
            UnityEngine.Debug.Log("Sports2DLauncher: Starting Python bridge...");
            Process localProcess = null; // Use a local variable
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = pythonExecutablePath;
                // Pass the script path as an argument
                startInfo.Arguments = $"\"{bridgeScriptFullPath}\""; 
                startInfo.WorkingDirectory = sports2dProjectPath; // Set working directory
                startInfo.UseShellExecute = false; // Don't use OS shell
                startInfo.CreateNoWindow = true; // Don't create a visible window
                startInfo.RedirectStandardOutput = true; // Capture output
                startInfo.RedirectStandardError = true; // Capture errors

                localProcess = Process.Start(startInfo);

                // Optional: Asynchronously read output/error streams to prevent blocking
                localProcess.OutputDataReceived += (sender, args) => UnityEngine.Debug.Log($"[Sports2DBridge Output]: {args.Data}");
                localProcess.ErrorDataReceived += (sender, args) => UnityEngine.Debug.LogError($"[Sports2DBridge Error]: {args.Data}");
                localProcess.BeginOutputReadLine();
                localProcess.BeginErrorReadLine();

                if (localProcess != null && !localProcess.HasExited) // Check if started successfully
                {
                    // Store the PID in Session State
                    SessionState.SetInt(ProcessIdKey, localProcess.Id);
                    UnityEngine.Debug.Log($"Sports2DLauncher: Python bridge started (PID: {localProcess.Id}). Stored PID in SessionState.");
                }
                else {
                    UnityEngine.Debug.LogError("Sports2DLauncher Error: Failed to start or process exited immediately.");
                    if(localProcess != null) localProcess.Dispose();
                    EditorApplication.isPlaying = false;
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Sports2DLauncher Error: Failed to start Python bridge: {e.Message}");
                if(localProcess != null) localProcess.Dispose();
                EditorApplication.isPlaying = false; // Prevent entering play mode if launch fails
            }
        }
        else if (state == PlayModeStateChange.EnteredEditMode) 
        {
            int pid = SessionState.GetInt(ProcessIdKey, -1);
            UnityEngine.Debug.LogWarning($"Sports2DLauncher: EnteredEditMode detected. PID from SessionState: {pid}. EditorApplication.isPlaying: {EditorApplication.isPlaying}");

            if (!EditorApplication.isPlaying) // Only if truly not playing anymore
            {
                UnityEngine.Debug.Log("Sports2DLauncher: Play mode truly exited (isPlaying is false), killing PID: " + pid);
                KillProcessById(pid); // pid will be -1 if nothing was stored or already cleared
                if (pid != -1) 
                {
                    SessionState.EraseInt(ProcessIdKey); // Crucial: erase after killing
                    UnityEngine.Debug.Log($"Sports2DLauncher: Erased PID {pid} from SessionState.");
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning("Sports2DLauncher: Received EnteredEditMode while EditorApplication.isPlaying is still true. Assuming domain reload or similar, NOT killing process.");
                // If isPlaying is true, we assume we are still in some form of play or transitioning, so don't kill.
                // The PID remains in SessionState. If the user *then* stops, the next EnteredEditMode will have isPlaying=false.
            }
        }
    }

    // Helper function to kill a process by ID
    private static void KillProcessById(int pid)
    {
         if (pid == -1)
        {
            UnityEngine.Debug.Log("Sports2DLauncher: No valid PID found in SessionState to stop.");
            return;
        }

        try
        {
            Process processToKill = Process.GetProcessById(pid);
            if (processToKill != null && !processToKill.HasExited) // Check if it exists and is running
            {
                UnityEngine.Debug.Log($"Sports2DLauncher: Attempting to stop Python bridge process with PID: {pid}...");
                try
                {
                    processToKill.Kill();
                    UnityEngine.Debug.Log("Sports2DLauncher: Kill() command sent. Waiting for exit...");

                    if (processToKill.WaitForExit(5000)) 
                    {
                        UnityEngine.Debug.Log($"Sports2DLauncher: Python bridge process (PID: {pid}) exited successfully.");
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"Sports2DLauncher: Python bridge process (PID: {pid}) did not exit after 5 seconds.");
                    }
                }
                catch (System.InvalidOperationException ex)
                {
                     // This can happen if the process terminated between GetProcessById and Kill
                    UnityEngine.Debug.LogWarning($"Sports2DLauncher: Process (PID: {pid}) was not running or exited before Kill could complete: {ex.Message}");
                }
                catch (Win32Exception ex)
                {
                    UnityEngine.Debug.LogError($"Sports2DLauncher Error: Failed to terminate process (PID: {pid}) (Win32Exception): {ex.Message}. Check permissions.");
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError($"Sports2DLauncher Error: Exception while trying to stop process (PID: {pid}): {e.Message}");
                }
                finally
                {
                    if (!processToKill.HasExited)
                    {
                         // If still running after attempts, log a warning
                         UnityEngine.Debug.LogError($"Sports2DLauncher: FAILED TO STOP PROCESS (PID: {pid}). It might require manual termination.");
                    }
                    processToKill.Dispose(); // Dispose the Process object we created
                }
            }
            else
            {
                UnityEngine.Debug.Log($"Sports2DLauncher: Process with PID {pid} was not found or had already exited.");
            }
        }
        catch (System.ArgumentException ex)
        { 
            // GetProcessById throws this if the process doesn't exist
            UnityEngine.Debug.Log($"Sports2DLauncher: Process with PID {pid} not found: {ex.Message}");
        }        
        catch (System.Exception ex)
        { 
            UnityEngine.Debug.LogError($"Sports2DLauncher: Error retrieving or killing process with PID {pid}: {ex.Message}");
        }
    }
}