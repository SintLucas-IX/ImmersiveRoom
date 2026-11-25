using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Zorgt dat het beeld van elke beamer precies op zijn wand valt. Studenten hoeven hier niets mee te doen.
///
/// In de build tekent elke wandcamera naar een eigen beeld. Dat beeld wordt vervormd op de beamer gezet, met vier
/// hoekpunten per wand. Instellen (kalibreren) doe je in de Immersive Room met F11, één wand tegelijk:
///   1-4 = hoek kiezen, pijltjes = hoek verschuiven (Shift = grote stappen, Ctrl = 1 pixel),
///   + / - = verhouding (tot de cirkel op de muur echt rond is), PageUp/PageDown = inhoud omhoog/omlaag,
///   R = hoeken van deze wand terug,
///   Tab = volgende wand (Shift+Tab = vorige), Enter = opslaan, Esc = stoppen zonder opslaan.
/// Bewaard in C:\Users\Public\ImmersiveRoom\wandkalibratie.json, voor elke build op die pc.
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

        public Vector2 Hoek(int i) => new Vector2(hoeken[i * 2], hoeken[i * 2 + 1]);
        public void ZetHoek(int i, Vector2 p) { hoeken[i * 2] = p.x; hoeken[i * 2 + 1] = p.y; }
        public WandInstelling Kopie() => new WandInstelling { hoeken = (float[])hoeken.Clone(), verhouding = verhouding, verschuiving = verschuiving };
    }

    [Serializable]
    class Bestand
    {
        public WandInstelling[] wanden;
        public string ingesteldOp = "";
        public string pc = "";
    }

    class Wand
    {
        public WandInstelling instelling = new WandInstelling();
        public Camera camera;
        public float horizontaalBeeldveld;    // zoals de camera in de scene stond (graden)
        public RenderTexture beeld;
        public Canvas uitvoer;                // op de beamer: het vervormde beeld
        public RawImage uitvoerBeeld;
        public Material warp;
        public GameObject testbeeld;          // tijdens het kalibreren, in het wandbeeld zelf
        public RectTransform[] hoekMerk = new RectTransform[4];
        public Image[] hoekMerkBeeld = new Image[4];
    }

    readonly Wand[] wanden = new Wand[AantalWanden];
    readonly Dictionary<Camera, float> beeldveldVan = new Dictionary<Camera, float>();
    WandInstelling[] voorKalibratie;
    int huidigeWand, huidigeHoek;
    GameObject bedienPaneel;
    Text bedienTekst;
    float meldingTot;
    Key herhaalToets = Key.None;
    float herhaalVolgende;
    Sprite cirkel;

    static bool InBuild => !Application.isEditor;
    static string Map => Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? Application.persistentDataPath, "ImmersiveRoom");
    static string Pad => Path.Combine(Map, "wandkalibratie.json");

    void Awake()
    {
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
            if (!File.Exists(Pad)) { Debug.Log("[WandKalibratie] Nog niet gekalibreerd (" + Pad + "). Druk F11 in de build."); return; }
            var b = JsonUtility.FromJson<Bestand>(File.ReadAllText(Pad));
            if (b == null || b.wanden == null || b.wanden.Length != AantalWanden) { Debug.LogWarning("[WandKalibratie] " + Pad + " is ongeldig."); return; }
            for (int i = 0; i < AantalWanden; i++)
            {
                var w = b.wanden[i];
                if (w != null && w.hoeken != null && w.hoeken.Length == 8 && w.verhouding > 0.2f && w.verhouding < 5f) wanden[i].instelling = w;
            }
            Debug.Log("[WandKalibratie] Geladen uit " + Pad + " (ingesteld " + b.ingesteldOp + ")");
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
            var b = new Bestand { wanden = new WandInstelling[AantalWanden], ingesteldOp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), pc = Environment.MachineName };
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

    // Het wandbeeld krijgt de verhouding van de echte wand (vierkante pixels), max 1920 breed of 2160 hoog
    void MaakBeeld(int i)
    {
        var w = wanden[i];
        float a = w.instelling.verhouding;
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

    // Horizontaal beeldveld blijft zoals in de scene (de wanden sluiten zo op elkaar aan), de hoogte volgt de verhouding
    void ZetCamera(int i)
    {
        var w = wanden[i];
        if (w.camera == null) return;
        float a = w.instelling.verhouding;
        float h = w.horizontaalBeeldveld * Mathf.Deg2Rad;
        var cam = w.camera;
        cam.ResetProjectionMatrix();
        cam.aspect = a;
        cam.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(h / 2f) / a) * Mathf.Rad2Deg;

        // Inhoud omhoog: het beeldvlak schuift omlaag (zelfde beeldveld, geen zwarte rand)
        float s = w.instelling.verschuiving;
        if (Mathf.Abs(s) > 0.0001f)
        {
            float n = cam.nearClipPlane;
            float top = n * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float rechts = top * a;
            float schuif = 2f * top * s;
            cam.projectionMatrix = Matrix4x4.Frustum(-rechts, rechts, -top - schuif, top - schuif, n, cam.farClipPlane);
        }
    }

    void BijSceneIngedeeld(IReadOnlyDictionary<UnityEngine.Object, int> wandVan)
    {
        // Eerst de camera's: die tekenen voortaan naar het wandbeeld
        foreach (var kv in wandVan)
        {
            if (!(kv.Key is Camera cam) || cam == null) continue;
            var w = wanden[kv.Value - 1];
            if (!beeldveldVan.ContainsKey(cam))
            {
                // Zoals de camera op een 16:9-beamer stond
                float v = cam.fieldOfView * Mathf.Deg2Rad;
                beeldveldVan[cam] = 2f * Mathf.Atan(Mathf.Tan(v / 2f) * StandaardVerhouding) * Mathf.Rad2Deg;
            }
            w.camera = cam;
            w.horizontaalBeeldveld = beeldveldVan[cam];
            cam.targetTexture = w.beeld;
            cam.cullingMask |= 1 << LaagUI;
            ZetCamera(kv.Value - 1);
        }
        // Dan de canvassen van de wanden (teksten): die tekenen mee in het wandbeeld, zodat ze ook vervormd worden
        foreach (var kv in wandVan)
        {
            if (!(kv.Key is Canvas canvas) || canvas == null) continue;
            var cam = wanden[kv.Value - 1].camera;
            if (cam == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.05f;
            ZetLaag(canvas.gameObject, LaagUI);
        }
        for (int i = 0; i < AantalWanden; i++)
            if (wanden[i].testbeeld != null) { Destroy(wanden[i].testbeeld); wanden[i].testbeeld = null; }
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

        if (!Bezig)
        {
            if (tb[KalibratieToets].wasPressedThisFrame && !WandIndeling.BezigMetInstellen)
            {
                if (!InBuild) { Debug.Log("[WandKalibratie] Kalibreren (F11) kan alleen in de build, niet in de Editor."); return; }
                BeginKalibratie();
            }
            return;
        }

        bool shift = tb.leftShiftKey.isPressed || tb.rightShiftKey.isPressed;
        bool ctrl = tb.leftCtrlKey.isPressed || tb.rightCtrlKey.isPressed;
        var w = wanden[huidigeWand];

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
        if (tb.rKey.wasPressedThisFrame)
        {
            w.instelling.hoeken = new WandInstelling().hoeken;
            ZetWarp(huidigeWand);
            ToonKalibratie();
            return;
        }

        // Pijltjes en + / -: één keer per druk, en snel herhalen als je de toets vasthoudt
        Key[] toetsen = { Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow, Key.Equals, Key.NumpadPlus, Key.Minus, Key.NumpadMinus, Key.PageUp, Key.PageDown };
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

        if (actief == Key.Equals || actief == Key.NumpadPlus || actief == Key.Minus || actief == Key.NumpadMinus)
        {
            float stap = shift ? 0.05f : ctrl ? 0.002f : 0.01f;
            float teken = (actief == Key.Minus || actief == Key.NumpadMinus) ? -1 : 1;
            w.instelling.verhouding = Mathf.Clamp(w.instelling.verhouding + teken * stap, 0.3f, 4f);
            MaakBeeld(huidigeWand);
            if (w.testbeeld != null) { Destroy(w.testbeeld); w.testbeeld = null; }
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

    void LateUpdate()
    {
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
        ToonKalibratie();
    }

    void StopKalibratie(string melding)
    {
        Bezig = false;
        herhaalToets = Key.None;
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
        for (int i = 0; i < AantalWanden; i++)
        {
            var w = wanden[i];
            bool deze = i == huidigeWand;
            w.uitvoerBeeld.color = deze ? Color.white : Color.black;   // alleen de wand die je kalibreert is verlicht
            if (deze && w.testbeeld == null) MaakTestbeeld(i);
            if (w.testbeeld != null) w.testbeeld.SetActive(deze);
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
        s += "\nVerhouding: " + wi.verhouding.ToString("0.000") + "   (zet + / - tot de cirkel op de muur rond is)\n" +
             "Inhoud omhoog: " + (wi.verschuiving * 100f).ToString("0.0") + " % van de hoogte   (PageUp / PageDown)\n\n" +
             "1-4 = hoek kiezen      pijltjes = hoek verschuiven (Shift = 20 px, Ctrl = 1 px)\n" +
             "R = hoeken van deze wand terug      Tab = volgende wand (Shift+Tab = vorige)\n" +
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
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = w.camera;
        canvas.planeDistance = w.camera.nearClipPlane + 0.02f;
        canvas.sortingOrder = 2000;
        w.testbeeld = go;

        float hoog = w.beeld.height;
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
