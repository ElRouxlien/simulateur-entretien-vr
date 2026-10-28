using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Gère la position des panneaux de dialogue, avec deux modes au choix :
/// - Suivi : les panneaux restent en place tant qu'on regarde à peu près devant eux,
///   et se replacent en douceur face à l'utilisateur quand il tourne la tête au-delà d'un angle ;
/// - Fixe  : les panneaux restent à leur place dans la pièce.
/// Un appui long sur le bouton du casque passe d'un mode à l'autre (choix mémorisé).
///
/// À placer sur un objet vide positionné à l'endroit de la caméra,
/// avec les canvas de dialogue comme enfants.
/// </summary>
public class FollowHeadYaw : MonoBehaviour
{
    private const string PrefKey = "panels_follow";

    [Tooltip("Caméra à suivre (Main Camera).")]
    [SerializeField] private Transform head;

    [Tooltip("Bouton du casque : un appui long change de mode (optionnel).")]
    [SerializeField] private HeadsetButton button;

    [Tooltip("Texte où afficher brièvement le mode choisi (optionnel, ex. InstructionText).")]
    [SerializeField] private TMP_Text feedbackText;

    [Tooltip("Mode au premier lancement (ensuite, le dernier choix est mémorisé).")]
    [SerializeField] private bool followByDefault = true;

    [Header("Mode suivi")]
    [Tooltip("Angle (degrés) à partir duquel les panneaux se replacent devant l'utilisateur.")]
    [SerializeField] private float triggerAngle = 30f;

    [Tooltip("Angle (degrés) en dessous duquel on considère les panneaux replacés.")]
    [SerializeField] private float stopAngle = 2f;

    [Tooltip("Durée approximative du replacement (secondes).")]
    [SerializeField] private float smoothTime = 0.35f;

    private bool follow;
    private float currentYaw;
    private float yawVelocity;
    private bool recentering;
    private Coroutine feedbackRoutine;

    public bool FollowEnabled => follow;

    private void Start()
    {
        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        follow = PlayerPrefs.GetInt(PrefKey, followByDefault ? 1 : 0) == 1;

        if (head != null)
            currentYaw = head.eulerAngles.y;

        if (button != null)
            button.LongPressed += ToggleMode;

        Apply();
    }

    private void OnDestroy()
    {
        if (button != null)
            button.LongPressed -= ToggleMode;
    }

    /// <summary>Passe du mode suivi au mode fixe, et inversement.</summary>
    public void ToggleMode()
    {
        SetFollow(!follow);
    }

    public void SetFollow(bool enabled)
    {
        follow = enabled;
        PlayerPrefs.SetInt(PrefKey, follow ? 1 : 0);
        PlayerPrefs.Save();

        // En passant en mode suivi, on ramène tout de suite les panneaux devant l'utilisateur.
        if (follow)
            recentering = true;

        string message = follow ? "Panneaux : suivent le regard" : "Panneaux : fixes";
        Debug.Log($"[Panels] {message}");
        ShowFeedback(message);
    }

    private void LateUpdate()
    {
        if (head == null)
            return;

        if (follow)
        {
            float targetYaw = head.eulerAngles.y;
            float delta = Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw));

            if (!recentering && delta > triggerAngle)
                recentering = true;

            if (recentering)
            {
                currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, smoothTime);
                if (delta < stopAngle)
                {
                    recentering = false;
                    yawVelocity = 0f;
                }
            }
        }

        Apply();
    }

    private void Apply()
    {
        if (head != null)
            transform.position = head.position;

        transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);
    }

    // ---------- Message temporaire ----------

    private void ShowFeedback(string message)
    {
        if (feedbackText == null)
            return;

        if (feedbackRoutine != null)
            StopCoroutine(feedbackRoutine);
        feedbackRoutine = StartCoroutine(FeedbackRoutine(message));
    }

    private IEnumerator FeedbackRoutine(string message)
    {
        string previous = feedbackText.text;
        feedbackText.text = message;
        yield return new WaitForSecondsRealtime(2f);

        // On ne remet l'ancien texte que si personne ne l'a changé entre-temps.
        if (feedbackText.text == message)
            feedbackText.text = previous;
        feedbackRoutine = null;
    }
}