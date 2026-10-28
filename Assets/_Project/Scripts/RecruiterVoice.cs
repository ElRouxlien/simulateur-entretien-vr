using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Voix du recruteur.
/// 1. Voix naturelle ElevenLabs (si une clé est configurée) : l'audio est joué par Unity,
///    depuis la bouche du recruteur, ce qui permet un lipsync basé sur le volume réel.
/// 2. En secours (pas de clé, quota épuisé, pas de réseau) :
///    - sur le téléphone : synthèse vocale native Android ;
///    - dans l'éditeur : lecture simulée (temps d'attente).
/// Speak() est une coroutine qui se termine quand la lecture est finie.
/// </summary>
public class RecruiterVoice : MonoBehaviour
{
    private const string ElevenLabsUrl = "https://api.elevenlabs.io/v1/text-to-speech/";
    private const int SampleRate = 22050; // format PCM 16 bits demandé à ElevenLabs

    [Header("Voix naturelle (ElevenLabs)")]
    [Tooltip("Configuration contenant la clé ElevenLabs (le même asset que pour Groq).")]
    [SerializeField] private ApiConfig config;

    [Tooltip("Source audio placée sur la tête du recruteur (le son vient de sa bouche).")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("Temps maximum pour recevoir l'audio (secondes).")]
    [SerializeField] private float cloudTimeout = 20f;

    [Tooltip("Amplification du volume mesuré pour le lipsync.")]
    [SerializeField] private float levelGain = 6f;

    [Header("Voix Android (secours)")]
    [Tooltip("Langue de la voix (ex. fr / FR).")]
    [SerializeField] private string language = "fr";
    [SerializeField] private string country = "FR";

    [Tooltip("Vitesse de lecture (1 = normale).")]
    [SerializeField, Range(0.5f, 2f)] private float speechRate = 1.0f;

    [Tooltip("Hauteur de la voix (1 = normale).")]
    [SerializeField, Range(0.5f, 2f)] private float pitch = 1.0f;

    [Header("Éditeur (pas de voix)")]
    [Tooltip("Durée simulée par caractère dans l'éditeur (secondes). 0 = pas d'attente.")]
    [SerializeField] private float editorSecondsPerChar = 0.03f;

    [Tooltip("Temps maximum d'attente avant que la lecture démarre (secondes).")]
    [SerializeField] private float startTimeout = 3f;

    [Tooltip("Durée maximum d'une lecture, par sécurité (secondes).")]
    [SerializeField] private float maxDuration = 60f;

    // Les callbacks Android arrivent sur un autre thread : file vers le thread Unity.
    private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();

    private AndroidJavaObject activity;
    private AndroidJavaObject tts;
    private bool ready;
    private bool failed;
    private int utteranceCount;

    /// <summary>Vrai pendant que le recruteur parle (utilisé pour le lipsync).</summary>
    public bool IsSpeaking { get; private set; }

    /// <summary>Vrai quand l'audio passe par Unity : le volume réel est alors disponible.</summary>
    public bool HasAudioLevel => audioSource != null && audioSource.isPlaying;

    /// <summary>Vrai pendant le téléchargement de l'audio (le recruteur n'a pas encore commencé à parler).</summary>
    public bool IsLoadingAudio { get; private set; }

    /// <summary>Volume actuel de la voix (0 à 1), pour le lipsync.</summary>
    public float AudioLevel { get; private set; }

    private readonly float[] levelBuffer = new float[256];
    private bool cloudDisabled; // désactivé pour la session après une erreur de clé ou de quota

    private void Start()
    {
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f; // son 3D : il vient de la position du recruteur
        }

        // La voix Android sert de secours : on la prépare même si ElevenLabs est configuré.
        if (Application.platform == RuntimePlatform.Android)
            InitAndroid();
    }

    private void Update()
    {
        while (mainThread.TryDequeue(out Action a))
            a();

        UpdateAudioLevel();
    }

    private void UpdateAudioLevel()
    {
        if (!HasAudioLevel)
        {
            AudioLevel = 0f;
            return;
        }

        audioSource.GetOutputData(levelBuffer, 0);
        float sum = 0f;
        for (int i = 0; i < levelBuffer.Length; i++)
            sum += levelBuffer[i] * levelBuffer[i];

        float rms = Mathf.Sqrt(sum / levelBuffer.Length);
        AudioLevel = Mathf.Clamp01(rms * levelGain);
    }

    private void OnDestroy()
    {
        if (tts == null)
            return;

        try
        {
            tts.Call<int>("stop");
            tts.Call("shutdown");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Voice] Arrêt : {e.Message}");
        }
    }

    // ---------- API ----------

    /// <summary>Lit le texte à voix haute et attend la fin de la lecture.</summary>
    public IEnumerator Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        IsSpeaking = true;
        try
        {
            yield return SpeakRoutine(text);
        }
        finally
        {
            IsSpeaking = false;
        }
    }

    private IEnumerator SpeakRoutine(string text)
    {
        // 1. Voix naturelle si possible
        if (CloudAvailable())
        {
            var result = new CloudResult();
            yield return SpeakElevenLabs(text, result);
            if (result.success)
                yield break;

            Debug.LogWarning("[Voice] Voix ElevenLabs indisponible, passage à la voix de secours.");
        }

        // 2. Voix de secours
        yield return SpeakFallback(text);
    }

    // ---------- ElevenLabs ----------

    private class CloudResult
    {
        public bool success;
    }

    private bool CloudAvailable()
    {
        return !cloudDisabled
               && config != null
               && !string.IsNullOrWhiteSpace(config.elevenLabsApiKey)
               && !string.IsNullOrWhiteSpace(config.elevenLabsVoiceId)
               && audioSource != null;
    }

    private IEnumerator SpeakElevenLabs(string text, CloudResult result)
    {
        string url = $"{ElevenLabsUrl}{config.elevenLabsVoiceId}?output_format=pcm_{SampleRate}";
        string json = "{\"text\":" + JsonString(text) +
                      ",\"model_id\":" + JsonString(config.elevenLabsModel) + "}";

        float requestStart = Time.realtimeSinceStartup;

        using (var www = new UnityWebRequest(url, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("xi-api-key", config.elevenLabsApiKey);
            www.timeout = Mathf.CeilToInt(cloudTimeout);

            IsLoadingAudio = true;
            yield return www.SendWebRequest();
            IsLoadingAudio = false;

            if (www.result != UnityWebRequest.Result.Success)
            {
                long code = www.responseCode;
                Debug.LogWarning($"[Voice] Erreur ElevenLabs ({code}) : {www.error} {www.downloadHandler?.text}");

                // Clé invalide, quota épuisé ou accès refusé : inutile de réessayer à chaque phrase.
                if (code == 401 || code == 402 || code == 403 || code == 429)
                {
                    cloudDisabled = true;
                    Debug.LogWarning("[Voice] ElevenLabs désactivé pour cette session (clé ou quota).");
                }
                yield break;
            }

            byte[] pcm = www.downloadHandler.data;
            if (pcm == null || pcm.Length < 2)
            {
                Debug.LogWarning("[Voice] ElevenLabs : audio vide.");
                yield break;
            }

            AudioClip clip = PcmToClip(pcm);
            Debug.Log($"[Voice] ElevenLabs : {clip.length:0.0} s d'audio reçus en " +
                      $"{Time.realtimeSinceStartup - requestStart:0.0} s.");

            audioSource.clip = clip;
            audioSource.Play();
            result.success = true;

            float endDeadline = Time.unscaledTime + clip.length + 2f;
            while (audioSource.isPlaying && Time.unscaledTime < endDeadline)
                yield return null;

            audioSource.Stop();
            audioSource.clip = null;
            Destroy(clip);
            Debug.Log("[Voice] Lecture terminée.");
        }
    }

    /// <summary>Convertit l'audio PCM 16 bits mono (little-endian) en AudioClip Unity.</summary>
    private static AudioClip PcmToClip(byte[] pcm)
    {
        int sampleCount = pcm.Length / 2;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short value = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8));
            samples[i] = value / 32768f;
        }

        AudioClip clip = AudioClip.Create("RecruiterVoice", sampleCount, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static string JsonString(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in value ?? string.Empty)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    // ---------- Voix de secours ----------

    private IEnumerator SpeakFallback(string text)
    {
        if (Application.platform != RuntimePlatform.Android)
        {
            // Éditeur : on simule le temps de lecture.
            float duration = Mathf.Min(text.Length * editorSecondsPerChar, maxDuration);
            Debug.Log($"[Voice] (éditeur) lecture simulée : {duration:0.0} s");
            yield return new WaitForSeconds(duration);
            yield break;
        }

        // Attendre que le moteur soit initialisé.
        float initDeadline = Time.unscaledTime + 5f;
        while (!ready && !failed && Time.unscaledTime < initDeadline)
            yield return null;

        if (!ready)
        {
            Debug.LogWarning("[Voice] Synthèse vocale indisponible, texte seulement.");
            yield break;
        }

        bool started = false;
        try
        {
            using (var bundle = new AndroidJavaObject("android.os.Bundle"))
            {
                string id = "recruiter_" + (++utteranceCount);
                // QUEUE_FLUSH = 0 : remplace toute lecture en cours.
                int result = tts.Call<int>("speak", text, 0, bundle, id);
                started = result == 0; // SUCCESS
                if (!started)
                    Debug.LogWarning($"[Voice] speak() a échoué (code {result}).");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Voice] Erreur speak : {e.Message}");
        }

        if (!started)
            yield break;

        // Attendre que la lecture démarre, puis qu'elle se termine.
        float startDeadline = Time.unscaledTime + startTimeout;
        while (!EngineIsSpeaking() && Time.unscaledTime < startDeadline)
            yield return new WaitForSecondsRealtime(0.1f);

        float endDeadline = Time.unscaledTime + maxDuration;
        while (EngineIsSpeaking() && Time.unscaledTime < endDeadline)
            yield return new WaitForSecondsRealtime(0.1f);

        Debug.Log("[Voice] Lecture terminée.");
    }

    // ---------- Android ----------

    private void InitAndroid()
    {
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                activity = player.GetStatic<AndroidJavaObject>("currentActivity");

            var listener = new InitListener(this);

            // Le moteur doit être créé sur le thread UI Android.
            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                try
                {
                    tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, listener);
                }
                catch (Exception e)
                {
                    mainThread.Enqueue(() => Fail($"Création impossible : {e.Message}"));
                }
            }));
        }
        catch (Exception e)
        {
            Fail($"Initialisation impossible : {e.Message}");
        }
    }

    private void OnInit(int status)
    {
        if (status != 0) // 0 = SUCCESS
        {
            Fail($"Initialisation échouée (statut {status}).");
            return;
        }

        try
        {
            using (var locale = new AndroidJavaObject("java.util.Locale", language, country))
            {
                int result = tts.Call<int>("setLanguage", locale);
                // -1 = données manquantes, -2 = langue non prise en charge
                if (result < 0)
                {
                    Fail($"Langue {language}-{country} indisponible (code {result}). " +
                         "Installe la voix française dans Paramètres → Synthèse vocale.");
                    return;
                }
            }

            tts.Call<int>("setSpeechRate", speechRate);
            tts.Call<int>("setPitch", pitch);

            ready = true;
            Debug.Log("[Voice] Synthèse vocale prête.");
        }
        catch (Exception e)
        {
            Fail($"Configuration impossible : {e.Message}");
        }
    }

    private bool EngineIsSpeaking()
    {
        try
        {
            return tts != null && tts.Call<bool>("isSpeaking");
        }
        catch
        {
            return false;
        }
    }

    private void Fail(string message)
    {
        failed = true;
        Debug.LogWarning($"[Voice] {message}");
    }

    /// <summary>Implémentation C# de TextToSpeech.OnInitListener.</summary>
    private class InitListener : AndroidJavaProxy
    {
        private readonly RecruiterVoice owner;

        public InitListener(RecruiterVoice owner)
            : base("android.speech.tts.TextToSpeech$OnInitListener")
        {
            this.owner = owner;
        }

        // Appelé par Android (thread UI) quand le moteur est prêt.
        public void onInit(int status)
        {
            owner.mainThread.Enqueue(() => owner.OnInit(status));
        }
    }
}