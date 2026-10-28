using UnityEngine;

/// <summary>
/// Configuration de l'API Groq (clé + modèle), stockée dans un asset à part.
/// L'asset contenant la clé ne doit JAMAIS être envoyé sur Git
/// (dossier Assets/_Project/Secrets à ajouter au .gitignore).
/// Créer l'asset : clic droit dans Project → Create → InterviewVR → Api Config.
/// </summary>
[CreateAssetMenu(fileName = "ApiConfig", menuName = "InterviewVR/Api Config")]
public class ApiConfig : ScriptableObject
{
    [Tooltip("Clé API Groq (console.groq.com → API Keys).")]
    public string groqApiKey = "";

    [Tooltip("Modèle Groq utilisé pour le recruteur.")]
    public string groqModel = "openai/gpt-oss-120b";
}
