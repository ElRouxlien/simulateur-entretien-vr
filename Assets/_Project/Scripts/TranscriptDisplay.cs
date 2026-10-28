using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Affiche la transcription sur le panneau VR :
/// les phrases validées en blanc, la phrase en cours en gris.
/// Garde tout l'historique en mémoire (utile plus tard pour l'IA),
/// mais n'affiche que la fin pour que le texte "défile".
/// </summary>
public class TranscriptDisplay : MonoBehaviour
{
    [Header("Moteurs de reconnaissance")]
    [Tooltip("Utilisé dans l'éditeur Unity (clavier ou dictée Windows).")]
    [SerializeField] private SpeechToTextProvider editorProvider;

    [Tooltip("Utilisé sur le téléphone Android.")]
    [SerializeField] private AndroidSpeechProvider androidProvider;

    [Header("Affichage")]
    [SerializeField] private TMP_Text transcriptText;
    [SerializeField] private TMP_Text statusText;

    [Header("Réglages")]
    [SerializeField] private bool startListeningOnPlay = false;
    [SerializeField] private int maxVisibleCharacters = 400;
    [SerializeField] private string placeholder = "Parlez, votre texte s'affichera ici...";

    private readonly StringBuilder fullTranscript = new StringBuilder();
    private string currentPartial = string.Empty;
    private SpeechToTextProvider speechProvider;

    private void Awake()
    {
        // Choix automatique du moteur selon la plateforme.
#if UNITY_ANDROID && !UNITY_EDITOR
        speechProvider = androidProvider;
#else
        speechProvider = editorProvider;
#endif
    }

    /// <summary>Tout ce qui a été dit depuis le début.</summary>
    public string FullTranscript => fullTranscript.ToString();

    /// <summary>Moteur de reconnaissance choisi pour cette plateforme.</summary>
    public SpeechToTextProvider Provider => speechProvider;

    /// <summary>Efface le panneau (nouvelle réponse du candidat).</summary>
    public void Clear()
    {
        fullTranscript.Clear();
        currentPartial = string.Empty;
        RefreshTranscript();
    }

    private void OnEnable()
    {
        if (speechProvider == null)
            return;

        speechProvider.PartialResult += HandlePartial;
        speechProvider.FinalResult += HandleFinal;
        speechProvider.ListeningChanged += HandleListeningChanged;
        speechProvider.Error += HandleError;
    }

    private void OnDisable()
    {
        if (speechProvider == null)
            return;

        speechProvider.PartialResult -= HandlePartial;
        speechProvider.FinalResult -= HandleFinal;
        speechProvider.ListeningChanged -= HandleListeningChanged;
        speechProvider.Error -= HandleError;
    }

    private void Start()
    {
        Debug.Log($"[Speech] TranscriptDisplay démarré. Moteur choisi : {(speechProvider != null ? speechProvider.GetType().Name : "AUCUN")}");

        RefreshTranscript();
        HandleListeningChanged(false);

        if (speechProvider == null)
        {
            HandleError("aucun moteur de reconnaissance vocale assigné.");
            return;
        }

        if (startListeningOnPlay)
            speechProvider.StartListening();
    }

    private void HandlePartial(string text)
    {
        currentPartial = text;
        RefreshTranscript();
    }

    private void HandleFinal(string text)
    {
        if (fullTranscript.Length > 0)
            fullTranscript.Append(' ');

        fullTranscript.Append(text);
        currentPartial = string.Empty;
        RefreshTranscript();
    }

    private void HandleListeningChanged(bool listening)
    {
        if (statusText == null)
            return;

        statusText.text = listening
            ? "<color=#6BE675>Micro : à l'écoute</color>"
            : "<color=#AAAAAA>Micro : inactif</color>";
    }

    private void HandleError(string message)
    {
        Debug.LogWarning($"[Speech] {message}");

        if (statusText != null)
            statusText.text = $"<color=#FF6B6B>Erreur : {message}</color>";
    }

    private void RefreshTranscript()
    {
        if (transcriptText == null)
            return;

        string committed = fullTranscript.ToString();

        if (committed.Length == 0 && currentPartial.Length == 0)
        {
            transcriptText.text = $"<color=#888888>{placeholder}</color>";
            return;
        }

        // On réserve la place pour la phrase en cours, et on garde la fin du reste.
        int budget = maxVisibleCharacters - currentPartial.Length;
        string visible = KeepEnd(committed, budget);

        if (currentPartial.Length > 0)
        {
            if (visible.Length > 0)
                visible += " ";
            visible += $"<color=#AAAAAA>{currentPartial}</color>";
        }

        transcriptText.text = visible;
    }

    /// <summary>Garde les derniers caractères du texte, en coupant sur un mot entier.</summary>
    private static string KeepEnd(string text, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;

        if (text.Length <= maxLength)
            return text;

        string end = text.Substring(text.Length - maxLength);
        int firstSpace = end.IndexOf(' ');
        if (firstSpace >= 0 && firstSpace < end.Length - 1)
            end = end.Substring(firstSpace + 1);

        return "… " + end;
    }
}
