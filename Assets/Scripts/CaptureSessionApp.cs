using HoloLensCameraStream;
using HoloToolkit.Unity.SpatialMapping;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VR.WSA;
using Unity.IO.Compression;

using System.Collections;
#if !UNITY_EDITOR && UNITY_METRO
using System.IO.Compression;    // for ZipFile
#endif

public class CaptureSessionApp : MonoBehaviour {

    public bool PlacingAxisMarkers = true;
    public GameObject AxisPrefab;

    enum RecordingState { NotRecording, Initializing, Recording, Finalizing };

    struct PoseData
    {
        public float timestamp;
        public Vector3 position;
        public Vector3 eulerAngles;
    };
    List<PoseData> m_CurrentPoses = new List<PoseData>();

    RecordingState m_CurrentRecordingState;
    Text m_ToggleRecordingButtonText;

    public Button ToggleRecordingButton;

    // Injected objects
    VideoPanel _videoPanelUI;
    
    float m_RecordingStartTime;


    string m_CurrentRecordingLabel;
    string m_CurrentMeshFilename;
    string m_CurrentMeshFilepath;
    string m_CurrentPoseFilename;
    string m_CurrentPoseFilepath;

    byte[] _latestImageBytes;
    HoloLensCameraStream.Resolution _resolution;

    VideoCapture _videoCapture;

    CameraParameters _cameraParams;

    IntPtr _spatialCoordinateSystemPtr;

    int m_NumFrames;

    string OutputDirectoryBasePath;

    string m_CurrentOutputDirectory;
    string m_CurrentOutputFramesDirectory;

    // Use this for initialization
    void Start () {

        OutputDirectoryBasePath = MeshSaver.MeshFolderName;

        m_ToggleRecordingButtonText = ToggleRecordingButton.GetComponentInChildren<Text>();
        Button btn = ToggleRecordingButton.GetComponent<Button>();
        btn.onClick.AddListener(ToggleButtonOnClick);

        m_CurrentRecordingState = RecordingState.NotRecording;
        UpdateRecordingUI();

        //Fetch a pointer to Unity's spatial coordinate system if you need pixel mapping
        _spatialCoordinateSystemPtr = WorldManager.GetNativeISpatialCoordinateSystemPtr();

        //Call this in Start() to ensure that the CameraStreamHelper is already "Awake".
        CameraStreamHelper.Instance.GetVideoCaptureAsync(OnVideoCaptureCreated);
        //You could also do this "shortcut":
        //CameraStreamManager.Instance.GetVideoCaptureAsync(v => videoCapture = v);

        _videoPanelUI = GameObject.FindObjectOfType<VideoPanel>();
    }

    void UpdateRecordingUI()
    {
        switch (m_CurrentRecordingState)
        {
            case RecordingState.NotRecording:
                ToggleRecordingButton.interactable = true;
                m_ToggleRecordingButtonText.text = "Start recording";
                break;
            case RecordingState.Initializing:
                ToggleRecordingButton.interactable = false;
                m_ToggleRecordingButtonText.text = "Initializing...";
                break;
            case RecordingState.Recording:
                ToggleRecordingButton.interactable = true;
                m_ToggleRecordingButtonText.text = "Stop recording";
                break;
            case RecordingState.Finalizing:
                ToggleRecordingButton.interactable = false;
                m_ToggleRecordingButtonText.text = "Finalizing...";
                break;
            default:
                break;
        }
    }

    // Update is called once per frame
    void Update () {
		
	}

    private void OnDestroy()
    {
        if (_videoCapture != null)
        {
            //_videoCapture.FrameSampleAcquired -= OnFrameSampleAcquired;
            _videoCapture.Dispose();
        }
    }

    void OnVideoCaptureCreated(VideoCapture videoCapture)
    {
        if (videoCapture == null)
        {
            Debug.LogError("Did not find a video capture object. You may not be using the HoloLens.");
            return;
        }

        this._videoCapture = videoCapture;

        //Request the spatial coordinate ptr if you want fetch the camera and set it if you need to 
        CameraStreamHelper.Instance.SetNativeISpatialCoordinateSystemPtr(_spatialCoordinateSystemPtr);

        _resolution = CameraStreamHelper.Instance.GetLowestResolution();
        float frameRate = CameraStreamHelper.Instance.GetHighestFrameRate(_resolution);
        //videoCapture.FrameSampleAcquired += OnFrameSampleAcquired;

        //You don't need to set all of these params.
        //I'm just adding them to show you that they exist.
        _cameraParams = new CameraParameters();
        _cameraParams.cameraResolutionHeight = _resolution.height;
        _cameraParams.cameraResolutionWidth = _resolution.width;
        _cameraParams.frameRate = Mathf.RoundToInt(frameRate);
        _cameraParams.pixelFormat = CapturePixelFormat.BGRA32;
        _cameraParams.rotateImage180Degrees = true; //If your image is upside down, remove this line.
        _cameraParams.enableHolograms = false;

        UnityEngine.WSA.Application.InvokeOnAppThread(() => { _videoPanelUI.SetResolution(_resolution.width, _resolution.height); }, false);

        Debug.Log("Set up video capture. Ready to record.");
    }

    void OnVideoModeStarted(VideoCaptureResult result)
    {
        if (result.success == false)
        {
            Debug.LogWarning("Could not start video mode.");
            return;
        }

        m_NumFrames = 0;
        m_RecordingStartTime = Time.time;

        m_CurrentRecordingState = RecordingState.Recording;
        UpdateRecordingUI();

        Debug.Log(string.Format("Started video recording for session {0}", m_CurrentRecordingLabel));

        Debug.Log("Video capture started.");

        if (m_CurrentRecordingState == RecordingState.Recording)
        {
            this._videoCapture.RequestNextFrameSample(OnFrameSampleAcquired);
        }
    }

    void OnFrameSampleAcquired(VideoCaptureSample sample)
    {
        m_NumFrames++;

        //When copying the bytes out of the buffer, you must supply a byte[] that is appropriately sized.
        //You can reuse this byte[] until you need to resize it (for whatever reason).
        if (_latestImageBytes == null || _latestImageBytes.Length < sample.dataLength)
        {
            _latestImageBytes = new byte[sample.dataLength];
        }
        sample.CopyRawImageDataIntoBuffer(_latestImageBytes);

        //If you need to get the cameraToWorld matrix for purposes of compositing you can do it like this
        float[] cameraToWorldMatrixAsFloat;
        if (sample.TryGetCameraToWorldMatrix(out cameraToWorldMatrixAsFloat) == false)
        {
            return;
        }

        //If you need to get the projection matrix for purposes of compositing you can do it like this
        float[] projectionMatrixAsFloat;
        if (sample.TryGetProjectionMatrix(out projectionMatrixAsFloat) == false)
        {
            return;
        }

        // Right now we pass things across the pipe as a float array then convert them back into UnityEngine.Matrix using a utility method
        Matrix4x4 cameraToWorldMatrix = LocatableCameraUtils.ConvertFloatArrayToMatrix4x4(cameraToWorldMatrixAsFloat);
        Matrix4x4 projectionMatrix = LocatableCameraUtils.ConvertFloatArrayToMatrix4x4(projectionMatrixAsFloat);
        
        //This is where we actually use the image data
        UnityEngine.WSA.Application.InvokeOnAppThread(() =>
        {
            int numFrames = m_NumFrames;

            _videoPanelUI.SetBytes(_latestImageBytes);

            Texture2D tex = _videoPanelUI.rawImage.texture as Texture2D;
            byte[] jpgBytes = tex.EncodeToJPG();

            /*
            Vector3 inverseNormal = LocatableCameraUtils.GetNormalOfPose(cameraToWorldMatrix);
            Vector3 imagePosition = cameraToWorldMatrix.MultiplyPoint(Vector3.zero);

            // Throw out an indicator in the composite space 2 meters in front of us using the corresponding view matrices
            float distanceToMarker = 2f;
            Vector3 pointOnFaceBoxPlane = imagePosition - inverseNormal * distanceToMarker;
            Plane surfacePlane = new Plane(inverseNormal, pointOnFaceBoxPlane);

            Vector2 targetPoint = new Vector2(_resolution.width * 0.5f, _resolution.height * 0.5f);
            Vector3 mdPoint = LocatableCameraUtils.PixelCoordToWorldCoord(cameraToWorldMatrix, projectionMatrix, _resolution, targetPoint, surfacePlane);
            */

            //string infoText = String.Format("Position: {0}\t{1}\t{2}\nRotation: {3}\t{4}\t{5}", position.x, position.y, position.z, eulerAngles.x, eulerAngles.y, eulerAngles.z);
            //Debug.Log(infoText);


            float timestamp = Time.time - m_RecordingStartTime;

            PoseData pose = new PoseData();
            pose.timestamp = timestamp;
            pose.position = cameraToWorldMatrix.MultiplyPoint(Vector3.zero);

            Quaternion rotation = Quaternion.LookRotation(-cameraToWorldMatrix.GetColumn(2), cameraToWorldMatrix.GetColumn(1));
            pose.eulerAngles = rotation.eulerAngles;

            if (PlacingAxisMarkers && AxisPrefab != null)
            {
                Instantiate(AxisPrefab, pose.position, rotation);
            }


            m_CurrentPoses.Add(pose);
            
            SaveFrame(jpgBytes, numFrames);

            if (m_CurrentRecordingState == RecordingState.Recording)
            {
                this._videoCapture.RequestNextFrameSample(OnFrameSampleAcquired);
            }
        }, false);
    }

    private void SaveFrame(byte[] jpgBytes, int frameIdx)
    {
        string frameFilename = string.Format("frame{0:00000}.jpg", frameIdx);

        string frameFullPath = System.IO.Path.Combine(m_CurrentOutputFramesDirectory, frameFilename);

        File.WriteAllBytes(frameFullPath, jpgBytes);
    }

    void InitRecording()
    {
        if (this._videoCapture == null)
        {
            Debug.LogError("VideoCapture has not been set up.");
            return;
        }

        m_CurrentRecordingState = RecordingState.Initializing;
        UpdateRecordingUI();

        m_CurrentRecordingLabel = System.DateTime.Now.ToString("yyyyMMddHHmmss");
        
        m_CurrentOutputDirectory = System.IO.Path.Combine(OutputDirectoryBasePath, m_CurrentRecordingLabel);
        if (!Directory.Exists(m_CurrentOutputDirectory))
        {
            Directory.CreateDirectory(m_CurrentOutputDirectory);
        }

        m_CurrentOutputFramesDirectory = System.IO.Path.Combine(m_CurrentOutputDirectory, "frames");
        if (!Directory.Exists(m_CurrentOutputFramesDirectory))
        {
            Directory.CreateDirectory(m_CurrentOutputFramesDirectory);
        }

        m_CurrentPoseFilename = string.Format("{0}_pose.csv", m_CurrentRecordingLabel);
        m_CurrentPoseFilepath = System.IO.Path.Combine(OutputDirectoryBasePath, m_CurrentPoseFilename);


        m_CurrentPoses.Clear();

        this._videoCapture.StartVideoModeAsync(_cameraParams, OnVideoModeStarted);
    }

    void FinalizeRecording()
    {
        m_CurrentRecordingState = RecordingState.Finalizing;
        UpdateRecordingUI();

        Debug.Log("Stopping video recording...");
        this._videoCapture.StopVideoModeAsync(OnStoppedRecordingVideo);
    }

    void SaveMesh()
    {

        // SimpleMeshSerializer saves minimal mesh data (vertices and triangle indices) in the following format:
        // File header: vertex count (32 bit integer), triangle count (32 bit integer)
        // Vertex list: vertex.x, vertex.y, vertex.z (all 32 bit float)
        // Triangle index list: 32 bit integers

        List<MeshFilter> meshFilters = SpatialMappingManager.Instance.GetMeshFilters();

        m_CurrentMeshFilename = string.Format("{0}_mesh", m_CurrentRecordingLabel);

        Debug.Log("Saving mesh...");
        string meshFilepath = MeshSaver.Save(m_CurrentMeshFilename, meshFilters);

        m_CurrentMeshFilepath = meshFilepath;

        Debug.Log("Saved mesh");
    }

    void SavePoses()
    {
        // saving poses as CSV file in format:
        // <timestamp>,<position_x>,<position_y>,<position_z>,<rotation_x>,<rotation_y>,<rotation_z>
        // (where rotation is a ZXY set of euler angles in degrees)

        Debug.Log("Saving poses...");

        string delimiter = ",";

        StringBuilder sb = new StringBuilder();

        foreach (PoseData pose in m_CurrentPoses)
        {
            string timestamp = ((double)pose.timestamp).ToString();
            
            string pos_x = ((double)pose.position.x).ToString();
            string pos_y = ((double)pose.position.y).ToString();
            string pos_z = ((double)pose.position.z).ToString();

            string rot_x = ((double)pose.eulerAngles.x).ToString();
            string rot_y = ((double)pose.eulerAngles.y).ToString();
            string rot_z = ((double)pose.eulerAngles.z).ToString();

            string[] line = new string[] { timestamp, pos_x, pos_y, pos_z, rot_x, rot_y, rot_z };

            string joinedLine = string.Join(delimiter, line);

            sb.AppendLine(joinedLine);
        }

        File.WriteAllText(m_CurrentPoseFilepath, sb.ToString());

        Debug.Log("Saved poses");
    }

    private void OnStoppedRecordingVideo(VideoCaptureResult result)
    {
        if (result.success)
        {
            Debug.Log("Stopped recording video.");

            SaveMesh();
            
            SavePoses();
            
            Debug.Log("Compressing frames...");

            string sInDir = m_CurrentOutputFramesDirectory;
            string sOutFile = Path.Combine(OutputDirectoryBasePath, string.Format("{0}_frames.zip", m_CurrentRecordingLabel));

#if !UNITY_EDITOR && UNITY_METRO
            Debug.Log("Zipping frames...");
            ZipFile.CreateFromDirectory(sInDir, sOutFile);
            Debug.Log(string.Format("Frames compressed to {0}", sOutFile));
#else
            Debug.LogError("NOTE: Not zipping up any frames in editor mode because ZipFile is only in .NET code");
#endif
            
            

            OnRecordingFinalized();
        }
        else
        {
            Debug.LogError("Failed to stop recording video");
        }
    }
    
    void OnRecordingFinalized()
    {
        m_CurrentRecordingState = RecordingState.NotRecording;
        UpdateRecordingUI();

        string outputMessage = string.Format("Recording finished.\nRoom mesh: {0}\nPoses: {1}", m_CurrentMeshFilepath, m_CurrentPoseFilepath);
        Debug.Log(outputMessage);
    }

    void ToggleButtonOnClick()
    {
        if (m_CurrentRecordingState == RecordingState.NotRecording)
        {
            InitRecording();
        }
        else if (m_CurrentRecordingState == RecordingState.Recording)
        {
            FinalizeRecording();
        }
    }
}
