using UnityEngine;

/// <summary>
/// Configuration des services en ligne (clés + réglages), stockée dans un asset à part.
/// L'asset contenant les clés ne doit JAMAIS être envoyé sur Git
/// (dossier Assets/_Project/Secrets, exclu par le .gitignore).
/// Créer l'asset : clic droit dans Project → Create → InterviewVR → Api Config.
/// </summary>
[CreateAssetMenu(fileName = "ApiConfig", menuName = "InterviewVR/Api Config")]
public class ApiConfig : ScriptableObject
{
    [Header("Groq (recruteur IA)")]
    [Tooltip("Clé API Groq (console.groq.com → API Keys).")]
    public string groqApiKey = "";

    [Tooltip("Modèle Groq utilisé pour le recruteur.")]
    public string groqModel = "openai/gpt-oss-120b";

    [Header("ElevenLabs (voix naturelle)")]
    [Tooltip("Clé API ElevenLabs. Laisser vide pour utiliser la voix Android.")]
    public string elevenLabsApiKey = "";

    [Tooltip("ID de la voix ElevenLabs.")]
    public string elevenLabsVoiceId = "JdwJ7jL68CWmQZuo7KgG";

    [Tooltip("Modèle ElevenLabs (eleven_flash_v2_5 : rapide et économe ; eleven_multilingual_v2 : plus naturel, 2x plus cher).")]
    public string elevenLabsModel = "eleven_flash_v2_5";
}