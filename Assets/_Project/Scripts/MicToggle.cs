using Google.XR.Cardboard;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Active / désactive le micro à chaque appui :
/// - sur le téléphone : bouton du casque (le bras tape l'écran) ;
/// - dans l'éditeur : clic gauche dans la vue Game.
/// </summary>
public class MicToggle : MonoBehaviour
{
    [SerializeField] private TranscriptDisplay transcript;

    private SpeechToTextProvider speech;

    private void Start()
    {
        speech = transcript != null ? transcript.Provider : null;
        if (speech == null)
            Debug.LogError("[MicToggle] Aucun moteur de reconnaissance vocale.");
    }

    private void Update()
    {
        if (speech == null)
            return;

        if (WasPressedThisFrame())
            Toggle();
    }

    private bool WasPressedThisFrame()
    {
        if (Application.isEditor)
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        // Appui sur l'écran détecté par le plugin Cardboard.
        return Api.IsTriggerPressed;
    }

    private void Toggle()
    {
        if (speech.IsListening)
        {
            Debug.Log("[MicToggle] Micro coupé.");
            speech.StopListening();
        }
        else
        {
            Debug.Log("[MicToggle] Micro activé.");
            speech.StartListening();
        }
    }
}
