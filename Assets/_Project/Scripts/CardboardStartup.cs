using Google.XR.Cardboard;
using UnityEngine;

/// <summary>
/// Gère le cycle de vie du casque Cardboard (repris de la démo HelloCardboard) :
/// - au premier lancement, ouvre le scan du QR code du casque (ou "Passer" pour les réglages par défaut) ;
/// - met à jour les paramètres d'écran à chaque frame (indispensable pour l'affichage) ;
/// - gère les boutons engrenage (re-scanner le casque) et croix (quitter).
/// À placer sur un objet de la scène, une seule fois.
/// </summary>
public class CardboardStartup : MonoBehaviour
{
    private void Start()
    {
        // L'écran ne doit pas se mettre en veille pendant un entretien.
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Screen.brightness = 1.0f;

        if (Application.isEditor)
            return;

        // Premier lancement : aucun casque enregistré → scan du QR code.
        if (!Api.HasDeviceParams())
            Api.ScanDeviceParams();
    }

    private void Update()
    {
        if (Application.isEditor)
            return;

        if (Api.IsGearButtonPressed)
            Api.ScanDeviceParams();

        if (Api.IsCloseButtonPressed)
            Application.Quit();

        if (Api.HasNewDeviceParams())
            Api.ReloadDeviceParams();

        Api.UpdateScreenParams();
    }
}
