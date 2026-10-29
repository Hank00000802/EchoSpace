using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EchoSpace.UI
{
    /// <summary>
    /// 2D UI / Button one-shots. Assign clips in Inspector; Editor Play Mode
    /// can fall back to Assets/Audio/ButtonUI if fields are empty.
    /// </summary>
    public class UiSfx : MonoBehaviour
    {
        public static UiSfx Instance { get; private set; }

        private static bool s_isQuitting;

        [Header("Clips")]
        [SerializeField] private AudioClip buttonClick;
        [SerializeField] private AudioClip uiExit;
        [SerializeField] private AudioClip uiPopup;

        [Header("Playback")]
        [SerializeField] private AudioSource audioSource;
        [Range(0f, 1f)]
        [SerializeField] private float clickVolume = 1f;
        [Range(0f, 1f)]
        [SerializeField] private float exitVolume = 0.5f;
        [Range(0f, 1f)]
        [SerializeField] private float popupVolume = 0.5f;

        private const string ClickPath = "Assets/Audio/ButtonUI/ButtonClick.mp3";
        private const string ExitPath = "Assets/Audio/ButtonUI/UI_Exit.mp3";
        private const string PopupPath = "Assets/Audio/ButtonUI/UI_Popup.mp3";

        private void Awake()
        {
            s_isQuitting = false;
            if (Instance != null && Instance != this)
            {
                return;
            }

            Instance = this;
            EnsureAudioSource();
            TryLoadClipsIfMissing();
        }

        private void OnApplicationQuit()
        {
            s_isQuitting = true;
            Instance = null;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static void PlayClick()
        {
            Play(sfx => sfx.buttonClick, sfx => sfx.clickVolume);
        }

        public static void PlayExit()
        {
            Play(sfx => sfx.uiExit, sfx => sfx.exitVolume);
        }

        public static void PlayPopup()
        {
            Play(sfx => sfx.uiPopup, sfx => sfx.popupVolume);
        }

        public static void BindClick(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(PlayClick);
            button.onClick.AddListener(PlayClick);
        }

        private static void Play(System.Func<UiSfx, AudioClip> selector, System.Func<UiSfx, float> volumeSelector)
        {
            if (s_isQuitting || !Application.isPlaying)
            {
                return;
            }

            UiSfx sfx = Resolve();
            if (sfx == null)
            {
                return;
            }

            AudioClip clip = selector(sfx);
            if (clip == null)
            {
                return;
            }

            sfx.EnsureAudioSource();
            if (sfx.audioSource == null)
            {
                return;
            }

            float volume = volumeSelector != null ? Mathf.Clamp01(volumeSelector(sfx)) : 1f;
            sfx.audioSource.PlayOneShot(clip, volume);
        }

        private static UiSfx Resolve()
        {
            if (s_isQuitting || !Application.isPlaying)
            {
                return null;
            }

            if (Instance != null)
            {
                return Instance;
            }

            Instance = FindObjectOfType<UiSfx>();
            if (Instance != null)
            {
                Instance.EnsureAudioSource();
                Instance.TryLoadClipsIfMissing();
            }

            return Instance;
        }

        private void EnsureAudioSource()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.loop = false;
        }

        private void TryLoadClipsIfMissing()
        {
#if UNITY_EDITOR
            if (buttonClick == null)
            {
                buttonClick = AssetDatabase.LoadAssetAtPath<AudioClip>(ClickPath);
            }

            if (uiExit == null)
            {
                uiExit = AssetDatabase.LoadAssetAtPath<AudioClip>(ExitPath);
            }

            if (uiPopup == null)
            {
                uiPopup = AssetDatabase.LoadAssetAtPath<AudioClip>(PopupPath);
            }
#endif
        }
    }
}
