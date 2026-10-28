using System;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// Voix du recruteur.
/// - Sur le téléphone : synthèse vocale native Android (android.speech.tts.TextToSpeech), en français.
/// - Dans l'éditeur : pas de voix, on simule un temps de lecture pour tester le déroulé.
/// Speak() est une coroutine qui se termine quand la lecture est finie.
/// </summary>
public class RecruiterVoice : MonoBehaviour
{
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

    private void Start()
    {
        if (Application.platform == RuntimePlatform.Android)
            InitAndroid();
    }

    private void Update()
    {
        while (mainThread.TryDequeue(out Action a))
            a();
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