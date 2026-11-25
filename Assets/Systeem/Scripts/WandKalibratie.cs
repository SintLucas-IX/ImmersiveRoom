using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Zorgt dat het beeld van elke beamer precies op zijn wand valt. Studenten hoeven hier niets mee te doen.
///
/// In de build tekent elke wandcamera naar een eigen beeld, met een kijkvenster berekend uit de maten van de ruimte. Dat beeld
/// wordt vervormd op de beamer gezet, met vier hoekpunten per wand.
///
/// F9 = kalibreren met content (de gewone manier): op elke hoek een paal met blokjes en bollen; wand 4 is het ijkpunt,
///   stel wand 1 bij tot hoek 4-1 aansluit, Tab, wand 2 tot hoek 1-2 aansluit, Tab, wand 3 tot hoek 2-3 aansluit.
///   Pijltjes = inhoud verschuiven, + / - = groter / kleiner (Shift = grote stappen, Ctrl = kleine), R = wand terug,
///   Enter = opslaan, Esc = stoppen zonder opslaan.
/// F11 = hoekpunten van het beamerbeeld (alleen als het beeld niet goed op de wand ligt): 1-4 = hoek, pijltjes = verschuiven,
///   T = testbeeld of content, PageUp/PageDown = inhoud omhoog/omlaag, Tab, Enter, Esc.
/// Bewaard in C:\Users\Public\ImmersiveRoom\wandkalibratie.json (voor elke build op die pc). Bestaat dat niet, dan geldt de
/// standaard uit Assets/StreamingAssets/ImmersiveRoom (de Immersive Room op school, gekalibreerd 25-11-2026).
/// In de Editor gebeurt er niets: daar zie je de wanden gewoon in de Game-weergave (Display 2-5).
/// </summary>
public class WandKalibratie : MonoBehaviour
{
    /// <summary>Waar tijdens het kalibreren: de productie reageert dan niet op toetsen.</summary>
    public static bool Bezig { get; private set; }

    const int AantalWanden = 4;
    const Key KalibratieToets = Key.F11;
    const float StandaardVerhouding = 16f / 9f;
    static int LaagUI => LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 5;

    static readonly string[] WandUitleg =
    {
        "wand 1: de wand waar de pc tegenaan staat",
        "wand 2: de wand links (gezien vanaf de pc)",
        "wand 3: de achterwand (tegenover de pc)",
        "wand 4: de kleine wand rechts (gezien vanaf de pc)",
    };

    // Hoek 1 = linksboven, 2 = rechtsboven, 3 = rechtsonder, 4 = linksonder (zoals je leest, met de klok mee)
    static readonly string[] HoekNaam = { "linksboven", "rechtsboven", "rechtsonder", "linksonder" };
    static readonly Vector2[] BeeldHoek = { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };

    [Serializable]
    class WandInstelling
    {
        // Hoeken op de beamer (0-1, links onder = 0,0): x1, y1, x2, y2, x3, y3, x4, y4
        public float[] hoeken = { 0, 1, 1, 1, 1, 0, 0, 0 };
        public float verhouding = StandaardVerhouding;
        // Inhoud omhoog (+) of omlaag (-), als deel van de beeldhoogte. De camera kijkt dan iets lager/hoger.
        public float verschuiving = 0f;
        // Kalibratie met content (F9): inhoud naar rechts (+) en de grootte van de inhoud (1 = berekend uit de maten)
        public float schuifX = 0f;
        public float schaal = 1f;

        public Vector2 Hoek(int i) => new Vector2(hoeken[i * 2], hoeken[i * 2 + 1]);
        public void ZetHoek(int i, Vector2 p) { hoeken[i * 2] = p.x; hoeken[i * 2 + 1] = p.y; }
        public WandInstelling Kopie() => new WandInstelling { hoeken = (float[])hoeken.Clone(), verhouding = verhouding, verschuiving = verschuiving, schuifX = schuifX, schaal = schaal };
    }

    /// <summary>Maten van de Immersive Room (meter). Standaard: de ruimte op school (Luc, 25-11-2026).</summary>
    [Serializable]
    class Ruimte
    {
        public float breedteWand1 = 3.12f;   // wand 1 en 3 (dus ook de afstand tussen wand 2 en 4)
        public float breedteWand2 = 3.25f;   // wand 2 en 4 (dus ook de afstand tussen wand 1 en 3)
        public float hoogte = 2.70f;         // hoogte van het beeld op de wand
        public float ooghoogte = 1.65f;      // ogen van de kijker: gemiddelde Nederlander 15-50 jaar (± 1,75 m lang)
        public float kijkpuntNaarWand1 = 0f; // kijker vanuit het midden richting wand 1 (meter)
        public float kijkpuntNaarWand2 = 0f; // kijker vanuit het midden richting wand 2 (meter)
    }

    [Serializable]
    class Bestand
    {
        public Ruimte ruimte = new Ruimte();
        public WandInstelling[] wanden;
        public string ingesteldOp = "";
        public string pc = "";
    }

    class Wand
    {
        public WandInstelling instelling = new WandInstelling();
        public Camera camera;
        public RenderTexture beeld;
        public Canvas uitvoer;                // op de beamer: het vervormde beeld
        public RawImage uitvoerBeeld;
        public Material warp;
        public GameObject testbeeld;          // tijdens het kalibreren, in het wandbeeld zelf
        public GameObject kader;              // F9: geel kader om de wand die je bijstelt
        public Rect venster;                  // kijkvenster van de camera (tangens): x/y = links/onder, breedte/hoogte
        public readonly Dictionary<Canvas, float> canvassen = new Dictionary<Canvas, float>();   // canvassen in dit wandbeeld + afstand
        public RectTransform[] hoekMerk = new RectTransform[4];
        public Image[] hoekMerkBeeld = new Image[4];
    }

    readonly Wand[] wanden = new Wand[AantalWanden];
    readonly Dictionary<Camera, Vector3> basisPositie = new Dictionary<Camera, Vector3>();
    const float SceneOoghoogte = 1.65f;  // hoogte van de camera's in de scenes
    Ruimte ruimte = new Ruimte();
    WandInstelling[] voorKalibratie;
    int huidigeWand, huidigeHoek;
    bool metContent;   // T: kalibreren terwijl de scene (en de hoektest F9) in beeld is
    bool contentModus; // F9: de virtuele wereld per wand verschuiven en schalen
    const Key ContentToets = Key.F9;
    GameObject bedienPaneel;
    Text bedienTekst;
    float meldingTot;
    Key herhaalToets = Key.None;
    float herhaalVolgende;
    Sprite cirkel;

    static bool InBuild => !Application.isEditor;
    static string Map => Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? Application.persistentDataPath, "ImmersiveRoom");
    static string Pad => Path.Combine(Map, "wandkalibratie.json");
    // Standaard uit de template (Assets/StreamingAssets/ImmersiveRoom): de Immersive Room op school, gekalibreerd 25-11-2026
    static string StandaardPad => Path.Combine(Application.streamingAssetsPath, "ImmersiveRoom", "wandkalibratie.json");

    static WandKalibratie instantie;

    /// <summary>
    /// Richting van de kijker naar hoek h (0 = wand 1-2, 1 = wand 2-3, 2 = wand 3-4, 3 = wand 4-1), met x naar wand 2 en z naar wand 1.
    /// In de Editor (vierkant, 90 graden per wand): precies diagonaal.
    /// </summary>
    public static Vector2 HoekRichting(int h) => HoekPlek(h).normalized;

    /// <summary>Plek van hoek h vanaf de kijker (meter), x naar wand 2, z naar wand 1. In de Editor: de standaardmaten.</summary>
    public static Vector2 HoekPlek(int h)
    {
        float sx = (h == 0 || h == 1) ? 1 : -1, sz = (h == 0 || h == 3) ? 1 : -1;
        var r = instantie != null ? instantie.ruimte : new Ruimte();
        return new Vector2(sx * r.breedteWand1 / 2 - r.kijkpuntNaarWand2, sz * r.breedteWand2 / 2 - r.kijkpuntNaarWand1);
    }

    /// <summary>Hoogte van het beeld op de wand en de ooghoogte (meter).</summary>
    public static float WandHoogte => instantie != null ? instantie.ruimte.hoogte : new Ruimte().hoogte;
    public static float Ooghoogte => instantie != null ? instantie.ruimte.ooghoogte : new Ruimte().ooghoogte;

    readonly List<Canvas> verborgen = new List<Canvas>();

    // Build gestart met -kalibratie (zie "Kalibratie starten.bat"): meteen palen op de hoeken en de kalibratie met content
    void Start()
    {
        if (!InBuild) return;
        var args = Environment.GetCommandLineArgs();
        // -scene <naam>: meteen een andere scene openen (bv. ImmersiveRoom-3D-Demo)
        int si = Array.IndexOf(args, "-scene");
        if (si >= 0 && si + 1 < args.Length)
        {
            if (UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(args[si + 1]) >= 0 || Application.CanStreamedLevelBeLoaded(args[si + 1]))
                UnityEngine.SceneManagement.SceneManager.LoadScene(args[si + 1]);
            else Debug.LogWarning("[WandKalibratie] Scene '" + args[si + 1] + "' staat niet in de build.");
        }
        if (Array.IndexOf(args, "-kleurtest") >= 0) { ZetKleurtest(true); return; }
        if (Array.IndexOf(args, "-kalibratie") < 0) return;
        GetComponent<HoekTest>()?.Toon(true);
        contentModus = true;
        BeginKalibratie();
    }

    void Awake()
    {
        instantie = this;
        for (int i = 0; i < AantalWanden; i++) wanden[i] = new Wand();
        if (!InBuild) return;

        Laad();
        MaakZwarteCameras();
        for (int i = 0; i < AantalWanden; i++) MaakUitvoer(i);
        WandIndeling.SceneIngedeeld += BijSceneIngedeeld;
        WandIndeling.IndelingGewijzigd += ZetBeamers;
    }

    void OnDestroy()
    {
        WandIndeling.SceneIngedeeld -= BijSceneIngedeeld;
        WandIndeling.IndelingGewijzigd -= ZetBeamers;
    }

    // ---------- Bestand ----------

    void Laad()
    {
        try
        {
            string pad = File.Exists(Pad) ? Pad : StandaardPad;
            if (!File.Exists(pad)) { Debug.Log("[WandKalibratie] Nog niet gekalibreerd (" + Pad + "). Druk F9 in de build."); return; }
            var b = JsonUtility.FromJson<Bestand>(File.ReadAllText(pad));
            if (b == null || b.wanden == null || b.wanden.Length != AantalWanden) { Debug.LogWarning("[WandKalibratie] " + pad + " is ongeldig."); return; }
            for (int i = 0; i < AantalWanden; i++)
            {
                var w = b.wanden[i];
                if (w != null && w.hoeken != null && w.hoeken.Length == 8) wanden[i].instelling = w;
            }
            var r = b.ruimte;
            if (r != null && r.breedteWand1 > 0.5f && r.breedteWand2 > 0.5f && r.hoogte > 0.5f && r.ooghoogte > 0.1f && r.ooghoogte < r.hoogte)
                ruimte = r;
            Debug.Log("[WandKalibratie] Ruimte: wand 1 en 3 " + ruimte.breedteWand1 + " m, wand 2 en 4 " + ruimte.breedteWand2 + " m, hoogte " +
                      ruimte.hoogte + " m, ooghoogte " + ruimte.ooghoogte + " m");
            Debug.Log("[WandKalibratie] Geladen uit " + pad + " (ingesteld " + b.ingesteldOp + ")");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WandKalibratie] Kan " + Pad + " niet lezen: " + e.Message);
        }
    }

    bool Bewaar()
    {
        try
        {
            Directory.CreateDirectory(Map);
            var b = new Bestand { ruimte = ruimte, wanden = new WandInstelling[AantalWanden], ingesteldOp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), pc = Environment.MachineName };
            for (int i = 0; i < AantalWanden; i++) b.wanden[i] = wanden[i].instelling;
            File.WriteAllText(Pad, JsonUtility.ToJson(b, true));
            Debug.Log("[WandKalibratie] Bewaard in " + Pad);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[WandKalibratie] Kan " + Pad + " niet bewaren: " + e.Message);
            return false;
        }
    }

    // ---------- Opbouw ----------

    // Elke beamer krijgt een camera die alleen zwart tekent; het wandbeeld komt er als canvas overheen
    void MaakZwarteCameras()
    {
        for (int d = 1; d < Display.displays.Length; d++)
        {
            var go = new GameObject("Zwart display " + (d + 1));
            go.transform.SetParent(transform, false);
            var cam = go.AddComponent<Camera>();
            cam.cullingMask = 0;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.depth = -100;
            cam.targetDisplay = d;
        }
    }

    void MaakUitvoer(int i)
    {
        var w = wanden[i];
        var go = new GameObject("Uitvoer wand " + (i + 1), typeof(Canvas));
        go.transform.SetParent(transform, false);
        w.uitvoer = go.GetComponent<Canvas>();
        w.uitvoer.renderMode = RenderMode.ScreenSpaceOverlay;
        w.uitvoer.sortingOrder = 500;     // onder het F12-instelscherm (1000)
        var beeld = new GameObject("Beeld", typeof(RectTransform), typeof(RawImage));
        beeld.transform.SetParent(go.transform, false);
        var rt = beeld.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        w.uitvoerBeeld = beeld.GetComponent<RawImage>();
        w.uitvoerBeeld.raycastTarget = false;
        var shader = Resources.Load<Shader>("WandWarp");
        if (shader == null) Debug.LogError("[WandKalibratie] Shader WandWarp niet gevonden (Assets/Systeem/Resources).");
        w.warp = new Material(shader);
        w.uitvoerBeeld.material = w.warp;
        MaakBeeld(i);
        ZetWarp(i);
        w.uitvoer.targetDisplay = WandIndeling.DisplayVanWand(i + 1);
    }

    void ZetBeamers()
    {
        for (int i = 0; i < AantalWanden; i++)
            if (wanden[i].uitvoer != null) wanden[i].uitvoer.targetDisplay = WandIndeling.DisplayVanWand(i + 1);
    }

    // Breedte van wand i (meter): wand 1 en 3 zijn even breed, wand 2 en 4 ook
    float WandBreedte(int i) => i % 2 == 0 ? ruimte.breedteWand1 : ruimte.breedteWand2;

    // Het wandbeeld krijgt de verhouding van de echte wand (vierkante pixels), max 1920 breed of 2160 hoog
    void MaakBeeld(int i)
    {
        var w = wanden[i];
        float a = WandBreedte(i) / ruimte.hoogte;
        int breed = 1920, hoog = Mathf.RoundToInt(1920 / a);
        if (hoog > 2160) { hoog = 2160; breed = Mathf.RoundToInt(2160 * a); }
        var oud = w.beeld;
        w.beeld = new RenderTexture(breed, hoog, 24) { name = "Wandbeeld " + (i + 1) };
        w.beeld.Create();
        if (w.camera != null) w.camera.targetTexture = w.beeld;
        w.uitvoerBeeld.texture = w.beeld;
        if (oud != null) { oud.Release(); Destroy(oud); }
        ZetCamera(i);
    }

    /// <summary>
    /// Het kijkvenster van wand i, berekend uit de maten van de ruimte: vanaf het oog van de kijker gezien is de wand een rechthoek.
    /// links/rechts/onder/boven zijn de randen van die rechthoek gedeeld door de afstand tot de wand (tangens).
    /// Zo sluiten de vier wanden precies op elkaar aan, ook als de ruimte niet vierkant is of de kijker niet in het midden staat.
    /// Wand 1 ligt in de kijkrichting van Wand_1 (+Z), wand 2 in die van Wand_2 (+X), enzovoort.
    /// </summary>
    void Venster(int i, out float d, out float links, out float rechts, out float onder, out float boven)
    {
        float B = ruimte.breedteWand1, L = ruimte.breedteWand2;      // B = afstand wand 2-4, L = afstand wand 1-3
        float kx = ruimte.kijkpuntNaarWand2, kz = ruimte.kijkpuntNaarWand1;
        switch (i)
        {
            case 0: d = L / 2 - kz; links = -B / 2 - kx; rechts = B / 2 - kx; break;     // wand 1, rechts = +X
            case 1: d = B / 2 - kx; links = -(L / 2 - kz); rechts = L / 2 + kz; break;   // wand 2, rechts = -Z
            case 2: d = L / 2 + kz; links = -(B / 2 - kx); rechts = B / 2 + kx; break;   // wand 3, rechts = -X
            default: d = B / 2 + kx; links = -L / 2 - kz; rechts = L / 2 - kz; break;    // wand 4, rechts = +Z
        }
        onder = -ruimte.ooghoogte;
        boven = ruimte.hoogte - ruimte.ooghoogte;
    }

    void ZetCamera(int i)
    {
        var w = wanden[i];
        if (w.camera == null) return;
        var cam = w.camera;

        // Camera op de plek van het oog van de kijker (in de scene: midden van de ruimte op 1,65 m)
        if (!basisPositie.ContainsKey(cam)) basisPositie[cam] = cam.transform.position;
        cam.transform.position = basisPositie[cam] + new Vector3(ruimte.kijkpuntNaarWand2, ruimte.ooghoogte - SceneOoghoogte, ruimte.kijkpuntNaarWand1);

        Venster(i, out float d, out float links, out float rechts, out float onder, out float boven);
        d = Mathf.Max(0.1f, d);
        // Kalibratie met content (F9): het venster kleiner = inhoud groter, venster verschuiven = inhoud de andere kant op
        var wi = w.instelling;
        float s = Mathf.Clamp(wi.schaal, 0.3f, 3f);
        float mx = (links + rechts) / 2f, my = (onder + boven) / 2f;
        float hb = (rechts - links) / 2f / s, hh = (boven - onder) / 2f / s;
        mx -= wi.schuifX * 2f * hb;
        my -= wi.verschuiving * 2f * hh;
        links = mx - hb; rechts = mx + hb; onder = my - hh; boven = my + hh;

        float n = cam.nearClipPlane, f = n / d;
        cam.ResetProjectionMatrix();
        cam.aspect = (rechts - links) / (boven - onder);
        cam.fieldOfView = 2f * Mathf.Atan((boven - onder) / 2f / d) * Mathf.Rad2Deg;   // alleen ter info (bv. voor de teksten)
        cam.projectionMatrix = Matrix4x4.Frustum(links * f, rechts * f, onder * f, boven * f, n, cam.farClipPlane);
        w.venster = new Rect(links / d, onder / d, (rechts - links) / d, (boven - onder) / d);   // in tangens (per meter afstand)
        foreach (var kv in w.canvassen) if (kv.Key != null) PlaatsCanvas(w, kv.Key, kv.Value);
    }

    /// <summary>
    /// Zet een canvas als plat vlak precies in het (scheve) kijkvenster van de wandcamera, op 'afstand' meter voor de camera.
    /// Een gewoon Screen Space-canvas zou Unity in het midden van het beeld zetten en dan verschoven staan.
    /// Het canvas is 1080 eenheden hoog en zo breed als de verhouding van de wand.
    /// </summary>
    void PlaatsCanvas(Wand w, Canvas canvas, float afstand)
    {
        if (w.camera == null || w.venster.height <= 0) return;
        var v = w.venster;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = w.camera;
        var rt = (RectTransform)canvas.transform;
        float hoogte = v.height * afstand;
        float a = v.width / v.height;
        rt.pivot = new Vector2(0.5f, 0.5f);
        // Minstens 1920 x 1080 eenheden, zodat een ontwerp voor 16:9 er altijd in past (een wand is vaak smaller dan 16:9)
        rt.sizeDelta = new Vector2(Mathf.Max(1920f, 1080f * a), Mathf.Max(1080f, 1920f / a));
        rt.position = w.camera.transform.TransformPoint(new Vector3(v.center.x * afstand, v.center.y * afstand, afstand));
        rt.rotation = w.camera.transform.rotation;
        float schaal = hoogte / rt.sizeDelta.y;
        var ouder = rt.parent;
        rt.localScale = Vector3.one * (ouder != null ? schaal / Mathf.Max(0.0001f, ouder.lossyScale.x) : schaal);
        ZetLaag(canvas.gameObject, LaagUI);
    }

    /// <summary>
    /// Een canvas van een wand meetekenen in het wandbeeld (in de build). Geeft false in de Editor of als 'wand' geen wandcamera is.
    /// Kleinere 'afstand' = ervoor (testbeeld 0.4, teksten 0.5, 3D-beeld 0.6 meter).
    /// </summary>
    public static bool RegistreerCanvas(Canvas canvas, Camera wand, float afstand)
    {
        if (instantie == null || !InBuild || canvas == null) return false;
        foreach (var w in instantie.wanden)
        {
            if (w.camera != wand) continue;
            w.canvassen[canvas] = afstand;
            instantie.PlaatsCanvas(w, canvas, afstand);
            return true;
        }
        return false;
    }

    void BijSceneIngedeeld(IReadOnlyDictionary<UnityEngine.Object, int> wandVan)
    {
        // Omgevingslicht: een scene zonder gebakken lichtgegevens krijgt in een build anders zwart omgevingslicht
        // (in de Editor rekent Unity het zelf uit). Zo ziet de build eruit zoals in Play Mode.
        if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat)
        {
            var sh = new UnityEngine.Rendering.SphericalHarmonicsL2();
            var kleur = RenderSettings.ambientLight;
            sh.AddAmbientLight(QualitySettings.activeColorSpace == ColorSpace.Linear ? kleur.linear : kleur);
            RenderSettings.ambientProbe = sh;
        }
        else DynamicGI.UpdateEnvironment();

        // Eerst de camera's: die tekenen voortaan naar het wandbeeld
        foreach (var kv in wandVan)
        {
            if (!(kv.Key is Camera cam) || cam == null) continue;
            var w = wanden[kv.Value - 1];
            w.camera = cam;
            cam.targetTexture = w.beeld;
            cam.cullingMask |= 1 << LaagUI;
            ZetCamera(kv.Value - 1);
        }
        // Dan de canvassen van de wanden (teksten): als plat vlak in het wandbeeld, zodat ze ook vervormd worden
        foreach (var kv in wandVan)
        {
            if (!(kv.Key is Canvas canvas) || canvas == null) continue;
            var w = wanden[kv.Value - 1];
            if (w.camera == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            RegistreerCanvas(canvas, w.camera, 0.5f);
        }
        // Die vlakken (laag UI) zijn alleen voor de wandcamera's: andere camera's (bv. de voorbeelden op het bedienscherm) zien ze niet
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            bool wandcamera = false;
            foreach (var w in wanden) if (w.camera == cam) wandcamera = true;
            if (!wandcamera && cam.targetTexture != null && cam.transform.root != transform) cam.cullingMask &= ~(1 << LaagUI);
        }
        for (int i = 0; i < AantalWanden; i++)
        {
            var weg = new List<Canvas>();
            foreach (var kv in wanden[i].canvassen) if (kv.Key == null) weg.Add(kv.Key);
            foreach (var c in weg) wanden[i].canvassen.Remove(c);
            if (wanden[i].testbeeld != null) { Destroy(wanden[i].testbeeld); wanden[i].testbeeld = null; }
            if (wanden[i].kader != null) { Destroy(wanden[i].kader); wanden[i].kader = null; }
        }
        if (Bezig) ToonKalibratie();
    }

    static void ZetLaag(GameObject go, int laag)
    {
        go.layer = laag;
        foreach (Transform t in go.transform) ZetLaag(t.gameObject, laag);
    }

    // ---------- Wiskunde: homografie ----------

    void ZetWarp(int i)
    {
        var w = wanden[i];
        var beamer = new Vector2[4];
        for (int k = 0; k < 4; k++) beamer[k] = w.instelling.Hoek(k);
        if (Homografie(beamer, BeeldHoek, out var h)) w.warp.SetMatrix("_H", h);
        else Debug.LogWarning("[WandKalibratie] Hoeken van wand " + (i + 1) + " liggen op één lijn; vorige vervorming blijft.");
    }

    /// <summary>Berekent de 3x3-matrix die de punten 'van' (op de beamer) afbeeldt op 'naar' (in het wandbeeld).</summary>
    static bool Homografie(Vector2[] van, Vector2[] naar, out Matrix4x4 m)
    {
        var a = new double[8, 9];
        for (int k = 0; k < 4; k++)
        {
            double x = van[k].x, y = van[k].y, u = naar[k].x, v = naar[k].y;
            int r = k * 2;
            a[r, 0] = x; a[r, 1] = y; a[r, 2] = 1; a[r, 6] = -u * x; a[r, 7] = -u * y; a[r, 8] = u;
            a[r + 1, 3] = x; a[r + 1, 4] = y; a[r + 1, 5] = 1; a[r + 1, 6] = -v * x; a[r + 1, 7] = -v * y; a[r + 1, 8] = v;
        }
        // Gauss-eliminatie met pivot
        for (int c = 0; c < 8; c++)
        {
            int p = c;
            for (int r = c + 1; r < 8; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
            if (Math.Abs(a[p, c]) < 1e-12) { m = Matrix4x4.identity; return false; }
            for (int k = 0; k < 9; k++) { var t = a[c, k]; a[c, k] = a[p, k]; a[p, k] = t; }
            for (int r = 0; r < 8; r++)
            {
                if (r == c) continue;
                double f = a[r, c] / a[c, c];
                for (int k = c; k < 9; k++) a[r, k] -= f * a[c, k];
            }
        }
        var h = new float[8];
        for (int r = 0; r < 8; r++) h[r] = (float)(a[r, 8] / a[r, r]);
        m = Matrix4x4.identity;
        m[0, 0] = h[0]; m[0, 1] = h[1]; m[0, 2] = h[2];
        m[1, 0] = h[3]; m[1, 1] = h[4]; m[1, 2] = h[5];
        m[2, 0] = h[6]; m[2, 1] = h[7]; m[2, 2] = 1;
        return true;
    }

    // ---------- Kalibreren met F11 ----------

    void Update()
    {
        if (meldingTot > 0 && Time.unscaledTime > meldingTot) { meldingTot = 0; if (!Bezig && bedienPaneel != null) bedienPaneel.SetActive(false); }
        var tb = Keyboard.current;
        if (tb == null) return;

        if (!Bezig && tb[KleurtestToets].wasPressedThisFrame && InBuild) { ZetKleurtest(kleurBord[0] == null); return; }

        if (!Bezig)
        {
            bool f11 = tb[KalibratieToets].wasPressedThisFrame, f10 = tb[ContentToets].wasPressedThisFrame;
            if ((f11 || f10) && !WandIndeling.BezigMetInstellen)
            {
                if (!InBuild) { Debug.Log("[WandKalibratie] Kalibreren (F9/F11) kan alleen in de build, niet in de Editor."); return; }
                contentModus = f10;
                BeginKalibratie();
            }
            return;
        }

        bool shift = tb.leftShiftKey.isPressed || tb.rightShiftKey.isPressed;
        bool ctrl = tb.leftCtrlKey.isPressed || tb.rightCtrlKey.isPressed;
        var w = wanden[huidigeWand];

        if (contentModus) { UpdateContent(tb, shift, ctrl, w); return; }

        if (tb.escapeKey.wasPressedThisFrame)
        {
            for (int i = 0; i < AantalWanden; i++) { wanden[i].instelling = voorKalibratie[i]; MaakBeeld(i); ZetWarp(i); }
            StopKalibratie("Gestopt zonder opslaan. De vorige kalibratie geldt weer.");
            return;
        }
        if (tb.enterKey.wasPressedThisFrame || tb.numpadEnterKey.wasPressedThisFrame)
        {
            StopKalibratie(Bewaar() ? "Kalibratie bewaard in\n" + Pad : "Kalibratie toegepast, maar NIET bewaard (zie Player.log).");
            return;
        }
        if (tb.tabKey.wasPressedThisFrame)
        {
            huidigeWand = (huidigeWand + (shift ? AantalWanden - 1 : 1)) % AantalWanden;
            ToonKalibratie();
            return;
        }
        for (int k = 0; k < 4; k++)
        {
            if (tb[Key.Digit1 + k].wasPressedThisFrame || tb[Key.Numpad1 + k].wasPressedThisFrame) { huidigeHoek = k; ToonKalibratie(); return; }
        }
        if (tb.tKey.wasPressedThisFrame)
        {
            metContent = !metContent;
            ToonKalibratie();
            return;
        }
        if (tb.rKey.wasPressedThisFrame)
        {
            w.instelling.hoeken = new WandInstelling().hoeken;
            ZetWarp(huidigeWand);
            ToonKalibratie();
            return;
        }

        // Pijltjes en + / -: één keer per druk, en snel herhalen als je de toets vasthoudt
        Key[] toetsen = { Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow, Key.PageUp, Key.PageDown };
        Key actief = Key.None;
        foreach (var k in toetsen)
        {
            if (tb[k].wasPressedThisFrame) { actief = k; herhaalToets = k; herhaalVolgende = Time.unscaledTime + 0.4f; break; }
        }
        if (actief == Key.None && herhaalToets != Key.None)
        {
            if (!tb[herhaalToets].isPressed) herhaalToets = Key.None;
            else if (Time.unscaledTime >= herhaalVolgende) { actief = herhaalToets; herhaalVolgende = Time.unscaledTime + 0.04f; }
        }
        if (actief == Key.None) return;

        if (actief == Key.PageUp || actief == Key.PageDown)
        {
            // Stap in beamerpixels (zoals de pijltjes), omgerekend naar een deel van de beeldhoogte
            float pxH = shift ? 20 : ctrl ? 1 : 4;
            int dsp = WandIndeling.DisplayVanWand(huidigeWand + 1);
            float bh0 = dsp < Display.displays.Length ? Display.displays[dsp].systemHeight : 1080;
            float hoogteOpBeamer = Mathf.Max(0.05f, (w.instelling.Hoek(0).y + w.instelling.Hoek(1).y - w.instelling.Hoek(2).y - w.instelling.Hoek(3).y) / 2f);
            float stapS = pxH / bh0 / hoogteOpBeamer;
            w.instelling.verschuiving = Mathf.Clamp(w.instelling.verschuiving + (actief == Key.PageUp ? stapS : -stapS), -0.5f, 0.5f);
            ZetCamera(huidigeWand);
            ToonKalibratie();
            return;
        }


        int display = WandIndeling.DisplayVanWand(huidigeWand + 1);
        float bw = display < Display.displays.Length ? Display.displays[display].systemWidth : 1920;
        float bh = display < Display.displays.Length ? Display.displays[display].systemHeight : 1080;
        float px = shift ? 20 : ctrl ? 1 : 4;
        var d = Vector2.zero;
        if (actief == Key.LeftArrow) d.x = -px / bw;
        if (actief == Key.RightArrow) d.x = px / bw;
        if (actief == Key.UpArrow) d.y = px / bh;
        if (actief == Key.DownArrow) d.y = -px / bh;
        var p = w.instelling.Hoek(huidigeHoek) + d;
        p.x = Mathf.Clamp01(p.x); p.y = Mathf.Clamp01(p.y);
        w.instelling.ZetHoek(huidigeHoek, p);
        ZetWarp(huidigeWand);
        ToonKalibratie();
    }

    // ---------- Kleurtest voor de 3D-brillen (F8) ----------

    const Key KleurtestToets = Key.F8;
    readonly GameObject[] kleurBord = new GameObject[AantalWanden];

    // Wand 1: kleuren voor het LINKERoog (rood glas); moeten zo goed mogelijk wegvallen achter het blauwe glas (rechteroog)
    static readonly Color32[] TestRood =
    {
        new Color32(255, 0, 0, 255), new Color32(220, 0, 0, 255), new Color32(180, 0, 0, 255), new Color32(255, 40, 0, 255),
        new Color32(255, 80, 0, 255), new Color32(255, 120, 0, 255), new Color32(255, 0, 40, 255), new Color32(255, 0, 80, 255),
        new Color32(255, 0, 128, 255), new Color32(255, 60, 60, 255), new Color32(200, 0, 60, 255), new Color32(160, 0, 40, 255),
    };

    // Wand 3: kleuren voor het RECHTERoog (blauw glas); moeten zo goed mogelijk wegvallen achter het rode glas (linkeroog)
    static readonly Color32[] TestBlauw =
    {
        // Ronde 2 (Luc, 25-11: in ronde 1 was zuiver blauw 0/0/255 het beste, maar kwam nog door het rode glas): donkerder blauw
        new Color32(0, 0, 255, 255), new Color32(0, 0, 220, 255), new Color32(0, 0, 190, 255), new Color32(0, 0, 160, 255),
        new Color32(0, 0, 130, 255), new Color32(0, 0, 100, 255), new Color32(0, 20, 255, 255), new Color32(0, 20, 190, 255),
        new Color32(0, 40, 160, 255), new Color32(0, 30, 120, 255), new Color32(0, 10, 80, 255), new Color32(0, 0, 60, 255),
    };

    void ZetKleurtest(bool aan)
    {
        for (int i = 0; i < AantalWanden; i++)
            if (kleurBord[i] != null) { Destroy(kleurBord[i]); kleurBord[i] = null; }
        if (!aan) { ToonMelding("Kleurtest uit.", 3f); return; }
        kleurBord[0] = MaakKleurBord(0, TestRood, "ROOD   -   kijk door het BLAUWE glas (rechteroog, linkeroog dicht)");
        kleurBord[2] = MaakKleurBord(2, TestBlauw, "BLAUW   -   kijk door het RODE glas (linkeroog, rechteroog dicht)");
        ToonMelding("KLEURTEST 3D-BRIL\n\n" +
                    "Wand 1 (bij de pc): rode vlakken. Doe je LINKERoog dicht en kijk door het BLAUWE glas:\n" +
                    "welk nummer valt het meest weg (is het donkerst, bijna zwart)?\n\n" +
                    "Wand 3 (tegenover de pc): blauwe vlakken. Doe je RECHTERoog dicht en kijk door het RODE glas:\n" +
                    "welk nummer valt het meest weg?\n\n" +
                    "Twijfel tussen twee? Noem ze allebei.\n\nF8 = kleurtest uit", 0f);
    }

    GameObject MaakKleurBord(int i, Color32[] kleuren, string titel)
    {
        var w = wanden[i];
        if (w.camera == null) return null;
        var go = new GameObject("Kleurtest wand " + (i + 1), typeof(Canvas));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.sortingOrder = 3000;
        RegistreerCanvas(canvas, w.camera, 0.35f);
        Vlak(go.transform, Color.black, Vector2.zero, Vector2.one);
        var kop = Tekst(go.transform, titel, 56, TextAnchor.MiddleCenter, new Vector2(0, 0.88f), new Vector2(1, 0.98f));
        kop.color = new Color(0.6f, 0.6f, 0.6f);
        const int kolommen = 4, rijen = 3;
        for (int k = 0; k < kleuren.Length; k++)
        {
            int kol = k % kolommen, rij = k / kolommen;
            float x0 = 0.04f + kol * 0.24f, x1 = x0 + 0.20f;
            float y1 = 0.84f - rij * 0.28f, y0 = y1 - 0.20f;
            Vlak(go.transform, kleuren[k], new Vector2(x0, y0), new Vector2(x1, y1));
            var nr = Tekst(go.transform, (k + 1).ToString(), 64, TextAnchor.UpperCenter, new Vector2(x0, y0 - 0.075f), new Vector2(x1, y0 - 0.005f));
            nr.color = new Color(0.55f, 0.55f, 0.55f);
        }
        ZetLaag(go, LaagUI);
        return go;
    }

    // ---------- Kalibreren met content (F9) ----------

    void UpdateContent(Keyboard tb, bool shift, bool ctrl, Wand w)
    {
        if (tb.escapeKey.wasPressedThisFrame)
        {
            for (int i = 0; i < AantalWanden; i++) { wanden[i].instelling = voorKalibratie[i]; ZetCamera(i); }
            StopKalibratie("Gestopt zonder opslaan. De vorige kalibratie geldt weer.");
            return;
        }
        if (tb.enterKey.wasPressedThisFrame || tb.numpadEnterKey.wasPressedThisFrame)
        {
            StopKalibratie(Bewaar() ? "Kalibratie bewaard in\n" + Pad : "Kalibratie toegepast, maar NIET bewaard (zie Player.log).");
            return;
        }
        if (tb.tabKey.wasPressedThisFrame)
        {
            huidigeWand = (huidigeWand + (shift ? AantalWanden - 1 : 1)) % AantalWanden;
            ToonKalibratie();
            return;
        }
        if (tb.rKey.wasPressedThisFrame)
        {
            w.instelling.schuifX = 0; w.instelling.verschuiving = 0; w.instelling.schaal = 1;
            ZetCamera(huidigeWand);
            ToonKalibratie();
            return;
        }

        Key[] toetsen = { Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow, Key.Equals, Key.NumpadPlus, Key.Minus, Key.NumpadMinus };
        Key actief = Key.None;
        foreach (var k in toetsen)
            if (tb[k].wasPressedThisFrame) { actief = k; herhaalToets = k; herhaalVolgende = Time.unscaledTime + 0.4f; break; }
        if (actief == Key.None && herhaalToets != Key.None)
        {
            if (!tb[herhaalToets].isPressed) herhaalToets = Key.None;
            else if (Time.unscaledTime >= herhaalVolgende) { actief = herhaalToets; herhaalVolgende = Time.unscaledTime + 0.04f; }
        }
        if (actief == Key.None) return;

        // Stap: 0,2 % van het beeld (Shift = 1 %, Ctrl = 0,05 %)
        float stap = shift ? 0.01f : ctrl ? 0.0005f : 0.002f;
        var wi = w.instelling;
        switch (actief)
        {
            case Key.LeftArrow: wi.schuifX -= stap; break;
            case Key.RightArrow: wi.schuifX += stap; break;
            case Key.UpArrow: wi.verschuiving += stap; break;
            case Key.DownArrow: wi.verschuiving -= stap; break;
            case Key.Equals: case Key.NumpadPlus: wi.schaal *= 1f + stap; break;
            default: wi.schaal /= 1f + stap; break;
        }
        wi.schuifX = Mathf.Clamp(wi.schuifX, -0.5f, 0.5f);
        wi.verschuiving = Mathf.Clamp(wi.verschuiving, -0.5f, 0.5f);
        wi.schaal = Mathf.Clamp(wi.schaal, 0.5f, 2f);
        ZetCamera(huidigeWand);
        ToonKalibratie();
    }

    void ToonContentKalibratie()
    {
        for (int i = 0; i < AantalWanden; i++)
        {
            var w = wanden[i];
            w.uitvoerBeeld.color = Color.white;
            if (w.testbeeld != null) w.testbeeld.SetActive(false);
            if (w.kader == null) MaakKader(i);
            if (w.kader != null) w.kader.SetActive(i == huidigeWand);
            // Teksten van de productie (bv. "Druk op SPATIE") tijdelijk weg
            foreach (var kv in w.canvassen)
                if (kv.Key != null && !kv.Key.transform.IsChildOf(transform) && kv.Key.enabled) { kv.Key.enabled = false; verborgen.Add(kv.Key); }
        }
        var wi = wanden[huidigeWand].instelling;
        int vorige = huidigeWand == 0 ? 4 : huidigeWand;   // wand 1 sluit aan op wand 4, wand 2 op wand 1, enz.
        var s = "KALIBREREN MET CONTENT   wand " + (huidigeWand + 1) + " (geel kader)\n" +
                WandUitleg[huidigeWand] + "\n\n" +
                "Wand 4 is het ijkpunt. Volgorde: wand 1, dan wand 2, dan wand 3 (Tab).\n" +
                "Nu: laat de paal op de hoek wand " + vorige + "-" + (huidigeWand + 1) + " aansluiten op wand " + vorige + ".\n\n" +
                "pijltjes = inhoud verschuiven      + / - = inhoud groter / kleiner\n" +
                "(Shift = grote stappen, Ctrl = kleine stappen)\n" +
                "Tab = volgende wand (Shift+Tab = vorige)      R = deze wand terug\n" +
                "Enter = opslaan en klaar      Esc = stoppen zonder opslaan\n\n" +
                "Nu:  opzij " + (wi.schuifX * 100f).ToString("0.0") + " %   omhoog " + (wi.verschuiving * 100f).ToString("0.0") +
                " %   grootte " + (wi.schaal * 100f).ToString("0.0") + " %";
        ToonMelding(s, 0f);
    }

    // Geel kader langs de rand van het wandbeeld: zo zie je welke wand je bijstelt
    void MaakKader(int i)
    {
        var w = wanden[i];
        if (w.camera == null) return;
        var go = new GameObject("Kader wand " + (i + 1), typeof(Canvas));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.sortingOrder = 2000;
        RegistreerCanvas(canvas, w.camera, go.name.StartsWith("Kader") ? 0.38f : 0.4f);
        canvas.sortingOrder = 2000;
        float dik = 1080f * 0.012f;   // het canvas is 1080 eenheden hoog
        var geel = new Color(1f, 0.85f, 0f);
        Vlak(go.transform, geel, new Vector2(0, 0), new Vector2(0, 1), dik * 2, 0);
        Vlak(go.transform, geel, new Vector2(1, 0), new Vector2(1, 1), dik * 2, 0);
        Vlak(go.transform, geel, new Vector2(0, 0), new Vector2(1, 0), 0, dik * 2);
        Vlak(go.transform, geel, new Vector2(0, 1), new Vector2(1, 1), 0, dik * 2);
        ZetLaag(go, LaagUI);
        w.kader = go;
    }

    void LateUpdate()
    {
        // Canvassen in de wandbeelden elk frame op hun plek houden (Unity zet een canvas na een wisseling van modus zelf terug)
        if (InBuild)
            foreach (var wd in wanden)
                foreach (var kv in wd.canvassen)
                    if (kv.Key != null && kv.Key.isActiveAndEnabled) PlaatsCanvas(wd, kv.Key, kv.Value);

        if (!Bezig) return;
        // Gekozen hoek knippert
        var w = wanden[huidigeWand];
        bool aan = Mathf.Repeat(Time.unscaledTime, 0.6f) < 0.4f;
        for (int k = 0; k < 4; k++)
            if (w.hoekMerkBeeld[k] != null) w.hoekMerkBeeld[k].color = k == huidigeHoek ? (aan ? Color.red : Color.white) : new Color(0.1f, 0.1f, 0.1f);
    }

    void BeginKalibratie()
    {
        Bezig = true;
        voorKalibratie = new WandInstelling[AantalWanden];
        for (int i = 0; i < AantalWanden; i++) voorKalibratie[i] = wanden[i].instelling.Kopie();
        huidigeWand = 0;
        huidigeHoek = 0;
        if (contentModus) GetComponent<HoekTest>()?.Toon(true);   // palen op de hoeken
        ToonKalibratie();
    }

    void StopKalibratie(string melding)
    {
        Bezig = false;
        contentModus = false;
        herhaalToets = Key.None;
        foreach (var wd in wanden) if (wd.kader != null) { Destroy(wd.kader); wd.kader = null; }
        foreach (var c in verborgen) if (c != null) c.enabled = true;
        GetComponent<HoekTest>()?.Toon(false);   // palen weer weg
        verborgen.Clear();
        for (int i = 0; i < AantalWanden; i++)
        {
            var w = wanden[i];
            if (w.testbeeld != null) { Destroy(w.testbeeld); w.testbeeld = null; }
            w.uitvoerBeeld.color = Color.white;
        }
        ToonMelding(melding, 6f);
    }

    void ToonKalibratie()
    {
        if (contentModus) { ToonContentKalibratie(); return; }
        for (int i = 0; i < AantalWanden; i++)
        {
            var w = wanden[i];
            bool deze = i == huidigeWand;
            // Testbeeld: alleen de wand die je kalibreert is verlicht. Met content (T): alle wanden tonen de scene.
            w.uitvoerBeeld.color = deze || metContent ? Color.white : Color.black;
            if (deze && w.testbeeld == null) MaakTestbeeld(i);
            if (w.testbeeld != null)
            {
                w.testbeeld.SetActive(deze);
                // Met content blijven alleen de hoekmerken staan, zodat je ziet welke hoek je verschuift
                foreach (Transform kind in w.testbeeld.transform)
                    kind.gameObject.SetActive(!metContent || kind.name.StartsWith("Hoek "));
            }
        }

        var wi = wanden[huidigeWand].instelling;
        int display = WandIndeling.DisplayVanWand(huidigeWand + 1);
        float bw = display < Display.displays.Length ? Display.displays[display].systemWidth : 1920;
        float bh = display < Display.displays.Length ? Display.displays[display].systemHeight : 1080;
        var s = "KALIBREREN   wand " + (huidigeWand + 1) + " van 4   (Display " + (display + 1) + ")\n" +
                WandUitleg[huidigeWand] + "\n\n" +
                "Zet elke hoek van het beeld op de hoek van de echte wand.\n\n";
        for (int k = 0; k < 4; k++)
        {
            var p = wi.Hoek(k);
            s += (k == huidigeHoek ? ">> " : "    ") + "hoek " + (k + 1) + " " + HoekNaam[k] + ":  x " + Mathf.RoundToInt(p.x * bw) + "  y " + Mathf.RoundToInt((1 - p.y) * bh) + "\n";
        }
        s += "\nRuimte: wand 1/3 " + ruimte.breedteWand1.ToString("0.00") + " m, wand 2/4 " + ruimte.breedteWand2.ToString("0.00") +
             " m, hoogte " + ruimte.hoogte.ToString("0.00") + " m, ooghoogte " + ruimte.ooghoogte.ToString("0.00") +
             " m   (de rode cirkel moet rond zijn)\n" +
             "Inhoud omhoog: " + (wi.verschuiving * 100f).ToString("0.0") + " % van de hoogte   (PageUp / PageDown)\n\n" +
             "1-4 = hoek kiezen      pijltjes = hoek verschuiven (Shift = 20 px, Ctrl = 1 px)\n" +
             "R = hoeken van deze wand terug      Tab = volgende wand (Shift+Tab = vorige)\n" +
             "T = " + (metContent ? "testbeeld tonen" : "content tonen") + "\n" +
             "Enter = opslaan en klaar      Esc = stoppen zonder opslaan";
        ToonMelding(s, 0f);
    }

    // ---------- Testbeeld (wordt in het wandbeeld getekend, dus je ziet het vervormd zoals de productie) ----------

    void MaakTestbeeld(int i)
    {
        var w = wanden[i];
        if (w.camera == null) return;
        var go = new GameObject("Testbeeld wand " + (i + 1), typeof(Canvas));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.sortingOrder = 2000;
        RegistreerCanvas(canvas, w.camera, go.name.StartsWith("Kader") ? 0.38f : 0.4f);
        canvas.sortingOrder = 2000;
        w.testbeeld = go;

        float hoog = 1080f;   // het canvas is 1080 eenheden hoog
        var ouder = go.transform;
        Vlak(ouder, new Color(0.92f, 0.92f, 0.92f), Vector2.zero, Vector2.one);
        var lijn = new Color(0.35f, 0.35f, 0.35f);
        for (int k = 1; k < 10; k++)
        {
            float f = k / 10f;
            Vlak(ouder, lijn, new Vector2(f, 0), new Vector2(f, 1), 2, 0);
            Vlak(ouder, lijn, new Vector2(0, f), new Vector2(1, f), 0, 2);
        }
        // Rand: die moet precies op de rand van de echte wand liggen
        var rand = new Color(0.1f, 0.45f, 0.9f);
        float dik = hoog * 0.012f;
        Vlak(ouder, rand, new Vector2(0, 0), new Vector2(0, 1), dik * 2, 0);
        Vlak(ouder, rand, new Vector2(1, 0), new Vector2(1, 1), dik * 2, 0);
        Vlak(ouder, rand, new Vector2(0, 0), new Vector2(1, 0), 0, dik * 2);
        Vlak(ouder, rand, new Vector2(0, 1), new Vector2(1, 1), 0, dik * 2);

        // Cirkel in het midden: rond op de muur = juiste verhouding
        var c = new GameObject("Cirkel", typeof(RectTransform), typeof(Image));
        c.transform.SetParent(ouder, false);
        var crt = c.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(hoog * 0.6f, hoog * 0.6f);
        var cimg = c.GetComponent<Image>();
        cimg.sprite = Cirkel();
        cimg.color = new Color(0.85f, 0.15f, 0.15f);
        cimg.raycastTarget = false;

        Tekst(ouder, "WAND " + (i + 1), Mathf.RoundToInt(hoog * 0.09f), TextAnchor.UpperCenter, new Vector2(0, 0.6f), new Vector2(1, 0.95f));

        // Hoekmerken met nummer
        for (int k = 0; k < 4; k++)
        {
            var m = new GameObject("Hoek " + (k + 1), typeof(RectTransform), typeof(Image));
            m.transform.SetParent(ouder, false);
            var rt = m.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = BeeldHoek[k];
            rt.sizeDelta = new Vector2(hoog * 0.09f, hoog * 0.09f);
            w.hoekMerk[k] = rt;
            w.hoekMerkBeeld[k] = m.GetComponent<Image>();
            w.hoekMerkBeeld[k].raycastTarget = false;
            var t = Tekst(m.transform, (k + 1).ToString(), Mathf.RoundToInt(hoog * 0.07f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            t.color = Color.white;
        }
        ZetLaag(go, LaagUI);
    }

    Sprite Cirkel()
    {
        if (cirkel != null) return cirkel;
        const int n = 512;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float r = n / 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                // Ring van 4 % dik, zachte rand van 1 pixel
                float a = Mathf.Clamp01(r - d) * Mathf.Clamp01(d - (r - n * 0.04f));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        cirkel = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return cirkel;
    }

    static void Vlak(Transform ouder, Color kleur, Vector2 min, Vector2 max, float extraBreed = 0, float extraHoog = 0)
    {
        var go = new GameObject("Vlak", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(ouder, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = new Vector2(-extraBreed / 2, -extraHoog / 2);
        rt.offsetMax = new Vector2(extraBreed / 2, extraHoog / 2);
        var img = go.GetComponent<Image>();
        img.color = kleur;
        img.raycastTarget = false;
    }

    static Text Tekst(Transform ouder, string inhoud, int grootte, TextAnchor uitlijning, Vector2 min, Vector2 max)
    {
        var go = new GameObject("Tekst", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(ouder, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = inhoud;
        t.fontSize = grootte;
        t.alignment = uitlijning;
        t.color = new Color(0.1f, 0.1f, 0.1f);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    // ---------- Bedienscherm ----------

    void ToonMelding(string tekst, float seconden)
    {
        if (bedienPaneel == null)
        {
            var go = new GameObject("Bedienscherm: kalibratie", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = 0;
            canvas.sortingOrder = 1001;
            var schaal = go.GetComponent<CanvasScaler>();
            schaal.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            schaal.referenceResolution = new Vector2(1920, 1080);
            schaal.matchWidthOrHeight = 0.5f;
            var paneel = new GameObject("Paneel", typeof(RectTransform), typeof(Image));
            paneel.transform.SetParent(go.transform, false);
            var rt = paneel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.1f, 0.1f); rt.anchorMax = new Vector2(0.9f, 0.9f); rt.offsetMin = rt.offsetMax = Vector2.zero;
            paneel.GetComponent<Image>().color = new Color(0, 0, 0, 0.9f);
            bedienPaneel = paneel;
            bedienTekst = Tekst(paneel.transform, "", 30, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            bedienTekst.rectTransform.offsetMin = new Vector2(40, 40);
            bedienTekst.rectTransform.offsetMax = new Vector2(-40, -40);
            bedienTekst.color = Color.white;
            bedienTekst.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
        bedienTekst.text = tekst;
        bedienPaneel.SetActive(true);
        meldingTot = seconden > 0 ? Time.unscaledTime + seconden : 0;
    }
}
