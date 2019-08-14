using UnityEngine;


namespace RenderHeads.Media.AVProVideo
{
    public class PreviewMode : MonoBehaviour
    {
        // Stores values for runtime.
        public Texture2D equirectTexture;
        public Texture2D singleTargetTexture;
        public int currentClipIndex;
        public bool requestedPlayMode = false;
        public bool showROI = false;
        public bool showOverlappingBlocks = false;
        public bool showCut = true;
        public bool collapseViews = false;

        public void SetCurrentClip(int index)
        {
            currentClipIndex = index;
        }

        public Texture2D GetSingleROITexture()
        {
            if (singleTargetTexture == null)
            {
                singleTargetTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                singleTargetTexture.SetPixel(0, 0, new Color(0, 1, 0, 0.25f));
                singleTargetTexture.Apply();
            }
            return singleTargetTexture;
        }

        public Texture2D GetEquirectTexture(bool showTarget, Clip clip, float playTime)
        {
            if (!showTarget || clip.sp == null)
            {
                return equirectTexture;
            }
            return null;
        }

        // Use this for initialization
        void Start()
        {
            singleTargetTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            singleTargetTexture.SetPixel(0, 0, new Color(0, 1, 0, 0.4f));
            singleTargetTexture.Apply();
        }
    }
}