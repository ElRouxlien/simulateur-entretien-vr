using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simule une reconnaissance vocale au clavier, pour tester sans micro.
/// - Tape une phrase : elle s'affiche comme une transcription en cours (partielle).
/// - Entrée : la phrase est validée (résultat final).
/// - Retour arrière : efface le dernier caractère.
/// - F1 : "prononce" automatiquement une phrase de démo, mot par mot.
/// </summary>
public class KeyboardSimulationProvider : SpeechToTextProvider
{
    [Tooltip("Délai entre deux mots pour les phrases de démo (touche F1).")]
    [SerializeField] private float demoWordDelay = 0.25f;

    [Tooltip("Phrases jouées à tour de rôle avec la touche F1.")]
    [SerializeField, TextArea]
    private string[] demoPhrases =
    {
        "Bonjour, je m'appelle Julien et je suis étudiant en informatique.",
        "Je suis motivé par ce poste car il combine développement et intelligence artificielle.",
        "Mon principal point fort est ma capacité à apprendre rapidement de nouvelles technologies."
    };

    private readonly StringBuilder buffer = new StringBuilder();
    private Keyboard keyboard;
    private Coroutine demoRoutine;
    private int demoIndex;

    public override void StartListening()
    {
        if (IsListening)
            return;

        keyboard = Keyboard.current;
        if (keyboard == null)
        {
            RaiseError("Aucun clavier détecté.");
            return;
        }

        keyboard.onTextInput += OnTextInput;
        SetListening(true);
    }

    public override void StopListening()
    {
        if (keyboard != null)
            keyboard.onTextInput -= OnTextInput;

        if (demoRoutine != null)
        {
            StopCoroutine(demoRoutine);
            demoRoutine = null;
        }

        SetListening(false);
    }

    private void OnDisable()
    {
        StopListening();
    }

    private void Update()
    {
        if (!IsListening || keyboard == null || demoRoutine != null)
            return;

        if (keyboard.f1Key.wasPressedThisFrame && demoPhrases.Length > 0)
        {
            string phrase = demoPhrases[demoIndex % demoPhrases.Length];
            demoIndex++;
            demoRoutine = StartCoroutine(PlayDemoPhrase(phrase));
        }
    }

    private void OnTextInput(char c)
    {
        // Pendant une phrase de démo, on ignore la frappe.
        if (demoRoutine != null)
            return;

        switch (c)
        {
            case '\r':
            case '\n':
                Commit();
                return;

            case '\b':
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    RaisePartial(buffer.ToString());
                }
                return;
        }

        if (char.IsControl(c))
            return;

        buffer.Append(c);
        RaisePartial(buffer.ToString());
    }

    private IEnumerator PlayDemoPhrase(string phrase)
    {
        buffer.Clear();

        foreach (string word in phrase.Split(' '))
        {
            if (buffer.Length > 0)
                buffer.Append(' ');

            buffer.Append(word);
            RaisePartial(buffer.ToString());
            yield return new WaitForSeconds(demoWordDelay);
        }

        Commit();
        demoRoutine = null;
    }

    private void Commit()
    {
        string text = buffer.ToString().Trim();
        buffer.Clear();

        if (text.Length == 0)
        {
            RaisePartial(string.Empty);
            return;
        }

        RaiseFinal(text);
    }
}
