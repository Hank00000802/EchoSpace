using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace EchoSpace.VoiceInput
{
    public class VoiceInputController : MonoBehaviour
    {
        [SerializeField] Button micButton;
        [SerializeField] TMP_Text micButtonText;
        [SerializeField] TMP_InputField targetInputField;
        [SerializeField] int maxRecordSeconds = 30;
        [SerializeField] int sampleRate = 16000;
        [SerializeField] string microphoneDevice = null;
        [SerializeField] string transcribeUrl = "http://127.0.0.1:8000/transcribe";
        [SerializeField] bool appendToExistingText = true;

        private AudioClip recordingClip;
        private bool isRecording;
        private bool isTranscribing;

        private void Awake()
        {
            if (micButton == null)
            {
                Debug.LogError("[VoiceInput] micButton is not assigned.");
                return;
            }

            if (targetInputField == null)
            {
                Debug.LogError("[VoiceInput] targetInputField is not assigned.");
            }

            micButton.onClick.AddListener(OnMicButtonClicked);
            SetButtonText("Speak");
        }

        private void OnDestroy()
        {
            if (micButton != null)
            {
                micButton.onClick.RemoveListener(OnMicButtonClicked);
            }

            if (isRecording)
            {
                Microphone.End(microphoneDevice);
                isRecording = false;
            }
        }

        private void OnMicButtonClicked()
        {
            if (isTranscribing)
            {
                Debug.Log("[VoiceInput] Still transcribing, ignore click.");
                return;
            }

            if (!isRecording)
            {
                StartRecording();
            }
            else
            {
                StopRecordingAndTranscribe();
            }
        }

        private void StartRecording()
        {
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Debug.LogError("[VoiceInput] No microphone devices found.");
                SetButtonText("No Mic");
                return;
            }

            if (string.IsNullOrEmpty(microphoneDevice))
            {
                microphoneDevice = Microphone.devices[0];
            }

            Debug.Log($"[VoiceInput] Start recording on device: {microphoneDevice}");
            recordingClip = Microphone.Start(microphoneDevice, false, maxRecordSeconds, sampleRate);
            isRecording = true;
            SetButtonText("Recording...");
        }

        private void StopRecordingAndTranscribe()
        {
            if (!isRecording)
            {
                return;
            }

            int position = Microphone.GetPosition(microphoneDevice);
            Microphone.End(microphoneDevice);
            isRecording = false;

            if (recordingClip == null || position <= 0)
            {
                Debug.LogError("[VoiceInput] Recording failed or empty.");
                SetButtonText("Try Again");
                return;
            }

            AudioClip trimmed = TrimClip(recordingClip, position);
            recordingClip = null;

            byte[] wavBytes = WavUtility.FromAudioClip(trimmed);
            if (wavBytes == null || wavBytes.Length == 0)
            {
                Debug.LogError("[VoiceInput] Failed to convert AudioClip to WAV.");
                SetButtonText("Try Again");
                return;
            }

            Debug.Log($"[VoiceInput] WAV size: {wavBytes.Length} bytes, samples: {position}");
            StartCoroutine(PostWavToStt(wavBytes));
        }

        private AudioClip TrimClip(AudioClip source, int samplePosition)
        {
            int channels = source.channels;
            int samples = Mathf.Clamp(samplePosition, 1, source.samples);
            float[] data = new float[samples * channels];
            source.GetData(data, 0);

            AudioClip trimmed = AudioClip.Create(
                "VoiceInputTrimmed",
                samples,
                channels,
                source.frequency,
                false);
            trimmed.SetData(data, 0);
            return trimmed;
        }

        private IEnumerator PostWavToStt(byte[] wavBytes)
        {
            isTranscribing = true;
            SetButtonText("Transcribing...");

            WWWForm form = new WWWForm();
            form.AddBinaryData("file", wavBytes, "recording.wav", "audio/wav");

            using (UnityWebRequest request = UnityWebRequest.Post(transcribeUrl, form))
            {
                request.timeout = 60;
                Debug.Log($"[VoiceInput] POST {transcribeUrl}");
                yield return request.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool failed = request.result != UnityWebRequest.Result.Success;
#else
                bool failed = request.isNetworkError || request.isHttpError;
#endif
                if (failed)
                {
                    Debug.LogError($"[VoiceInput] STT request failed: {request.error} ({request.responseCode})");
                    SetButtonText("Try Again");
                    isTranscribing = false;
                    yield break;
                }

                string json = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                Debug.Log($"[VoiceInput] STT raw response: {json}");

                SttResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<SttResponse>(json);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[VoiceInput] Failed to parse STT JSON: {ex.Message}");
                    SetButtonText("Try Again");
                    isTranscribing = false;
                    yield break;
                }

                if (response == null || string.IsNullOrWhiteSpace(response.text))
                {
                    Debug.LogWarning("[VoiceInput] STT returned empty text.");
                    SetButtonText("Try Again");
                    isTranscribing = false;
                    yield break;
                }

                ApplyTranscript(response.text.Trim());
                SetButtonText("Speak");
            }

            isTranscribing = false;
        }

        private void ApplyTranscript(string transcript)
        {
            if (targetInputField == null)
            {
                Debug.LogError("[VoiceInput] targetInputField is not assigned; cannot apply transcript.");
                return;
            }

            if (!targetInputField.isActiveAndEnabled || !targetInputField.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[VoiceInput] Target input field is inactive; transcript stored but may not be visible.");
            }

            string oldText = targetInputField.text ?? string.Empty;
            string newText = appendToExistingText && !string.IsNullOrWhiteSpace(oldText)
                ? oldText + " " + transcript
                : transcript;

            targetInputField.SetTextWithoutNotify(newText);
            targetInputField.text = newText;
            targetInputField.caretPosition = newText.Length;
            targetInputField.selectionAnchorPosition = newText.Length;
            targetInputField.selectionFocusPosition = newText.Length;
            targetInputField.ForceLabelUpdate();

            if (targetInputField.textComponent != null)
            {
                targetInputField.textComponent.SetAllDirty();
                targetInputField.textComponent.ForceMeshUpdate();
            }

            Debug.Log($"[VoiceInput] Applied transcript. Old length={oldText.Length}, New length={newText.Length}, Field text={targetInputField.text}");
        }

        private void SetButtonText(string text)
        {
            if (micButtonText != null)
            {
                micButtonText.text = text;
            }
        }
    }
}
