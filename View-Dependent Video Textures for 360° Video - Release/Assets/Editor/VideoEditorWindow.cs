using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace RenderHeads.Media.AVProVideo
{
    public class VideoEditorWindow : EditorWindow
    {

        public static float ClipHeight = 30f;
        public static float SubClipHeight = 30f;
        public static float SubClipVertBuffer = 10f;
        public static float ClipVertBuffer = 35f;
        public static float SettingsHeight = 200;
        public static float ScrollBarHeight = 30f;
        public static int PIXELS_PER_SEC = 100;
        public static float LINE_INTERVAL_SEC = 5;
        public Rect windowRect;  // Standard size when not placed on timeline.
        public static int TEXT_FONT_SIZE = 36;

        [SerializeField]
        private List<Rect> windowRects = new List<Rect>();

        private static MediaPlayer _player;

        [SerializeField]
        private List<Clip> _clips = new List<Clip>();

        [SerializeField]
        private List<int> insideTimelineClipInd;
        private bool timelineDirty = true;
        private string currentFilePath;
        private string startTimeStr;
        private string endTimeStr;
        private Vector2 scrollPos;
        private Rect timelineRect;
        private Rect timelineViewRect;
        private static Texture2D timelineTex;
        private static Texture2D fileTex;
        private static Texture2D tmpEquirectTex;
        private static Texture2D CurViewTex;
        private static Texture2D nonCurViewTex;
        private static Texture2D validFramesTex;
        private static Texture2D cutTex;
        private static Texture2D warningCutTex;
        private static Texture2D postProcessWarningTex;
        private static Color HOT_PINK = new Color(225f / 255f, 105f / 255f, 180f / 255f);
        private static Color DEEP_PINK = new Color(225f / 255f, 20f / 255f, 147f / 255f);
        private float timelineTextHeight = 70;
        private float previewTextHeight = 50;
        private Rect timelineBox;
        private float timelineHeight = 400f;
        private float timelineCushionHeight = 40f;
        private float PreviewCushionTop = 70f;
        private float PreviewCushionLeft = 30f;
        private float fileButtonHeight = 20f;
        private Vector3[] pixelBoundPoints;

        public bool stopApplication = true;

        public static string XMLInFilePath = "";
        public static string XMLOutFilepath = "";
        public static string RawMediaFilepath = "";
        public static float LoopArcHeight = 100f;

        public Scene MainScene;
        public GameObject _Sphere;
        public HeadTrack _headtrack;
        public PreviewMode _previewComponent;
        public MultiplePlayerControl _multimedia;

        public bool _showROITarget = true;
        public bool _showCut = true;
        public bool _collapseViews = false;
        public bool UpdateHeadTrackTimeline = false;

        Vector3[] lineSegments;
        Vector3[] timeIndicatorTri = new Vector3[3];  // Coordinates of triangular vertices.

        public Rect _ButtonsArea;

        public Vector2 lastMousePosition = new Vector2(-1, -1);
        public int selectedClipIndex = -1; // Used for keeping track of which clip is being dragged on timeline.

        public Camera HeadsetCamera;
        public Rect sceneRect;
        public Rect equirectRect;
        public RenderTexture HeadsetTexture;

        public int clipIndexSelecting = -1;

        [MenuItem("Window/Video Editor Window")]
        static void Init()
        {
            // Get existing open window or if none, make a new one:
            VideoEditorWindow window = (VideoEditorWindow)EditorWindow.GetWindow(typeof(VideoEditorWindow));
            window.autoRepaintOnSceneChange = true;
            window.Show();
        }

        static void InitWindow()
        {
            Debug.Log("Inside InitWindow");

            // Get existing open window or if none, make a new one:
            VideoEditorWindow window = (VideoEditorWindow)EditorWindow.GetWindow(typeof(VideoEditorWindow));
            window.autoRepaintOnSceneChange = true;
            window.Show();
        }

        public static VideoEditorWindow Instance
        {
            get; private set;
        }

        public static bool IsOpen
        {
            get { return Instance != null; }
        }

        public static void ShowWindow()
        {
            GetWindow<VideoEditorWindow>();
        }

        public void Awake()
        {
            HeadsetCamera = Camera.main;
        }


        private static void getTextures()
        {
            Color[] cols;

            VideoEditorWindow window = EditorWindow.GetWindow<VideoEditorWindow>(false, "Video Editor Window");

            timelineTex = new Texture2D((int)window.position.width, (int)window.position.height, TextureFormat.RGBA32, false);
            if (window.position.width > 0 && window.position.height > 0)
            {
                cols = timelineTex.GetPixels();
                for (int i = 0; i < cols.Length; ++i)
                {
                    cols[i] = new Color(162f / 255f, 200f / 255f, 236f / 255f);
                }
                timelineTex.SetPixels(cols);
                timelineTex.Apply();

                tmpEquirectTex = MakeTex((int)1, (int)1, Color.black);
                tmpEquirectTex.Apply();
            }

            CurViewTex = MakeTex((int)1, (int)1, HOT_PINK);
            CurViewTex.Apply();

            warningCutTex = MakeTex((int)1, (int)1, new Color(255f / 255f, 128f / 255f, 0f / 255f, 1f));  // Orange
            warningCutTex.Apply();

            cutTex = MakeTex((int)1, (int)1, new Color(76f / 255f, 0 / 255f, 153 / 255f, 1f));  // Purple
            cutTex.Apply();

            validFramesTex = MakeTex((int)1, (int)1, new Color(221f / 255f, 188f / 255f, 149f / 255f, 0.7f));
            validFramesTex.Apply();

            postProcessWarningTex = MakeTex((int)1, (int)1, new Color(0f / 255f, 255f / 255f, 255f / 255f, 1f));
            postProcessWarningTex.Apply();

            nonCurViewTex = MakeTex((int)1, (int)1, new Color(0f / 255f, 107f / 255f, 164f / 255f));
            nonCurViewTex.Apply();
        }

        string ReadMediaInfo(string mediaFile)  // AVPro media player doesn't have this info until it is playing. Use ffprobe to get info.
        {
            // Start the child process.
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            Debug.Log("Video path is " + mediaFile);
            startInfo.Arguments = "/C ffprobe -v error -select_streams v:0 -show_entries stream=duration -of default=noprint_wrappers=1:nokey=1 " + mediaFile;
            process.StartInfo = startInfo;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }

        void UpdateGameObjReferences()
        {
            MainScene = SceneManager.GetActiveScene();
            GameObject[] gameObjects = MainScene.GetRootGameObjects();

            foreach (GameObject gameobj in gameObjects)
            {
                if (gameobj.name == "360SphereVideo")
                {
                    foreach (Transform transform in gameobj.transform)
                    {
                        if (transform.gameObject.name == "Sphere")
                        {
                            _Sphere = transform.gameObject;
                            break;
                        }
                    }
                    break;
                }
            }

            PlayableClip[] playableClips = _Sphere.GetComponents<PlayableClip>();
            _player = _Sphere.GetComponent<MultiplePlayerControl>().Player;
            _headtrack = _Sphere.GetComponent<HeadTrack>();
            _previewComponent = _Sphere.GetComponent<PreviewMode>();
            _multimedia = _Sphere.GetComponent<MultiplePlayerControl>();

            foreach (Clip clip in _clips)
            {
                clip.GenerateAndUpdateComponents(_Sphere);
            }
        }

        void OnEnable()
        {
            Instance = this;
            UpdateGameObjReferences();
            _headtrack.videoSize = new Vector2(0, 0);
            _headtrack.videoFps = 0.0f;

            windowRect = new Rect(500f, 500f, 300, SettingsHeight);

            if (RawMediaFilepath == "" && _player != null)
            {
                currentFilePath = _player.m_VideoPath;
            }
            else
            {
                currentFilePath = RawMediaFilepath;
            }

            timelineRect = new Rect(0, position.height - timelineHeight - 20, position.width, timelineHeight * 2 + timelineCushionHeight);
            timelineViewRect = new Rect(0, 0, position.width * 10, timelineHeight + timelineCushionHeight);
            HeadsetCamera = Camera.main;
            if (HeadsetTexture != null)
            {
                HeadsetCamera.targetTexture = null;
                DestroyImmediate(HeadsetTexture);
            }

            HeadsetTexture = new RenderTexture((int)sceneRect.width,
                                               (int)sceneRect.height,
                                               (int)RenderTextureFormat.ARGB32);
            this.UpdateSceneRect();
            getTextures();

            UpdateTimelineLineInterval();

            timelineDirty = true;
            if (GetTimelineClips().Count > 0 && !stopApplication)
                EditorApplication.isPlaying = true;
        }

        void UpdateTimelineLineInterval()
        {
            float totalWidth = timelineViewRect.width;
            float totalSec = totalWidth / (float)PIXELS_PER_SEC;
            int num_lines = (int)(totalSec / LINE_INTERVAL_SEC);

            if (lineSegments == null || lineSegments.Length != num_lines)
            {
                lineSegments = new Vector3[num_lines * 2];

                for (int i = 0; i < num_lines; i++)
                {
                    float pixelX = i * PIXELS_PER_SEC * LINE_INTERVAL_SEC;

                    lineSegments[i * 2] = new Vector3(pixelX, 0, 0);
                    lineSegments[i * 2 + 1] = new Vector3(pixelX, timelineViewRect.height, 0);
                }
            }
        }

        int[] GetInsideTimelineSeq()
        {
            if (insideTimelineClipInd != null)
            {
                return insideTimelineClipInd.ToArray();
            }
            return null;
        }

        List<int> GetTimelineClips()
        {
            if (timelineDirty)
            {
                int[] timelineOrderBefore = GetInsideTimelineSeq();
                insideTimelineClipInd = new List<int>();

                List<int> tmp = _clips.Select((clip, index) => new { TimePos = clip.timelinePos, Index = index }).OrderBy(pair => pair.TimePos).Select(pair => pair.Index).ToList();
                foreach (int ind in tmp)
                {
                    if (_clips[ind].insideTimeline)
                    {
                        insideTimelineClipInd.Add(ind);
                    }
                }

                timelineDirty = false;

                int[] timelineOrderAfter = GetInsideTimelineSeq();
                if (timelineOrderBefore != null && timelineOrderAfter != null && !Enumerable.SequenceEqual(timelineOrderBefore, timelineOrderAfter) && EditorApplication.isPlaying)
                {
                    UpdateHeadTrackTimeline = true;
                }
            }
            return insideTimelineClipInd;
        }

        void SnapClipsTogether()
        {
            Event e = Event.current;
            if (e.button == 1)
            {
                return;
            }
            else
            {
                GetTimelineClips();

                if (insideTimelineClipInd.Count > 0)
                {
                    if (Mathf.Abs(_clips[insideTimelineClipInd[0]].timelinePos) > 1e-5f)
                    {
                        _clips[insideTimelineClipInd[0]].SetTimelinePos(0f);
                    }
                }
                for (int i = 1; i < insideTimelineClipInd.Count; i++)
                {
                    if (Mathf.Abs(_clips[insideTimelineClipInd[i]].timelinePos - _clips[insideTimelineClipInd[i - 1]].TimelineEndPos()) > 1e-5f)
                    {
                        _clips[insideTimelineClipInd[i]].SetTimelinePos(_clips[insideTimelineClipInd[i - 1]].TimelineEndPos());
                    }
                }

                if (UpdateHeadTrackTimeline)
                {
                    UpdateGameObjReferences();
                    _headtrack.RefreshTimeline();
                    UpdateHeadTrackTimeline = false;
                }
            }
        }

        void drawLines()
        {
            Color originalColor = GUI.color;
            GUI.color = Color.blue;
            GUIStyle timeLabelStyle = new GUIStyle("label");
            timeLabelStyle.fontSize = 18;
            timeLabelStyle.fontStyle = FontStyle.Bold;
            timeLabelStyle.normal.textColor = Color.blue;

            for (int i = 0; i < lineSegments.Length / 2; i++)
            {
                float labelPixelX = lineSegments[i * 2].x;
                GUI.Label(new Rect(labelPixelX, scrollPos.y, 50, 30), (i * LINE_INTERVAL_SEC).ToString(), timeLabelStyle);  // GUI.label is faster to render than Handles.label. Using the latter would lag.
            }
            Handles.color = Color.white;
            Handles.DrawLines(lineSegments);
            GUI.color = originalColor;
        }

        void drawTimeIndicator()
        {
            _headtrack = _Sphere.GetComponent<HeadTrack>();  // Refresh pointer to HeadTrack component.

            if (_previewComponent.currentClipIndex >= 0 && _previewComponent.currentClipIndex < _clips.Count)
            {
                float currentTime = _multimedia.GetCurrentTime();

                // Find play time relative to start of clip.
                float relativeTime = currentTime - _clips[_previewComponent.currentClipIndex].start;
                float indicatorX = windowRects[_previewComponent.currentClipIndex].x + relativeTime * PIXELS_PER_SEC;
                float indicatorLineHalfWidth = 3f;
                EditorGUI.DrawRect(new Rect(indicatorX - indicatorLineHalfWidth, timelineRect.y, 2 * indicatorLineHalfWidth, timelineRect.height - 20f), Color.blue);

                // Draw triangle on top of time indicator.
                float triWidth = 15;
                float triHeight = 2 * triWidth;
                timeIndicatorTri[0].x = indicatorX - triWidth;
                timeIndicatorTri[0].y = timelineRect.y - triHeight;
                timeIndicatorTri[0].z = 0;
                timeIndicatorTri[1].x = indicatorX + triWidth;
                timeIndicatorTri[1].y = timelineRect.y - triHeight;
                timeIndicatorTri[1].z = 0;
                timeIndicatorTri[2].x = indicatorX;
                timeIndicatorTri[2].y = timelineRect.y - 1;
                timeIndicatorTri[2].z = 0;
                Handles.color = Color.blue;
                Handles.DrawAAConvexPolygon(timeIndicatorTri);
            }
        }

        void drawTimeline()
        {
            GUIStyle timelineTextStyle = new GUIStyle();
            timelineTextStyle.fontSize = TEXT_FONT_SIZE;
            timelineTextStyle.fontStyle = FontStyle.Bold;
            timelineTextStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(20, position.height - timelineHeight - 20 - timelineTextHeight, 100, timelineTextHeight), "Timeline", timelineTextStyle);

            EditorGUILayout.BeginHorizontal();
            timelineViewRect = new Rect(0, 0, position.width * 10, 2000f);
            if (!_previewComponent.collapseViews && _multimedia.IsPlaying())
            {
                int curView = _headtrack.GetVisibleViewStatus();
                Rect rect = GetClipRect(_headtrack.GetCurrentClipIndex(), curView);
                if (rect.y < timelineBox.y + rect.height)
                {
                    if (rect.yMax > timelineBox.y)
                    {
                        scrollPos.y -= rect.height * 2;
                    }
                    else
                    {
                        scrollPos.y -= timelineBox.y - rect.y + rect.height;
                    }
                }
                else if (rect.y > position.height - rect.height - ScrollBarHeight * 2)
                {
                    if (rect.y < position.height)
                        scrollPos.y += rect.height * 2;
                    else
                        scrollPos.y += rect.yMax - position.height + rect.height + ScrollBarHeight * 2;
                }
                scrollPos.y = Mathf.Clamp(scrollPos.y, 0.0f, timelineViewRect.height);
            }
            timelineRect = new Rect(0, position.height - timelineHeight - timelineCushionHeight, position.width, timelineHeight + timelineCushionHeight);
            scrollPos = GUI.BeginScrollView(timelineRect, scrollPos, timelineViewRect);

            if (timelineTex == null)
            {
                getTextures();
            }
            GUI.DrawTexture(new Rect(0, 0, position.width * 10, timelineViewRect.height), timelineTex, ScaleMode.StretchToFill);
            GUI.Box(new Rect(0, 0, position.width * 10, timelineViewRect.height), "");
            timelineBox = new Rect(0, position.height - timelineHeight - timelineCushionHeight, position.width, timelineRect.height);

            drawLines();
            EditorGUILayout.EndScrollView();
        }

        private static Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];

            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();

            return result;
        }

        void SaveEditToFile(bool tmp = false)
        {
            XmlSerializer writer = new XmlSerializer(_clips.GetType());
            StreamWriter file;
            if (!tmp)
            {
                file = new StreamWriter(XMLOutFilepath);
            }
            else
            {
                file = new StreamWriter(Path.Combine(Application.dataPath, "StreamingAssets", "Editor", "tmp.xml"));
            }
            writer.Serialize(file, _clips);
            file.Close();
        }

        void ResetComponents()
        {
            GatedClip[] existingSP = _Sphere.GetComponents<GatedClip>();
            foreach (GatedClip existSP in existingSP)
            {
                existSP.enabled = false;
                DestroyImmediate(existSP);
            }

            PlayableClip[] existingPlayableClip = _Sphere.GetComponents<PlayableClip>();
            foreach (PlayableClip clip in existingPlayableClip)
            {
                clip.enabled = false;
                DestroyImmediate(clip);
            }

            _clips = new List<Clip>();
            insideTimelineClipInd = new List<int>();
            windowRects = new List<Rect>();
        }

        void OpenEdit(bool tmp = false)
        {
            XmlDocument doc = new XmlDocument();
            string actualPath = Path.Combine(Application.dataPath, "StreamingAssets", "Editor", XMLInFilePath);
            Debug.Log("Reading in " + actualPath);
            if (!tmp)
            {
                doc.Load(actualPath);
            }
            else
            {
                doc.Load(Path.Combine(Application.dataPath, "Editor/tmp.xml"));
            }

            ResetComponents();


            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                string name = node["name"].InnerText;

                float start = float.Parse(node["start"].InnerText, CultureInfo.InvariantCulture.NumberFormat);
                float end = float.Parse(node["end"].InnerText, CultureInfo.InvariantCulture.NumberFormat);
                bool insideTimeline = bool.Parse(node["insideTimeline"].InnerText);
                float timelinePos = float.Parse(node["timelinePos"].InnerText, CultureInfo.InvariantCulture.NumberFormat);
                bool gated = node["Gated"].InnerText != null ? bool.Parse(node["Gated"].InnerText) : false;
                bool muteAudio = node["muteAudio"] != null ? bool.Parse(node["muteAudio"].InnerText) : false;

                if (node["sp"] != null)
                {
                    StoryPoint sp = StoryPoint.ReadFromXML(node["sp"], _Sphere);
                    AddClip(name, start, end, insideTimeline, timelinePos, gated, sp, muteAudio);
                }
                else
                {
                    Debug.Log("Reading in " + node.InnerText);
                    AddClip(name, start, end, insideTimeline, timelinePos, gated, null, muteAudio);
                }
            }
            timelineDirty = true;
            Debug.Log("Clips length: " + _clips.Count);
        }

        bool MatchCurrentView(int j, int f)
        {
            if (_headtrack == null)
            {
                UpdateGameObjReferences();
            }
            if (!_multimedia.IsPlaying())
            {
                return false;
            }
            if (!_headtrack.CurrentClipPlaying(_clips[j]))
            {
                return false;
            }
            if (_headtrack.PassedGate())
            {
                return false;
            }
            return f == _headtrack.GetVisibleView();
        }

        bool MatchCurrentLoop(int j, int f, LoopArc arc)
        {
            if (_headtrack == null)
            {
                UpdateGameObjReferences();
            }
            if (!EditorApplication.isPlaying)
            {
                return false;
            }
            if (!_headtrack.CurrentClipPlaying(_clips[j]))
            {
                return false;
            }
            if (!MatchCurrentView(j, f))
            {
                return false;
            }
            float time = _headtrack.GetCurrentTime();
            return _clips[j].sp.views[f].MatchCurrentLoop(arc, time);
        }

        void HandleTimelineEquirectClick()
        {
            Event e = Event.current;
            Rect timelineWithoutScroll = new Rect(timelineRect);
            timelineWithoutScroll.height -= 20;
            Debug.Log("Clicked " + e.mousePosition);

            if (timelineWithoutScroll.Contains(e.mousePosition))
            {
                Debug.Log("Clicked inside timeline.");
                float clickedX = e.mousePosition.x + scrollPos.x;
                float clickedTime = clickedX / PIXELS_PER_SEC;
                Debug.Log("Clicked x: " + clickedX + ". Clicked time: " + clickedTime);

                this.PlayScene();
                UpdateGameObjReferences();
                _headtrack.SeekRequest(clickedTime);

                if (clipIndexSelecting >= 0)
                {
                    float currentTime = _multimedia.GetTimeIndicatorTime();
                    _clips[clipIndexSelecting].sp.gateTime = currentTime - _clips[clipIndexSelecting].start;
                }
            }
            else if (equirectRect.Contains(e.mousePosition))
            {
                Debug.Log("Clicked inside Equirect: " + e.mousePosition + ". Equirect: " + equirectRect.ToString());
                float relativeX = (e.mousePosition.x - equirectRect.x) / equirectRect.width;
                float relativeY = (e.mousePosition.y - equirectRect.y) / equirectRect.height;
                Debug.Log("Relative x, y: " + relativeX + ", " + relativeY);

                if (clipIndexSelecting >= 0)
                {
                    Debug.Log("Selecting target for clip index: " + clipIndexSelecting + ": " + _clips[clipIndexSelecting]);
                    if (_previewComponent.equirectTexture != null)
                    {
                        Debug.Log("Target texture size: " + _previewComponent.equirectTexture.width + ", " + _previewComponent.equirectTexture.height);

                        // Draw selected target pixel on texture.
                        if (_clips[clipIndexSelecting].sp == null)
                        {
                            _clips[clipIndexSelecting].GenerateAndUpdateComponents(_Sphere);
                        }
                        if (!e.control)
                        {
                            Debug.Log("Control key not pressed. Setting target X and target Y");
                            _clips[clipIndexSelecting].sp.targetX = relativeX * _previewComponent.equirectTexture.width;
                            _clips[clipIndexSelecting].sp.targetY = relativeY * _previewComponent.equirectTexture.height;
                        }
                        else
                        {
                            Debug.Log("Control key pressed. Setting target Width and height");
                            Debug.Log("PreviewComponent width and height: " + _previewComponent.equirectTexture.width + ", " + _previewComponent.equirectTexture.height);
                            Debug.Log("Target x and y: " + _clips[clipIndexSelecting].sp.targetX + ", " + _clips[clipIndexSelecting].sp.targetY);
                            if (relativeX * _previewComponent.equirectTexture.width < _clips[clipIndexSelecting].sp.targetX)
                            {
                                Debug.Log("Second click x is less than first click. Wrapping around!");
                                _clips[clipIndexSelecting].sp.targetWidth = relativeX * _previewComponent.equirectTexture.width + _previewComponent.equirectTexture.width - _clips[clipIndexSelecting].sp.targetX;
                            }
                            else
                            {
                                Debug.Log("Second click is to the right of first click!");
                                _clips[clipIndexSelecting].sp.targetWidth = relativeX * _previewComponent.equirectTexture.width - _clips[clipIndexSelecting].sp.targetX;
                            }
                            _clips[clipIndexSelecting].sp.targetHeight = relativeY * _previewComponent.equirectTexture.height - _clips[clipIndexSelecting].sp.targetY;
                        }
                        float currentTime = _multimedia.GetTimeIndicatorTime();
                        _clips[clipIndexSelecting].sp.gateTime = currentTime - _clips[clipIndexSelecting].start;

                        Debug.Log("Set target pixel to " + _clips[clipIndexSelecting].sp.targetX + ", " + _clips[clipIndexSelecting].sp.targetY
                            + " with width " + _clips[clipIndexSelecting].sp.targetWidth + " and height " + _clips[clipIndexSelecting].sp.targetHeight
                            + " at time " + _clips[clipIndexSelecting].sp.gateTime);
                    }
                    else
                    {
                        Debug.Log("No texture available");
                    }
                }
                else
                {
                    Debug.Log("No clip currently selecting target.");
                }
            }
        }

        bool clickInsideTrack(int i, Vector2 mouse)
        {
            if (!_clips[i].HasViews())
            {
                return GetClipRect(i).Contains(mouse);
            }
            else
            {
                for (int f = 0; f < _clips[i].GetNumViews(); f++)
                {
                    if (GetClipRect(i, f).Contains(mouse))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        void DetectClickEvents()
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown)
            {
                if (equirectRect.Contains(e.mousePosition))
                {
                    HandleTimelineEquirectClick();
                    return;
                }
                bool clickInsideWindow = false;
                for (int i = 0; i < _clips.Count; i++)
                {
                    if (_clips[i].insideTimeline && (windowRects[i].Contains(e.mousePosition) || clickInsideTrack(i, e.mousePosition)))
                    {
                        clickInsideWindow = true;
                        Debug.Log("Clip number " + i + ": " + windowRects[i].ToString() + " contains mouse position " + new Vector2(e.mousePosition.x, e.mousePosition.y));
                        if (_clips[i].sp != null && _clips[i].sp._component != null)
                        {
                            // Debugging info.
                            Debug.Log("Gate frame is " + _clips[i].sp._component.GetGateFrame());
                            Debug.Log("Gate time is " + _clips[i].sp._component.gateTime);
                            Debug.Log("Fps: " + _headtrack.GetVideoFps());
                            Debug.Log("start time " + _clips[i].sp._component.startTime);
                            Debug.Log("Gate timecode is " + _clips[i].sp._component.GetGateTimecode());
                            Debug.Log("Clip rect is " + GetClipRect(i) + ". Position width is " + position.width + ". Scrollbar height: " + ScrollBarHeight);
                            Debug.Log("Current view is " + _headtrack.GetVisibleViewStatus());
                            Debug.Log("Clips count is " + _clips.Count + ". Playable clips count is : " + _Sphere.GetComponents<PlayableClip>().Length);
                            Debug.Log("Timelineview rect is " + timelineViewRect);
                            Debug.Log("Scroll pos: " + scrollPos + ". POsition width: " + position.width + ". POsition height: " + position.height + ". Timline box: " + timelineBox);
                        }
                        break;
                    }
                    else if (_ButtonsArea.Contains(e.mousePosition))
                    {
                        clickInsideWindow = true;
                        break;
                    }
                    else if (e.mousePosition.y > position.height - ScrollBarHeight)
                    {
                        clickInsideWindow = true;
                        break;
                    }
                }

                if (!clickInsideWindow)
                {
                    HandleTimelineEquirectClick();
                }
            }
        }

        void RenderHeadsetView()
        {
            GUIStyle previewTextStyle = new GUIStyle();
            previewTextStyle.fontSize = TEXT_FONT_SIZE;
            previewTextStyle.normal.textColor = Color.white;
            previewTextStyle.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(PreviewCushionLeft, fileButtonHeight + 20f, 100f, previewTextHeight - 20f), "Headset View", previewTextStyle);


            if (HeadsetTexture != null && HeadsetTexture.width > 0 && HeadsetTexture.height > 0)
            {
                GUI.DrawTexture(sceneRect, HeadsetTexture);
            }
        }

        bool HasROI()
        {
            int currentClipIndex = _previewComponent.currentClipIndex;
            if (EditorApplication.isPlaying && currentClipIndex >= 0 && currentClipIndex < _clips.Count && _clips[currentClipIndex].Gated)
            {
                if (_previewComponent.equirectTexture != null && _previewComponent.equirectTexture.width > 0 && _previewComponent.equirectTexture.height > 0)
                {
                    return true;
                }
            }
            return false;
        }

        List<Rect> GetROIRect()
        {
            List<Rect> targetRects = new List<Rect>();
            Debug.Assert(_previewComponent.currentClipIndex >= 0 && _clips[_previewComponent.currentClipIndex].sp != null);
            Debug.Assert(_previewComponent.equirectTexture != null && _previewComponent.equirectTexture.width > 0 && _previewComponent.equirectTexture.height > 0);
            Rect rawTargetRect = _clips[_previewComponent.currentClipIndex].sp.GetTargetRect();

            if (rawTargetRect.xMax <= _previewComponent.equirectTexture.width)
            {
                float newX = equirectRect.x + rawTargetRect.x / (float)_previewComponent.equirectTexture.width * equirectRect.width;
                float newY = equirectRect.y + rawTargetRect.y / (float)_previewComponent.equirectTexture.height * equirectRect.height;
                float newWidth = rawTargetRect.width / (float)_previewComponent.equirectTexture.width * equirectRect.width;
                float newHeight = rawTargetRect.height / (float)_previewComponent.equirectTexture.height * equirectRect.height;
                newWidth = Mathf.Max(newWidth, 5f);
                newHeight = Mathf.Max(newHeight, 5f);
                targetRects.Add(new Rect(newX, newY, newWidth, newHeight));
            }
            else
            {
                float newX = equirectRect.x + rawTargetRect.x / (float)_previewComponent.equirectTexture.width * equirectRect.width;
                float newY = equirectRect.y + rawTargetRect.y / (float)_previewComponent.equirectTexture.height * equirectRect.height;
                float newWidth = equirectRect.xMax - newX;
                float newHeight = rawTargetRect.height / (float)_previewComponent.equirectTexture.height * equirectRect.height;
                newWidth = Mathf.Max(newWidth, 5f);
                newHeight = Mathf.Max(newHeight, 5f);
                targetRects.Add(new Rect(newX, newY, newWidth, newHeight));

                newX = equirectRect.x;
                newY = equirectRect.y + rawTargetRect.y / (float)_previewComponent.equirectTexture.height * equirectRect.height;
                newWidth = (rawTargetRect.xMax - (float)_previewComponent.equirectTexture.width) / (float)_previewComponent.equirectTexture.width * equirectRect.width;
                newHeight = rawTargetRect.height / (float)_previewComponent.equirectTexture.height * equirectRect.height;
                newWidth = Mathf.Max(newWidth, 5f);
                newHeight = Mathf.Max(newHeight, 5f);
                targetRects.Add(new Rect(newX, newY, newWidth, newHeight));
            }
            return targetRects;
        }

        void DrawLineOnEquirect(Vector3 p0, Vector3 p1)
        {
            Vector3[] line = new Vector3[2];
            float width = 10f; // 15 for figure
            bool breakIntoTwo = false;
            Vector3 test;
            if (p0.x < p1.x)
            {
                test = p0;
                test.x += equirectRect.width;
                if ((test - p1).magnitude < (p0 - p1).magnitude)
                {
                    breakIntoTwo = true;
                }
            }
            else
            {
                test = p1;
                test.x += equirectRect.width;
                if ((test - p0).magnitude < (p1 - p0).magnitude)
                {
                    breakIntoTwo = true;
                }
            }

            if (!breakIntoTwo)
            {
                line[0] = p0;
                line[1] = p1;
                Handles.DrawAAPolyLine(width, line);
            }
            else
            {
                if (p0.x < p1.x)
                {
                    // P0 should be to the right of P1.
                    Vector3 tmp = p0;
                    p0 = p1;
                    p1 = tmp;
                }

                line[0] = p0;
                Vector3 wrap = p1;
                wrap.x += equirectRect.width;
                float slope = (wrap.y - line[0].y) / (wrap.x - line[0].x);
                float dy = slope * (equirectRect.xMax - line[0].x);
                line[1] = p1;
                line[1].x = equirectRect.xMax;
                line[1].y = line[0].y + dy;
                Handles.DrawAAPolyLine(width, new Vector3[] { line[0], line[1] });

                wrap = p0;
                wrap.x -= equirectRect.width;
                line[1] = p1;
                slope = (line[1].y - wrap.y) / (line[1].x - wrap.x);
                line[0] = p0;
                line[0].x = equirectRect.xMin;
                dy = slope * (line[1].x - equirectRect.xMin);
                line[0].y = line[1].y - dy;
                Handles.DrawAAPolyLine(width, new Vector3[] { line[0], line[1] });
            }
        }

        void DrawViewportBoundsOnEquirect()
        {
            if (!EditorApplication.isPlaying || _previewComponent.equirectTexture == null || _previewComponent.equirectTexture.width == 0 || _previewComponent.equirectTexture.height == 0)
            {
                return;
            }
            Vector2[] pixelBounds = _headtrack.GetViewportPixels();  // Top, right, bottom, left.
            Debug.Assert(pixelBounds.Length > 0);
            Vector2 videoRes = _headtrack.GetVideoDimensions();
            if (pixelBoundPoints == null || pixelBoundPoints.Length == 0)
            {
                pixelBoundPoints = new Vector3[pixelBounds.Length];
            }

            for (int i = 0; i < pixelBounds.Length; i++)
            {
                pixelBoundPoints[i].x = equirectRect.x + pixelBounds[i].x / (float)videoRes.x * equirectRect.width;
                pixelBoundPoints[i].y = equirectRect.y + pixelBounds[i].y / (float)videoRes.y * equirectRect.height;
                pixelBoundPoints[i].z = 0;
            }

            Handles.color = DEEP_PINK;
            if (pixelBoundPoints != null && pixelBoundPoints.Length > 0)
            {
                for (int i = 0; i < pixelBoundPoints.Length - 1; i++)
                {
                    // Draw direct lines between corners of the viewport mapped onto the equirect.
                    DrawLineOnEquirect(pixelBoundPoints[i], pixelBoundPoints[i + 1]);
                }
            }
        }

        void RenderEquirectView()
        {
            GUIStyle previewTextStyle = new GUIStyle();
            previewTextStyle.fontSize = TEXT_FONT_SIZE;
            previewTextStyle.normal.textColor = Color.white;
            previewTextStyle.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(equirectRect.x, fileButtonHeight + 20f, 100f, previewTextHeight - 20f), "Equirectangular View", previewTextStyle);

            Rect flippedRect = new Rect(equirectRect);
            flippedRect.y += flippedRect.height;
            flippedRect.height = -flippedRect.height;
            if (_previewComponent.equirectTexture != null && _previewComponent.equirectTexture.width > 0 && _previewComponent.equirectTexture.height > 0)
            {
                UpdateGameObjReferences();
                float currentTime = _headtrack.GetCurrentTime();

                if (clipIndexSelecting >= 0)
                {
                    GUI.DrawTexture(flippedRect, _previewComponent.GetEquirectTexture(false, null, currentTime));
                    List<Rect> targetRect = GetROIRect();
                    foreach (Rect rect in targetRect)
                        GUI.DrawTexture(rect, _previewComponent.GetSingleROITexture(), ScaleMode.StretchToFill);
                }
                else
                {
                    GUI.DrawTexture(flippedRect, _previewComponent.GetEquirectTexture(false, null, currentTime));
                }
            }
            else
            {
                GUI.DrawTexture(flippedRect, tmpEquirectTex, ScaleMode.StretchToFill);
            }
            UpdateGameObjReferences();
            DrawViewportBoundsOnEquirect();
            if (_previewComponent.showROI && this.HasROI() && clipIndexSelecting < 0)
            {
                List<Rect> targetRect = GetROIRect();

                // If target rect is valid, fill it in with target texture.
                foreach (Rect rect in targetRect)
                    GUI.DrawTexture(rect, _previewComponent.GetSingleROITexture(), ScaleMode.StretchToFill);
            }
        }

        public void Update()
        {
            if (HeadsetCamera == null || _previewComponent == null)
            {
                UpdateGameObjReferences();
            }
            this.UpdateSceneRect();
            if (HeadsetCamera != null && HeadsetTexture.width > 0 && HeadsetTexture.height > 0)
            {
                HeadsetCamera = Camera.main;
                HeadsetCamera.targetTexture = HeadsetTexture;
                HeadsetCamera.Render();
                HeadsetCamera.targetTexture = null;
            }
            else
            {
                Debug.Log("NOT RENDERING headset texture");
            }
  
            if (EditorApplication.isPlaying && _previewComponent.equirectTexture == null)
            {
                _previewComponent = _Sphere.GetComponent<PreviewMode>();
            }
        }

        void UpdateHeadsetRect()
        {
            int desiredWidth = (int)(position.width / 3);  // Max width.
            int desiredHeight = (int)(position.height - timelineRect.height - fileButtonHeight - timelineTextHeight - previewTextHeight);  // Max height.

            HeadsetCamera = Camera.main;
            float aspect = Camera.main.aspect;
            int desiredWidthHeight = (int)(desiredWidth / aspect);  // Width if we go with max height.
            int desiredHeightWidth = (int)(desiredHeight * aspect);  // Height if we go with max width.

            int actualHeight, actualWidth;
            if (desiredWidthHeight <= desiredHeight)
            {
                actualHeight = desiredWidthHeight;
                actualWidth = desiredWidth;
            }
            else
            {
                actualHeight = desiredHeight;
                actualWidth = desiredHeightWidth;
            }
            sceneRect = new Rect(PreviewCushionLeft, PreviewCushionTop + fileButtonHeight, actualWidth, actualHeight);

            if (HeadsetTexture == null ||
                HeadsetTexture.width != sceneRect.width ||
                HeadsetTexture.height != sceneRect.height)
            {
                HeadsetTexture = new RenderTexture((int)sceneRect.width,
                                                    (int)sceneRect.height,
                                                    (int)RenderTextureFormat.ARGB32);
            }
        }

        void UpdateEquirectRect()
        {
            if (_previewComponent.equirectTexture == null)
            {
                equirectRect = new Rect(sceneRect);
                equirectRect.x = sceneRect.xMax + 20f;
                return;
            }

            int desiredWidth = (int)(position.width - equirectRect.x);  // Max width.
            int desiredHeight = (int)(position.height - timelineRect.height - fileButtonHeight - timelineTextHeight - previewTextHeight);  // Max height.

            float equirectAspect = (float)_previewComponent.equirectTexture.width / (float)_previewComponent.equirectTexture.height;

            int desiredWidthHeight = (int)(desiredWidth / equirectAspect);  // Height if we go with max width.
            int desiredHeightWidth = (int)(desiredHeight * equirectAspect);  // Width if we go with max height.

            int actualHeight, actualWidth;
            if (desiredWidthHeight <= desiredHeight)
            {
                actualHeight = desiredWidthHeight;
                actualWidth = desiredWidth;
            }
            else
            {
                actualHeight = desiredHeight;
                actualWidth = desiredHeightWidth;
            }

            equirectRect = new Rect(sceneRect.xMax + 20f, PreviewCushionTop + fileButtonHeight, actualWidth, actualHeight);
            Debug.Assert(Mathf.Abs(Mathf.Abs(equirectRect.width) / Mathf.Abs(equirectRect.height) - equirectAspect) < 0.05f);  // Preview texture's aspect ratio should be equal to camera aspect ratio.
        }

        void UpdateSceneRect()
        {
            this.UpdateHeadsetRect();
            this.UpdateEquirectRect();
        }

        void PlayScene()
        {
            if (!EditorApplication.isPlaying && !stopApplication)
            {
                foreach (Clip clip in _clips)
                {
                    clip.GenerateAndUpdateComponents(_Sphere);
                }
                EditorUtility.SetDirty(this);
                EditorApplication.isPlaying = true;

                UpdateGameObjReferences();
                CheckAudio();
            }
        }

        void StopScene()
        {
            if (EditorApplication.isPlaying)
            {
                stopApplication = true;
                EditorApplication.isPlaying = false;
            }
        }

        public void CheckAudio()
        {
            if (_Sphere.GetComponent<AudioSource>() == null || _Sphere.GetComponent<AudioSource>().clip == null)
            {
                AudioSource audioSource = _Sphere.AddComponent<AudioSource>();
                string clipName = Path.GetFileNameWithoutExtension(_multimedia.Player.m_VideoPath);
                AudioClip audioClip = Resources.Load<AudioClip>(clipName);
                audioSource.clip = audioClip;
            }
        }

        void OnGUI()
        {
            this.UpdateSceneRect();
            DetectClickEvents();
            VideoEditorWindow[] windows = Resources.FindObjectsOfTypeAll<VideoEditorWindow>();

            GUIStyle upperButtonStyle = new GUIStyle("button");
            upperButtonStyle.normal.textColor = Color.white;
            upperButtonStyle.fontSize = 14;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Edit", upperButtonStyle, GUILayout.Width(100), GUILayout.Height(fileButtonHeight)))
            {
                string selectedFile = EditorUtility.OpenFilePanel(
                    "Open Edit",
                    Path.Combine(Application.dataPath, "StreamingAssets", "Editor"),
                    "xml");
                if (selectedFile != "")
                {
                    XMLInFilePath = Path.GetFileName(selectedFile);
                    OpenEdit();
                    XMLInFilePath = "";
                }
            }

            if (GUILayout.Button("Save Edit", upperButtonStyle, GUILayout.Width(100), GUILayout.Height(fileButtonHeight)))
            {
                String timeStamp = DateTime.Now.ToShortDateString();
                string selectedFile = EditorUtility.SaveFilePanel(
                    "Save edit as XML",
                    Path.Combine(Application.dataPath, "StreamingAssets", "Editor"),
                    timeStamp + ".xml",
                    "xml");
                if (selectedFile != "")
                {
                    XMLOutFilepath = selectedFile;
                    SaveEditToFile();
                }
            }

            currentFilePath = EditorGUILayout.TextField("File: ", currentFilePath, GUILayout.Height(fileButtonHeight));
            startTimeStr = EditorGUILayout.TextField("Start: ", startTimeStr, GUILayout.Height(fileButtonHeight), GUILayout.Width(300));
            endTimeStr = EditorGUILayout.TextField("End: ", endTimeStr, GUILayout.Height(fileButtonHeight), GUILayout.Width(300));

            if (GUILayout.Button("Add Clip", upperButtonStyle, GUILayout.Width(100), GUILayout.Height(fileButtonHeight)))
            {
                this.AddClip(currentFilePath, startTimeStr, endTimeStr);
            }
            EditorGUILayout.EndHorizontal();

            UpdateTimelineLineInterval();
            DrawPlayButton();
            drawTimeline();

            // Draw normal clips.
            CheckForTimelineClips();
            DrawClipsOutsideTimeline();

            // Create SceneView inside window.
            RenderHeadsetView();
            RenderEquirectView();

            if (GetTimelineClips().Count > 0)
            {
                this.PlayScene();
            }
        }

        void DrawPlayButton()
        {
            _ButtonsArea = new Rect(position.width - 1000, position.height - timelineHeight - 20 - timelineTextHeight, 1000, 40);
            GUILayout.BeginArea(_ButtonsArea);
            GUILayout.BeginHorizontal();

            GUIStyle originalStyle = new GUIStyle(GUI.skin.button);
            GUIStyle buttonsTextStyle = new GUIStyle(GUI.skin.button);
            buttonsTextStyle.fontSize = 20;
            buttonsTextStyle.fontStyle = FontStyle.Bold;
            buttonsTextStyle.normal.textColor = Color.white;

            if (GUILayout.Button("Play", buttonsTextStyle, GUILayout.Height(timelineTextHeight - 20)))
            {
                Debug.Log("Clicked play button");
                stopApplication = false;
                UpdateGameObjReferences();
                _headtrack.SetPlayMode(true);
                this.PlayScene();
            }

            if (GUILayout.Button("Pause", buttonsTextStyle, GUILayout.Height(timelineTextHeight - 20)))
            {
                this.PlayScene();
                _headtrack.SetPlayMode(false);
            }

            if (GUILayout.Button("Stop", buttonsTextStyle, GUILayout.Height(timelineTextHeight - 20)))
            {
                this.StopScene();
            }

            GUIStyle buttonsAreaTextStyle = "toggle";
            buttonsAreaTextStyle.fontSize = 20;
            buttonsAreaTextStyle.fontStyle = FontStyle.Bold;
            buttonsAreaTextStyle.normal.textColor = Color.white;

            _showROITarget = GUILayout.Toggle(_showROITarget, "Show ROI", buttonsAreaTextStyle, GUILayout.Height(timelineTextHeight - 20));
            _previewComponent.showROI = _showROITarget;

            _collapseViews = GUILayout.Toggle(_collapseViews, "Collapse Views", buttonsAreaTextStyle, GUILayout.Height(timelineTextHeight - 20));
            if (_collapseViews != _previewComponent.collapseViews)
            {
                RedrawLoopArcs();
                _previewComponent.collapseViews = _collapseViews;
            }

            _showCut = GUILayout.Toggle(_showCut, "Show Graph Cut", buttonsAreaTextStyle, GUILayout.Height(timelineTextHeight - 20));
            _previewComponent.showCut = _showCut;

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.skin.button = originalStyle;  // Revert back to original style.
        }

        void CheckForTimelineClips()
        {
            for (int i = 0; i < _clips.Count; i++)
            {
                bool overlapping = OverLapsTimeline(windowRects[i]);
                if (!_clips[i].insideTimeline && overlapping)
                {
                    _clips[i].SetInsideTimeline(overlapping, (windowRects[i].x + scrollPos.x) / PIXELS_PER_SEC, _Sphere);
                    timelineDirty = true;
                }
                else if (_clips[i].insideTimeline && !overlapping)
                {
                    Debug.Log("Clip " + i + " is changing from inside timeline to outside timeline.");
                    float originalX = windowRects[i].x + scrollPos.x;
                    windowRects[i] = new Rect(originalX, windowRects[i].y, windowRects[i].width, windowRects[i].height);
                    _clips[i].SetInsideTimeline(overlapping, -1f, _Sphere);
                    timelineDirty = true;
                }
            }
        }

        void DrawClipsOutsideTimeline()
        {
            BeginWindows();
            Event e = Event.current;
            SnapClipsTogether();

            GUIStyle windowLabelFontStyle = new GUIStyle("window");
            windowLabelFontStyle.fontSize = 20;
            windowLabelFontStyle.fontStyle = FontStyle.Bold;
            windowLabelFontStyle.normal.textColor = Color.white;

            for (int i = 0; i < _clips.Count; i++)
            {
                Rect newRect = new Rect(windowRects[i]);
                if (_clips[i].insideTimeline)
                {
                    // Move clip to fit within timeline.
                    if (!_previewComponent.collapseViews)
                        newRect.y = timelineBox.y + ClipVertBuffer * 2 + ClipHeight + (SubClipVertBuffer + SubClipHeight) * (StoryPoint.NUM_VIEWS - 1) - scrollPos.y;
                    else
                        newRect.y = timelineBox.y + ClipVertBuffer * 2 + ClipHeight - scrollPos.y;

                    if (newRect.y < timelineBox.y + ClipVertBuffer)
                    {
                        newRect.y = timelineBox.y + ClipVertBuffer;
                    }

                    // How tall settings box can be.
                    float SETTINGS_BOX_HEIGHT = 500f;
                    newRect.height = Mathf.Clamp(timelineBox.yMax - 20f - newRect.y, 100f, SETTINGS_BOX_HEIGHT);
                    if (newRect.xMax - scrollPos.x > timelineBox.xMax - 20f)
                    {
                        newRect.xMax = timelineBox.xMax - 20f + scrollPos.x;
                    }

                    // Re-adjust window width to be proportional to length of clip.
                    newRect.width = _clips[i].Duration() * PIXELS_PER_SEC;

                    if (newRect.xMax > timelineRect.width - ScrollBarHeight)
                    {
                        if (timelineRect.width - ScrollBarHeight > newRect.x)
                            newRect.xMax = Math.Min(newRect.xMax, timelineRect.width - ScrollBarHeight);
                    }

                    if (windowRects[i].Contains(e.mousePosition) && e.button == 1)
                    {
                        if (_clips[i].insideTimeline)
                        {
                            _clips[i].SetInsideTimeline(_clips[i].insideTimeline, (windowRects[i].x + scrollPos.x) / PIXELS_PER_SEC, _Sphere);
                            timelineDirty = true;
                        }
                    }
                    // Implement clicking and dragging of clip previews (not built into GUI.boxes in Unity).
                    else if (GetClipRect(i).Contains(e.mousePosition) && e.button == 1 && e.type == EventType.MouseDown && e.type != EventType.MouseDrag)
                    {
                        // Mouse click.
                        lastMousePosition = e.mousePosition;
                        selectedClipIndex = i;
                    }
                    else if (selectedClipIndex >= 0 && e.button == 1 && e.type == EventType.MouseDrag)
                    {
                        // Mouse drag. Only drag the clip that is selected when clicked.
                        if (selectedClipIndex == i)
                        {
                            if (lastMousePosition.x >= 0 && lastMousePosition.y >= 0)
                            {
                                newRect.x += e.mousePosition.x - lastMousePosition.x;
                                lastMousePosition = e.mousePosition;
                                _clips[i].SetInsideTimeline(_clips[i].insideTimeline, (newRect.x + scrollPos.x) / PIXELS_PER_SEC, _Sphere);
                                timelineDirty = true;
                            }
                            else
                            {
                                lastMousePosition = e.mousePosition;
                            }
                        }
                    }
                    else if (e.type == EventType.MouseUp)
                    {
                        lastMousePosition = new Vector2(-1, -1);
                        selectedClipIndex = -1;  // Reset clip index.
                    }
                    else
                    {
                        newRect.x = _clips[i].timelinePos * PIXELS_PER_SEC - scrollPos.x;
                    }

                    // Reassign to window rects.
                    windowRects[i] = newRect;
                    if (selectedClipIndex != i)
                    {
                        DrawClipPreview(i);
                    }
                }
                else
                {
                    if (newRect.x < 0)
                    {
                        newRect.x = windowRects[i].x + scrollPos.x;
                    }
                    newRect.width = windowRect.width;
                    windowRects[i] = newRect;
                }
            }

            DrawArcs();
            drawTimeIndicator();

            if (selectedClipIndex >= 0)
            {
                if (_clips[selectedClipIndex].insideTimeline)
                {
                    DrawClipPreview(selectedClipIndex);
                }
                windowRects[selectedClipIndex] = GUI.Window(selectedClipIndex, windowRects[selectedClipIndex], DoSettingsWindow, _clips[selectedClipIndex].GetWindowName(), windowLabelFontStyle);
            }
            for (int i = 0; i < _clips.Count; i++)
            {
                Rect newRect = new Rect(windowRects[i]);
                if (selectedClipIndex != i)
                {
                    windowRects[i] = GUI.Window(i, windowRects[i], DoSettingsWindow, _clips[i].GetWindowName(), windowLabelFontStyle);
                }
            }
            EndWindows();
            DrawGateTimes();
        }

        bool OverLapsTimeline(Rect clipRect)
        {
            Rect overlapRange = new Rect(timelineBox);
            overlapRange.y += ClipHeight / 2;
            return clipRect.yMax > overlapRange.y;
        }

        void AddClip(string name, float startTime, float endTime, bool insideTimeline, float timelinePos, bool gated, StoryPoint sp = null, bool newMuteAudio = false)
        {
            int prevLength = _clips.Count;
            _clips.Add(new Clip(name, startTime, endTime, insideTimeline, timelinePos, windowRects.Count, newMuteAudio));

            Rect newRect = new Rect(windowRect);

            if (_clips[prevLength].insideTimeline)
            {
                newRect.y = timelineBox.y - scrollPos.y;
                Debug.Log("Timelinebox is " + timelineBox.y + ". Scroll pos: " + scrollPos.y + ". New rect: " + newRect.y);

                // Re-adjust window width to be proportional to length of clip.
                newRect.width = _clips[prevLength].Duration() * PIXELS_PER_SEC;
            }

            _clips[prevLength].Gated = gated;

            if (sp != null)
            {
                _clips[prevLength].ReadSP(sp, _Sphere);
            }

            if (!_clips[prevLength].Gated)
            {
                _clips[prevLength].SetUpClipComponent(_Sphere);
            }
            windowRects.Add(newRect);
            _clips[_clips.Count - 1].GenerateAndUpdateComponents(_Sphere);
        }

        void AddClip(string name, string startTimeStr, string endTimeStr)
        {
            int prevLength = _clips.Count;
            float startTime = float.Parse(startTimeStr, CultureInfo.InvariantCulture.NumberFormat);
            float endTime = float.Parse(endTimeStr, CultureInfo.InvariantCulture.NumberFormat);

            _clips.Add(new Clip(name, startTime, endTime, false, -1f, prevLength));
            windowRects.Add(windowRect);
        }

        void DrawGateTimes()
        {
            if (_multimedia == null || _headtrack == null)
            {
                UpdateGameObjReferences();
            }
            for (int i = 0; i < _clips.Count; i++)
            {
                if (!_clips[i].HasViews())
                {
                    float timelineGateTime = _clips[i].GetGateTimeOnTimeline();
                    if (timelineGateTime < 0f)
                    {
                        continue;
                    }
                    Vector3[] points = _clips[i].sp.GetGateTimePoints(GetClipRect(i), PIXELS_PER_SEC);
                    Handles.color = Color.red;
                    Handles.DrawAAPolyLine(3f, points);
                }
                else
                {
                    float timelineGateTime = _clips[i].GetGateTimeOnTimeline();
                    if (timelineGateTime < 0f)
                    {
                        continue;
                    }
                    for (int f = 0; f < _clips[i].GetNumViews(); f++)
                    {
                        if (!withinTimelineVertical(i, f))
                            continue;
                        int gateFrame = (int)(_clips[i].sp.gateTime * _headtrack.GetVideoFps());
                        if (_previewComponent.showCut && _clips[i].sp.HasCut() && !_clips[i].isTargetView(f) && _clips[i].sp.cut[f].frames.Contains(gateFrame))
                        {
                            continue;
                        }

                        if (f > 0 && _previewComponent.collapseViews)
                        {
                            continue;
                        }

                        if (GetClipRect(i, f).y <= position.height - ScrollBarHeight)
                        {
                            Vector3[] points = _clips[i].sp.GetGateTimePoints(GetClipRect(i, f), PIXELS_PER_SEC);
                            Handles.color = _clips[i].isTargetView(f) ? Color.green : Color.red;
                            Handles.DrawAAPolyLine(8f, points);
                        }
                    }
                }
            }
        }

        bool withinTimelineVertical(int i, int f)
        {
            Rect rect = GetClipRect(i, f);
            if (rect.y < timelineBox.y || rect.yMax > timelineBox.yMax)
            {
                return false;
            }
            return true;
        }

        void RedrawLoopArcs()
        {
            for (int i = 0; i < _clips.Count; i++)
            {
                if (_clips[i].HasViews())
                {
                    for (int f = 0; f < _clips[i].GetNumViews(); f++)
                    {
                        _clips[i].MarkLoopArcsDirty(f);
                    }
                }
            }
        }

        void DrawArcs()
        {
            for (int i = 0; i < _clips.Count; i++)
            {
                _headtrack = _Sphere.GetComponent<HeadTrack>();

                if (_clips[i].HasViews())
                {
                    for (int f = 0; f < _clips[i].GetNumViews(); f++)
                    {
                        List<LoopArc> loopArcs = _clips[i].GetViewLoopArcs(f);

                        if (!_clips[i].Gated)
                        {
                            continue;
                        }
                        if (_clips[i].isTargetView(f))
                        {
                            continue;
                        }
                        if (!_previewComponent.collapseViews && !withinTimelineVertical(i, f))
                        {
                            continue;
                        }

                        if (loopArcs != null)
                        {
                            for (int j = 0; j < loopArcs.Count; j++)
                            {
                                Rect baseRectOfArcs = _previewComponent.collapseViews ? GetClipRect(i, 0) : GetClipRect(i, f);
                                Vector3[] points = loopArcs[j].GenerateArcPoints(baseRectOfArcs, PIXELS_PER_SEC, LoopArcHeight);
                                bool matchLoop = this.MatchCurrentLoop(i, f, loopArcs[j]);

                                Handles.color = matchLoop ? Color.magenta : Color.red;
                                float width = loopArcs[j].GetArcWidth();

                                if (points != null && points.Length > 0)
                                {
                                    Handles.DrawAAPolyLine(2f, points);

                                    Vector3[] arrow = loopArcs[j].GetArrow();
                                    Handles.DrawAAConvexPolygon(arrow);
                                }
                            }
                        }
                    }
                }
            }
        }

        Rect GetClipRect(int windowID, int viewNum = 0, int frameOffset = 0, int duration = -1)
        {
            Rect clipRect = new Rect(windowRects[windowID]);
            clipRect.y = timelineRect.y - scrollPos.y + ClipVertBuffer + 5;
            clipRect.x += 1;

            clipRect.width = _clips[windowID].Duration() * PIXELS_PER_SEC - 2;
            clipRect.height = ClipHeight;
            if (frameOffset > 0)
            {
                float sec = ((float)frameOffset) / _headtrack.GetVideoFps();
                clipRect.x += sec * PIXELS_PER_SEC;
            }
            if (duration != -1)
            {
                float sec = ((float)duration) / _headtrack.GetVideoFps();
                clipRect.width = sec * PIXELS_PER_SEC;
            }
            if (clipRect.xMax > timelineRect.width - ScrollBarHeight)
            {
                if (timelineRect.width - ScrollBarHeight > clipRect.x)
                    clipRect.xMax = Math.Min(clipRect.xMax, timelineRect.width - ScrollBarHeight);
            }
            if (viewNum == 0)
            {
                return clipRect;
            }

            Rect subclipRect = new Rect(clipRect);
            subclipRect.height = SubClipHeight;
            subclipRect.y = clipRect.yMax + (SubClipVertBuffer + SubClipHeight) * (viewNum - 1) + SubClipVertBuffer;
            if (subclipRect.y < position.height && subclipRect.yMax > position.height - ScrollBarHeight)
            {
                if (position.height - ScrollBarHeight > subclipRect.y)
                    subclipRect.yMax = position.height - ScrollBarHeight;
                else
                {
                    subclipRect.height = 0;
                }
            }

            return subclipRect;
        }

        Texture2D GetClipPreviewTexture(int trackNum, bool IsCurrentClip)
        {
            if (_multimedia == null)
            {
                UpdateGameObjReferences();
            }
            if (!EditorApplication.isPlaying)
            {
                return trackNum == 0 ? CurViewTex : nonCurViewTex;
            }
            else
            {
                int view = _headtrack.GetVisibleViewStatus();
                return view == trackNum && IsCurrentClip ? CurViewTex : nonCurViewTex;
            }
        }

        void DrawClipPreview(int windowID)
        {
            UpdateGameObjReferences();

            if (CurViewTex == null || cutTex == null || postProcessWarningTex == null || nonCurViewTex == null || warningCutTex == null || validFramesTex == null)
            {
                Debug.Log("Getting textures! ");
                getTextures();
            }

            Rect clipRect = GetClipRect(windowID);
            Rect DesRect = new Rect(clipRect);

            float margin = 5;
           
            // Rest of space for description.
            DesRect.height = DesRect.height * 3 / 4;
            DesRect.width = 3f / 4f * clipRect.width - 2 * margin;
            DesRect.x = clipRect.xMax - margin - DesRect.width;
            DesRect.y = clipRect.y + clipRect.height / 2 - DesRect.height / 2;
            DesRect.x = clipRect.x + margin;
            if (clipRect.y >= timelineBox.y)
            {
                GUI.DrawTexture(clipRect, GetClipPreviewTexture(0, windowID == _headtrack.GetCurrentClipIndex()), ScaleMode.StretchToFill);
            }

            if (_clips[windowID].HasViews() && _clips[windowID].sp != null && !_clips[windowID].isTargetView(0) && _clips[windowID].sp.HasCostMatrices())
            {
                List<Block> blocks = _clips[windowID].sp.GetValidBlocksGrayFrame(0);
                foreach (Block block in blocks)
                {
                    // Draws gray frames.
                    GUI.DrawTexture(GetClipRect(windowID, 0, block.start, block.end - block.start), validFramesTex, ScaleMode.StretchToFill);
                }
            }
            if (_previewComponent.showCut && _clips[windowID].HasViews() && _clips[windowID].sp != null && !_clips[windowID].isTargetView(0) && _clips[windowID].sp.HasCut())
            {
                List<Block> blocks = _clips[windowID].sp.GetCutTimeBlock(0);
                if (blocks != null)
                {
                    foreach (Block block in blocks)
                    {
                        if (block.postProcessWarning)
                        {
                            if (!block.penalized && clipRect.y >= timelineBox.y)
                            {
                                GUI.DrawTexture(GetClipRect(windowID, 0, block.start, block.end - block.start), cutTex, ScaleMode.StretchToFill);
                            }
                            else
                            {
                                if (clipRect.y >= timelineBox.y)
                                    GUI.DrawTexture(GetClipRect(windowID, 0, block.start, block.end - block.start), postProcessWarningTex, ScaleMode.StretchToFill);
                            }
                        }
                        else
                        {
                            GUI.DrawTexture(GetClipRect(windowID, 0, block.start, block.end - block.start), cutTex, ScaleMode.StretchToFill);
                        }
                    }
                }
            }

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.wordWrap = true;
            labelStyle.fontSize = 20;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.normal.textColor = Color.cyan;
            string des = _clips[windowID].GetClipPreviewDes();

            if (!_previewComponent.collapseViews && StoryPoint.NUM_VIEWS > 1)
            {
                des += " : V" + 0;
            }
            if (clipRect.y >= timelineBox.y)
            {
                GUI.Label(DesRect, des, labelStyle);
            }

            if (_previewComponent.collapseViews)
            {
                return;
            }

            for (int i = 0; i < StoryPoint.NUM_VIEWS - 1; i++)
            {
                Rect subclipRect = GetClipRect(windowID, i + 1);

                if (subclipRect.xMax < 0 || subclipRect.x > position.width || subclipRect.y < timelineBox.y)
                    continue;

                GUI.DrawTexture(subclipRect, GetClipPreviewTexture(i + 1, windowID == _headtrack.GetCurrentClipIndex()), ScaleMode.StretchToFill);

                if (_clips[windowID].HasViews() && _clips[windowID].sp != null && !_clips[windowID].isTargetView(i + 1) && _clips[windowID].sp.HasCostMatrices())
                {

                    List<Block> blocks = _clips[windowID].sp.GetValidBlocksGrayFrame(i + 1);
                    foreach (Block block in blocks)
                    {
                        // Draws gray frames.
                        GUI.DrawTexture(GetClipRect(windowID, i + 1, block.start, block.end - block.start), validFramesTex, ScaleMode.StretchToFill);
                    }
                }

                if (_previewComponent.showCut && _clips[windowID].HasViews() && _clips[windowID].sp != null && !_clips[windowID].isTargetView(i + 1) && _clips[windowID].sp.HasCut())
                {
                    List<Block> blocks = _clips[windowID].sp.GetCutTimeBlock(i + 1);
                    if (blocks != null)
                    {
                        foreach (Block block in blocks)
                        {

                            if (block.postProcessWarning)
                            {
                                if (!block.penalized)
                                {
                                    GUI.DrawTexture(GetClipRect(windowID, i + 1, block.start, block.end - block.start), cutTex, ScaleMode.StretchToFill);
                                }
                                else
                                {
                                    GUI.DrawTexture(GetClipRect(windowID, i + 1, block.start, block.end - block.start), postProcessWarningTex, ScaleMode.StretchToFill);
                                }
                            }
                            else
                            {
                                GUI.DrawTexture(GetClipRect(windowID, i + 1, block.start, block.end - block.start), cutTex, ScaleMode.StretchToFill);
                            }
                        }
                    }
                }

                Rect subclipDesRect = new Rect(DesRect);
                subclipDesRect.height = subclipRect.height * 3 / 4;
                subclipDesRect.y = subclipRect.y + subclipRect.height / 2 - subclipDesRect.height / 2;
                GUI.Label(subclipDesRect, _clips[windowID].GetClipPreviewDes() + " : V" + (i + 1), labelStyle);
            }
        }

        // Make the contents of the window
        void DoSettingsWindow(int windowID)
        {
            float rowHeight = 25f;
            float labelWidth = 350f;
            float buttonWidth = 200f;
            EditorGUILayout.BeginHorizontal();
            _clips[windowID].scrollPos = EditorGUILayout.BeginScrollView(_clips[windowID].scrollPos);
            EditorGUILayout.BeginVertical();

            int FIGUREFONT = 32;
            int NORMALFONT = 20;

            GUIStyle settingsToggleFontStyle = "toggle";
            settingsToggleFontStyle.fontSize = NORMALFONT;
            settingsToggleFontStyle.fontStyle = FontStyle.Bold;
            settingsToggleFontStyle.normal.textColor = Color.white;

            GUIStyle settingsLabelFontStyle = new GUIStyle("label");
            settingsLabelFontStyle.fontSize = NORMALFONT;
            settingsLabelFontStyle.fontStyle = FontStyle.Bold;
            settingsLabelFontStyle.normal.textColor = Color.white;

            GUIStyle settingsTextFieldFontStyle = new GUIStyle("textfield");
            settingsTextFieldFontStyle.fontSize = NORMALFONT;
            settingsTextFieldFontStyle.fontStyle = FontStyle.Normal;
            settingsTextFieldFontStyle.normal.textColor = Color.white;

            GUIStyle settingsButtonFontStyle = new GUIStyle("button");
            settingsButtonFontStyle.fontSize = NORMALFONT;
            settingsButtonFontStyle.fontStyle = FontStyle.Bold;
            settingsButtonFontStyle.normal.textColor = Color.white;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(" ", settingsLabelFontStyle, GUILayout.Height(25f));
            EditorGUILayout.EndHorizontal();

            string toggleValueBefore = _clips[windowID].Gated.ToString();
            string toggleValueAfter = GUILayout.Toggle(_clips[windowID].Gated, "Gated Clip?", settingsToggleFontStyle).ToString();
            if (toggleValueAfter != toggleValueBefore)
            {
                _clips[windowID].MarkStoryPoint(_Sphere);
            }

            toggleValueBefore = _clips[windowID].muteAudio.ToString();
            toggleValueAfter = GUILayout.Toggle(_clips[windowID].muteAudio, "Mute audio?", settingsToggleFontStyle).ToString();

            if (toggleValueBefore != toggleValueAfter)
            {
                _clips[windowID].ToggleMuteAudio(_Sphere);
            }

            if (_clips[windowID].Gated)
            {

                if (GUILayout.Button(_clips[windowID].GetSelectTargetLabel(), settingsButtonFontStyle, GUILayout.Width(buttonWidth), GUILayout.Height(rowHeight)))
                {
                    _clips[windowID].selectingTarget = !_clips[windowID].selectingTarget;

                    if (_clips[windowID].selectingTarget)
                    {
                        Debug.Log("Selecting target!");
                        UpdateGameObjReferences();
                        _headtrack.playMode = false;  // Pause video.
                        clipIndexSelecting = windowID;
                    }
                    else
                    {
                        clipIndexSelecting = -1;
                    }
                }

                _clips[windowID].sp.lookAt = GUILayout.Toggle(_clips[windowID].sp.lookAt, "Lookat Gate?", settingsToggleFontStyle);

                float originalLoopLength = _clips[windowID].sp.minLoopLengthSec;
                try
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Min Arc Length (sec): ", settingsLabelFontStyle, GUILayout.Width(labelWidth), GUILayout.Height(rowHeight));
                    _clips[windowID].sp.minLoopLengthSec = float.Parse(EditorGUILayout.TextField(_clips[windowID].sp.minLoopLengthSec.ToString(), settingsTextFieldFontStyle, GUILayout.Width(200), GUILayout.Height(rowHeight)), CultureInfo.InvariantCulture.NumberFormat);
                    EditorGUILayout.EndHorizontal();
                }
                catch (Exception)
                {
                    _clips[windowID].sp.minLoopLengthSec = originalLoopLength;
                }

                if (!Mathf.Approximately(_clips[windowID].sp.minLoopLengthSec, originalLoopLength))
                {
                    Debug.Log("Min loop length for clip " + windowID + " updated to " + _clips[windowID].sp.minLoopLengthSec);
                    _clips[windowID].sp.ComputeAllGrayFrames();
                }

                float originalPerceptualThres = _clips[windowID].sp.perceptualThreshold;
                try
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Perceptual Threshold: ", settingsLabelFontStyle, GUILayout.Width(labelWidth), GUILayout.Height(rowHeight));
                    _clips[windowID].sp.perceptualThreshold = float.Parse(EditorGUILayout.TextField(_clips[windowID].sp.perceptualThreshold.ToString(), settingsTextFieldFontStyle, GUILayout.Width(200), GUILayout.Height(rowHeight)), CultureInfo.InvariantCulture.NumberFormat);
                    EditorGUILayout.EndHorizontal();
                }
                catch (Exception)
                {
                    _clips[windowID].sp.perceptualThreshold = originalPerceptualThres;
                }

                if (!Mathf.Approximately(_clips[windowID].sp.perceptualThreshold, originalPerceptualThres))
                {
                    Debug.Log("Perceptual threshold for clip " + windowID + " updated to " + _clips[windowID].sp.perceptualThreshold);
                    _clips[windowID].sp.ComputeAllGrayFrames();
                }

                if (GUILayout.Button("Load Arc Costs", settingsButtonFontStyle, GUILayout.Width(buttonWidth), GUILayout.Height(rowHeight)))
                {
                    string selectedFolder = EditorUtility.OpenFolderPanel(
                        "Import Loops",
                        Path.Combine(Application.dataPath, "StreamingAssets", "Editor/"),
                        "json");
                    _clips[windowID].SetLoopsFolder(selectedFolder);
                }

                if (GUILayout.Button("Apply Gate", settingsButtonFontStyle, GUILayout.Width(buttonWidth), GUILayout.Height(rowHeight)))
                {
                    string selectedDirectory = EditorUtility.OpenFolderPanel(
                        "Import cut file",
                        Path.Combine(Application.dataPath, "StreamingAssets/", "Editor/"),
                        Path.Combine(Application.dataPath, "StreamingAssets/", "Editor/"));

                    if (Directory.Exists(selectedDirectory))
                    {
                        DirectoryInfo d = new DirectoryInfo(selectedDirectory);
                        string[] files = d.GetFiles().Select(x => x.ToString()).ToArray();
                        foreach (string f in files)
                        {
                            Debug.Log("File: " + f);
                            if (f.EndsWith(".xml"))
                            {
                                string parent = Directory.GetParent(f).ToString();
                                string edgeCostFile = Path.GetFileName(f);
                                string subpath = Path.Combine(parent, edgeCostFile);
                                _clips[windowID].SetEdgeCostFile(subpath, _Sphere);
                            }
                        }
                        foreach (string f in files)
                        {
                            if (f.EndsWith("allArcs.json"))
                            {
                                string parent = Directory.GetParent(f).ToString();
                                string bestArcFile = Path.GetFileName(f);
                                string subpath = Path.Combine(parent, bestArcFile);
                                _clips[windowID].SetAllArcsFile(subpath);
                            }
                        }

                        foreach (string f in files)
                        {
                            if (f.EndsWith("valid.json"))
                            {
                                string parent = Directory.GetParent(f).ToString();
                                string validArcFile = Path.GetFileName(f);
                                string subpath = Path.Combine(parent, validArcFile);
                                _clips[windowID].SetValidArcFile(subpath);
                            }
                        }

                        foreach (string f in files)
                        {
                            if (f.EndsWith("extraCosts.json"))
                            {
                                string parent = Directory.GetParent(f).ToString();
                                string extraCostsFile = Path.GetFileName(f);
                                string subpath = Path.Combine(parent, extraCostsFile);
                                _clips[windowID].SetExtraCostsFile(subpath);
                            }
                        }
                        foreach (string f in files)
                        {
                            if (f.EndsWith("cut.json"))
                            {
                                string parent = Directory.GetParent(f).ToString();
                                string cutFile = Path.GetFileName(f);
                                string subpath = Path.Combine(parent, cutFile);
                                _clips[windowID].SetCutFile(subpath);
                            }
                        }

                    }
                }

                string toggleBefore = _clips[windowID].sp.jumpImmediately.ToString();
                string toggleAfter = GUILayout.Toggle(_clips[windowID].sp.jumpImmediately, "Enable Forward Jumps?").ToString();

                if (toggleBefore != toggleAfter)
                {
                    _clips[windowID].ToggleJumpForward(_Sphere);
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Jump Perceptual Threshold: ", settingsLabelFontStyle, GUILayout.Width(labelWidth), GUILayout.Height(rowHeight));
                _clips[windowID].sp.jumpThreshold = float.Parse(EditorGUILayout.TextField(_clips[windowID].sp.jumpThreshold.ToString(), settingsTextFieldFontStyle, GUILayout.Width(200), GUILayout.Height(rowHeight)), CultureInfo.InvariantCulture.NumberFormat);
                EditorGUILayout.EndHorizontal();

                toggleBefore = _clips[windowID].sp.crossFadeAudio.ToString();
                toggleAfter = GUILayout.Toggle(_clips[windowID].sp.crossFadeAudio, "Cross-fade audio?").ToString();

                if (toggleBefore != toggleAfter)
                {
                    _clips[windowID].ToggleCrossFadeAudio(_Sphere);
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndHorizontal();

            GUI.DragWindow();
        }
    }
}