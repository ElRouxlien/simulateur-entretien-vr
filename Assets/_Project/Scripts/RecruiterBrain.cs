using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// "Cerveau" du recruteur. Comme pour la reconnaissance vocale, le reste de
/// l'application ne connaît que cette classe : on pourra remplacer le recruteur
/// scénarisé (tests) par une vraie IA sans toucher à l'InterviewManager.
/// </summary>
public abstract class RecruiterBrain : MonoBehaviour
{
    /// <summary>Message d'accueil + première question.</summary>
    public abstract Task<string> StartInterviewAsync();

    /// <summary>Réaction du recruteur à la réponse du candidat (relance ou question suivante).</summary>
    public abstract Task<string> ReplyAsync(string candidateAnswer);

    /// <summary>L'entretien est-il terminé (après la dernière réponse) ?</summary>
    public abstract bool IsFinished { get; }

    /// <summary>Retour final sur l'entretien (points forts, points à améliorer).</summary>
    public virtual Task<string> GetFeedbackAsync()
    {
        return Task.FromResult("Le retour sur l'entretien n'est pas disponible avec ce recruteur.");
    }
}