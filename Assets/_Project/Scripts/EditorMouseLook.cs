using UnityEngine;
using UnityEngine.InputSystem;

// Simule les mouvements de tête avec la souris, uniquement dans l'éditeur.
// Maintenir le clic droit et bouger la souris pour regarder autour de soi.
public class EditorMouseLook : MonoBehaviour
{
    [SerializeField] private float sensitivity = 0.15f;
    [SerializeField] private float maxPitch = 80f;

    private float yaw;
    private float pitch;

    private void Start()
    {
        // Sur le téléphone, c'est le gyroscope (Cardboard) qui gère la tête.
        if (!Application.isEditor)
        {
            enabled = false;
            return;
        }

        // Dans l'éditeur, il n'y a pas de casque : le Tracked Pose Driver
        // remettrait la rotation à zéro à chaque image et bloquerait la souris.
        foreach (var behaviour in GetComponents<Behaviour>())
        {
            if (behaviour != this && behaviour.GetType().Name == "TrackedPoseDriver")
            {
                behaviour.enabled = false;
                Debug.Log("[EditorMouseLook] Tracked Pose Driver désactivé dans l'éditeur.");
            }
        }

        Vector3 angles = transform.localEulerAngles;
        yaw = angles.y;
        pitch = angles.x > 180f ? angles.x - 360f : angles.x;
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.isPressed)
            return;

        Vector2 delta = mouse.delta.ReadValue() * sensitivity;
        yaw += delta.x;
        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);

        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}