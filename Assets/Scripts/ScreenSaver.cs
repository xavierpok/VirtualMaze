
using System.Security.AccessControl;
using System.Runtime.InteropServices;
using Eyelink.Structs;
using HDF.PInvoke;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VirtualMaze.Assets.Scripts.Raycasting;
using VirtualMaze.Assets.Scripts.Utils;
/// <summary>
/// TODO cancel button
/// </summary>

public class ScreenSaver : BasicGUIController {
    /* Flags */
    private const int No_Missing = 0x0;
    private const int Ignore_Data = 0x1;

    /* Screen bounds */
    private static readonly Vector2Int minBound = Vector2Int.zero;
    private static readonly Vector2Int maxBound = new Vector2Int(1920, 1080);

    /* Caching to prevent excess GC */
    private WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();

    /* Number of Unity Frames to process in a batch */
    private const int Frame_Per_Batch = 100;

    /* acceptable time difference between edf and session triggers for approximation of missing trigger */
    private const int Accepted_Time_Diff = 20;

    /* Rect representing rough pixel location of hint image boundary (Unity Coords) */
    // Most recently -- Deprecated in favour of if the multicast cone overlaps with the hint image.
    // This controls the area around the hint image, where if the center of the multicast cone would hit this, that datapoint is not multicast.
    private const float HINT_IMAGE_CLEARANCE = 0f;
     // set to 0 for now -- deprecated for the time being. Feel free to re-enable if deemed relevant to investigation again
    private const Rect HINT_IMAGE_BOUNDARY = new Rect(( (836 - HINT_IMAGE_CLEARANCE), (880 - HINT_IMAGE_CLEARANCE),
                                                        248 + 2 * HINT_IMAGE_CLEARANCE,170 + 2 * HINT_IMAGE_CLEARANCE));
    // the above WOULD be set to const, but it cannot be.

    [SerializeField]
    private GameObject binWallPrefab = null;

    [SerializeField]
    private GameObject CueBinCollider = null;
    [SerializeField]
    private GameObject HintBinCollider = null;

    //UI objects
    public FileSelector eyeLinkFileInput;
    public FileSelector sessionInput;
    public FileSelector folderInput;

    [SerializeField]
    private InputField distToScreenInput = null;
    [SerializeField]
    private InputField gazeRadiusInput = null;
    [SerializeField]
    private InputField stepSizeInput = null;
    [SerializeField]
    private InputField screenPixelDimsX = null;
    [SerializeField]
    private InputField screenPixelDimsY= null;
    [SerializeField]
    private InputField screenCmDimsX = null;
    [SerializeField]
    private InputField screenCmDimsY = null;

    //non-SerializeField for runtime usage


    public Text sessionInfo;

    public GazePointPool gazePointPool;

    /* Camera which renders the view of the subject */
    public Camera viewport;

    public Transform robot;
    public FadeCanvas fadeController;

    public CueController cueController;

    public RectTransform GazeCanvas;
    public Slider progressBar;








    private void Awake() {
        eyeLinkFileInput.OnPathSelected.AddListener(ChooseEyelinkFile);
        sessionInput.OnPathSelected.AddListener(ChooseSession);
        folderInput.OnPathSelected.AddListener(ChooseFolder);

        List<Dropdown.OptionData> list = new List<Dropdown.OptionData>();

        /* starts at 1 since index 0 is Start Scene in build settings */
        for (int i = 1; i < SceneManager.sceneCountInBuildSettings; i++) {
            string sceneName = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
            list.Add(new Dropdown.OptionData(sceneName));
        }
        //TODO create a dropdown in the UI so that the scene can be selected.
    }

    private void Start() {
        if (Application.isEditor) { //for testing purposes.
            ChooseEyelinkFile(@"D:\Desktop\NUS\FYP\LatestData");
            ChooseSession(@"D:\Desktop\NUS\FYP\LatestData");
            ChooseFolder(@"D:\Desktop\NUS\FYP\LatestData");

            Debug.Log(Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase).AbsolutePath)));
        }
    }

    public void OnRender() {
        // check if file exists
        string sessionPath = sessionInput.text;
        if (!Directory.Exists(sessionPath) && !File.Exists(sessionPath)) {
            Console.WriteError($"{sessionPath} does not exist");
            return;
        }

        //check if directory exists
        string toFolderPath = folderInput.text;
        if (!Directory.Exists(toFolderPath)) {
            Console.WriteError($"{toFolderPath} does not exist");
            return;
        }

        string edfPath = eyeLinkFileInput.text;
        if (!File.Exists(edfPath)) { //check if file exist
            Console.WriteError($"{edfPath} does not exist");
            return;
        }
        bool successFlag = default;
        RaycastSettings raycastSettings = RaycastSettings.FromString(distToScreen: distToScreenInput.text, 
            gazeRadius: gazeRadiusInput.text, 
            density: stepSizeInput.text, 
            screenPixelX: screenPixelDimsX.text,
            screenPixelY: screenPixelDimsY.text,
            screenCmX: screenCmDimsX.text,
            screenCmY: screenCmDimsY.text,
            successFlag: out successFlag);
        
        if (!successFlag) {
            Debug.Log("Error was found in raycast settings, default used.");
        }


        StartCoroutine(ProcessSessionDataTask(sessionInput.text, eyeLinkFileInput.text, folderInput.text, raycastSettings));
    }


    private bool isMatFile(string filePath) {
        return IsFileWithExtension(filePath, ".mat");
    }

    private bool IsFileWithExtension(string filePath, string extension) {
        return Path.GetExtension(filePath).Equals(extension, StringComparison.InvariantCultureIgnoreCase);
    }

    private IEnumerable<string> GetSessionFilesFromDirectory(string dirPath) {
        return Directory.EnumerateFiles(dirPath, "*.txt");
    }

    void ChooseSession(string filePath) {
        if (Directory.Exists(filePath)) {
            IEnumerable<string> filesToProcess = GetSessionFilesFromDirectory(filePath);
            sessionInfo.text = "";
            Console.Write($"Processing in the following order of:\n\n{string.Join("\n", filesToProcess)}");
            SetInputFieldValid(sessionInput, true);
            sessionInput.text = filePath;
            return;
        }
        else if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
            return;
        }

        bool success = isMatFile(filePath);
        int numFrames = 0;

        if (success) {
            sessionInput.text = filePath;
            sessionInfo.text = $"{numFrames} frames";
        }
        else {
            Console.Write("Invalid Session File Detected, Unsupported File Type or Data");
        }
        SetInputFieldValid(sessionInput, success);
    }

    void ChooseEyelinkFile(string filePath) {
        if (string.IsNullOrEmpty(filePath)) return;

        eyeLinkFileInput.text = filePath;
        SetInputFieldValid(eyeLinkFileInput, File.Exists(filePath));
    }

    void ChooseFolder(string file) {
        if (string.IsNullOrEmpty(file)) { return; }

        folderInput.text = file;
        SetInputFieldValid(folderInput, Directory.Exists(file));
    }

    private IEnumerator PrepareScene(string sceneName) {
        if (!SceneManager.GetActiveScene().name.Equals(sceneName)) {
            AsyncOperation opr = SceneManager.LoadSceneAsync(sceneName);

            while (!opr.isDone) {
                yield return null;
            }
        }
    }

    public IEnumerator ProcessSessionDataTask(string sessionPath, string edfPath, string toFolderPath, RaycastSettings raycastSettings) {
        /* Setup */
        H5.close();
        H5.open();
        fadeController.gameObject.SetActive(false);
        CueBinCollider.SetActive(true);
        HintBinCollider.SetActive(true);

        EyeDataReader eyeReader = null;

        Physics.SyncTransforms();

        if (isMatFile(edfPath)) {
            try {
                eyeReader = new EyeMatReader(edfPath);
            }
            catch (Exception e) {
                Debug.LogException(e);
                Console.WriteError("Unable to open eye data mat file.");
            }
        }

        ISessionDataReader sessionReader = CreateSessionReader(sessionPath);

        if (eyeReader == null || sessionReader == null) {
            yield break;
        }

        gazePointPool?.PreparePool();

        progressBar.value = 0;
        progressBar.gameObject.SetActive(true);

        cueController.SetMode(CueController.Mode.Recording);
        cueController.UpdatePosition(robot);
        Physics.SyncTransforms();

        yield return PrepareScene("Double Tee");

        string filename = $"{Path.GetFileNameWithoutExtension(sessionPath)}_{Path.GetFileNameWithoutExtension(edfPath)}.csv";
        string gazeRadiusNoDot = $"{raycastSettings.GazeRadius}".Replace(".","-");
        string multiCastName = $"{Path.GetFileNameWithoutExtension(sessionPath)}_{Path.GetFileNameWithoutExtension(edfPath)}_r{gazeRadiusNoDot}.csv";
        DateTime start = DateTime.Now;
        Debug.LogError($"s: {start}");


        using (RayCastWriteManager areaCastWriteManager = RayCastWriteManager.GetCsvManager(
                Path.Combine(toFolderPath,multiCastName)))
        using (RayCastRecorder recorder = new RayCastRecorder(toFolderPath, filename)) {
            yield return ProcessSession(sessionReader,
             eyeReader, recorder, areaCastWriteManager, raycastSettings);
        }
        Console.Write($"s: {start}, e: {DateTime.Now}");
        Debug.LogError($"s: {start}, e: {DateTime.Now}");

        /* Clean up */
        //SceneManager.LoadScene("Start");
        fadeController.gameObject.SetActive(true);
        progressBar.gameObject.SetActive(false);
        cueController.SetMode(CueController.Mode.Experiment);
        CueBinCollider.SetActive(false);
        HintBinCollider.SetActive(false);
        //SessionStatusDisplay.ResetStatus();

        print($"{minGaze}, {maxGaze}");
    }

    private bool HasEdfSessionEnded(MessageEvent msgEvent, SessionTrigger trigger) {
        //there exits edf files with multiple sessions in them, each session is divided by a ExperimentVersionTrigger
        return msgEvent == null || msgEvent.dataType == DataTypes.NO_PENDING_ITEMS || trigger == SessionTrigger.ExperimentVersionTrigger;
    }

    private decimal EnqueueData(Queue<SessionData> sessionFrames, ISessionDataReader sessionReader, Queue<AllFloatData> fixations, EyeDataReader eyeReader, out int status, out string reason) {
        Profiler.BeginSample("Enqueue");
        decimal sessionEventPeriod = LoadToNextTriggerSession(sessionReader, sessionFrames, out SessionData sessionData);
        uint edfEventPeriod = LoadToNextTriggerEdf(eyeReader, fixations, out MessageEvent edfdata, out SessionTrigger edfTrigger);

        if (sessionData.trigger != edfTrigger) {
            throw new Exception("Unaligned session and eyedata! Are there missing triggers in eyelink or Unity data?");
        }

        decimal excessTime = (sessionEventPeriod - edfEventPeriod);

        status = No_Missing;
        reason = null;

        /* if missing trigger detected and the time difference between the 2 files more than the threshold*/
        if (Math.Abs(excessTime) > Accepted_Time_Diff) {
            status = Ignore_Data;
            reason = $"Time difference too large.";
        }

        print($"ses: { sessionEventPeriod:F4}, edf: { edfEventPeriod:F4}, excess: {excessTime:F4} |{fixations.Peek().ToString()} {sessionFrames.Count}");
        Profiler.EndSample();
        return excessTime;
    }

    private ISessionDataReader CreateSessionReader(string filePath) {
        try {
            switch (Path.GetExtension(filePath).ToLower()) {
                case ".txt":
                    return new SessionReader(filePath);
                case ".mat":
                    return new MatSessionReader(filePath);
                default:
                    Debug.LogWarning($"File extension not supported{filePath}");
                    return null;
            }
        }
        catch (Exception e) {
            Debug.LogException(e);
            return null;
        }
    }

    private IEnumerator ProcessSession(ISessionDataReader sessionReader, EyeDataReader eyeReader, RayCastRecorder recorder, RayCastWriteManager multicastWriteManager, RaycastSettings raycastSettings) {
        int frameCounter = 0;
        int trialCounter = 1;

        if (sessionReader == null) {
            yield break;
        }

        //set up all variables here

        Queue<SessionData> sessionFrames = new Queue<SessionData>();
        Queue<AllFloatData> fixations = new Queue<AllFloatData>();

        List<Fsample> binSamples = new List<Fsample>();
        Queue<BinWallManager.BinGazeJobData> jobQueue = new Queue<BinWallManager.BinGazeJobData>();
        HashSet<int> binsHitId = new HashSet<int>();
        Debug.Log($"Initialised AreaRaycastManager with:\n" + 
            $"Radius : {raycastSettings.GazeRadius} \n"+
            $"Step Size : {raycastSettings.StepSize} \n"+
            $"distToScreen : {raycastSettings.DistToScreen}\n" +
            $"screenDims : {raycastSettings.ScreenCmDims}\n" +
            $"pixelDims : {raycastSettings.ScreenPixelDims}\n" +
            $"toIgnoreNameList : {AreaRayCastManager.defaultToIgnoreNameList}");
        AreaRaycastManager areaRaycastManager = 
            new AreaRaycastManager(
                angularRadius: raycastSettings.GazeRadius,
                angularStepSize: raycastSettings.StepSize,
                distToScreen: raycastSettings.DistToScreen,
                screenDims : raycastSettings.ScreenCmDims,
                pixelDims : raycastSettings.ScreenPixelDims,
                toIgnoreNameList : areaRaycastManager.defaultToIgnoreNameList
            );



        //Move to first Trial Trigger
        AllFloatData data = PrepareFiles(sessionReader, eyeReader, SessionTrigger.TrialStartedTrigger);


        //feed in current Data due to preparation moving the data pointer forward
        fixations.Enqueue(data);

        int debugMaxMissedOffset = 0;

        List<Fsample> sampleCache = new List<Fsample>();
        int numberOfTriggers = 0;
        while (sessionReader.HasNext /*&& numberOfTriggers < 8*/) {
            numberOfTriggers++;
            /*add current to buffer since sessionData.timeDelta is the time difference from the previous frame.
             * and the previous frame raised a trigger for it to be printed in this frame*/

            sessionFrames.Enqueue(sessionReader.CurrentData);
            decimal excessTime = 0; 
            // dummy because it's in a try-catch below
            // decimal will always have a value fed to it, will break if try fails.
            try {
                excessTime = EnqueueData(sessionFrames, sessionReader, fixations, eyeReader, out int status, out string reason);
            } catch (Exception e) {
                Debug.LogException(e);
                yield break;
            } 
            decimal timepassed = fixations.Peek().time;
            decimal c1 = 0;
            decimal c2 = 0;

            decimal timeOffset = excessTime / (sessionFrames.Count - 1);

            print($"timeError: {excessTime}|{timeOffset} for {sessionFrames.Count} frames @ {sessionReader.CurrentIndex} and {fixations.Count} fix");
            uint gazeTime = 0;

            decimal debugtimeOffset = 0;

            while (sessionFrames.Count > 0 && fixations.Count > 0) {
                SessionData sessionData = sessionFrames.Dequeue();
                frameCounter++;

                decimal period;
                if (sessionFrames.Count > 0) {
                    //peek since next sessionData holds the time it takes from this data to the next
                    period = (sessionFrames.Peek().timeDeltaMs) - timeOffset;
                }
                else {
                    //use current data's timedelta to approximate since peeking at the next data's timedelta is not supported
                    period = (sessionData.timeDeltaMs) - timeOffset;
                }

                KahanSummation(ref debugtimeOffset, ref c2, timeOffset);
                KahanSummation(ref timepassed, ref c1, period);

                MoveRobotTo(robot, sessionData);

                BinWallManager.ResetWalls();

                List<Fsample> frameGazes = new List<Fsample>();

                bool isLastSampleInFrame = false;
                AllFloatData currData = null;

                while (gazeTime <= timepassed && fixations.Count > 0) {
                    currData = fixations.Dequeue();
                    gazeTime = currData.time;

                    isLastSampleInFrame = gazeTime > timepassed;

                    if (currData is MessageEvent || isLastSampleInFrame) {
                        break;
                    }
                    else if (currData is Fsample) {
                        binSamples.Add((Fsample)currData);
                    }
                }

                Profiler.BeginSample("SyncTransform");
                Physics.SyncTransforms();
                Profiler.EndSample();

                /* process binsamples and let the raycast run in Jobs */
                Profiler.BeginSample("RaycastingSinglePrepare");
                RaycastGazesJob rCastJob = RaycastGazes(binSamples, recorder, currData, default);
                Profiler.EndSample();

                /* Start the binning process while rCastJob is running */
                // Profiler.BeginSample("Binning");
                // BinGazes(binSamples, binRecorder, jobQueue, mapper);
                // Profiler.EndSample();

                Profiler.BeginSample("MulticastingPrepare");        
                // Go through all in binSample to decide which need areacasting, and schedule areacasting & writing just for those
                foreach(Fsample fsample in binSamples) {
                    if (fsample.dataType == DataTypes.SAMPLESTARTFIX){
                        // do a check to make sure it is not on a hint/view image

                        bool toAreaCast = true;
                        Ray probeRay;



                        if (HINT_IMAGE_BOUNDARY.Contains(fsample.RightGaze)) {
                            toAreaCast = false;
                            Debug.Log($"Skipped multicasting at {fsample.time} due to coordinate being close to hint image");
                            probeRay = viewport.ViewportPointToRay(viewportGaze);
                            // Here partially as a sanity check -- 
                            // Make sure that the raycast method & coordinate method of detecting hint image agree at least sometimes.
                        }
                        else if (IsInScreenBounds(fsample.RightGaze) && fsample.dataType != DataTypes.SAMPLEINVALID) {
                            Vector2 viewportGaze = RangeCorrector.HD_TO_VIEWPORT.
                                correctVector(fsample.RightGaze);
                            probeRay = viewport.ViewportPointToRay(viewportGaze);
                        } else {
                            probeRay = RayConstants.NullRay;
                            // skip probe-casting
                            // Just because is invalid might not mean not subject to area casting
                            // E.g. looking at corner of screen.
                            // Still valid for area-casting.
                        }
                        if (!(RayConstants.IsAbsoluteEqual(probeRay, RayConstants.NullRay))) {
                            RaycastHit multiCastProbeHit;
                            bool probeRayCastSucceed = Physics.Raycast(probeRay.origin, probeRay.direction, out multiCastProbeHit, float.MaxValue, BinWallManager.ignoreBinningLayer);
                            
                            String hitName;
                            if (probeRayCastSucceed) {                            
                                hitName = RelativeHitLocFinder.getChainedName(multiCastProbeHit.transform.gameObject);
                            } else{
                                hitName = "";
                            }
                            if (hitName.ToLower().Contains("cueimage") ||
                                 hitName.ToLower().Contains("hintimage")) {
                                toAreaCast = false;
                                Debug.Log($"Skipped multicasting at {fsample.time} due to coordinate impacting hint image");
                            }
                        }

                        Tuple<RaycastHit[], Fsample[], AreaRaycastManager.OffsetData[]> areacastResult;
                        if (toAreaCast) {
                        areacastResult = 
                            areaRaycastManager.ScheduleAreaCasting(
                                sampleToCast: fsample,
                                viewport: viewport
                            );
                        } else {
                            areacastResult = 
                            areaRaycastManager.ScheduleDummyCasting(
                                sampleToCast: fsample,
                                viewport: viewport
                            );
                        }
                        
                        areaRaycastManager.
                            ScheduleHitWriteAndDispose(
                                time: fsample.time,
                                results : areacastResult.Item1,
                                raycastSamples : areacastResult.Item2,
                                offsets : areacastResult.Item3,
                                resultsTask: areacastResult,
                                subjectLoc: robot.transform.position,
                                rawGaze: fsample.RightGaze,
                                writeManager: multicastWriteManager

                            );
                        
                    }
                }
                Profiler.EndSample();



                Profiler.BeginSample("RaycastingSingleProcess");
                if (rCastJob != null) {
                    using (rCastJob) {
                        rCastJob.h.Complete(); //force completion if not done yet
                        rCastJob.Process(currData, recorder, robot, isLastSampleInFrame, gazePointPool, displayGazes: frameCounter == Frame_Per_Batch, GazeCanvas, viewport);
                    }
                }
                Profiler.EndSample();



                // Profiler.BeginSample("MultiCast Processing");
                // while (jobQueue.Count > 0) {
                //     using (BinWallManager.BinGazeJobData job = jobQueue.Dequeue()) {
                //         while (!job.h.IsCompleted) {

                //         }
                //         job.h.Complete();

                //         job.process(mapper, binsHitId);

                //         Profiler.BeginSample("HDFwrite");
                //         binRecorder.RecordMovement(job.sampleTime, binsHitId);
                //         Profiler.EndSample();

                //         Profiler.BeginSample("ClearHashset");
                //         binsHitId.Clear();
                //         Profiler.EndSample();
                //     }
                // }
                // Profiler.EndSample();

                binSamples.Clear();
                if (currData is Fsample) {
                    binSamples.Add((Fsample)currData);
                }
                else if (currData is MessageEvent) {
                    ProcessTrigger(currData);
                }

                frameCounter %= Frame_Per_Batch;
                if (frameCounter == 0) {
                    progressBar.value = sessionReader.ReadProgress;
                    yield return null;
                }
                gazePointPool?.ClearScreen();
            }

            Debug.Log($"ses: {sessionFrames.Count}| fix: {fixations.Count}, timestamp {gazeTime:F4}, timepassed{timepassed:F4}");
            decimal finalExcess = gazeTime - timepassed;

            Debug.Log($"FINAL EXCESS: {finalExcess} | {sessionReader.CurrentData.timeDeltaMs}");
            Debug.Log($"Frame End total time offset: {debugtimeOffset}");

            //clear queues to prepare for next trigger
            sessionFrames.Clear();

            if (Math.Abs(finalExcess) > Accepted_Time_Diff) {
                Debug.LogError($"Final Excess ({finalExcess}) Larger that Accepted time diff ({Accepted_Time_Diff})");
            }

            // DO CLEANUP IF EXCESS FIXATIONS HERE
            if (fixations.Count > 0) {
                Debug.LogWarning($"-----\n{fixations.Count} fixations assumed to belong to next trigger, " +
                    "and handled seperately from main processing loop, \n" +
                    $"Starting from timestamp {fixations.Peek().time}\n-----");
                List<Fsample> leftOverSamples = new List<Fsample>();
                AllFloatData nextEyeDataEvent = null;
                bool isLastSampleInFrame = false;

                while (fixations.Count > 0) {
                    nextEyeDataEvent = fixations.Dequeue();
                    gazeTime = nextEyeDataEvent.time;

                    isLastSampleInFrame = gazeTime > timepassed;
                    if (nextEyeDataEvent is MessageEvent) {
                        break;
                    }
                    else if (nextEyeDataEvent is Fsample) {
                        leftOverSamples.Add((Fsample)nextEyeDataEvent);
                    }

                }

                RaycastGazesJob leftOverRCastJob = RaycastGazes(leftOverSamples, recorder, nextEyeDataEvent, default);
                if (leftOverRCastJob != null) {
                    using (leftOverRCastJob) {
                        leftOverRCastJob.h.Complete(); //force completion if not done yet
                        leftOverRCastJob.Process(nextEyeDataEvent,
                         recorder, robot,
                         isLastSampleInFrame, gazePointPool, 
                         displayGazes: frameCounter == Frame_Per_Batch, GazeCanvas, viewport);
                    }
                }

                Profiler.BeginSample("MulticastingCleanUp");
                foreach(Fsample fsample in leftOverSamples) {
                    if (fsample.dataType == DataTypes.SAMPLESTARTFIX){
                        // do a check to make sure it is not on a hint/view image

                        bool toAreaCast = true;
                        Ray probeRay;
                        if (IsInScreenBounds(fsample.RightGaze) && fsample.dataType != DataTypes.SAMPLEINVALID) {
                            Vector2 viewportGaze = RangeCorrector.HD_TO_VIEWPORT.
                                correctVector(fsample.RightGaze);
                            probeRay = viewport.ViewportPointToRay(viewportGaze);
                            Debug.Log($"Probe Ray : {probeRay.origin}, {probeRay.direction}");
                        } else {
                            probeRay = RayConstants.NullRay;
                            // skip probe-casting
                            // Just because is invalid might not mean not subject to area casting
                            // E.g. looking at corner of screen.
                            // Still valid for area-casting.
                        }
                        if (!(RayConstants.IsAbsoluteEqual(probeRay, RayConstants.NullRay))) {
                            RaycastHit multiCastProbeHit;
                            bool probeRayCastSucceed = Physics.Raycast(probeRay.origin, probeRay.direction, out multiCastProbeHit, float.MaxValue, BinWallManager.ignoreBinningLayer);
                            
                            String hitName;
                            if (probeRayCastSucceed) {                            
                                hitName = RelativeHitLocFinder.getChainedName(multiCastProbeHit.transform.gameObject);
                            } else{
                                hitName = "";
                            }
                            if (hitName.ToLower().Contains("cueimage") ||
                                 hitName.ToLower().Contains("hintimage")) {
                                toAreaCast = false;
                                Debug.Log($"Skipped multicasting at {fsample.time}");
                            }
                        }

                        Tuple<RaycastHit[], Fsample[], AreaRaycastManager.OffsetData[]> areacastResult;
                        if (toAreaCast) {
                        areacastResult = 
                            areaRaycastManager.ScheduleAreaCasting(
                                sampleToCast: fsample,
                                viewport: viewport
                            );
                        } else {
                            areacastResult = 
                            areaRaycastManager.ScheduleDummyCasting(
                                sampleToCast: fsample,
                                viewport: viewport
                            );
                        }
                        
                        areaRaycastManager.
                            ScheduleHitWriteAndDispose(
                                time: fsample.time,
                                results : areacastResult.Item1,
                                raycastSamples : areacastResult.Item2,
                                offsets : areacastResult.Item3,
                                resultsTask: areacastResult,
                                subjectLoc: robot.transform.position,
                                rawGaze: fsample.RightGaze,
                                writeManager: multicastWriteManager

                            );
                        
                    }
                }
                Profiler.EndSample();
                
            }

            // if (fixations.Count > 0) {
            //     Debug.LogWarning($"{fixations.Count} fixations assumed to belong to next trigger");
            //     while (fixations.Count > 0) {
            //         debugMaxMissedOffset = Math.Max(fixations.Count, debugMaxMissedOffset);

            //         if (ProcessData(fixations.Dequeue(), recorder, false, binRecorder) == SessionTrigger.TrialStartedTrigger) {
            //             trialCounter++;
            //             SessionStatusDisplay.DisplayTrialNumber(trialCounter);
            //         }
            //     }
            // }
        }



        Debug.LogError(debugMaxMissedOffset);

    }

    

    private RaycastGazesJob RaycastGazes(List<Fsample> binSamples, RayCastRecorder recorder, AllFloatData currData, JobHandle dependancy) {
        int numSamples = binSamples.Count;

        if (numSamples == 0) {
            if (currData is MessageEvent) {
                recorder.FlagEvent(((MessageEvent)currData).message);
            }
            return null;
        }

        NativeArray<RaycastCommand> cmds = new NativeArray<RaycastCommand>(numSamples, Allocator.TempJob);
        NativeArray<RaycastHit> results = new NativeArray<RaycastHit>(numSamples, Allocator.TempJob);


        for (int i = 0; i < numSamples; i++) {
            Vector2 origGaze = binSamples[i].RightGaze;
            if (IsInScreenBounds(origGaze)) {
                Vector2 viewportGaze = RangeCorrector.HD_TO_VIEWPORT.correctVector(origGaze);
                Ray r = viewport.ViewportPointToRay(viewportGaze);
                cmds[i] = new RaycastCommand(r.origin, r.direction, layerMask: BinWallManager.ignoreBinningLayer);
            }
        }
        JobHandle h = RaycastCommand.ScheduleBatch(cmds, results, 1, dependancy);

        return new RaycastGazesJob(h, numSamples, binSamples, results, cmds);
    }

    // private SessionTrigger ProcessData(AllFloatData data, RayCastRecorder recorder, bool isLastSampleInFrame, BinRecorder binRecorder) {
    //     switch (data.dataType) {
    //         case DataTypes.SAMPLE_TYPE:
    //             Fsample fs = (Fsample)data;
    //             if (IsInScreenBounds(fs.rawRightGaze)) {
    //                 gazePointPool?.AddGazePoint(GazeCanvas, viewport, fs.RightGaze);

    //                 RaycastToScene(fs.RightGaze, out string objName, out Vector2 relativePos, out Vector3 objHitPos, out Vector3 gazePoint);

    //                 recorder.WriteSample(data.dataType, data.time, objName, relativePos, objHitPos, gazePoint, fs.rawRightGaze, robot.position, robot.rotation.eulerAngles.y, isLastSampleInFrame);
    //             }
    //             else {
    //                 //ignore if gaze is out of bounds
    //                 recorder.IgnoreSample(data.dataType, data.time, fs.rawRightGaze, robot.position, robot.rotation.eulerAngles.y, isLastSampleInFrame);
    //             }
    //             return SessionTrigger.NoTrigger;
    //         case DataTypes.MESSAGEEVENT:
    //             MessageEvent fe = (MessageEvent)data;
    //             ProcessTrigger(fe.trigger, cueController);

    //             recorder.FlagEvent(fe.message);

    //             return fe.trigger;
    //         default:
    //             //ignore others for now
    //             //Debug.LogWarning($"Unsupported EDF DataType Found! ({type})");
    //             return SessionTrigger.NoTrigger;
    //     }
    // }



    private float minGaze = float.PositiveInfinity;
    private float maxGaze = float.NegativeInfinity;

    private void BinGazes(List<Fsample> sampleCache, BinRecorder recorder, Queue<BinWallManager.BinGazeJobData> jobQueue, BinMapper mapper) {
        List<Vector2> gazeCache = new List<Vector2>();

        Vector2 prev = Vector2.negativeInfinity;

        Profiler.BeginSample("Loading Gazes");

        foreach (Fsample fs in sampleCache) {
            Vector2 gaze = fs.RightGaze;
            if (Vector2.SqrMagnitude(prev - gaze) > BinWallManager.gazeSqDistThreshold) {
                gazeCache.Add(gaze);
                prev = gaze;
            }
        }
        Profiler.EndSample();

        Profiler.BeginSample("Identify Obj");

        float maxSqDist = BinWallManager.IdentifyObjects(gazeCache, viewport, binWallPrefab, mapper);
        if (maxSqDist > -1) {
            minGaze = Mathf.Min(minGaze, maxSqDist);
        }
        maxGaze = Mathf.Max(maxGaze, maxSqDist);

        Profiler.EndSample();

        Profiler.BeginSample("RaycastAndSave");

        int modder = Mathf.FloorToInt(BinWallManager.secondaryOffset.Count / Mathf.Lerp(BinWallManager.secondaryOffset.Count * 0.50f, BinWallManager.secondaryOffset.Count, maxSqDist / (mapper.MaxPossibleSqDistance())));

        JobHandle latestHandle = default;

        foreach (Fsample fs in sampleCache) {
            Profiler.BeginSample("BinSingleGaze");

            BinWallManager.BinGazeJobData jobData = BinWallManager.BinGaze(fs.RightGaze, fs.time, viewport, latestHandle, modder);
            if (jobData != null) {
                latestHandle = jobData.h;
                jobQueue.Enqueue(jobData);
            }

            Profiler.EndSample();
        }
        Profiler.EndSample();
    }

    public static bool IsInScreenBounds(Vector2 gazeXY) {
        /* If gaze point is not NaN and within screen bounds */
        return !gazeXY.isNaN() && gazeXY.x <= maxBound.x && gazeXY.y <= maxBound.y && gazeXY.x >= minBound.x && gazeXY.y >= minBound.y;
    }

    private void ProcessTrigger(AllFloatData data) {
        if (data.dataType == DataTypes.MESSAGEEVENT) {
            ProcessTrigger(((MessageEvent)data).trigger, cueController);
        }
    }

    /// <summary>
    /// Processes the Trigger by showing or hiding the cues.
    /// </summary>
    /// <param name="trigger">Current Trigger</param>
    public static void ProcessTrigger(SessionTrigger trigger, CueController cueController) {

        switch (trigger) {
            case SessionTrigger.CueOffsetTrigger:
                cueController.HideCue();
                cueController.ShowHint();
                SessionStatusDisplay.DisplaySessionStatus("Trial Running");
                break;

            case SessionTrigger.TrialStartedTrigger:
                cueController.HideHint();
                cueController.ShowCue();
                SessionStatusDisplay.DisplaySessionStatus("Showing Cue");
                break;

            case SessionTrigger.TimeoutTrigger:
            case SessionTrigger.TrialEndedTrigger:
                cueController.HideAll();
                if (trigger == SessionTrigger.TimeoutTrigger) {
                    SessionStatusDisplay.DisplaySessionStatus("Time out");
                }
                else {
                    SessionStatusDisplay.DisplaySessionStatus("Trial Ended");
                }

                break;

            case SessionTrigger.ExperimentVersionTrigger:
                SessionStatusDisplay.DisplaySessionStatus("Next Session");
                break;

            case SessionTrigger.NoTrigger:
            //do nothing
            default:
                Debug.LogError($"Unidentified Session Trigger: {trigger}");
                break;
        }
    }

    /// <summary>
    /// Fires a raycast into the Scene based on the sample data to determine what object the sample data is fixating upon.
    /// </summary>
    /// <param name="gazeData">Data sample from edf file</param>
    /// <param name="objName">Name of object the gazed object</param>
    /// <param name="relativePos">Local 2D offset of from the center of the object gazed</param>
    /// <param name="objHitPos">World position of the object in the scene</param>
    /// <param name="gazePoint">World position of the point where the gaze fixates the object</param>
    /// <returns>True if an object was in the path of the gaze</returns>
    private bool RaycastToScene(Vector3 gazeData,
                                 out string objName,
                                 out Vector2 relativePos,
                                 out Vector3 objHitPos,
                                 out Vector3 gazePoint) {
        Ray r = viewport.ScreenPointToRay(gazeData);

        if (Physics.Raycast(r, out RaycastHit hit, maxDistance: 200, layerMask: BinWallManager.ignoreBinningLayer)) {
            Transform objhit = hit.transform;

            objName = hit.transform.name;
            relativePos = ComputeLocalPostion(objhit, hit);
            objHitPos = objhit.position;
            gazePoint = hit.point;

            return false;

        }
        else {
            objName = null;
            relativePos = Vector2.zero;
            gazePoint = Vector3.zero;
            objHitPos = Vector3.zero;

            return false;
        }
    }

    private static readonly Vector3[] axes = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

    private static Vector3 ModelNormal(Vector3 normal) {
        Vector3 result = Vector3.up;
        float min = float.MaxValue;
        foreach (Vector3 axis in axes) {
            float sqDist = (axis - normal).sqrMagnitude;
            if (sqDist < min) {
                min = sqDist;
                result = axis;
            }
        }

        return result;
    }

    /// <summary>
    /// </summary>
    /// <param name="objHit">Transform of the object hit by the raycast</param>
    /// <param name="hit"></param>
    /// <returns>2D location of the point fixated by that gaze relative to the center of the image</returns>
    private static Vector2 ComputeLocalPostion(Transform objHit, RaycastHit hit) {
        Vector3 hitNormal = ModelNormal(hit.normal);

        Vector3 normal;

        Vector3 dist = hit.point - objHit.position;

        if (objHit.name.ToLower().Contains("image")) {
            normal = hitNormal;
            dist = Quaternion.FromToRotation(hit.normal, normal) * dist;
        }
        else if (objHit.name.ToLower().Contains("poster")) {
            normal = ModelNormal(objHit.forward); //blue axis used as normal
        }
        else if (Mathf.Abs(hitNormal.y) == 1) { //either hit the floor or ceiling
            normal = ModelNormal(objHit.up); //green axis used as normal
        }
        else { //any other object
            normal = ModelNormal(objHit.right); //green axis used as normal
        }

        dist = Quaternion.FromToRotation(normal, Vector3.back) * dist;  //orientate for stabilization
        dist = Quaternion.FromToRotation(Vector3.back, Vector3.up) * dist; //orientate to top down veiw

        Vector2 result = new Vector2(dist.x, dist.z);

        if (normal == Vector3.forward) {
            result *= -1; //rotate 180 degrees
        }

        return result;
    }

    private AllFloatData PrepareFiles(ISessionDataReader sessionReader, EyeDataReader eyeReader, SessionTrigger firstOccurance) {
        FindNextSessionTrigger(sessionReader, firstOccurance);
        return FindNextEdfTrigger(eyeReader, firstOccurance);
    }

    /// <summary>
    /// Moves session Reader to point to the session reader.
    /// </summary>
    /// <param name="sessionReader">Session reader to move</param>
    /// <param name="trigger">SessionTrigger to move to</param>
    private void FindNextSessionTrigger(ISessionDataReader sessionReader, SessionTrigger trigger) {
        //move sessionReader to point to first trial
        while (sessionReader.Next()) {
            if (sessionReader.CurrentData.trigger == trigger) {
                MoveRobotTo(robot, sessionReader.CurrentData);
                break;
            }
        }
    }

    /// <summary>
    /// Moves the current pointer to point to the next trigger. Any data between the current point and the 
    /// next trigger is ignored.
    /// </summary>
    /// <param name="eyeReader"></param>
    /// <param name="trigger">The next trigger to find</param>
    /// <returns>Current data pointed to by the eyeReader</returns>
    private AllFloatData FindNextEdfTrigger(EyeDataReader eyeReader, SessionTrigger trigger) {
        AllFloatData data = null;

        //move edfFile to point to first trial
        bool foundNextTrigger = false;
        while (!foundNextTrigger) {
            data = eyeReader.GetNextData();

            if (data.dataType == DataTypes.MESSAGEEVENT) {
                MessageEvent ev = (MessageEvent)data;

                foundNextTrigger = ev.trigger == trigger;
            }
            else if (data.dataType == DataTypes.NO_PENDING_ITEMS) {
                foundNextTrigger = true;
            }
        }

        return data;
    }

    /// <summary>
    /// Loads data from the next data point to the next trigger (inclusive) and returns the total taken from the current 
    /// position top the next Trigger
    /// 
    /// TODO: since the time delta is time difference of the last frame, total time should include the time difference of the 
    /// next frame after the trigger.
    /// </summary>
    /// <param name="reader">The SessionReader to be read</param>
    /// <param name="frames">the Queue to store the data</param>
    /// <param name="trigger">The trigger where the loading stops</param>
    /// <returns>Total time taken from one current trigger to the next</returns>
    private decimal LoadToNextTriggerSession(ISessionDataReader reader, Queue<SessionData> frames, out SessionData data) {
        /* Kahan Summation Algo for more accurate floating pt addition */
        decimal totalTime = 0;//sum
        decimal c = 0;

        data = null;
        bool isNextEventFound = false;

        // Conditon evaluation is Left to Right and it short circuits.
        // Please do not change the order of this if conditon.
        while (!isNextEventFound && reader.Next()) {
            data = reader.CurrentData;
            frames.Enqueue(data);

            KahanSummation(ref totalTime, ref c, data.timeDeltaMs);

            isNextEventFound = data.trigger != SessionTrigger.NoTrigger;
        }

        return totalTime;
    }

    [MethodImpl(MethodImplOptions.NoOptimization)]
    private void KahanSummation(ref decimal sum, ref decimal c, decimal item) {
        decimal y = item - c;
        decimal t = sum + y;
        c = (t - sum) - y;
        sum = t;
    }

    private uint LoadToNextTriggerEdf(EyeDataReader reader, Queue<AllFloatData> fixations, out MessageEvent latest, out SessionTrigger edfTrigger) {
        bool isNextEventFound = false;

        while (!isNextEventFound) {
            AllFloatData data = reader.GetNextData();

            if (data == null) {
                isNextEventFound = true;
                continue;
            }

            DataTypes type = data.dataType;

            fixations.Enqueue(data);

            if (type == DataTypes.MESSAGEEVENT) {
                isNextEventFound = true;
                MessageEvent ev = (MessageEvent)data;
                latest = ev;
                edfTrigger = ev.trigger;
                return ev.time - fixations.Peek().time;
            }
            else if (type == DataTypes.NO_PENDING_ITEMS) {
                break;
            }
        }

        latest = null;
        edfTrigger = SessionTrigger.NoTrigger;
        return 0;
    }

    /// <summary>
    /// Positions the robot as stated in the Session file.
    /// </summary>
    /// <param name="robot">Transfrom of the robot to move</param>
    /// <param name="reader">Session data of the Object</param>
    private void MoveRobotTo(Transform robot, SessionData reader) {
        RobotMovement.MoveRobotTo(robot, reader.config);
        cueController.UpdatePosition(robot);
    }

    private void SaveScreen(Camera cam, string filename) {
        Texture2D tex = new Texture2D(cam.pixelWidth, cam.pixelHeight, TextureFormat.RGB24, false);
        tex.ReadPixels(cam.pixelRect, 0, 0);
        tex.Apply();
        byte[] bytes = tex.EncodeToPNG();
        Destroy(tex);

        File.WriteAllBytes(filename, bytes);
    }
}

