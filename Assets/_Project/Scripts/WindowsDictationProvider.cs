using UnityEngine;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using UnityEngine.Windows.Speech;
#endif

/// <summary>
/// Reconnaissance vocale via la dictée intégrée de Windows.
/// Sert à tester dans l'éditeur Unity sur PC, avec le micro de l'ordinateur.
/// La langue reconnue est celle configurée dans les paramètres vocaux de Windows.
/// </summary>
public class WindowsDictationProvider : SpeechToTextProvider
{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    [Tooltip("Relance automatiquement l'écoute après un silence, pour une écoute continue.")]
    [SerializeField] private bool autoRestart = true;

    [Tooltip("Affiche dans la Console tout ce que fait la dictée (utile pour diagnostiquer).")]
    [SerializeField] private bool verboseLogs = true;

    private void Log(string message)
    {
        if (verboseLogs)
            Debug.Log($"[Dictation] {message}");
    }

    private DictationRecognizer recognizer;
    private bool wantListening;
    private bool restartRequested;

    public override void StartListening()
    {
        wantListening = true;

        if (recognizer == null)
            CreateRecognizer();

        if (recognizer.Status == SpeechSystemStatus.Failed)
        {
            Log("Moteur en échec, recréation.");
            recognizer.Dispose();
            CreateRecognizer();
        }

        if (recognizer.Status != SpeechSystemStatus.Running)
            recognizer.Start();

        Log($"Démarrage demandé. Statut du moteur : {recognizer.Status}");
        SetListening(true);
    }

    public override void StopListening()
    {
        wantListening = false;
        restartRequested = false;

        if (recognizer != null && recognizer.Status == SpeechSystemStatus.Running)
            recognizer.Stop();

        SetListening(false);
    }

    private void CreateRecognizer()
    {
        Log($"Reconnaissance vocale prise en charge sur ce PC : {PhraseRecognitionSystem.isSupported}");

        recognizer = new DictationRecognizer();

        recognizer.DictationHypothesis += text =>
        {
            Log($"Partiel : {text}");
            RaisePartial(text);
        };
        recognizer.DictationResult += (text, confidence) =>
        {
            Log($"Final ({confidence}) : {text}");
            RaiseFinal(text);
        };
        recognizer.DictationComplete += OnDictationComplete;
        recognizer.DictationError += (error, hresult) =>
        {
            Debug.LogError($"[Dictation] Erreur : {error} (code 0x{hresult:X8})");
            RaiseError($"{error} (code 0x{hresult:X8})");
        };

        Log("Moteur de dictée créé.");
    }

    private void OnDictationComplete(DictationCompletionCause cause)
    {
        Log($"Session de dictée terminée : {cause}");
        SetListening(false);

        switch (cause)
        {
            case DictationCompletionCause.Complete:
            case DictationCompletionCause.Canceled:
                // Arrêt normal (StopListening, ou sortie du mode Play).
                break;

            case DictationCompletionCause.TimeoutExceeded:
            case DictationCompletionCause.PauseLimitExceeded:
                // Silence trop long : on relance pour garder une écoute continue.
                // La relance se fait à la frame suivante (dans Update), pas dans ce callback.
                if (wantListening && autoRestart)
                    restartRequested = true;
                break;

            default:
                // Micro indisponible, réseau, etc. : on ne relance pas en boucle.
                wantListening = false;
                RaiseError($"Dictée arrêtée : {cause}");
                break;
        }
    }

    private void Update()
    {
        if (!restartRequested)
            return;

        restartRequested = false;
        if (wantListening)
            StartListening();
    }

    private void OnDestroy()
    {
        if (recognizer == null)
            return;

        if (recognizer.Status == SpeechSystemStatus.Running)
            recognizer.Stop();

        recognizer.Dispose();
        recognizer = null;
    }
#else
    public override void StartListening()
    {
        RaiseError("La dictée Windows n'est disponible que sur Windows.");
    }

    public override void StopListening()
    {
    }
#endif
}