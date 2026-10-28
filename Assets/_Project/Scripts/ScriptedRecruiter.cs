using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Recruteur de test, sans IA : il pose une liste de questions fixes et
/// reprend un extrait de la réponse pour montrer qu'il l'a "entendue".
/// Sert à valider tout le déroulé de l'entretien avant de brancher une vraie IA.
/// </summary>
public class ScriptedRecruiter : RecruiterBrain
{
    [SerializeField, TextArea]
    private string welcome = "Bonjour et bienvenue. Je suis ravi de vous rencontrer.";

    [SerializeField, TextArea]
    private string[] questions =
    {
        "Pour commencer, pouvez-vous vous présenter en quelques mots ?",
        "Qu'est-ce qui vous motive à postuler chez nous ?",
        "Quel est, selon vous, votre principal point fort ?",
        "Parlez-moi d'une difficulté que vous avez rencontrée et de la façon dont vous l'avez surmontée.",
        "Avez-vous des questions à me poser ?"
    };

    [SerializeField, TextArea]
    private string closing = "Merci pour cet échange, l'entretien est terminé.";

    [Tooltip("Délai simulé de \"réflexion\" du recruteur (secondes).")]
    [SerializeField] private float thinkingDelay = 1.0f;

    private int questionIndex;

    public override bool IsFinished => questionIndex >= questions.Length;

    public override async Task<string> StartInterviewAsync()
    {
        questionIndex = 0;
        await Task.Delay(Mathf.RoundToInt(thinkingDelay * 1000));
        return $"{welcome}\n\n{questions[questionIndex]}";
    }

    public override async Task<string> ReplyAsync(string candidateAnswer)
    {
        await Task.Delay(Mathf.RoundToInt(thinkingDelay * 1000));

        string ack = string.IsNullOrWhiteSpace(candidateAnswer)
            ? "Je ne vous ai pas bien entendu, mais passons à la suite."
            : $"D'accord, vous dites : « {Excerpt(candidateAnswer)} ». Merci.";

        questionIndex++;

        if (IsFinished)
            return $"{ack}\n\n{closing}";

        return $"{ack}\n\n{questions[questionIndex]}";
    }

    private static string Excerpt(string text, int maxLength = 80)
    {
        text = text.Trim();
        if (text.Length <= maxLength)
            return text;

        int cut = text.LastIndexOf(' ', maxLength);
        return text.Substring(0, cut > 0 ? cut : maxLength) + "…";
    }
}
