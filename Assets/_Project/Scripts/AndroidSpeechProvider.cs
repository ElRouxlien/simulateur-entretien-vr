using System;
using System.Collections.Concurrent;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Reconnaissance vocale native Android (android.speech.SpeechRecognizer).
/// - Demande la permission micro au premier lancement.
/// - Envoie les résultats partiels pendant que l'utilisateur parle (texte qui défile).
/// - Relance automatiquement l'écoute après chaque phrase ou silence (écoute continue).
/// Ne fonctionne que sur un vrai téléphone Android (pas dans l'éditeur).
/// </summary>
public class AndroidSpeechProvider : SpeechToTextProvider
{
#pragma warning disable 0414 // champs non utilisés dans l'éditeur
    [Tooltip("Langue de reconnaissance (ex. fr-FR, fr-BE, en-US).")]
    [SerializeField] private string languageTag = "fr-FR";

    [Tooltip("Relance l'écoute après chaque phrase ou silence.")]
    [SerializeField] private bool autoRestart = true;

    [Tooltip("Délai avant de relancer l'écoute (secondes).")]
    [SerializeField] private float restartDelay = 0.2f;

    [Tooltip("Écrit le déroulement dans les logs (visible avec Android Logcat).")]
    [SerializeField] private bool verboseLogs = true;
#pragma warning restore 0414

#if UNITY_ANDROID && !UNITY_EDITOR
    // Codes d'erreur de android.speech.SpeechRecognizer
    private const int ErrorNetworkTimeout = 1;
    private const int ErrorNetwork = 2;
    private const int ErrorAudio = 3;
    private const int ErrorServer = 4;
    private const int ErrorClient = 5;
    private const int ErrorSpeechTimeout = 6;
    private const int ErrorNoMatch = 7;
    private const int ErrorRecognizerBusy = 8;
    private const int ErrorInsufficientPermissions = 9;

    // Les callbacks Android arrivent sur le thread UI Android :
    // on les renvoie sur le thread principal Unity via cette file.
    private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

    private AndroidJavaObject activity;
    private AndroidJavaObject recognizer;
    private AndroidJavaObject recognizerIntent;
    private RecognitionListenerProxy listener;

    private bool wantListening;
    private bool permissionRequested;
    private float restartAt = -1f;

    // ---------- API publique ----------

    public override void StartListening()
    {
        wantListening = true;

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            RequestMicrophonePermission();
            return;
        }

        if (!EnsureInitialized())
            return;

        BeginSession();
    }

    public override void StopListening()
    {
        wantListening = false;
        restartAt = -1f;

        RunOnUiThread(() => recognizer?.Call("stopListening"));
        SetListening(false);
    }

    // ---------- Cycle de vie Unity ----------

    private void Update()
    {
        while (mainThreadQueue.TryDequeue(out Action action))
            action();

        if (restartAt > 0f && Time.unscaledTime >= restartAt)
        {
            restartAt = -1f;
            if (wantListening)
                BeginSession();
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (recognizer == null)
            return;

        if (paused)
        {
            restartAt = -1f;
            RunOnUiThread(() => recognizer?.Call("cancel"));
        }
        else if (wantListening)
        {
            ScheduleRestart(0.5f);
        }
    }

    private void OnDestroy()
    {
        wantListening = false;
        AndroidJavaObject toDestroy = recognizer;
        recognizer = null;

        if (toDestroy != null)
        {
            RunOnUiThread(() =>
            {
                toDestroy.Call("destroy");
                toDestroy.Dispose();
            });
        }
    }

    // ---------- Initialisation ----------

    private void RequestMicrophonePermission()
    {
        if (permissionRequested)
            return;

        permissionRequested = true;
        Log("Demande de la permission micro.");

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => Enqueue(() =>
        {
            permissionRequested = false;
            Log("Permission micro accordée.");
            StartListening();
        });
        callbacks.PermissionDenied += _ => Enqueue(() =>
        {
            permissionRequested = false;
            wantListening = false;
            SetListening(false);
            RaiseError("Permission micro refusée. Autorise-la dans les paramètres du téléphone.");
        });

        Permission.RequestUserPermission(Permission.Microphone, callbacks);
    }

    private bool EnsureInitialized()
    {
        if (activity != null)
            return true;

        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            using (var speechRecognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer"))
            {
                bool available = speechRecognizerClass.CallStatic<bool>("isRecognitionAvailable", activity);
                Log($"Reconnaissance vocale disponible : {available}");

                if (!available)
                {
                    activity = null;
                    wantListening = false;
                    RaiseError("Aucun service de reconnaissance vocale sur ce téléphone.");
                    return false;
                }
            }

            recognizerIntent = new AndroidJavaObject("android.content.Intent", "android.speech.action.RECOGNIZE_SPEECH");
            recognizerIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE_MODEL", "free_form");
            recognizerIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE", languageTag);
            recognizerIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.PARTIAL_RESULTS", true);
            recognizerIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.MAX_RESULTS", 1);

            listener = new RecognitionListenerProxy(this);
            return true;
        }
        catch (Exception e)
        {
            activity = null;
            wantListening = false;
            RaiseError($"Initialisation impossible : {e.Message}");
            return false;
        }
    }

    // ---------- Sessions d'écoute ----------

    private void BeginSession()
    {
        Log("Début d'une session d'écoute.");
        SetListening(true);

        RunOnUiThread(() =>
        {
            try
            {
                // SpeechRecognizer doit être créé et utilisé sur le thread UI Android.
                if (recognizer == null)
                {
                    using (var speechRecognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer"))
                        recognizer = speechRecognizerClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", activity);

                    recognizer.Call("setRecognitionListener", listener);
                }

                recognizer.Call("startListening", recognizerIntent);
            }
            catch (Exception e)
            {
                Enqueue(() => RaiseError($"Démarrage impossible : {e.Message}"));
            }
        });
    }

    private void ScheduleRestart(float delay)
    {
        if (wantListening && autoRestart)
            restartAt = Time.unscaledTime + delay;
        else
            SetListening(false);
    }

    // ---------- Résultats (thread principal Unity) ----------

    private void HandlePartial(string text)
    {
        if (!string.IsNullOrEmpty(text))
            RaisePartial(text);
    }

    private void HandleResults(string text)
    {
        Log($"Résultat : {text}");

        if (!string.IsNullOrEmpty(text))
            RaiseFinal(text);
        else
            RaisePartial(string.Empty);

        ScheduleRestart(restartDelay);
    }

    private void HandleError(int code)
    {
        Log($"Erreur Android {code}");

        switch (code)
        {
            case ErrorSpeechTimeout:
            case ErrorNoMatch:
            case ErrorClient:
                // Silence, rien compris, ou fin de session : cas normaux en écoute continue.
                RaisePartial(string.Empty);
                ScheduleRestart(restartDelay);
                break;

            case ErrorRecognizerBusy:
                RunOnUiThread(() => recognizer?.Call("cancel"));
                ScheduleRestart(1f);
                break;

            case ErrorInsufficientPermissions:
                wantListening = false;
                SetListening(false);
                RaiseError("Permission micro manquante.");
                break;

            case ErrorNetwork:
            case ErrorNetworkTimeout:
            case ErrorServer:
                RaiseError("Problème réseau, nouvelle tentative...");
                ScheduleRestart(2f);
                break;

            case ErrorAudio:
                RaiseError("Problème d'enregistrement audio.");
                ScheduleRestart(2f);
                break;

            default:
                RaiseError($"Erreur de reconnaissance (code {code}).");
                ScheduleRestart(2f);
                break;
        }
    }

    // ---------- Utilitaires ----------

    private void Enqueue(Action action) => mainThreadQueue.Enqueue(action);

    private void RunOnUiThread(Action action)
    {
        if (activity == null)
            return;

        activity.Call("runOnUiThread", new AndroidJavaRunnable(action));
    }

    private void Log(string message)
    {
        if (verboseLogs)
            Debug.Log($"[AndroidSpeech] {message}");
    }

    private static string ExtractText(AndroidJavaObject bundle)
    {
        if (bundle == null)
            return string.Empty;

        using (AndroidJavaObject list = bundle.Call<AndroidJavaObject>("getStringArrayList", "results_recognition"))
        {
            if (list == null || list.Call<int>("size") == 0)
                return string.Empty;

            using (AndroidJavaObject first = list.Call<AndroidJavaObject>("get", 0))
                return first?.Call<string>("toString") ?? string.Empty;
        }
    }

    /// <summary>Implémentation C# de l'interface Java android.speech.RecognitionListener.</summary>
    private class RecognitionListenerProxy : AndroidJavaProxy
    {
        private readonly AndroidSpeechProvider owner;

        public RecognitionListenerProxy(AndroidSpeechProvider owner)
            : base("android.speech.RecognitionListener")
        {
            this.owner = owner;
        }

        public override AndroidJavaObject Invoke(string methodName, AndroidJavaObject[] javaArgs)
        {
            switch (methodName)
            {
                case "onPartialResults":
                {
                    string text = ExtractText(javaArgs[0]);
                    owner.Enqueue(() => owner.HandlePartial(text));
                    return null;
                }

                case "onResults":
                {
                    string text = ExtractText(javaArgs[0]);
                    owner.Enqueue(() => owner.HandleResults(text));
                    return null;
                }

                case "onError":
                {
                    int code = javaArgs[0].Call<int>("intValue");
                    owner.Enqueue(() => owner.HandleError(code));
                    return null;
                }

                case "onReadyForSpeech":
                    owner.Enqueue(() => owner.Log("Prêt, parlez."));
                    return null;
            }

            // Autres callbacks de l'interface (onRmsChanged, onEndOfSpeech, etc.) : ignorés.
            if (methodName.StartsWith("on"))
                return null;

            // equals / hashCode / toString : gérés par Unity.
            return base.Invoke(methodName, javaArgs);
        }
    }
#else
    public override void StartListening()
    {
        RaiseError("La reconnaissance Android ne fonctionne que sur un téléphone Android.");
    }

    public override void StopListening()
    {
    }
#endif
}
