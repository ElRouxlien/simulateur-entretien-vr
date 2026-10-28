using System;
using UnityEngine;

/// <summary>
/// Base commune à tous les moteurs de reconnaissance vocale.
/// Le reste de l'application ne connaît que cette classe : on peut donc
/// remplacer la dictée Windows (éditeur) par la reconnaissance Android
/// (téléphone) sans toucher au reste du code.
/// </summary>
public abstract class SpeechToTextProvider : MonoBehaviour
{
    /// <summary>Texte provisoire, mis à jour pendant que l'utilisateur parle.</summary>
    public event Action<string> PartialResult;

    /// <summary>Phrase terminée et validée par le moteur.</summary>
    public event Action<string> FinalResult;

    /// <summary>Le micro vient de passer en écoute (true) ou de s'arrêter (false).</summary>
    public event Action<bool> ListeningChanged;

    /// <summary>Un problème est survenu (micro indisponible, réseau, etc.).</summary>
    public event Action<string> Error;

    public bool IsListening { get; private set; }

    public abstract void StartListening();
    public abstract void StopListening();

    protected void RaisePartial(string text) => PartialResult?.Invoke(text);
    protected void RaiseFinal(string text) => FinalResult?.Invoke(text);
    protected void RaiseError(string message) => Error?.Invoke(message);

    protected void SetListening(bool listening)
    {
        if (IsListening == listening)
            return;

        IsListening = listening;
        ListeningChanged?.Invoke(listening);
    }
}
