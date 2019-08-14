using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;

namespace RenderHeads.Media.AVProVideo
{

    public class HeadTrack : MonoBehaviour
    {
        public GameObject sphere;
        public bool playMode = false;

        private MultiplePlayerControl _multipleMedia;
        private MediaPlayer _player;
        private GatedClip[] _gatedClips;
        private PlayableClip[] _playableClips;
        private int _currentClipIndex = -1;
        private PreviewMode _previewMode;
        public bool initialized = false;
        public Vector3[] boundary;
        public Vector2[] viewportPixels;
        public float[] viewCenters = null;
        public Vector3[] viewportCoordToCheck = null;

        public Vector2 videoSize;
        public float videoFps;
        public static float _LOOP_TRANS_DURATION = 1f / 2f;

        private float currentTime;

        public static int BOUNDARY_NUM_SAMPLES = 41;
        public static int VIEWPORT_NUM_SAMPLES = 9;

        private int CurrentVisibleView = -1;

        public float GetCurrentTime()
        {
            return currentTime;
        }

        public float GetDisplayRate()
        {
            return _player.Info.GetVideoDisplayRate();
        }

        public Vector2 GetVideoDimensions()
        {
            if (_player == null)
            {
                RefreshTimeline();
            }
            if (Mathf.Approximately(this.videoSize.x, 0f) || Mathf.Approximately(this.videoSize.y, 0f))
            {
                Debug.Log("Computing Video Size...");
                videoSize = new Vector2(float.Parse(this.ReadMediaInfo("width")), float.Parse(this.ReadMediaInfo("height")));
                Debug.Log("Video size: " + videoSize);
            }
            return videoSize;
        }

        public float GetVideoFps()
        {
            if (_player == null)
            {
                RefreshTimeline();
            }
            if (Mathf.Approximately(videoFps, 0f))
            {
                Debug.Log("Computing Video Fps...");
                if (_player.Info != null)
                {
                    videoFps = _player.Info.GetVideoFrameRate();
                }
                else
                {
                    string framerate_str = ReadMediaInfo("r_frame_rate");
                    string[] framerate_arr = framerate_str.Split('/');
                    videoFps = float.Parse(framerate_arr[0]) / float.Parse(framerate_arr[1]);
                }
                Debug.Log("Video fps: " + videoFps);
            }
            return videoFps;
        }

        string ReadMediaInfo(string query)  // AVPro media player doesn't have this info until it is playing. Use ffprobe to get info.
        {
            // Start the child process.
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/C ffprobe -v error -select_streams v:0 -show_entries stream=" + query + " -of default=noprint_wrappers=1:nokey=1 \"" + Path.Combine(Application.streamingAssetsPath, _player.m_VideoPath) + "\"";
            process.StartInfo = startInfo;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output;
        }

        public void PlayVideo()
        {
            if (_multipleMedia != null)
            {
                _multipleMedia.Play();
            }
            else
            {
                Debug.Log("Player not initialized.");
            }
        }

        public void PauseVideo()
        {
            if (_multipleMedia != null)
            {
                _multipleMedia.Pause();
            }
            else
            {
                Debug.Log("Player not initialized.");
            }
        }

        public void SeekRequest(float sec)  // Sec on timeline.
        {
            _playableClips = GetComponents<PlayableClip>();
            Array.Sort(_playableClips);

            Debug.Log("Seek Request: " + sec);

            // Find the right clip.
            int requestedClipIndex = -1;
            for (int i = 0; i < _playableClips.Length - 1; i++)
            {
                if (sec >= _playableClips[i].timelinePos && sec < _playableClips[i + 1].timelinePos)
                {
                    requestedClipIndex = i;
                    break;
                }
            }

            int lastIndex = _playableClips.Length - 1;
            if (sec >= _playableClips[lastIndex].timelinePos && sec < _playableClips[lastIndex].timelinePos + (_playableClips[lastIndex].endTime - _playableClips[lastIndex].startTime))
            {
                requestedClipIndex = lastIndex;
            }

            // Seek to location.
            if (requestedClipIndex >= 0)
            {
                _multipleMedia = sphere.GetComponent<MultiplePlayerControl>();
                _player = _multipleMedia.Player;
                _currentClipIndex = requestedClipIndex;
                float videoTime = (sec - _playableClips[requestedClipIndex].timelinePos) + _playableClips[requestedClipIndex].startTime;
                Debug.Log("Set current clip index to " + _currentClipIndex + ": " + _playableClips[_currentClipIndex].ToString());

                if (_player != null && _player.Control != null)
                {
                    Debug.Log("Seeking to video time: " + videoTime);
                    _multipleMedia.Seek(videoTime);
                    this.CurrentVisibleView = GetVisibleView();
                }
                else
                {
                    Debug.Log("Either player or player control is null. Not seeking.");
                }
            }
            else
            {
                Debug.Log("Did not find clip that seek time falls under. Ignoring.");
            }
        }

        public void RefreshTimeline()
        {
            if (sphere == null)
            {
                sphere = GameObject.Find("Sphere");
            }
            _multipleMedia = sphere.GetComponent<MultiplePlayerControl>();
            _player = _multipleMedia.Player;
            _gatedClips = GetComponents<GatedClip>();
            _playableClips = GetComponents<PlayableClip>();
            _previewMode = GetComponent<PreviewMode>();

            Debug.Assert(_previewMode != null);

            _previewMode.equirectTexture = null;
            this.SetupEquirectTexture();
            Array.Sort(_playableClips);

            Debug.Log("Inside refresh timeline. Number of playable clips: " + _playableClips.Length + ", Number of gated clips: " + _gatedClips.Length);
            for (int i = 0; i < _playableClips.Length; i++)
            {
                if (_playableClips[i].enabled)
                {
                    _currentClipIndex = i;
                    break;
                }
            }

            initialized = true;
        }

        void InitViewportCoord()
        {
            viewportCoordToCheck = new Vector3[VIEWPORT_NUM_SAMPLES];
            viewportCoordToCheck[0] = new Vector3(0, 1, 0);  // top left corner
            viewportCoordToCheck[1] = new Vector3(1, 1, 0);  // top right corner
            viewportCoordToCheck[2] = new Vector3(1, 0, 0);  // bottom right corner
            viewportCoordToCheck[3] = new Vector3(0, 0, 0);  // bottom left corner
            viewportCoordToCheck[4] = new Vector3(0.5f, 0.5f, 0);  // center
            viewportCoordToCheck[5] = new Vector3(0.25f, 0.25f, 0);
            viewportCoordToCheck[6] = new Vector3(0.25f, 0.75f, 0);
            viewportCoordToCheck[7] = new Vector3(0.75f, 0.25f, 0);
            viewportCoordToCheck[8] = new Vector3(0.75f, 0.75f, 0);
        }

        void InitBoundary()
        {
            boundary = new Vector3[BOUNDARY_NUM_SAMPLES];
            int num_per_side = (boundary.Length - 1) / 4;
            for (int i = 0; i < num_per_side; i++)
            {
                float step = 1.0f / num_per_side;
                boundary[i] = new Vector3(step * i, 1, 0);
            }

            for (int i = 0; i < num_per_side; i++)
            {
                float step = 1.0f / num_per_side;
                boundary[num_per_side + i] = new Vector3(1, 1 - step * i, 0);
            }

            for (int i = 0; i < num_per_side; i++)
            {
                float step = 1.0f / num_per_side;
                boundary[num_per_side * 2 + i] = new Vector3(1 - step * i, 0, 0);
            }

            for (int i = 0; i < num_per_side; i++)
            {
                float step = 1.0f / num_per_side;
                boundary[num_per_side * 3 + i] = new Vector3(0, step * i, 0);
            }
            boundary[boundary.Length - 1] = new Vector3(0, 1, 0);
        }

        // Use this for initialization
        void Start()
        {
            InitBoundary();
            _currentClipIndex = -1;
            RefreshTimeline();
            InitViewportCoord();
            _previewMode = GetComponent<PreviewMode>();
        }

        void SetupEquirectTexture()
        {
            if (_player.TextureProducer != null && _previewMode.equirectTexture == null)
            {
                Texture frame = _player.TextureProducer.GetTexture();
                if (frame != null)
                {
                    _previewMode.equirectTexture = new Texture2D(frame.width, frame.height, TextureFormat.ARGB32, false);
                }
            }
        }

        public void SetPlayMode(bool mode)
        {
            playMode = mode;
            if (_previewMode == null)
            {
                _previewMode = GetComponent<PreviewMode>();
            }
            _previewMode.requestedPlayMode = mode;
        }

        public int GetCurrentClipIndex()
        {
            return _currentClipIndex;
        }

        public bool CurrentClipPlaying(Clip clip)
        {
            if (_currentClipIndex < 0 || _currentClipIndex >= _playableClips.Length)
            {
                return false;
            }
            if (_playableClips == null)
            {
                RefreshTimeline();
            }
            return Mathf.Approximately(clip.start, _playableClips[_currentClipIndex].startTime) && Mathf.Approximately(clip.end, _playableClips[_currentClipIndex].endTime);
        }

        public bool PassedGate()
        {
            if (_currentClipIndex < 0)
            {
                return true;
            }
            return _playableClips[_currentClipIndex].PassedGate(GetCurrentTime(), _multipleMedia);
        }

        public float[] GetViewCenters()
        {
            if (viewCenters == null || viewCenters.Length == 0 || viewCenters.Length != StoryPoint.NUM_VIEWS)
            {
                float spacing = 2 * Mathf.PI / (float)StoryPoint.NUM_VIEWS;
                viewCenters = new float[StoryPoint.NUM_VIEWS];
                for (int i = 0; i < StoryPoint.NUM_VIEWS; i++)
                {
                    viewCenters[i] = -Mathf.PI + i * spacing;
                }
            }
            return viewCenters;
        }

        bool isWithinView(float xrad, float viewcenter)
        {
            bool within = Mathf.Abs(xrad - viewcenter) <= StoryPoint.VIEW_HFOV / 2f ||
                Mathf.Abs(xrad + 2 * Mathf.PI - viewcenter) <= StoryPoint.VIEW_HFOV / 2f ||
                Mathf.Abs(xrad - 2 * Mathf.PI - viewcenter) <= StoryPoint.VIEW_HFOV / 2f;
            return within;
        }

        float DistFromViewCenter(float xrad, float facecenter)
        {
            return Mathf.Min(Mathf.Abs(xrad - facecenter),
                             Mathf.Abs(xrad + 2 * Mathf.PI - facecenter),
                             Mathf.Abs(xrad - 2 * Mathf.PI - facecenter));
        }

        public int GetVisibleViewStatus()
        {
            if (!playMode)
            {
                return GetVisibleView();
            }
            if (CurrentVisibleView < 0)
            {
                return 0;
            }
            return CurrentVisibleView;
        }

        public int GetVisibleView()
        {
            if (viewCenters == null || viewCenters.Length != StoryPoint.NUM_VIEWS)
                GetViewCenters();

            if (viewportCoordToCheck == null || viewportCoordToCheck.Length != VIEWPORT_NUM_SAMPLES)
            {
                InitViewportCoord();
            }

            Vector3 direction = GetSphereHitDirection(viewportCoordToCheck[4]);
            Vector2 lonlat = ConvertCartesianToLonLat(direction);

            int closestView = 0;
            float closestViewDist = float.MaxValue;
            for (int j = 0; j < viewCenters.Length; j++)
            {
                float dist = DistFromViewCenter(lonlat.x, viewCenters[j]);
                if (dist < closestViewDist)
                {
                    closestViewDist = dist;
                    closestView = j;
                }
            }
            return closestView;
        }

        public int[] GetViewsContainingLonLat(Vector2[] lonlat)
        {
            if (viewCenters == null || viewCenters.Length == 0 || viewCenters.Length != StoryPoint.NUM_VIEWS)
                GetViewCenters();
            List<int> targetViews = new List<int>();
            for (int i = 0; i < lonlat.Length; i++)
            {
                for (int j = 0; j < viewCenters.Length; j++)
                {
                    if (isWithinView(lonlat[i].x, viewCenters[j]))
                    {
                        targetViews.Add(j);
                    }
                }
            }
            return targetViews.Distinct().ToArray();
        }

        Vector2 ConvertCartesianToLonLat(Vector3 cart)
        {
            // Need to re-orient axes to traditional Cartesian axes.
            Debug.Assert(Mathf.Approximately(cart.magnitude, 1.0f));
            float y = cart.x;
            float z = cart.y;
            float x = cart.z;

            float r = (float)Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2) + Math.Pow(z, 2));
            float azimuth = Mathf.Atan2(y, x);
            float zenith = Mathf.Acos(z / r);

            float xrad = azimuth;
            float yrad = Mathf.PI / 2.0f - zenith;
            return new Vector2(xrad, yrad);  // Center of equirect is (0, 0)
        }

        public Vector2 ConvertCartesianToPixel(Vector3 cart)
        {
            Vector2 lonlat = ConvertCartesianToLonLat(cart);
            float xrad = lonlat.x;
            float yrad = lonlat.y;
            Vector2 res = GetVideoDimensions();
            float pixelX = xrad / Mathf.PI * (res.x / 2.0f) + (res.x / 2.0f);
            float pixelY = (res.y / 2.0f) - yrad / (Mathf.PI / 2.0f) * (res.y / 2.0f);
            
            return new Vector2(pixelX, pixelY);
        }

        public Vector3 GetSphereHitDirection(Vector3 viewportCoord)
        {
            Ray ray = Camera.main.ViewportPointToRay(viewportCoord);

            float sphereRadius = Camera.main.transform.localScale.x;
            Vector3 sphereCenter = Camera.main.transform.position;
            float a = Vector3.Dot(ray.direction, ray.direction);
            float b = 2 * Vector3.Dot(ray.direction, (ray.origin - sphereCenter));
            float c = Vector3.Dot((ray.origin - sphereCenter), (ray.origin - sphereCenter)) - Mathf.Pow(sphereRadius, 2);
            float d = b * b - 4 * a * c;

            if (d < 0)
            {
                Debug.LogError("Discriminant is less than 0");
            }
            float t = (-b + Mathf.Sqrt(d)) / (2 * a);
            if (t < 0)
            {
                t = (-b - Mathf.Sqrt(d)) / (2 * a);
            }
            Vector3 hitPoint = ray.origin + ray.direction * t;
            return (hitPoint - sphereCenter).normalized;
        }

        public Vector2[] GetViewportPixels()
        {
            // ray.origin is bottom left of viewport (on near clipping plane). ray.direction should be vector from camera position to ray.origin, normalized to length 1.
            if (boundary == null || boundary.Length != BOUNDARY_NUM_SAMPLES)
            {
                InitBoundary();
            }
            if (viewportPixels == null || viewportPixels.Length == 0)
            {
                viewportPixels = new Vector2[boundary.Length];
            }
            for (int i = 0; i < viewportPixels.Length; i++) {
                boundary[i].z = Camera.main.nearClipPlane;
                Vector3 hitDirection = GetSphereHitDirection(boundary[i]);
                viewportPixels[i] = ConvertCartesianToPixel(hitDirection);
            }
            return viewportPixels;
        }

        public float GetCurrentClipEndTime()
        {
            if (playMode && _currentClipIndex >= 0 && _currentClipIndex < _playableClips.Length)
                return _playableClips[_currentClipIndex].endTime;
            else
                return -1;
        }

        public float GetCurrentClipStartTime()
        {
            if (playMode)
                return _playableClips[_currentClipIndex].startTime;
            else
                return -1;
        }

        public int GetCurrentClipStartFrame()
        {
            if (_currentClipIndex < 0)
            {
                Debug.Log("Current clip index is " + _currentClipIndex);
                return 0;
            }
            return (int)(_playableClips[_currentClipIndex].startTime * GetVideoFps());
        }

        public bool ShouldCrossFadeAudio()
        {
            if (!playMode)
            {
                return false;
            }
            if (_currentClipIndex < 0 || _currentClipIndex >= _playableClips.Length)
            {
                return false;
            }
            GatedClip sp = _playableClips[_currentClipIndex] as GatedClip;
            if (sp == null)
            {
                return _playableClips[_currentClipIndex].crossFadeAudio;
            }
            else
            {
                return sp.crossFadeAudio;
            }
        }

        public bool ShouldMuteAudio()
        {
            if (!playMode)
            {
                return false;
            }
            if (_currentClipIndex < 0 || _currentClipIndex >= _playableClips.Length)
            {
                return false;
            }
            GatedClip sp = _playableClips[_currentClipIndex] as GatedClip;
            if (sp == null)
            {
                return _playableClips[_currentClipIndex].muteAudio;
            }
            else
            {
                return sp.muteAudio;
            }
        }

        public float GetMediaFullDuration()
        {
            return _multipleMedia.Player.Info.GetDurationMs() / 1000f;
        }

        // Update is called once per frame
        void Update()
        {
            _multipleMedia = sphere.GetComponent<MultiplePlayerControl>();
            _player = _multipleMedia.Player;
            _gatedClips = GetComponents<GatedClip>();
            _playableClips = GetComponents<PlayableClip>();
            _previewMode = GetComponent<PreviewMode>();
            Array.Sort(_playableClips);
            this.transform.position = Camera.main.transform.position;

            currentTime = _multipleMedia.GetCurrentTime();
            CurrentVisibleView = GetVisibleView();
            if (_multipleMedia.IsPaused())
            {
                float relativeTime = currentTime - _playableClips[_currentClipIndex].startTime;
                Debug.Log("Current frame: " + (int)(currentTime * _multipleMedia.Player.Info.GetVideoFrameRate()) + ". Current view is " + CurrentVisibleView + ". Current clip: " + GetCurrentClipIndex());
                Debug.Log("Relative frame: " + (int)(relativeTime * _multipleMedia.Player.Info.GetVideoFrameRate()));
            }

            currentTime = _multipleMedia.GetCurrentTime();

            if (initialized)  // Need in case the first clip does not start at t = 0s of the raw video.
            {
                Debug.Log("Is playing? " + _multipleMedia.IsPlaying());
                _multipleMedia.Seek(_playableClips[0].startTime);
                if (Mathf.Abs(_player.Control.GetCurrentTimeMs() / 1000f - _playableClips[0].startTime) < GetVideoFps())
                {
                    initialized = false;
                }
                Debug.Log("Initialized. Current time: " + Mathf.Abs(_player.Control.GetCurrentTimeMs()) / 1000f + ", Initialized? " + initialized.ToString());
                return;
            }

            // If there are no playable clips, stop.
            if (_playableClips.Length == 0 || _currentClipIndex < 0 || _currentClipIndex >= _playableClips.Length)
            {
                this.SetPlayMode(false);
                this.PauseVideo();
                return;
            }

            if (!_multipleMedia.SyncMediaPlayers() && _multipleMedia.isTransitioning)
            {
                Debug.Log("Not in sync DESPITE FORCEWAIT");
                if (_multipleMedia.Player.Control.IsSeeking())
                {
                    playMode = false;
                }
                else if (_multipleMedia.OtherPlayer.Control.IsSeeking())
                {
                    playMode = false;
                }
            }

            currentTime = _multipleMedia.GetCurrentTime();

            if (playMode)
            {
                this.PlayVideo();
            }
            else
            {
                this.PauseVideo();
            }
            this.SetupEquirectTexture();

            // Storing equirect texture into PreviewMode component, which is read by VideoEditorWindow.
            if (_player.TextureProducer != null)
            {
                if (_previewMode.equirectTexture != null)
                {
                    MediaPlayer equirectPlayer = _multipleMedia.GetEquirectPlayer();
                    if (equirectPlayer != null)
                    {
                        _previewMode.equirectTexture = (Texture2D) equirectPlayer.TextureProducer.GetTexture();
                    }
                }
            }
           
            if (currentTime < _playableClips[_currentClipIndex].startTime - 5)
            {
                Debug.Log("Current time " + currentTime + " is less than start time of clip index " + _currentClipIndex + ": " + _playableClips[_currentClipIndex] + ". Skipping ahead...");
                _multipleMedia.Seek(_playableClips[_currentClipIndex].startTime);
                return;
            }

            _previewMode.SetCurrentClip(_playableClips[_currentClipIndex].clipIndex);
            int previousClipIndex = _currentClipIndex;
            int currentFrame = (int)(currentTime * _multipleMedia.Player.Info.GetVideoFrameRate());

            // Check whether playable clip is gated clip.
            GatedClip gc = _playableClips[_currentClipIndex] as GatedClip;
            if (gc != null)
            {
                // This clip is a gated clip.
                if ((_player.Control.IsPlaying() || _multipleMedia.IsPaused()) && gc.checkSalient(currentTime, _multipleMedia, this))
                {
                    currentTime = _multipleMedia.GetCurrentTime();
                    Debug.Log("Current time: " + currentTime + ". Current face: " + GetVisibleView() + ". Passed gated clip " + gc + ". Moving from index: " + _currentClipIndex);

                    if (_multipleMedia.isTransitioning)
                    {
                        Debug.Log("Finishing in-progress transition.");
                        _multipleMedia.FinishTransition();
                        Debug.Log("Multiple media is transition? " + _multipleMedia.isTransitioning);
                    }
                    else
                    {
                        Debug.Log("No in-progress transition");
                    }
                    _playableClips = sphere.GetComponents<PlayableClip>();
                    Array.Sort(_playableClips);

                    _currentClipIndex++;
                    _multipleMedia.expectedCurrentFrame = 0;
                    _multipleMedia.expectedNextFrame = 1;
                    while (_currentClipIndex < _playableClips.Length && !_playableClips[_currentClipIndex].enabled)
                    {
                        _currentClipIndex++;
                        _multipleMedia.expectedCurrentFrame = 0;
                        _multipleMedia.expectedNextFrame = 1;
                    }
                    Debug.Log("Next clip index: " + _currentClipIndex);
                    if (_currentClipIndex >= 0 && _currentClipIndex < _playableClips.Length) {
                        Debug.Log(_playableClips[_currentClipIndex].ToString());
                    }
                    _multipleMedia.UpdateAudio();
                }
            }
            else  // Not a gated clip.
            {
                if (_player.Control.IsPlaying() && _playableClips[_currentClipIndex].checkSalient(currentTime, _multipleMedia, this))
                {
                    _playableClips = sphere.GetComponents<PlayableClip>();
                    Array.Sort(_playableClips);
                    Debug.Log("Current time: " + currentTime + ". Finished clip " + _playableClips[_currentClipIndex] + ". Moving from index: " + _currentClipIndex);
                    _currentClipIndex++;
                    _multipleMedia.expectedCurrentFrame = 0;
                    _multipleMedia.expectedNextFrame = 1;

                    while (_currentClipIndex < _playableClips.Length && !_playableClips[_currentClipIndex].enabled)
                    {
                        _currentClipIndex++;
                        _multipleMedia.expectedCurrentFrame = 0;
                        _multipleMedia.expectedNextFrame = 1;
                    }
                    Debug.Log("Next clip index: " + _currentClipIndex);
                    _multipleMedia.UpdateAudio();
                }
            }

            if (_currentClipIndex >= _playableClips.Length)
            {
                _currentClipIndex = -1;
                Debug.Log("End of all clips.");
                SetPlayMode(false);
                return;
            }

            if (previousClipIndex != _currentClipIndex && Mathf.Abs(_playableClips[_currentClipIndex].startTime - _playableClips[previousClipIndex].endTime) > 0.5f)
            {
                Debug.Log("Jumping to beginning of next clip index: " + _currentClipIndex + ". " + _playableClips[_currentClipIndex].ToString());
                _multipleMedia.Seek(_playableClips[_currentClipIndex].startTime);
            }
        }
    }
}
