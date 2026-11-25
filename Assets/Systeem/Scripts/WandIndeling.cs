using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Koppelt de vier wanden aan de juiste beamers. Studenten hoeven hier niets mee te doen.
///
/// In Unity is wand 1 t/m 4 altijd Display 2 t/m 5. Windows nummert de beamers echter in een eigen volgorde
/// (die kan zelfs veranderen na een herstart). Daarom stel je in de build één keer in welke beamer welke wand is:
/// druk op F12, elke beamer toont een grote letter, en op het bedienscherm vul je per wand de letter in.
/// De indeling wordt bewaard in C:\Users\Public\ImmersiveRoom\wandindeling.json en geldt voor elke build op die pc.
///
/// Wanden in de Immersive Room, van bovenaf gezien met de klok mee (zoals de camera's Wand_1 t/m Wand_4):
/// 1 = de wand bij de pc, 2 = links, 3 = achterwand, 4 = kleine wand rechts (links/rechts gezien vanaf de pc).
/// </summary>
public class WandIndeling : MonoBehaviour
{
    /// <summary>Waar tijdens het instellen: de productie reageert dan niet op toetsen.</summary>
    public static bool BezigMetInstellen { get; private set; }

    const int AantalWanden = 4;
    const Key InstelToets = Key.F12;

    static readonly string[] WandUitleg =
    {
        "wand 1: de wand waar de pc tegenaan staat",
        "wand 2: de wand links (gezien vanaf de pc)",
        "wand 3: de achterwand (tegenover de pc)",
        "wand 4: de kleine wand rechts (gezien vanaf de pc)",
    };

    static readonly Color[] Kleuren =
    {
        new Color(0.85f, 0.20f, 0.20f), // A rood
        new Color(0.15f, 0.55f, 0.20f), // B groen
        new Color(0.20f, 0.35f, 0.85f), // C blauw
        new Color(0.85f, 0.60f, 0.10f), // D oranje
    };

    [Serializable]
    class Bestand
    {
        public int[] wandNaarDisplay = { 1, 2, 3, 4 }; // wand 1 t/m 4 -> Unity-display (0 = bedienscherm)
        public string ingesteldOp = "";
        public string pc = "";
    }

    static WandIndeling instantie;

    // Wand n (1-4) -> Unity-display. Standaard: wand n = Display n+1, zoals in de scene.
    int[] indeling = { 1, 2, 3, 4 };
    bool bestandGevonden;

    // Camera's en canvassen uit de scene, met hun wand volgens de scene (1-4)
    readonly Dictionary<UnityEngine.Object, int> wandVan = new Dictionary<UnityEngine.Object, int>();

    // Instelscherm
    readonly List<GameObject> testbeelden = new List<GameObject>();
    readonly List<Text> testbeeldTeksten = new List<Text>();
    GameObject bedienPaneel;
    Text bedienTekst;
    GameObject meldingNietIngesteld;
    int stap;               // welke wand we nu vragen (0-3)
    int[] gekozen;          // per wand de gekozen letter (-1 = nog niet)
    int aantalBeamers;
    float meldingTot;

    static string Map => Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? Application.persistentDataPath, "ImmersiveRoom");
    static string Pad => Path.Combine(Map, "wandindeling.json");
    // Standaard uit de template (Assets/StreamingAssets/ImmersiveRoom): de Immersive Room op school, ingesteld 25-11-2026
    static string StandaardPad => Path.Combine(Application.streamingAssetsPath, "ImmersiveRoom", "wandindeling.json");

    // In de Editor laten we de displays zoals ze zijn: daar kies je zelf Display 2-5 in de Game-weergave.
    static bool InBuild => !Application.isEditor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void StartAutomatisch()
    {
        if (instantie != null) return;
        var go = new GameObject("WandIndeling (automatisch)");
        DontDestroyOnLoad(go);
        instantie = go.AddComponent<WandIndeling>();
        go.AddComponent<WandKalibratie>();
        go.AddComponent<HoekTest>();
    }

    /// <summary>Na elke geladen scene: welke camera's en canvassen bij welke wand (1-4) horen.</summary>
    public static event Action<IReadOnlyDictionary<UnityEngine.Object, int>> SceneIngedeeld;

    /// <summary>Na F12: de beamers zijn anders aan de wanden gekoppeld.</summary>
    public static event Action IndelingGewijzigd;

    /// <summary>Op welk Unity-display (index) wand 1-4 staat.</summary>
    public static int DisplayVanWand(int wand)
    {
        if (instantie == null || wand < 1 || wand > AantalWanden) return wand;
        return InBuild ? instantie.indeling[wand - 1] : wand;
    }

    void Awake()
    {
        if (InBuild)
        {
            // Alle beamers aanzetten (ook als een scene AlleSchermenAan mist)
            for (int i = 1; i < Display.displays.Length; i++) Display.displays[i].Activate();
        }
        Laad();
        SceneManager.sceneLoaded += BijSceneGeladen;
        MaakMeldingNietIngesteld();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= BijSceneGeladen;
    }

    // ---------- Bestand ----------

    void Laad()
    {
        bestandGevonden = false;
        try
        {
            string pad = File.Exists(Pad) ? Pad : StandaardPad;
            if (!File.Exists(pad)) { Debug.Log("[WandIndeling] Nog niet ingesteld (" + Pad + " bestaat niet). Druk F12 in de build."); return; }
            var b = JsonUtility.FromJson<Bestand>(File.ReadAllText(pad));
            if (b == null || b.wandNaarDisplay == null || b.wandNaarDisplay.Length != AantalWanden)
            {
                Debug.LogWarning("[WandIndeling] " + pad + " is ongeldig; standaardindeling gebruikt.");
                return;
            }
            indeling = b.wandNaarDisplay;
            bestandGevonden = true;
            Debug.Log("[WandIndeling] Geladen uit " + pad + ": " + Beschrijf(indeling) + " (ingesteld " + b.ingesteldOp + ")");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WandIndeling] Kan " + Pad + " niet lezen: " + e.Message);
        }
    }

    bool Bewaar()
    {
        try
        {
            Directory.CreateDirectory(Map);
            var b = new Bestand { wandNaarDisplay = indeling, ingesteldOp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), pc = Environment.MachineName };
            File.WriteAllText(Pad, JsonUtility.ToJson(b, true));
            Debug.Log("[WandIndeling] Bewaard in " + Pad + ": " + Beschrijf(indeling));
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[WandIndeling] Kan " + Pad + " niet bewaren: " + e.Message);
            return false;
        }
    }

    static string Beschrijf(int[] w)
    {
        var s = "";
        for (int i = 0; i < w.Length; i++) s += (i > 0 ? ", " : "") + "wand " + (i + 1) + " = display " + (w[i] + 1);
        return s;
    }

    // ---------- Toepassen ----------

    void BijSceneGeladen(Scene scene, LoadSceneMode modus)
    {
        // Objecten van een vorige scene zijn weg
        var weg = new List<UnityEngine.Object>();
        foreach (var kv in wandVan) if (kv.Key == null) weg.Add(kv.Key);
        foreach (var o in weg) wandVan.Remove(o);

        foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // Camera's die naar een RenderTexture tekenen (bedienscherm, 3D-ogen) gaan niet naar een beamer
            // en de eigen camera's van WandIndeling/WandKalibratie (zwarte achtergrond per beamer) zijn geen wandcamera's
            if (cam.targetTexture != null || wandVan.ContainsKey(cam) || IsEigen(cam.gameObject)) continue;
            if (cam.targetDisplay >= 1 && cam.targetDisplay <= AantalWanden) wandVan[cam] = cam.targetDisplay;
        }
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || wandVan.ContainsKey(canvas) || IsEigen(canvas.gameObject)) continue;
            if (canvas.targetDisplay >= 1 && canvas.targetDisplay <= AantalWanden) wandVan[canvas] = canvas.targetDisplay;
        }
        PasToe();
        SceneIngedeeld?.Invoke(wandVan);
    }

    void PasToe()
    {
        if (!InBuild) return;
        foreach (var kv in wandVan)
        {
            if (kv.Key == null) continue;
            int display = indeling[kv.Value - 1];
            if (kv.Key is Camera cam) cam.targetDisplay = display;
            else if (kv.Key is Canvas canvas) canvas.targetDisplay = display;
        }
    }

    bool IsEigen(GameObject go)
    {
        return go.transform.root == transform;
    }

    // ---------- Instellen met F12 ----------

    void Update()
    {
        var toetsenbord = Keyboard.current;
        if (meldingTot > 0 && Time.unscaledTime > meldingTot) { meldingTot = 0; if (!BezigMetInstellen) bedienPaneel.SetActive(false); }
        if (toetsenbord == null) return;

        if (!BezigMetInstellen)
        {
            if (toetsenbord[InstelToets].wasPressedThisFrame && !WandKalibratie.Bezig) BeginInstellen();
            return;
        }

        if (toetsenbord.escapeKey.wasPressedThisFrame) { StopInstellen("Gestopt zonder opslaan. De oude indeling blijft."); return; }
        if (toetsenbord.backspaceKey.wasPressedThisFrame && stap > 0) { stap--; gekozen[stap] = -1; ToonStap(); return; }

        for (int letter = 0; letter < aantalBeamers; letter++)
        {
            if (!toetsenbord[Key.A + letter].wasPressedThisFrame) continue;
            if (Array.IndexOf(gekozen, letter) >= 0) { ToonStap("Letter " + Letter(letter) + " is al gekozen. Kies een andere letter."); return; }
            gekozen[stap] = letter;
            stap++;
            if (stap >= Math.Min(AantalWanden, aantalBeamers)) { RondAf(); return; }
            ToonStap();
            return;
        }
    }

    void BeginInstellen()
    {
        aantalBeamers = InBuild ? Mathf.Min(Display.displays.Length - 1, Kleuren.Length) : AantalWanden;
        if (aantalBeamers < 1)
        {
            ToonMelding("Geen beamers gevonden: Windows ziet maar één scherm.", 5f);
            return;
        }
        BezigMetInstellen = true;
        stap = 0;
        gekozen = new[] { -1, -1, -1, -1 };
        MaakTestbeelden();
        ToonStap();
    }

    void RondAf()
    {
        var nieuw = (int[])indeling.Clone();
        for (int w = 0; w < AantalWanden; w++)
            if (gekozen[w] >= 0) nieuw[w] = gekozen[w] + 1;   // letter A = display 1 (de eerste beamer)
        if (!InBuild)
        {
            // In de Editor is het alleen een proef: de Game-weergave toont altijd Display 2-5 = wand 1-4
            StopInstellen("Proef in de Editor: niets bewaard.\nIn de build zou dit worden:\n" + Beschrijf(nieuw));
            return;
        }
        indeling = nieuw;
        bool ok = Bewaar();
        bestandGevonden = ok || bestandGevonden;
        PasToe();
        IndelingGewijzigd?.Invoke();
        StopInstellen(ok ? "Wandindeling bewaard en toegepast:\n" + Beschrijf(indeling) : "Toegepast, maar NIET bewaard (zie de Console).");
    }

    void StopInstellen(string melding)
    {
        BezigMetInstellen = false;
        foreach (var t in testbeelden) Destroy(t);
        testbeelden.Clear();
        testbeeldTeksten.Clear();
        meldingNietIngesteld.SetActive(InBuild && !bestandGevonden);
        ToonMelding(melding, 6f);
    }

    static string Letter(int i) => ((char)('A' + i)).ToString();

    void ToonStap(string extra = "")
    {
        int laatste = Math.Min(AantalWanden, aantalBeamers);
        var s = "WANDINDELING INSTELLEN   (stap " + (stap + 1) + " van " + laatste + ")\n\n" +
                "Op elke beamer staat een grote letter.\n" +
                "Kijk naar " + WandUitleg[stap] + ".\n\n" +
                "Welke letter staat daar?  Druk " + LetterLijst() + ".\n\n";
        for (int w = 0; w < laatste; w++)
            s += "Wand " + (w + 1) + ":  " + (gekozen[w] >= 0 ? Letter(gekozen[w]) : (w == stap ? "?" : "-")) + "\n";
        s += "\nBackspace = vorige stap      Esc = stoppen zonder opslaan";
        if (extra != "") s += "\n\n" + extra;
        ToonMelding(s, 0f);

        // Op de beamer zelf laten zien welke wand al bij die letter hoort
        for (int letter = 0; letter < testbeeldTeksten.Count; letter++)
        {
            int wand = Array.IndexOf(gekozen, letter);
            testbeeldTeksten[letter].text = wand >= 0 ? "= wand " + (wand + 1) : "";
        }
    }

    string LetterLijst()
    {
        var s = "";
        for (int i = 0; i < aantalBeamers; i++) s += (i == 0 ? "" : i == aantalBeamers - 1 ? " of " : ", ") + Letter(i);
        return s;
    }

    // ---------- Beelden ----------

    static Font Lettertype => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    Canvas MaakCanvas(string naam, int display, int volgorde)
    {
        var go = new GameObject(naam, typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.targetDisplay = display;
        canvas.sortingOrder = volgorde;
        var schaal = go.GetComponent<CanvasScaler>();
        schaal.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        schaal.referenceResolution = new Vector2(1920, 1080);
        schaal.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    static RectTransform Vlak(Transform ouder, string naam, Color kleur, Vector2 min, Vector2 max)
    {
        var go = new GameObject(naam, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(ouder, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.color = kleur;
        img.raycastTarget = false;
        return rt;
    }

    static Text Tekst(Transform ouder, string inhoud, int grootte, TextAnchor uitlijning, Vector2 min, Vector2 max, Color kleur)
    {
        var go = new GameObject("Tekst", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(ouder, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = new Vector2(30, 30); rt.offsetMax = new Vector2(-30, -30);
        var t = go.GetComponent<Text>();
        t.font = Lettertype;
        t.text = inhoud;
        t.fontSize = grootte;
        t.alignment = uitlijning;
        t.color = kleur;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    void MaakTestbeelden()
    {
        for (int letter = 0; letter < aantalBeamers; letter++)
        {
            var canvas = MaakCanvas("Testbeeld " + Letter(letter), letter + 1, 1000);
            var ouder = canvas.transform;
            Vlak(ouder, "Achtergrond", Kleuren[letter], Vector2.zero, Vector2.one);

            // Raster om de beamer uit te richten: lijnen op elke 10 %
            var lijn = new Color(1, 1, 1, 0.35f);
            for (int i = 1; i < 10; i++)
            {
                float f = i / 10f, d = 0.0015f;
                Vlak(ouder, "Raster", lijn, new Vector2(f - d, 0), new Vector2(f + d, 1));
                Vlak(ouder, "Raster", lijn, new Vector2(0, f - d * 1.8f), new Vector2(1, f + d * 1.8f));
            }
            // Witte rand langs de buitenkant van het beeld
            const float r = 0.008f;
            Vlak(ouder, "Rand", Color.white, new Vector2(0, 0), new Vector2(r, 1));
            Vlak(ouder, "Rand", Color.white, new Vector2(1 - r, 0), new Vector2(1, 1));
            Vlak(ouder, "Rand", Color.white, new Vector2(0, 0), new Vector2(1, r * 1.8f));
            Vlak(ouder, "Rand", Color.white, new Vector2(0, 1 - r * 1.8f), new Vector2(1, 1));

            Tekst(ouder, Letter(letter), 560, TextAnchor.MiddleCenter, new Vector2(0, 0.1f), new Vector2(1, 0.95f), Color.white);
            testbeeldTeksten.Add(Tekst(ouder, "", 110, TextAnchor.LowerCenter, new Vector2(0, 0.03f), new Vector2(1, 0.3f), Color.white));
            Tekst(ouder, "Display " + (letter + 2), 32, TextAnchor.UpperLeft, new Vector2(0, 0), new Vector2(1, 1), Color.white);
            testbeelden.Add(canvas.gameObject);
        }
    }

    void ToonMelding(string tekst, float seconden)
    {
        if (bedienPaneel == null)
        {
            var canvas = MaakCanvas("Bedienscherm: wandindeling", 0, 1000);
            bedienPaneel = Vlak(canvas.transform, "Paneel", new Color(0, 0, 0, 0.88f), new Vector2(0.15f, 0.12f), new Vector2(0.85f, 0.88f)).gameObject;
            bedienTekst = Tekst(bedienPaneel.transform, "", 34, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Color.white);
        }
        bedienTekst.text = tekst;
        bedienPaneel.SetActive(true);
        meldingTot = seconden > 0 ? Time.unscaledTime + seconden : 0;
    }

    void MaakMeldingNietIngesteld()
    {
        var canvas = MaakCanvas("Bedienscherm: melding wandindeling", 0, 999);
        meldingNietIngesteld = canvas.gameObject;
        var vlak = Vlak(canvas.transform, "Balk", new Color(0.6f, 0.1f, 0.1f, 0.9f), new Vector2(0, 0), new Vector2(0.42f, 0.05f));
        var t = Tekst(vlak, "Wandindeling niet ingesteld: druk F12", 26, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, Color.white);
        t.rectTransform.offsetMin = new Vector2(16, 0);
        t.rectTransform.offsetMax = new Vector2(-16, 0);
        meldingNietIngesteld.SetActive(InBuild && !bestandGevonden);
    }
}
