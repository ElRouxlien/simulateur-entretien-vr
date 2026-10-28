using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Bouton du casque Cardboard (le bras qui tape l'écran), avec deux gestes :
/// - appui court  → Clicked      (activer / couper le micro)
/// - appui long   → LongPressed  (changer le mode d'affichage des panneaux)
/// Dans l'éditeur : clic gauche (court ou long).
/// </summary>
public class HeadsetButton : MonoBehaviour
{
    [Tooltip("Durée d'appui à partir de laquelle c'est un appui long (secondes).")]
    [SerializeField] private float longPressTime = 1.0f;

    [Tooltip("Délai minimum entre deux appuis pris en compte (secondes).")]
    [SerializeField] private float cooldown = 0.3f;

    public event Action Clicked;
    public event Action LongPressed;

    private bool wasDown;
    private float downTime;
    private bool longPressFired;
    private float nextAllowed;

    private void Update()
    {
        bool down = IsDown();

        // Début de l'appui
        if (down && !wasDown)
        {
            if (Time.unscaledTime >= nextAllowed)
            {
                downTime = Time.unscaledTime;
                longPressFired = false;
            }
            else
            {
                longPressFired = true; // appui ignoré (rebond)
            }
        }

        // Appui maintenu assez longtemps → appui long (déclenché sans attendre le relâchement)
        if (down && !longPressFired && Time.unscaledTime - downTime >= longPressTime)
        {
            longPressFired = true;
            Debug.Log("[Button] Appui long");
            LongPressed?.Invoke();
        }

        // Relâchement
        if (!down && wasDown)
        {
            if (!longPressFired)
            {
                Debug.Log("[Button] Appui court");
                Clicked?.Invoke();
            }
            nextAllowed = Time.unscaledTime + cooldown;
        }

        wasDown = down;
    }

    private static bool IsDown()
    {
        if (Application.isEditor)
            return Mouse.current != null && Mouse.current.leftButton.isPressed;

        var touch = Touchscreen.current;
        return touch != null && touch.primaryTouch.press.isPressed;
    }
}
