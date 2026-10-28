using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Anime le visage du recruteur avec ses blendshapes (format ARKit) :
/// - lipsync : avec la voix ElevenLabs, la bouche suit le volume réel de la voix ;
///   avec la voix Android (jouée hors de Unity), le mouvement est simulé ;
/// - clignement des yeux naturel, à intervalles aléatoires.
/// Le script cherche les blendshapes par nom dans tous les meshes du personnage
/// (tête, dents, etc.), pour que tout bouge ensemble.
/// À placer sur l'objet Recruiter.
/// </summary>
public class RecruiterFace : MonoBehaviour
{
    [Tooltip("Voix du recruteur (pour savoir quand il parle).")]
    [SerializeField] private RecruiterVoice voice;

    [Header("Noms des blendshapes")]
    [SerializeField] private string jawOpen = "jawOpen";
    [SerializeField] private string mouthFunnel = "mouthFunnel";
    [SerializeField] private string blinkLeft = "eyeBlinkLeft";
    [SerializeField] private string blinkRight = "eyeBlinkRight";

    [Header("Lipsync")]
    [Tooltip("Valeur maximale des blendshapes (100 en général dans Unity).")]
    [SerializeField] private float maxWeight = 100f;

    [Tooltip("Ouverture maximale de la bouche (0 à 1).")]
    [SerializeField, Range(0f, 1f)] private float mouthOpenAmount = 0.45f;

    [Tooltip("Nombre de syllabes par seconde (rythme de la bouche).")]
    [SerializeField] private float syllablesPerSecond = 5f;

    [Tooltip("Douceur des mouvements de la bouche.")]
    [SerializeField] private float mouthSmoothing = 18f;

    [Header("Clignement des yeux")]
    [SerializeField] private Vector2 blinkInterval = new Vector2(2f, 6f);
    [SerializeField] private float blinkDuration = 0.15f;

    private struct ShapeRef
    {
        public SkinnedMeshRenderer renderer;
        public int index;
    }

    private readonly Dictionary<string, List<ShapeRef>> shapes = new Dictionary<string, List<ShapeRef>>();

    private float mouth;      // ouverture actuelle (0..1)
    private float funnel;     // arrondi des lèvres (0..1)
    private float nextBlink;
    private float blinkStart = -1f;
    private float noiseSeed;

    private void Start()
    {
        Collect(jawOpen);
        Collect(mouthFunnel);
        Collect(blinkLeft);
        Collect(blinkRight);

        noiseSeed = Random.value * 100f;
        ScheduleBlink();

        if (!shapes.ContainsKey(jawOpen))
            Debug.LogWarning($"[Face] Blendshape « {jawOpen} » introuvable : pas de lipsync.");
    }

    private void LateUpdate()
    {
        UpdateMouth();
        UpdateBlink();
    }

    // ---------- Lipsync simulé ----------

    private void UpdateMouth()
    {
        bool speaking = voice != null && voice.IsSpeaking;
        float targetMouth = 0f;
        float targetFunnel = 0f;

        if (speaking && voice.IsLoadingAudio)
        {
            // Audio en cours de téléchargement : bouche fermée en attendant.
        }
        else if (speaking && voice.HasAudioLevel)
        {
            // Lipsync réel : l'ouverture suit le volume de la voix.
            float t = Time.time;
            targetMouth = voice.AudioLevel * mouthOpenAmount;
            targetFunnel = Mathf.Clamp01(Mathf.PerlinNoise(noiseSeed + 50f, t * 1.7f) - 0.45f) * 0.8f * voice.AudioLevel;
        }
        else if (speaking)
        {
            // Lipsync simulé (voix Android, jouée hors de Unity).
            float t = Time.time;
            // Ouverture rythmée (syllabes) + variation aléatoire, pour éviter un mouvement mécanique.
            float syllable = Mathf.Abs(Mathf.Sin(t * syllablesPerSecond * Mathf.PI));
            float variation = Mathf.PerlinNoise(noiseSeed, t * 2.3f);
            targetMouth = Mathf.Clamp01(syllable * (0.35f + 0.65f * variation)) * mouthOpenAmount;

            // De temps en temps, des lèvres un peu arrondies (« o », « ou »).
            targetFunnel = Mathf.Clamp01(Mathf.PerlinNoise(noiseSeed + 50f, t * 1.7f) - 0.45f) * 0.8f;
        }

        float k = 1f - Mathf.Exp(-mouthSmoothing * Time.deltaTime);
        mouth = Mathf.Lerp(mouth, targetMouth, k);
        funnel = Mathf.Lerp(funnel, targetFunnel, k);

        Set(jawOpen, mouth);
        Set(mouthFunnel, funnel);
    }

    // ---------- Clignement ----------

    private void UpdateBlink()
    {
        if (blinkStart < 0f && Time.time >= nextBlink)
            blinkStart = Time.time;

        float value = 0f;
        if (blinkStart >= 0f)
        {
            float p = (Time.time - blinkStart) / blinkDuration; // 0 → 1
            if (p >= 1f)
            {
                blinkStart = -1f;
                ScheduleBlink();
            }
            else
            {
                value = Mathf.Sin(p * Mathf.PI); // fermeture puis ouverture
            }
        }

        Set(blinkLeft, value);
        Set(blinkRight, value);
    }

    private void ScheduleBlink()
    {
        nextBlink = Time.time + Random.Range(blinkInterval.x, blinkInterval.y);
    }

    // ---------- Blendshapes ----------

    private void Collect(string shapeName)
    {
        if (string.IsNullOrEmpty(shapeName))
            return;

        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null)
                continue;

            int index = smr.sharedMesh.GetBlendShapeIndex(shapeName);
            if (index < 0)
                continue;

            if (!shapes.TryGetValue(shapeName, out var list))
                shapes[shapeName] = list = new List<ShapeRef>();

            list.Add(new ShapeRef { renderer = smr, index = index });
            Debug.Log($"[Face] « {shapeName} » trouvé sur {smr.name}");
        }
    }

    private void Set(string shapeName, float value01)
    {
        if (!shapes.TryGetValue(shapeName, out var list))
            return;

        float weight = Mathf.Clamp01(value01) * maxWeight;
        foreach (var s in list)
            s.renderer.SetBlendShapeWeight(s.index, weight);
    }
}