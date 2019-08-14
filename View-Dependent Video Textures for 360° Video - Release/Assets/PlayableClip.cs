using System;
using UnityEngine;

namespace RenderHeads.Media.AVProVideo
{
    public class PlayableClip : MonoBehaviour, IComparable
    {

        // Simple playable clip.
        public string clipName;
        public float startTime;
        public float endTime;
        public float timelinePos;
        public HeadTrack _headTrack;
        public int clipIndex;
        public bool crossFadeAudio = true;  // Whether or not to crossfade audio.
        public bool muteAudio = false;

        protected MeshRenderer _mesh = null;

        public int CompareTo(object obj)
        {
            if (obj == null)
                return 1;

            PlayableClip otherPlayableClip = obj as PlayableClip;
            if (otherPlayableClip == null)
                return 1;
            return this.timelinePos.CompareTo(otherPlayableClip.timelinePos);
        }

        public override bool Equals(System.Object obj)
        {
            PlayableClip otherPlayableClip = obj as PlayableClip;
            if (otherPlayableClip == null)
            {
                return false;
            }
            return Math.Abs(this.startTime - otherPlayableClip.startTime) < 1e-5f && Math.Abs(this.endTime - otherPlayableClip.endTime) < 1e-5f;
        }

        public override int GetHashCode()
        {
            return string.Format("{0}_{1}_{2}", this.startTime, this.endTime, _headTrack).GetHashCode();
        }

        public virtual bool PassedGate(float time, MultiplePlayerControl multicontrol)
        {
            return true;
        }

        public virtual bool checkSalient(float currentTime, MultiplePlayerControl multiMedia, HeadTrack _headtrack)
        {
            if (_headTrack == null)
                _headTrack = GetComponent<HeadTrack>();
            MediaPlayer player = multiMedia.Player;

            float threshold = 1.0f / _headtrack.GetVideoFps();
            if (Math.Abs(currentTime - endTime) < threshold)
            {
                return true;  // Saliency means we passed this clip. Can move pointer to next clip.
            }
            return false;
        }

        public virtual bool isTargetView(int f)
        {
            return true;
        }

        public override string ToString()
        {
            return "Playable Clip: " + clipName + " (" + startTime + " to " + endTime + "). Clipindex: " + clipIndex + ". Timeline pos: " + timelinePos;
        }

        // Use this for initialization
        void Start()
        {
            //Debug.Log("Initializing playable clip!" + this.ToString());
            _headTrack = GetComponent<HeadTrack>();
            _mesh = this.GetComponent<MeshRenderer>();
            _mesh.material.shader = Shader.Find("Inside Sphere Blend");
        }
    }
}
