using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using System.Linq;

namespace RenderHeads.Media.AVProVideo
{
    [Serializable]
    public class LoopArc : IComparable
    {
        public static float WIDTH_PER_LOOP = 0.01f;
        public float fromTime;
        public float toTime;

        public float _clipStart;
        public float _clipEnd;
        
        private Rect _rect;
        private bool _isDirty = false;  // Set to true when start or end of clip changes.

        private Vector3[] _ArcPoints;
        private Vector3[] _Arrow;
        private int perpArrowLength = 10;

        public int arrowStart = 1;  // Index of arcpoints to start arrow at.

        public LoopArc()
        {

        }

        public LoopArc(float from, float to, float clipStart, float clipEnd)
        {
            fromTime = from;
            toTime = to;
            _clipStart = clipStart;
            _clipEnd = clipEnd;
        }

        public override string ToString()
        {
            string basic = "Loop Arc from " + fromTime + " to " + toTime;  
            return basic;
        }

        public override int GetHashCode()
        {
            return string.Format("{0}_{1}", this.fromTime, this.toTime).GetHashCode();
        }

        public override bool Equals(System.Object obj)
        {
            LoopArc otherLoop = obj as LoopArc;
            if (otherLoop == null)
            {
                return false;
            }
            return Mathf.Approximately(this.fromTime, otherLoop.fromTime) && Mathf.Approximately(this.toTime, otherLoop.toTime);
        }

        public int CompareTo(object obj)
        {
            if (obj == null)
                return 1;

            LoopArc otherLoopArc = obj as LoopArc;
            if (otherLoopArc == null)
                return 1;
            return this.fromTime.CompareTo(otherLoopArc.fromTime);
        }

        public void ResetArcPoints(Rect rect)
        {
            if (_isDirty || !rect.Equals(_rect))
                _ArcPoints = null;
        }

        public float GetArcWidth()
        {
            float width = WIDTH_PER_LOOP;
            return Mathf.Min(5f, 2f + width);
        }

        public void SetArcPoints(Vector3[] points)
        {
            _ArcPoints = points;
        }

        public Vector3[] GetArrow()
        {
            return _Arrow;
        }

        public Vector3[] GenerateArrow()
        {
            if (_Arrow == null || _Arrow.Length == 0)
            {
                _Arrow = new Vector3[3];
            }

            if (_ArcPoints.Length == 0)
            {
                return _Arrow;
            }

            if (_Arrow[0] != _ArcPoints[_ArcPoints.Length-1])
            {
                _Arrow[0] = _ArcPoints[_ArcPoints.Length - 1];
            }
            Vector3 directionOfArrow = _ArcPoints[_ArcPoints.Length - 1] - _ArcPoints[_ArcPoints.Length - 1 - arrowStart];
            Vector3 rootOfArrow = _ArcPoints[_ArcPoints.Length - 1] - Vector3.Normalize(directionOfArrow) * perpArrowLength;
            if ((_Arrow[1] + _Arrow[2])/2 != rootOfArrow)
            {
                Vector3 perpArrow = Vector3.Cross(directionOfArrow, new Vector3(0, 0, 1));
                perpArrow = Vector3.Normalize(perpArrow) * perpArrowLength / 2;
                _Arrow[1] = rootOfArrow + perpArrow;
                _Arrow[2] = rootOfArrow - perpArrow;
            }
            return _Arrow;
        }

        public Vector3[] GenerateArcPoints(Rect rect, float PIXELS_PER_SEC, float jumpArcHeight, bool printDebug=false)
        {

            if (_isDirty || !rect.Equals(_rect))
            {
                // Generate and save arc points into _ArcPoints.
                if (printDebug)
                {
                    Debug.Log("Generating arc points: " + this.ToString());
                }
                var startPoint = new Vector2((this.fromTime - _clipStart) * PIXELS_PER_SEC + rect.x, rect.y);
                var endPoint = new Vector2((this.toTime - _clipStart) * PIXELS_PER_SEC + rect.x, rect.y);
                var midPoint = (startPoint + endPoint) / 2;
                midPoint.y = rect.y - jumpArcHeight;

                List<Vector3> points = new List<Vector3>();
                for (float t = 0f; t < 1f; t += 0.05f)
                {
                    var ab = Vector2.Lerp(startPoint, midPoint, t);
                    var bc = Vector2.Lerp(midPoint, endPoint, t);
                    var result = Vector2.Lerp(ab, bc, t);
                    points.Add(new Vector3(result.x, result.y, 0));
                }
                points.Add(new Vector3(endPoint.x, endPoint.y, 0));

                if (printDebug)
                {
                    Debug.Log("From " + _clipStart + " to " + _clipEnd + ": " + startPoint + " to " + endPoint);
                }

                _ArcPoints = points.ToArray();
                GenerateArrow();

                _isDirty = false;
                _rect = rect;
            }
            return _ArcPoints;
        }

        public void MarkDirty()
        {
            _isDirty = true;
        }
    }

    public class GatedClip : PlayableClip  // Gated Clip
    {
        public float gateTime = -1f;
        public bool lookAt = true;
        public float jumpThreshold = 1.0f;
        public LoopArc[] loopArcs;  // Times from which we can jump seamlessly to another part of the story point to form a loop.
        public bool jumpImmediately = false;  // Whether or not to transition jump to the end of clip once viewing condition is met.
        public View[] views;
        public StoryPoint sp;
        
        public float targetX;
        public float targetY;
        public float targetWidth;
        public float targetHeight;
        public float minLoopLengthSec;
        public float perceptualThreshold;
        private int _currentView = -1;
        private bool _ROIWithinView = false;
        public int[] ROIviews;
        public Rect targetRect;

        public LoopArc NextLoopArc = null;

        public LoopArc GetNextLoop()
        {
            return NextLoopArc;
        }

        public override string ToString()
        {
            string[] loopTimesStr = null;

            if (loopArcs != null)
            {
                loopTimesStr = new string[loopArcs.Length];
                for (int i = 0; i < loopArcs.Length; i++)
                {
                    loopTimesStr[i] = loopArcs[i].ToString();
                }
            }
            if (loopTimesStr !=null)
                return "Gated clip: " + startTime + " to " + endTime + ". Clip index: " + clipIndex + ". Loop times: " + string.Join(", ", loopTimesStr);
            return "Gated Clip: " + startTime + " to " + endTime + ". Clip index: " + clipIndex + ". Timeline pos: " + timelinePos;
        }

        public override bool isTargetView(int f)
        {
            if (ROIviews == null)
            {
                return false;
            }
            if (sp.lookAt)
            {
                foreach (int i in ROIviews)
                {
                    if (f == i)
                    {
                        return true;
                    }
                }
                return false;
            }
            else
            {
                foreach (int i in ROIviews)
                {
                    if (f == i)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public int GetGateFrame()
        {
            return (int)(this.gateTime * _headTrack.GetVideoFps());
        }

        public int GetGateTimecode()
        {
            return (int)((this.startTime + this.gateTime) * _headTrack.GetVideoFps());
        }

        public override bool PassedGate(float currentTime, MultiplePlayerControl multiMedia)
        {
            float relativeTime = currentTime - this.startTime;
            int currentFrame = (int)(relativeTime * multiMedia.Player.Info.GetVideoFrameRate());
            int gateFrame = (int)(this.gateTime * multiMedia.Player.Info.GetVideoFrameRate());
            return currentFrame > gateFrame;
        }

        public void Transition(int currentFrame, int curView, MultiplePlayerControl multiMedia, HeadTrack _headtrack)
        {
            int gateFrame = (int)(this.gateTime * multiMedia.Player.Info.GetVideoFrameRate());

            if (!isTargetView(curView) && (multiMedia.expectedCurrentFrame == gateFrame || multiMedia.expectedNextFrame == gateFrame) && currentFrame != multiMedia.expectedCurrentFrame && currentFrame != multiMedia.expectedNextFrame)
            {
                Debug.LogError("SKIPPED GATE FRAME!");
            }
            else if (currentFrame > gateFrame || isTargetView(curView))
            {
                multiMedia.expectedCurrentFrame = currentFrame;
                multiMedia.expectedNextFrame = currentFrame+1;

                if (sp.jumpImmediately && currentFrame < gateFrame - HeadTrack._LOOP_TRANS_DURATION && isTargetView(curView))
                {
                    int jumpToFrame = gateFrame - (int)(HeadTrack._LOOP_TRANS_DURATION * multiMedia.Player.Info.GetVideoFrameRate());
                    if (sp.views[curView].CanJumpTo(currentFrame, jumpToFrame, sp.jumpThreshold))
                    {
                        float jumpToTime = this.gateTime - HeadTrack._LOOP_TRANS_DURATION;
                        Debug.Log("Frame rate: " + multiMedia.Player.Info.GetVideoFrameRate());
                        Debug.Log("Sp start time: " + sp.startTime + " Jump to time: " + jumpToTime);
                        Debug.Log("GOING to jump from " + (sp.startTime + 1f * currentFrame / multiMedia.Player.Info.GetVideoFrameRate()) + " to time " + (sp.startTime + jumpToTime));
                        this.sp.views[curView].AddLoopArc(sp.startTime + 1f * currentFrame / multiMedia.Player.Info.GetVideoFrameRate(), sp.startTime + jumpToTime, sp.startTime, sp.endTime);
                        Debug.Log("Sp now has " + this.sp.views[curView].GetLoopArcs().Count() + " looparcs");

                        LoopArc currentLoop = this.sp.views[curView].GetCurrentLoop();
                        multiMedia.currentLoopArc = currentLoop;
                        multiMedia._mediaOffset = currentLoop.fromTime - currentLoop.toTime;
                        Debug.Log("Media current loop arc is " + multiMedia.currentLoopArc + ". Set media offset to " + multiMedia._mediaOffset);

                        // Seek to apply media offset.
                        multiMedia.isTransitioning = true;
                        multiMedia.SyncMediaPlayers();
                        multiMedia.expectedCurrentFrame = jumpToFrame;
                        multiMedia.expectedNextFrame = jumpToFrame + 1;
                    }
                }

                return; // Don't transition if current frame is past gate frame.
            }

            int jumpTo;
            int fromFrame = currentFrame;
            if (sp.ReachedCutFrame(curView, currentFrame))
            {
                jumpTo = sp.FindJumpToFrame(curView, currentFrame);
                Debug.Log("Reached cut frame. Jumping to: " + jumpTo);
            }
            else if (sp.PastLastCutFrame(curView, currentFrame))  // May happen if computer suddenly lagged and missed a frame update.
            {
                Debug.LogWarning("ERROR! REACHED DANGER ZONE " + curView + ", " + currentFrame);
                int lastCutFrame = sp.FindLastCutFrame(curView);
                jumpTo = sp.FindJumpToFrame(curView, lastCutFrame);
                fromFrame = lastCutFrame;
            }
            else
            {
                jumpTo = currentFrame + 1;
            }
            multiMedia.expectedCurrentFrame = currentFrame;
            multiMedia.expectedNextFrame = jumpTo;

            if (jumpTo == currentFrame)
            {
                Debug.Log("Paused media player because jumpto " + jumpTo + " is same as current frame: " + currentFrame);
                _headtrack.playMode = false;
                multiMedia.Pause();
            }
            else if (jumpTo != currentFrame + 1)  // if jumping to a frame that's not the next frame, then perform a seek. Otherwise just keep playing.
            {
                float jumpToTime = 1f * jumpTo / multiMedia.Player.Info.GetVideoFrameRate();
                Debug.Log("Frame rate: " + multiMedia.Player.Info.GetVideoFrameRate());
                Debug.Log("Sp start tiem: " + sp.startTime + " Jump to time: " + jumpToTime);
                Debug.Log("GOING to jump from " + (sp.startTime + 1f * currentFrame / multiMedia.Player.Info.GetVideoFrameRate()) + " to time " + (sp.startTime + jumpToTime));
                this.sp.views[curView].AddLoopArc(sp.startTime + currentFrame / multiMedia.Player.Info.GetVideoFrameRate(), sp.startTime + jumpToTime, sp.startTime, sp.endTime);
                Debug.Log("Sp now has " + this.sp.views[curView].GetLoopArcs().Count() + " looparcs");

                // Set media offset.
                LoopArc currentLoop = this.sp.views[curView].GetCurrentLoop();
                multiMedia.currentLoopArc = currentLoop;
                multiMedia._mediaOffset = currentLoop.fromTime - currentLoop.toTime;

                Debug.Log("Set media offset to " + multiMedia._mediaOffset);

                // Seek to apply media offset.
                multiMedia.isTransitioning = true;
                multiMedia.SyncMediaPlayers();
                multiMedia.expectedCurrentFrame = jumpTo;
                multiMedia.expectedNextFrame = jumpTo + 1;
            }
        }

        public override bool checkSalient(float currentTime, MultiplePlayerControl multiMedia, HeadTrack _headtrack)
        {
            float relativeTime = currentTime - this.startTime;
            int currentFrame = (int)(relativeTime * multiMedia.Player.Info.GetVideoFrameRate());
            int gateFrame = (int)(this.gateTime * multiMedia.Player.Info.GetVideoFrameRate());
            int curView = _headTrack.GetVisibleView();
            
            if (_currentView != curView)
            {
                if (multiMedia.isTransitioning && multiMedia.currentLoopArc.toTime > multiMedia.currentLoopArc.fromTime)
                {
                    Debug.Log("VIEW CHANGED IN MIDDLE OF TRANSITION. Was " + _currentView + ". Now: " + curView + ". Calling FinishTransition.");
                    multiMedia.FinishTransition();
                }
                if (multiMedia.IsPaused())
                {
                    if (GetComponent<PreviewMode>().requestedPlayMode)
                    {
                        Debug.Log("CURVIEW WAS IN PAUSED STATE. Now: " + curView + ". Playing again.");
                        _headtrack.playMode = true;
                    }
                }
                _currentView = curView;
            }

            if (!_ROIWithinView)
            {
                if (isTargetView(curView))
                {
                    _ROIWithinView = true;
                    if (multiMedia.isTransitioning)
                    {
                        Debug.Log("TARGET CAME INTO VIEW AGAIN. Calling FinishTransition");
                        multiMedia.FinishTransition();
                    }
                    if (multiMedia.IsPaused())
                    {
                        _headtrack.playMode = true;
                        multiMedia.Play();
                    }
                }
            }
            if (multiMedia.isTransitioning && (!isTargetView(curView) && sp.IsCutFrame(curView, currentFrame)))
            {
                Debug.Log("Current frame: " + currentFrame + ". Entered another valid frame when transitioning.... Exiting transition");
                multiMedia.FinishTransition();
                Debug.Log("Exited? " + !multiMedia.isTransitioning);
            }

            if (multiMedia.isTransitioning)
            {
                Debug.Log("Current loop is " + multiMedia.currentLoopArc);
                multiMedia.UnidirectionalTransition();
                multiMedia.expectedCurrentFrame = currentFrame;
                multiMedia.expectedNextFrame = currentFrame + 1;
            }
            else
            {
                Transition(currentFrame, _currentView, multiMedia, _headtrack);
            }
            return currentTime > this.endTime;  // Return true if Headtrack can move onto the next clip. Else false.
        }

        public void setROI(float newTargetX, float newTargetY, float newTargetWidth, float newTargetHeight)
        {
            if (!Mathf.Approximately(this.targetX, newTargetX) || !Mathf.Approximately(this.targetY, newTargetY) ||
                !Mathf.Approximately(this.targetWidth, newTargetWidth) || !Mathf.Approximately(this.targetHeight, newTargetHeight))
            {
                this.targetX = newTargetX;
                this.targetY = newTargetY;
                this.targetWidth = newTargetWidth;
                this.targetHeight = newTargetHeight;
                if (_headTrack == null)
                {
                    _headTrack = GetComponent<HeadTrack>();
                }
                Vector2[] lonlats = ConvertTargetRectToLonLat();
                if (new Rect(targetX, targetY, targetWidth, targetHeight) != new Rect(0.0f, 0.0f, 1f, 1f))
                {
                    this.ROIviews = _headTrack.GetViewsContainingLonLat(lonlats);
                }
            }
        }

        private Vector2[] ConvertTargetRectToLonLat(bool debug=false)
        {
            if (_headTrack == null)
            {
                _headTrack = GetComponent<HeadTrack>();
            }

            //float overlap = StoryPoint.FACE_HFOV / (360f / StoryPoint.NUM_FACES) / 2f; // In degrees between adjacent faces.
            float cameraHFOV = Mathf.PI / 180f * Camera.main.aspect * Camera.main.fieldOfView;  // aspect is width divided by height. field of view is vertical, in degrees.
            float cameraVFOV = Mathf.PI / 180f * Camera.main.fieldOfView;  // field of view is vertical, in degrees.

            float xrad = (targetX - (_headTrack.GetVideoDimensions().x / 2.0f)) / (_headTrack.GetVideoDimensions().x / 2.0f) * Mathf.PI;
            float yrad = ((_headTrack.GetVideoDimensions().y / 2.0f) - targetY) / (_headTrack.GetVideoDimensions().y / 2.0f) * Mathf.PI / 2;  // top
            float xrad2 = ((targetX + targetWidth) - (_headTrack.GetVideoDimensions().x / 2.0f)) / (_headTrack.GetVideoDimensions().x / 2.0f) * Mathf.PI;
            float yrad2 = ((_headTrack.GetVideoDimensions().y / 2.0f) - (targetY + targetHeight)) / (_headTrack.GetVideoDimensions().y / 2.0f) * Mathf.PI / 2;  //bottom

            if (debug)
                Debug.Log("Target lonlat from " + xrad + ", " + yrad + " to " + xrad2 + ", " + yrad2);

            float x_spacing = xrad2 - xrad;
            int num_x_samples = 0;
            while (x_spacing > cameraHFOV)
            {
                num_x_samples = num_x_samples + 1;
                x_spacing = x_spacing / 2f;
            }

            float y_spacing = (yrad - yrad2);
            int num_y_samples = 0;
            while (y_spacing > cameraVFOV)
            {
                num_y_samples = num_y_samples + 1;
                y_spacing = y_spacing / 2f;
            }
            

            if (num_x_samples > 0 & num_y_samples == 0)
            {
                num_y_samples = num_y_samples + 1;
                y_spacing = y_spacing / 2f;
            }

            if (num_y_samples > 0 & num_x_samples == 0)
            {
                num_x_samples = num_x_samples + 1;
                x_spacing = x_spacing / 2f;
            }

            if (debug)
            {
                Debug.Log("Num x samples: " + num_x_samples + ". Num y samples: " + num_y_samples);
                Debug.Log("X spacing: " + x_spacing + ". Y spacing: " + y_spacing);
            }

            Vector2[] lonlats = new Vector2[4 + num_x_samples*num_y_samples];
            lonlats[0] = new Vector2(xrad, yrad);
            lonlats[1] = new Vector2(xrad2, yrad);
            lonlats[2] = new Vector2(xrad2, yrad2);
            lonlats[3] = new Vector2(xrad, yrad2);
            //lonlats[4] = new Vector2((xrad2 + xrad) / 2f, (yrad2 + yrad) / 2f);

            for (int i = 0; i < num_x_samples; i++)
            {
                for (int j = 0; j < num_y_samples; j++)
                {
                    lonlats[4 + i * j] = new Vector2(xrad + x_spacing*(i+1), yrad2 + y_spacing*(j+1));
                }
            }
            if (debug)
            {
                Debug.Log("Returning final lon lats: " + string.Join(",", lonlats));
            }
            return lonlats;
        }

        // Use this for initialization
        void Start()
        {
            _headTrack = GetComponent<HeadTrack>();
            _mesh = this.GetComponent<MeshRenderer>();
            _mesh.material.shader = Shader.Find("Inside Sphere Blend");
        }
    }
}