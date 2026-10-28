using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Déroulé de l'entretien :
/// 1. le recruteur pose une question et la lit à voix haute (micro coupé, bouton inactif) ;
/// 2. le candidat active le micro (appui court sur le bouton du casque, ou clic gauche dans l'éditeur) et répond ;
/// 3. il coupe le micro : la réponse part au recruteur, qui réagit et relance ;
/// 4. on recommence jusqu'à la fin de l'entretien ;
/// 5. l'IA analyse tout l'entretien et affiche un retour (points forts / à améliorer) ;
/// 6. un appui relance un nouvel entretien.
/// </summary>
public class InterviewManager : MonoBehaviour
{
    private enum State { RecruiterThinking, RecruiterSpeaking, WaitingForCandidate, Listening, GeneratingFeedback, Finished }

    [Header("Références")]
    [SerializeField] private TranscriptDisplay transcript;
    [SerializeField] private RecruiterBrain recruiter;
    [SerializeField] private TMP_Text recruiterText;
    [SerializeField] private TMP_Text instructionText;
    [Tooltip("Voix du recruteur (optionnel : sans voix, on peut répondre tout de suite).")]
    [SerializeField] private RecruiterVoice voice;
    [Tooltip("Bouton du casque (appui court = micro).")]
    [SerializeField] private HeadsetButton button;

    [Header("Feedback final")]
    [Tooltip("Panneau affiché à la fin de l'entretien (caché au départ).")]
    [SerializeField] private GameObject feedbackPanel;
    [SerializeField] private TMP_Text feedbackText;
    [Tooltip("Panneau de transcription, caché pendant le feedback (optionnel).")]
    [SerializeField] private GameObject transcriptPanel;
    [Tooltip("Panneau des questions du recruteur, caché pendant le feedback (optionnel).")]
    [SerializeField] private GameObject recruiterPanel;
    [Tooltip("Texte d'instruction dans le panneau de feedback (« Appuyez pour recommencer »).")]
    [SerializeField] private TMP_Text feedbackInstructionText;

    [Header("Réglages")]
    [Tooltip("Temps max d'attente du texte final après la coupure du micro (secondes).")]
    [SerializeField] private float finalResultTimeout = 1.5f;


    private State state;
    private SpeechToTextProvider speech;
    private readonly StringBuilder answer = new StringBuilder();
    private string lastPartial = string.Empty;

    private void Start()
    {
        speech = transcript != null ? transcript.Provider : null;
        if (speech == null)
        {
            Debug.LogError("[Interview] Aucun moteur de reconnaissance vocale.");
            return;
        }

        speech.PartialResult += OnPartial;
        speech.FinalResult += OnFinal;

        if (button != null)
            button.Clicked += OnButtonClicked;
        else
            Debug.LogError("[Interview] Aucun HeadsetButton assigné.");

        if (feedbackPanel != null)
            feedbackPanel.SetActive(false);

        StartCoroutine(BeginInterview());
    }

    private void OnDestroy()
    {
        if (speech != null)
        {
            speech.PartialResult -= OnPartial;
            speech.FinalResult -= OnFinal;
        }

        if (button != null)
            button.Clicked -= OnButtonClicked;
    }

    private void OnButtonClicked()
    {
        Debug.Log($"[Interview] Appui détecté (état : {state})");
        ToggleMic();
    }

    // ---------- Déroulé ----------

    private IEnumerator BeginInterview()
    {
        SetState(State.RecruiterThinking);
        if (recruiterText != null)
            recruiterText.text = "<i>...</i>";
        var task = recruiter.StartInterviewAsync();
        yield return new WaitUntil(() => task.IsCompleted);
        yield return PresentRecruiter(task);
    }

    /// <summary>Appelé à chaque appui sur le bouton.</summary>
    public void ToggleMic()
    {
        if (state == State.Finished)
        {
            Debug.Log("[Interview] Nouvel entretien.");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        if (state == State.WaitingForCandidate)
            StartAnswer();
        else if (state == State.Listening)
            StartCoroutine(EndAnswer());
    }

    private void StartAnswer()
    {
        answer.Clear();
        lastPartial = string.Empty;
        transcript.Clear();

        SetState(State.Listening);
        speech.StartListening();
    }

    private IEnumerator EndAnswer()
    {
        SetState(State.RecruiterThinking);
        speech.StopListening();

        // Android envoie le texte final juste après l'arrêt : on l'attend un peu.
        float deadline = Time.unscaledTime + finalResultTimeout;
        while (lastPartial.Length > 0 && Time.unscaledTime < deadline)
            yield return null;

        if (lastPartial.Length > 0)
            AppendToAnswer(lastPartial);

        string candidateAnswer = answer.ToString();

        // Rien entendu : on n'envoie rien à l'IA, on laisse le candidat réessayer.
        if (string.IsNullOrWhiteSpace(candidateAnswer))
        {
            Debug.Log("[Interview] Réponse vide, rien n'est envoyé.");
            SetState(State.WaitingForCandidate);
            if (instructionText != null)
                instructionText.text = "Je ne vous ai pas entendu. Appuyez pour répondre à nouveau.";
            yield break;
        }

        Debug.Log($"[Interview] Réponse envoyée : {candidateAnswer}");

        var task = recruiter.ReplyAsync(candidateAnswer);
        yield return new WaitUntil(() => task.IsCompleted);
        yield return PresentRecruiter(task);
    }

    /// <summary>Affiche le message du recruteur, le fait lire à voix haute, puis rend la main au candidat.</summary>
    private IEnumerator PresentRecruiter(System.Threading.Tasks.Task<string> task)
    {
        if (task.IsFaulted)
        {
            Debug.LogError($"[Interview] Erreur du recruteur : {task.Exception?.GetBaseException().Message}");
            recruiterText.text = "<color=#FF6B6B>Le recruteur n'a pas pu répondre. Réessayez.</color>";
            SetState(State.WaitingForCandidate);
            yield break;
        }

        recruiterText.text = task.Result;

        // Le candidat ne peut répondre qu'une fois la lecture terminée.
        if (voice != null)
        {
            SetState(State.RecruiterSpeaking);
            yield return voice.Speak(task.Result);
        }

        if (recruiter.IsFinished)
        {
            yield return ShowFeedback();
            yield break;
        }

        SetState(State.WaitingForCandidate);
    }

    /// <summary>Demande à l'IA un retour sur tout l'entretien et l'affiche.</summary>
    private IEnumerator ShowFeedback()
    {
        // On cache les panneaux de questions / réponses : seul le feedback reste affiché.
        if (transcriptPanel != null)
            transcriptPanel.SetActive(false);
        if (recruiterPanel != null)
            recruiterPanel.SetActive(false);
        if (feedbackPanel != null)
            feedbackPanel.SetActive(true);

        // Les instructions s'affichent désormais dans le panneau de feedback.
        if (feedbackInstructionText != null)
            instructionText = feedbackInstructionText;

        SetState(State.GeneratingFeedback);

        if (feedbackText != null)
            feedbackText.text = "<i>Analyse de votre entretien en cours...</i>";

        var task = recruiter.GetFeedbackAsync();
        yield return new WaitUntil(() => task.IsCompleted);

        if (feedbackText != null)
        {
            if (task.IsFaulted)
            {
                Debug.LogError($"[Interview] Erreur du feedback : {task.Exception?.GetBaseException().Message}");
                feedbackText.text = "<color=#FF6B6B>Le retour n'a pas pu être généré.</color>";
            }
            else
            {
                feedbackText.text = task.Result;
            }
        }

        SetState(State.Finished);
    }

    // ---------- Reconnaissance vocale ----------

    private void OnPartial(string text)
    {
        if (state == State.Listening)
            lastPartial = text ?? string.Empty;
        // Après la coupure du micro, un texte vide signifie "rien de plus" :
        // on garde le dernier texte partiel pour ne pas perdre la fin de la réponse.
        else if (state == State.RecruiterThinking && !string.IsNullOrEmpty(text))
            lastPartial = text;
    }

    private void OnFinal(string text)
    {
        if (state != State.Listening && state != State.RecruiterThinking)
            return;

        AppendToAnswer(text);
        lastPartial = string.Empty;
    }

    private void AppendToAnswer(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (answer.Length > 0)
            answer.Append(' ');
        answer.Append(text.Trim());
    }

    // ---------- Affichage ----------

    private void SetState(State newState)
    {
        state = newState;
        Debug.Log($"[Interview] État : {state}");

        if (instructionText != null)
        {
            instructionText.text = state switch
            {
                State.WaitingForCandidate => "Appuyez sur le bouton pour répondre",
                State.Listening => "Appuyez à nouveau quand vous avez terminé",
                State.RecruiterThinking => "Le recruteur réfléchit...",
                State.RecruiterSpeaking => "Le recruteur parle...",
                State.GeneratingFeedback => "Le recruteur prépare son retour...",
                State.Finished => "Appuyez sur le bouton pour recommencer un entretien",
                _ => "Entretien terminé"
            };
        }

    }
}