#if UNITY_EDITOR || UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN
	#define UNITY_PLATFORM_SUPPORTS_LINEAR
#elif UNITY_IOS || UNITY_ANDROID
	#if UNITY_5_5_OR_NEWER || (UNITY_5 && !UNITY_5_0 && !UNITY_5_1 && !UNITY_5_2 && !UNITY_5_3 && !UNITY_5_4)
		#define UNITY_PLATFORM_SUPPORTS_LINEAR
	#endif
#endif
#if UNITY_5_4_OR_NEWER || (UNITY_5 && !UNITY_5_0)
	#define UNITY_HELPATTRIB
#endif

using UnityEngine;
using System.IO;

//-----------------------------------------------------------------------------
// Copyright 2015-2018 RenderHeads Ltd.  All rights reserverd.
//-----------------------------------------------------------------------------

namespace RenderHeads.Media.AVProVideo
{
	/// <summary>
	/// Sets up a mesh to display the video from a MediaPlayer
	/// </summary>
	[AddComponentMenu("AVPro Video/MultiplePlayerControl", 300)]
#if UNITY_HELPATTRIB
	[HelpURL("http://renderheads.com/product/avpro-video/")]
#endif
	public class MultiplePlayerControl : MonoBehaviour 
	{
		[Header("Media Source")]

		[SerializeField]
		private MediaPlayer[] _media = null;

        public float _lastSeekTime = 0;
        public AudioSource _audioSource = null;

        public LoopArc currentLoopArc = null;

        public int expectedCurrentFrame = 0;
        public int expectedNextFrame = 1;

		public MediaPlayer Player
		{
			get {
                return _media[_returnMediaIndex];
            }
			set {
                if (_media[_returnMediaIndex] != value)
                {
                    _media[_returnMediaIndex] = value;
                    _isDirty = true;
                }
            }
		}

        public MediaPlayer OtherPlayer
        {
            get
            {
                return _media[_otherMediaIndex];
            }
        }

        [Tooltip("Default texture to display when the video texture is preparing")]
		[SerializeField]
		private Texture2D _defaultTexture = null;

        public int _mainMediaIndex = 0;
        public int _sideMediaIndex = -1; // Used for cross fading during loop transitions.
        public int _returnMediaIndex = 0;
        private int _lastBlendValue = 1;  // 1 means fully main texture, 0 means fully side texture. Initialize to 1.

        public float _mediaOffset = 0.0f;
        public bool isTransitioning = false;
        public LoopArc shortcutJumpArc = null;

        public Texture2D DefaultTexture
		{
			get { return _defaultTexture; }
			set { if (_defaultTexture != value) { _defaultTexture = value; _isDirty = true; } }
		}

		[Space(8f)]
		[Header("Renderer Target")]

		[SerializeField]
		private Renderer _mesh = null;

		public Renderer MeshRenderer
		{
			get { return _mesh; }
			set { if (_mesh != value) { _mesh = value; _isDirty = true; } }
		}

		[SerializeField]
		private string _texturePropertyName = "_MainTex";

        [SerializeField]
        private string _sideTexturePropertyName = "_SideTex";

        [SerializeField]
        private string _blendPropertyName = "_Blend";

        public string TexturePropertyName
		{
			get { return _texturePropertyName; }
			set
			{
				if (_texturePropertyName != value)
				{
					_texturePropertyName = value;
#if UNITY_5_6_OR_NEWER
					_propTexture = Shader.PropertyToID(_texturePropertyName);
#endif
					_isDirty = true;
				}
			}
		}

        public int _otherMediaIndex
        {
            get {
                return 1 - _returnMediaIndex;
            }
        }

        public override string ToString()
        {
            string msg = "Return media index: " + _returnMediaIndex + ", Other media index: " + _otherMediaIndex + ". Last blend value: " + _lastBlendValue + ". Current blend value: " + _mesh.material.GetFloat("_Blend") + "Return media time : " + Player.Control.GetCurrentTimeMs() / 1000f + ". Audio muted? " + Player.Control.IsMuted() + ". Other media time: " + OtherPlayer.Control.GetCurrentTimeMs() / 1000f + ". Audio muted? " + OtherPlayer.Control.IsMuted();
            return msg;
        }

        [SerializeField]
		private Vector2 _offset = Vector2.zero;

		public Vector2 Offset
		{
			get { return _offset; }
			set { if (_offset != value) { _offset = value; _isDirty = true; } }
		}

		[SerializeField]
		private Vector2 _scale = Vector2.one;

		public Vector2 Scale
		{
			get { return _scale; }
			set { if (_scale != value) { _scale = value; _isDirty = true; } }
		}

		private bool _isDirty = false;
		private Texture _lastTextureApplied;
#if UNITY_5_6_OR_NEWER
		private int _propTexture;
		private int _propSideTexture;
		private int _propBlend;
#endif

		private static int _propStereo;
		private static int _propAlphaPack;
		private static int _propApplyGamma;
		private static int _propLayout;

		private const string PropChromaTexName = "_ChromaTex";
		private static  int _propChromaTex;

		private const string PropUseYpCbCrName = "_UseYpCbCr";
		private static int _propUseYpCbCr;

        public static float TRANSITION_BUFFER = 0.3f;  // Loop transition buffer on each side of jump time.

        public void UpdateAudio()
        {
            AudioSource[] sources = GetComponents<AudioSource>();
            foreach (AudioSource source in sources)
            {
                if (source.clip != null && source.clip.name == Path.GetFileNameWithoutExtension(Player.m_VideoPath))
                {
                    _audioSource = source;
                }
                else
                {
                    source.mute = true;
                }
            }

            bool crossFade = GetComponent<HeadTrack>().ShouldCrossFadeAudio();
            bool muteAudio = GetComponent<HeadTrack>().ShouldMuteAudio();
            if (!crossFade)
            {
                if (!_media[_returnMediaIndex].Control.IsMuted())
                {
                    _media[_returnMediaIndex].Control.MuteAudio(true);
                }
                if (!_media[_otherMediaIndex].Control.IsMuted())
                {
                    _media[_otherMediaIndex].Control.MuteAudio(true);  // Mute all. Manually sync Unity AudioSource.
                }

                if (_audioSource != null)
                {
                    _audioSource.mute = muteAudio;
                    if (_audioSource.time < GetComponent<HeadTrack>().GetCurrentClipStartTime())
                    {
                        _audioSource.time = GetComponent<HeadTrack>().GetCurrentClipStartTime();
                    }

                    if (_audioSource.time > GetComponent<HeadTrack>().GetCurrentClipEndTime())
                    {
                        _audioSource.Pause();
                    }
                    else
                    {
                        _audioSource.UnPause();
                    }
                }
            }
            else  // Crossfade audio.
            {
                if (_audioSource != null)
                {
                    _audioSource.mute = true;
                }

                float currentBlendVal = _mesh.material.GetFloat(_propBlend);
                if (!muteAudio)
                {
                    if (_media[_returnMediaIndex].Control.IsMuted())
                    {
                        _media[_returnMediaIndex].Control.MuteAudio(false);
                    }

                    if (_media[_otherMediaIndex].Control.IsMuted())
                    {
                        _media[_otherMediaIndex].Control.MuteAudio(false);
                    }
                    Player.Control.SetVolume(currentBlendVal);
                    OtherPlayer.Control.SetVolume(1 - currentBlendVal);
                }
                else
                {
                    if (!_media[_returnMediaIndex].Control.IsMuted())
                    {
                        _media[_returnMediaIndex].Control.MuteAudio(true);
                    }

                    if (!_media[_otherMediaIndex].Control.IsMuted())
                    {
                        _media[_otherMediaIndex].Control.MuteAudio(true);
                    }
                }
            }
        }

        private void UpdateReturnPlayer(float blendValue)
        {
            if (blendValue < 0.5f && _lastBlendValue == 0)
            {
                Debug.Log("BLEND VALUE LESS THAN 0.5f. BUT NEED to update to 1.");
                Debug.Log("Last blend value: " + _lastBlendValue + ". Blend value is " + blendValue + ". Return player index is " + _returnMediaIndex);
            }
            if (blendValue >= 0.5f && _lastBlendValue == 1)
            {
                Debug.Log("BLEND VALUE MORE THAN 0.5f. BUT NEED to update to 0.");
                Debug.Log("Last blend value: " + _lastBlendValue + ". Blend value is " + blendValue + ". Return player index is " + _returnMediaIndex);
            }
            _returnMediaIndex = _lastBlendValue == 0 ? _mainMediaIndex : _sideMediaIndex;
            UpdateAudio();
            Debug.Log("Updated return player index to " + _returnMediaIndex);
        }


        public bool IsPaused()
        {
            if (Player == null || Player.Control == null)
            {
                return true;
            }
            foreach (MediaPlayer player in _media)
            {
                if (player.Control.IsPaused())
                    return true;
            }
            return false;
        }

        public bool IsPlaying()
        {
            if (Player == null || Player.Control == null)
            {
                return false;
            }
            bool found = false;
            foreach (MediaPlayer player in _media)
            {
                if (player.Control.IsPlaying())
                {
                    found = true;
                }
            }
            return found;
        }

        public void Play()
        {
            foreach (MediaPlayer player in _media)
            {
                player.Control.Play();
            }
            UpdateAudio();
        }

        public void Pause()
        {
            foreach (MediaPlayer player in _media)
            {
                player.Control.Pause();
            }
            UpdateAudio();
        }

        public void Seek(float timeSec)
        {
            if (this.Player.Control != null) {
                this.Pause();

                _media[_returnMediaIndex].Control.Seek(timeSec * 1000f);

                _lastSeekTime = timeSec;
                float sideSec = Mathf.Max(0.0f, timeSec - _mediaOffset);
                this._media[_otherMediaIndex].Control.Seek(sideSec * 1000f);
                Debug.Log("After seeking. Return index time: " + _media[_returnMediaIndex].Control.GetCurrentTimeMs() / 1000f + ", Other index time: " + _media[_otherMediaIndex].Control.GetCurrentTimeMs() / 1000f);
                _lastBlendValue = _returnMediaIndex == _mainMediaIndex ? 1 : 0;
                UpdateAudio();

                if (GetComponent<PreviewMode>().requestedPlayMode)
                    this.Play();
            }
        }

        public float GetCurrentTime()
        {
            if (!isTransitioning)
            {
                if (_media[_returnMediaIndex].Control != null)
                {
                    return _media[_returnMediaIndex].Control.GetCurrentTimeMs() / 1000f;
                }
                return 0.0f;
            }
            else
            {
                if (_media[_otherMediaIndex].Control != null)
                {
                    return _media[_otherMediaIndex].Control.GetCurrentTimeMs() / 1000f;
                }
                return 0.0f;
            }
        }

        private void Awake()
		{
			if (_propStereo == 0)
			{
				_propStereo = Shader.PropertyToID("Stereo");
			}
			if (_propAlphaPack == 0)
			{
				_propAlphaPack = Shader.PropertyToID("AlphaPack");
			}
			if (_propApplyGamma == 0)
			{
				_propApplyGamma = Shader.PropertyToID("_ApplyGamma");
			}
			if (_propLayout == 0)
			{
				_propLayout = Shader.PropertyToID("Layout");
			}
			if (_propChromaTex == 0)
			{
				_propChromaTex = Shader.PropertyToID(PropChromaTexName);
			}
			if (_propUseYpCbCr == 0)
			{
				_propUseYpCbCr = Shader.PropertyToID(PropUseYpCbCrName);
			}
		}

        public bool isBetween(float time, float from, float to)
        {
            return time > from - 1f / OtherPlayer.Info.GetVideoFrameRate() && 
                time < to;
        }

        public void FinishTransition()
        {
            float currentBlendVal = _mesh.material.GetFloat(_propBlend);
            UpdateReturnPlayer(currentBlendVal);  // Round blend value to nearest whole number.
            int finalBlend = _returnMediaIndex == _mainMediaIndex ? 1 : 0;
            Debug.Log("Blend value was: " + currentBlendVal + ". Finished transition. Updated blend value to " + finalBlend);
            _mesh.material.SetFloat(_propBlend, (float)finalBlend);
            _lastBlendValue = finalBlend;
            this.isTransitioning = false;
            this._mediaOffset = 0.0f;
            Debug.Log("Multimedia isTransitioning: " + isTransitioning);
        }

        // Transition where blending happens only in the latter half.
        public bool UnidirectionalTransition()
        {
            LoopArc loopArc = currentLoopArc;
            float loopStart = loopArc.toTime;  // Blend time with respect to main player (forward).
            float loopEnd = loopStart + HeadTrack._LOOP_TRANS_DURATION;  // Blend time with respect to main player (forward).
            if (GetComponent<HeadTrack>().GetCurrentClipEndTime() >= 0)
            {
                if (GetComponent<HeadTrack>().GetMediaFullDuration() - loopArc.fromTime < HeadTrack._LOOP_TRANS_DURATION)
                {
                    loopEnd = loopArc.toTime + GetComponent<HeadTrack>().GetMediaFullDuration() - loopArc.fromTime;
                }
            }

            if (!Mathf.Approximately(_mediaOffset, loopArc.fromTime - loopArc.toTime))
            {
                _mediaOffset = loopArc.fromTime - loopArc.toTime;
            }
            float currentTime = GetCurrentTime();
            float currentBlendVal = _mesh.material.GetFloat(_propBlend);

            // Can happen when loop is done or when user seeks in the middle of a transition.
            if (!isBetween(currentTime, loopStart, loopEnd))
            {
                UpdateReturnPlayer(currentBlendVal);
                int finalBlend = _returnMediaIndex == _mainMediaIndex ? 1 : 0;
                _mesh.material.SetFloat(_propBlend, (float)finalBlend);
                _lastBlendValue = finalBlend;
                isTransitioning = false;
                UpdateAudio();
                return true;  // Done with transition.
            }

            float intermediateBlend = (currentTime - loopStart) / (loopEnd - loopStart);
            intermediateBlend = Mathf.Clamp(intermediateBlend, 0f, 1f);
            if (_lastBlendValue == 0)
            {
                _mesh.material.SetFloat(_propBlend, intermediateBlend);
            }
            else
            {
                _mesh.material.SetFloat(_propBlend, 1.0f - intermediateBlend);
            }
            Debug.Log("Updated blend value to " + _mesh.material.GetFloat(_propBlend));
            isTransitioning = true;
            UpdateAudio();
            return false;  // Not done with loop transition.
        }

        public float GetTimeIndicatorTime()
        {
            if (_media[_returnMediaIndex].Control == null)
            {
                return 0.0f;
            }
            if (_mesh == null)
            {
                _mesh = GetComponent<MeshRenderer>();
            }
            float currentBlendVal = _mesh.material.GetFloat(_propBlend);
            if (currentBlendVal < 0.5f)
            {
                return _media[_sideMediaIndex].Control.GetCurrentTimeMs() / 1000f;
            }
            else
            {
                return _media[_mainMediaIndex].Control.GetCurrentTimeMs() / 1000f;
            }
        }

        public MediaPlayer GetEquirectPlayer()
        {
            if (_media[_returnMediaIndex].Control == null)
            {
                return null;
            }
            float currentBlendVal = _mesh.material.GetFloat(_propBlend);
            if (currentBlendVal < 0.5f)
            {
                return _media[_sideMediaIndex];
            }
            else
            {
                return _media[_mainMediaIndex];
            }
        }

        public void ForceUpdate()
		{
			_isDirty = true;
			LateUpdate();
		}

        public bool SyncMediaPlayers()
        {
            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
            }

            // Sync two media players.
            if (_sideMediaIndex < 0 || _media[_mainMediaIndex].Control == null || _media[_sideMediaIndex].Control == null)
            {
                Debug.Log("Side media index: " + _sideMediaIndex);
                return true;
            }

            if (_media[_returnMediaIndex].Info.GetVideoFrameRate() > 0)
            {
                float FrameDurationSec = 1.0f / Player.Info.GetVideoFrameRate();
                if (Player.Control.GetCurrentTimeMs() / 1000f - _mediaOffset < 0 || (isTransitioning && Mathf.Approximately(Player.Control.GetCurrentTimeMs() / 1000f, Player.Info.GetDurationMs() / 1000)))
                {
                    return true;
                }
                else if ((Player.Control.GetCurrentTimeMs() / 1000f - OtherPlayer.Control.GetCurrentTimeMs() / 1000f) * _mediaOffset < 0 ||
                    Mathf.Abs((Player.Control.GetCurrentTimeMs() / 1000f - OtherPlayer.Control.GetCurrentTimeMs() / 1000f) - _mediaOffset) >= FrameDurationSec)  // Should have same sign.
                {
                    this.Pause();
                    if (Player.Control.GetCurrentTimeMs() / 1000f - _mediaOffset >= 0f)
                    {
                        OtherPlayer.Control.Seek((Player.Control.GetCurrentTimeMs() / 1000f - _mediaOffset) * 1000f);
                    }

                    UpdateAudio();
                    if (GetComponent<PreviewMode>().requestedPlayMode)
                    {
                        this.Play();
                    }
                }
                float offsetSec = Mathf.Abs(Player.Control.GetCurrentTimeMs() / 1000f - OtherPlayer.Control.GetCurrentTimeMs() / 1000f);
                return offsetSec * _mediaOffset >= 0 && Mathf.Abs(offsetSec - _mediaOffset) < FrameDurationSec;
            }
            return true;
        }

        // We do a LateUpdate() to allow for any changes in the texture that may have happened in Update()
        private void LateUpdate()
		{
			bool applied = false;

            // Try to apply texture from media
            if (_media != null && _media[_mainMediaIndex].TextureProducer != null)
			{
                Texture resamplerTex = _media[_mainMediaIndex].FrameResampler == null || _media[_mainMediaIndex].FrameResampler.OutputTexture == null ? null : _media[_mainMediaIndex].FrameResampler.OutputTexture[0];
				Texture texture = _media[_mainMediaIndex].m_Resample ? resamplerTex : _media[_mainMediaIndex].TextureProducer.GetTexture(0);

				if (texture != null)
				{
					// Check for changing texture
					if (texture != _lastTextureApplied)
					{
						_isDirty = true;
					}

					if (_isDirty)
					{
						int planeCount = _media[_mainMediaIndex].m_Resample ? 1 : _media[_mainMediaIndex].TextureProducer.GetTextureCount();
						for (int plane = 0; plane < planeCount; plane++)
						{
                            
							Texture resamplerTexPlane = _media[_mainMediaIndex].FrameResampler == null || _media[_mainMediaIndex].FrameResampler.OutputTexture == null ? null : _media[_mainMediaIndex].FrameResampler.OutputTexture[plane];
							texture = _media[_mainMediaIndex].m_Resample ? resamplerTexPlane : _media[_mainMediaIndex].TextureProducer.GetTexture(plane);
                            if (texture != null)
							{
								ApplyMapping(texture, _media[_mainMediaIndex].TextureProducer.RequiresVerticalFlip(), plane);

                                if (_sideMediaIndex >= 0)
                                {
                                    Texture resamplerTexPlane2 = _media[_sideMediaIndex].FrameResampler == null || _media[_sideMediaIndex].FrameResampler.OutputTexture == null ? null : _media[_sideMediaIndex].FrameResampler.OutputTexture[plane];
                                    Texture texture2 = _media[_sideMediaIndex].m_Resample ? resamplerTexPlane2 : _media[_sideMediaIndex].TextureProducer.GetTexture(plane);
                                    ApplyMapping(texture2, _media[_sideMediaIndex].TextureProducer.RequiresVerticalFlip(), plane, true);
                                }
                            }
						}
					}
					applied = true;
				}
			}
            if (!applied)
			{
				if (_defaultTexture != _lastTextureApplied)
				{
					_isDirty = true;
				}
				if (_isDirty)
				{
					ApplyMapping(_defaultTexture, false);
					ApplyMapping(_defaultTexture, false, 0, true);  // Apply default texture to secondary texture property too.
				}
			}
		}
		
		private void ApplyMapping(Texture texture, bool requiresYFlip, int plane = 0, bool side = false)
		{
			if (_mesh != null)
			{
				_isDirty = false;

				Material[] meshMaterials = _mesh.materials;
				if (meshMaterials != null)
				{
					for (int i = 0; i < meshMaterials.Length; i++)
					{
						Material mat = meshMaterials[i];
						if (mat != null)
						{
							if (plane == 0)
							{
#if UNITY_5_6_OR_NEWER
                                if (!side)
                                {
                                    mat.SetTexture(_propTexture, texture);
                                }
                                else
                                {
                                    mat.SetTexture(_propSideTexture, texture);
                                }
#else
                                if (!side)
								    mat.SetTexture(_texturePropertyName, texture);
                                else
                                    mat.SetTexture(_sideTexturePropertyname, texture);
#endif

								_lastTextureApplied = texture;

								if (texture != null)
								{
#if UNITY_5_6_OR_NEWER
                                    if (requiresYFlip)
									{
										mat.SetTextureScale(_propTexture, new Vector2(_scale.x, -_scale.y));
										mat.SetTextureOffset(_propTexture, Vector2.up + _offset);

                                        mat.SetTextureScale(_propSideTexture, new Vector2(_scale.x, -_scale.y));
										mat.SetTextureOffset(_propSideTexture, Vector2.up + _offset);
									}
									else
									{
                                        mat.SetTextureScale(_propTexture, _scale);
										mat.SetTextureOffset(_propTexture, _offset);

                                        mat.SetTextureScale(_propSideTexture, _scale);
                                        mat.SetTextureOffset(_propSideTexture, _offset);
                                    }
#else
									if (requiresYFlip)
									{
										mat.SetTextureScale(_texturePropertyName, new Vector2(_scale.x, -_scale.y));
										mat.SetTextureOffset(_texturePropertyName, Vector2.up + _offset);

                                        mat.SetTextureScale(_sideTexturePropertyName, new Vector2(_scale.x, -_scale.y));
										mat.SetTextureOffset(_sideTexturePropertyName, Vector2.up + _offset);
									}
									else
									{
										mat.SetTextureScale(_texturePropertyName, _scale);
										mat.SetTextureOffset(_texturePropertyName, _offset);

                                        mat.SetTextureScale(_sideTexturePropertyName, _scale);
										mat.SetTextureOffset(_sideTexturePropertyName, _offset);
									}
#endif
                                }
                            }
							else if (plane == 1)
							{
								if (mat.HasProperty(_propUseYpCbCr) && mat.HasProperty(_propChromaTex))
								{
									mat.EnableKeyword("USE_YPCBCR");
                                    Debug.Log("Setting texture plane 1");
                                    mat.SetTexture(_propChromaTex, texture);
#if UNITY_5_6_OR_NEWER
									if (requiresYFlip)
									{
										mat.SetTextureScale(_propChromaTex, new Vector2(_scale.x, -_scale.y));
										mat.SetTextureOffset(_propChromaTex, Vector2.up + _offset);
									}
									else
									{
										mat.SetTextureScale(_propChromaTex, _scale);
										mat.SetTextureOffset(_propChromaTex, _offset);
									}
#else
									if (requiresYFlip)
									{
										mat.SetTextureScale(PropChromaTexName, new Vector2(_scale.x, -_scale.y));
										mat.SetTextureOffset(PropChromaTexName, Vector2.up + _offset);
									}
									else
									{
										mat.SetTextureScale(PropChromaTexName, _scale);
										mat.SetTextureOffset(PropChromaTexName, _offset);
									}
#endif
								}
							}

							if (_media != null)
							{
								// Apply changes for layout
								if (mat.HasProperty(_propLayout))
								{
									Helper.SetupLayoutMaterial(mat, _media[_mainMediaIndex].VideoLayoutMapping);
								}
								// Apply changes for stereo videos
								if (mat.HasProperty(_propStereo))
								{
									Helper.SetupStereoMaterial(mat, _media[_mainMediaIndex].m_StereoPacking, _media[_mainMediaIndex].m_DisplayDebugStereoColorTint);
								}
								// Apply changes for alpha videos
								if (mat.HasProperty(_propAlphaPack))
								{
									Helper.SetupAlphaPackedMaterial(mat, _media[_mainMediaIndex].m_AlphaPacking);
								}
#if UNITY_PLATFORM_SUPPORTS_LINEAR
								// Apply gamma
								if (mat.HasProperty(_propApplyGamma) && _media[_mainMediaIndex].Info != null)
								{
									Helper.SetupGammaMaterial(mat, _media[_mainMediaIndex].Info.PlayerSupportsLinearColorSpace());
								}
#else
								_propApplyGamma |= 0;
#endif
							}
						}
					}
				}
			}
		}

        private void OnEnable()
		{
			if (_mesh == null)
			{
				_mesh = this.GetComponent<MeshRenderer>();
				if (_mesh == null)
				{
					Debug.LogWarning("[AVProVideo] No mesh renderer set or found in gameobject");
				}
			}

            _returnMediaIndex = _mainMediaIndex;
            _sideMediaIndex = _media.Length > 1 ? 1 : -1;
#if UNITY_5_6_OR_NEWER
			_propTexture = Shader.PropertyToID(_texturePropertyName);
			_propSideTexture = Shader.PropertyToID(_sideTexturePropertyName);
			_propBlend = Shader.PropertyToID(_blendPropertyName);
#endif

			_isDirty = true;
			if (_mesh != null)
			{
				LateUpdate();
			}
		}
		
		private void OnDisable()
		{
			ApplyMapping(_defaultTexture, false);
			ApplyMapping(_defaultTexture, false, 0, true);
		}
	}
}