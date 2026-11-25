using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Eén regel in een state: "als deze toets (of deze NFC-tag) komt, doe dan dit".
/// </summary>
[Serializable]
public class Toetsactie
{
    [Tooltip("Korte uitleg voor jezelf, bv. 'Bezoeker legt de brief neer'.")]
    public string omschrijving;

    [Tooltip("De toets die deze actie start. Een Bluetooth-toetsenbord, ESP32 of Arduino stuurt gewoon toetsen.")]
    public Key toets = Key.None;

    [Tooltip("Of: de code van een NFC-tag (de lezer typt die code + Enter). Laat leeg als je een toets gebruikt.\n" +
             "De code van je tag zie je op het bedienscherm en in de Console nadat je hem scant.")]
    public string nfcCode = "";

    [Tooltip("Wat er moet gebeuren: object aan/uit (SetActive), geluid afspelen (AudioSource.Play), video afspelen (VideoPlayer.Play), ...")]
    public UnityEvent acties = new UnityEvent();

    [Tooltip("Aan: na de acties gaat de productie naar 'Volgende state'. Uit: de productie blijft in deze state.")]
    public bool naarAndereState = true;

    [Tooltip("Het nummer van de state waar de productie daarna heen gaat (de eerste state is 0).")]
    public int volgendeState;
}

/// <summary>
/// Eén state = één plaatje uit je storyboard.
/// </summary>
[Serializable]
public class ProductieState
{
    [Tooltip("Naam van deze state, bv. 'Wachten op bezoeker'.")]
    public string naam;

    [Tooltip("Wat er gebeurt zodra de productie in deze state komt.")]
    public UnityEvent bijBinnenkomen = new UnityEvent();

    [Tooltip("Welke toetsen of NFC-tags in deze state iets doen. Dezelfde toets mag in een andere state iets anders doen.")]
    public List<Toetsactie> toetsen = new List<Toetsactie>();
}

/// <summary>
/// De productie bestaat uit states (de plaatjes van je storyboard).
/// Elke toetsaanslag of NFC-scan kan iets laten gebeuren en de productie naar een volgende state brengen.
/// Alles stel je in de Inspector in: je hoeft hiervoor niet te programmeren.
/// </summary>
public class ProductieStates : MonoBehaviour
{
    [Tooltip("De states van je productie, van boven naar beneden: state 0, 1, 2, ...")]
    public List<ProductieState> states = new List<ProductieState>();

    [Tooltip("Toetsen of NFC-tags die in ELKE state werken, bv. een 3D-geprint figuurtje dat je op de lezer zet.\n" +
             "Eerst kijkt de productie naar de toetsen van de huidige state, daarna naar deze lijst.")]
    public List<Toetsactie> inElkeState = new List<Toetsactie>();

    [Tooltip("In welke state de productie begint.")]
    public int startState = 0;

    [Tooltip("Optioneel: tekst op het bedienscherm die laat zien in welke state je bent en wat je laatst hebt gedrukt of gescand.")]
    public TMP_Text infoBedienscherm;

    public int HuidigeState { get; private set; }

    // NFC-lezers typen hun code heel snel achter elkaar, gevolgd door Enter.
    string nfcBuffer = "";
    float laatsteTekenTijd;
    string laatsteToets = "-";
    string laatsteNfcCode = "-";

    void OnEnable()
    {
        if (Keyboard.current != null) Keyboard.current.onTextInput += BijTeken;
    }

    void OnDisable()
    {
        if (Keyboard.current != null) Keyboard.current.onTextInput -= BijTeken;
    }

    void Start()
    {
        GaNaarState(startState);
    }

    void Update()
    {
        var toetsenbord = Keyboard.current;
        if (toetsenbord == null) return;

        // Tijdens het instellen van de beamers (F12) zijn de toetsen voor de wandindeling, niet voor de productie
        if (WandIndeling.BezigMetInstellen) { wachtendeToets = Key.None; nfcBuffer = ""; return; }

        float nu = Time.unscaledTime;
        foreach (var knop in toetsenbord.allKeys)
        {
            if (knop == null || !knop.wasPressedThisFrame) continue;

            if (knop.keyCode == Key.Enter || knop.keyCode == Key.NumpadEnter)
            {
                // Enter na een snelle reeks tekens = een NFC-code
                bool wasCode = inReeks && nfcBuffer.Length >= 4;
                if (wasCode) VerwerkNfcCode(nfcBuffer);
                nfcBuffer = ""; inReeks = false; wachtendeToets = Key.None;
                if (!wasCode) VerwerkToets(knop.keyCode);
                laatsteToetsTijd = nu;
                continue;
            }

            // Een NFC-lezer typt zijn code heel snel. Toetsen die snel achter elkaar komen, horen bij een code
            // en worden NIET als losse toets uitgevoerd (de code bevat soms letters zoals A of F).
            if (nu - laatsteToetsTijd < ReeksTijd) { inReeks = true; wachtendeToets = Key.None; }
            else if (inReeks && nu - laatsteToetsTijd > 0.5f) inReeks = false;
            if (!inReeks) { wachtendeToets = knop.keyCode; wachtSinds = nu; }
            laatsteToetsTijd = nu;
        }

        // Een losse toets voeren we pas uit als er heel even niets na kwam
        if (wachtendeToets != Key.None && nu - wachtSinds >= ReeksTijd)
        {
            var toets = wachtendeToets;
            wachtendeToets = Key.None;
            VerwerkToets(toets);
        }
    }

    const float ReeksTijd = 0.08f;
    Key wachtendeToets = Key.None;
    float wachtSinds, laatsteToetsTijd = -1f;
    bool inReeks;

    void BijTeken(char teken)
    {
        if (char.IsControl(teken) || WandIndeling.BezigMetInstellen) return;
        // Meer dan een halve seconde stil? Dan begint er een nieuwe code.
        if (Time.unscaledTime - laatsteTekenTijd > 0.5f) nfcBuffer = "";
        laatsteTekenTijd = Time.unscaledTime;
        nfcBuffer += teken;
    }

    /// <summary>Gaat naar een andere state en voert de acties 'bij binnenkomen' uit. Je kunt dit ook vanuit een UnityEvent aanroepen.</summary>
    public void GaNaarState(int nummer)
    {
        if (nummer < 0 || nummer >= states.Count)
        {
            Debug.LogWarning("ProductieStates: state " + nummer + " bestaat niet (er zijn " + states.Count + " states).");
            return;
        }
        HuidigeState = nummer;
        Debug.Log("Naar state " + nummer + ": " + states[nummer].naam);
        states[nummer].bijBinnenkomen.Invoke();
        ToonInfo();
    }

    void VerwerkToets(Key toets)
    {
        laatsteToets = toets.ToString();
        foreach (var lijst in new[] { states[HuidigeState].toetsen, inElkeState })
        {
            foreach (var actie in lijst)
            {
                if (actie.toets == toets && string.IsNullOrWhiteSpace(actie.nfcCode))
                {
                    Voer(actie);
                    return;
                }
            }
        }
        ToonInfo();
    }

    void VerwerkNfcCode(string code)
    {
        code = code.Trim();
        laatsteNfcCode = code;
        Debug.Log("NFC-code gelezen: " + code + "  (kopieer deze code naar 'Nfc Code' in de Inspector)");
        foreach (var lijst in new[] { states[HuidigeState].toetsen, inElkeState })
        {
            foreach (var actie in lijst)
            {
                if (!string.IsNullOrWhiteSpace(actie.nfcCode) &&
                    string.Equals(actie.nfcCode.Trim(), code, StringComparison.OrdinalIgnoreCase))
                {
                    Voer(actie);
                    return;
                }
            }
        }
        ToonInfo();
    }

    void Voer(Toetsactie actie)
    {
        actie.acties.Invoke();
        if (actie.naarAndereState) GaNaarState(actie.volgendeState);
        else ToonInfo();
    }

    void ToonInfo()
    {
        if (infoBedienscherm == null || states.Count == 0) return;
        var state = states[HuidigeState];
        string tekst = "State " + HuidigeState + ": " + state.naam +
                       "\nLaatste toets: " + laatsteToets +
                       "\nLaatste NFC-code: " + laatsteNfcCode +
                       "\n\nIn deze state:";
        foreach (var actie in state.toetsen)
        {
            string wat = string.IsNullOrWhiteSpace(actie.nfcCode) ? actie.toets.ToString() : "NFC " + actie.nfcCode;
            tekst += "\n  " + wat + "  →  " + actie.omschrijving;
        }
        if (inElkeState.Count > 0) tekst += "\nIn elke state:";
        foreach (var actie in inElkeState)
        {
            string wat = string.IsNullOrWhiteSpace(actie.nfcCode) ? actie.toets.ToString() : "NFC " + actie.nfcCode;
            tekst += "\n  " + wat + "  →  " + actie.omschrijving;
        }
        infoBedienscherm.text = tekst;
    }
}
