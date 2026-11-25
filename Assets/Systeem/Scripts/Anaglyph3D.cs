using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// OPTIE: 3D op de wanden met een rood/blauw- of rood/cyaan-brilletje (anaglyph).
/// Per wand maakt dit script twee extra camera's: het linker- en het rechteroog. Een shader voegt die samen tot één beeld.
/// Zet 'Aan' in de Inspector, of druk tijdens het spelen op de wisseltoets (standaard F3).
/// Let op: elke wand wordt dan drie keer getekend. Bij een zware scene kan de productie trager worden.
/// </summary>
public class Anaglyph3D : MonoBehaviour
{
    public enum Bril { RoodBlauw = 0, RoodCyaanKleur = 1, RoodCyaanDubois = 2, RoodCyaanGrijs = 3, Gekalibreerd = 4 }

    [Tooltip("3D aan of uit.")]
    public bool aan = false;

    [Tooltip("Welk brilletje en welke weergave? Rood glas zit links.\n" +
             "Rood Cyaan Grijs: rustigst en lichtst (aanrader). Kleur: houdt de sfeer. Dubois: kleur met minder spookbeelden.\n" +
             "Rood Blauw: voor brilletjes met een blauw (geen cyaan) glas.")]
    public Bril bril = Bril.Gekalibreerd;

    [Tooltip("Alleen bij Gekalibreerd: kleur voor het linkeroog (rood glas). Gekozen met de kleurtest (F8) in de Immersive Room:\n" +
             "de rode kleur die het BLAUWE glas het best tegenhoudt (25-11-2026: 180/0/0).")]
    public Color kleurLinks = new Color32(180, 0, 0, 255);

    [Tooltip("Alleen bij Gekalibreerd: kleur voor het rechteroog (blauw glas). Gekozen met de kleurtest (F8):\n" +
             "de lichtste blauwe kleur die het RODE glas nog goed tegenhoudt (25-11-2026: 0/0/100).")]
    public Color kleurRechts = new Color32(0, 0, 100, 255);

    [Tooltip("Afstand tussen de ogen in meters. Groter = sterker 3D-effect (maar vermoeiender). Normaal 0.065.")]
    [Range(0f, 0.3f)] public float oogafstand = 0.065f;

    [Tooltip("Op deze afstand (in meters) ligt een object precies 'op' de wand. Dichterbij komt het uit de wand, verder weg ligt het erachter.")]
    [Range(0.5f, 30f)] public float focusAfstand = 4f;

    [Tooltip("Hoe licht het 3D-beeld is. Door een gekleurd glas zie je maar één kleur, dus het beeld lijkt donkerder. Normaal 1.6.")]
    [Range(0.5f, 4f)] public float helderheid = 1.2f;

    [Tooltip("Kleiner dan 1 maakt vooral de donkere delen lichter. Normaal 0.85.")]
    [Range(0.3f, 1.2f)] public float gamma = 0.85f;

    [Tooltip("Met deze toets zet je 3D aan of uit tijdens het spelen. Kies een toets die je productie niet gebruikt.")]
    public Key wisselToets = Key.F3;

    [Tooltip("Het materiaal met de shader Systeem/Anaglyph (staat in Assets/Systeem/Anaglyph).")]
    public Material anaglyphMateriaal;

    [Tooltip("De camera's van de wanden.")]
    public Camera[] wanden;

    class Paar
    {
        public Camera wand, links, rechts;
        public RenderTexture beeldLinks, beeldRechts;
        public GameObject canvas;
        public Canvas canvasComponent;
        public Material materiaal;
        public int oudeMask;
    }

    readonly List<Paar> paren = new List<Paar>();
    const int Breedte = 1920, Hoogte = 1080;

    void Start()
    {
        foreach (var wand in wanden)
        {
            if (wand != null) paren.Add(MaakPaar(wand));
        }
        Zet(aan);
    }

    void Update()
    {
        var toetsenbord = Keyboard.current;
        if (toetsenbord != null && wisselToets != Key.None && toetsenbord[wisselToets].wasPressedThisFrame) Zet(!aan);
        if (aan) Bijwerken();

        // Het 3D-beeld volgt de beamer van de wand (die kan tijdens het spelen veranderen, zie WandIndeling)
        foreach (var p in paren) p.canvasComponent.targetDisplay = p.wand.targetDisplay;
    }

    /// <summary>Zet 3D aan of uit. Kan ook vanuit een state (UnityEvent) worden aangeroepen.</summary>
    public void Zet(bool nieuw)
    {
        aan = nieuw;
        foreach (var p in paren)
        {
            p.links.enabled = aan;
            p.rechts.enabled = aan;
            p.canvas.SetActive(aan);
            // De wandcamera hoeft de scene niet meer zelf te tekenen als 3D aan staat.
            // In de build tekent hij dan alleen nog de teksten en het 3D-beeld (laag UI), zie WandKalibratie.
            int alleenUI = 1 << LayerMask.NameToLayer("UI");
            p.wand.cullingMask = aan ? (p.wand.targetTexture != null ? alleenUI : 0) : p.oudeMask;
        }
        if (aan) Bijwerken();
    }

    Paar MaakPaar(Camera wand)
    {
        var p = new Paar { wand = wand, oudeMask = wand.cullingMask };
        p.beeldLinks = new RenderTexture(Breedte, Hoogte, 24) { name = wand.name + " links" };
        p.beeldRechts = new RenderTexture(Breedte, Hoogte, 24) { name = wand.name + " rechts" };
        p.links = MaakOog(wand, "Oog links", p.beeldLinks);
        p.rechts = MaakOog(wand, "Oog rechts", p.beeldRechts);

        // Beeld op het scherm van deze wand, ONDER de teksten van de wand (die staan op sorting order 0)
        p.canvas = new GameObject("Anaglyph " + wand.name, typeof(Canvas));
        p.canvas.transform.SetParent(transform, false);
        var canvas = p.canvas.GetComponent<Canvas>();
        p.canvasComponent = canvas;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.targetDisplay = wand.targetDisplay;
        canvas.sortingOrder = -100;
        // In de build tekent de wandcamera naar een wandbeeld (WandKalibratie): het 3D-beeld gaat daarin mee, achter de teksten
        WandKalibratie.RegistreerCanvas(canvas, wand, 0.6f);
        var beeld = new GameObject("Beeld", typeof(RectTransform), typeof(RawImage));
        beeld.transform.SetParent(p.canvas.transform, false);
        beeld.layer = p.canvas.layer;
        var rt = beeld.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        p.materiaal = new Material(anaglyphMateriaal);
        p.materiaal.SetTexture("_RechtsTex", p.beeldRechts);
        var raw = beeld.GetComponent<RawImage>();
        raw.texture = p.beeldLinks;
        raw.material = p.materiaal;
        raw.raycastTarget = false;
        return p;
    }

    static Camera MaakOog(Camera wand, string naam, RenderTexture doel)
    {
        var go = new GameObject(naam);
        go.transform.SetParent(wand.transform, false);
        var oog = go.AddComponent<Camera>();
        oog.CopyFrom(wand);
        oog.targetTexture = doel;
        oog.targetDisplay = 0;
        // De ogen zien de teksten en het 3D-beeld zelf niet (laag UI), anders kijken ze naar hun eigen beeld
        oog.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        oog.depth = wand.depth - 1;   // eerst de ogen, dan de wand
        oog.enabled = false;
        return oog;
    }

    void Bijwerken()
    {
        foreach (var p in paren)
        {
            p.links.transform.localPosition = new Vector3(-oogafstand / 2f, 0, 0);
            p.rechts.transform.localPosition = new Vector3(oogafstand / 2f, 0, 0);
            float aspect = p.wand.aspect;   // de verhouding van de wand (zie WandKalibratie)
            p.links.projectionMatrix = OogProjectie(p.wand, aspect, oogafstand, focusAfstand, true);
            p.rechts.projectionMatrix = OogProjectie(p.wand, aspect, oogafstand, focusAfstand, false);
            p.materiaal.SetFloat("_Modus", (float)bril);
            p.materiaal.SetFloat("_Helderheid", helderheid);
            p.materiaal.SetFloat("_Gamma", gamma);
            p.materiaal.SetColor("_KleurLinks", kleurLinks);
            p.materiaal.SetColor("_KleurRechts", kleurRechts);
        }
    }

    /// <summary>
    /// Off-axis projectie: beide ogen kijken recht vooruit, maar het beeldvlak schuift een beetje,
    /// zodat objecten op 'focusAfstand' voor beide ogen op dezelfde plek op de wand vallen.
    /// </summary>
    public static Matrix4x4 OogProjectie(Camera wand, float aspect, float oogafstand, float focusAfstand, bool links)
    {
        float near = wand.nearClipPlane, far = wand.farClipPlane;
        // Het kijkvenster van de wandcamera zelf (in de build scheef, berekend uit de maten van de ruimte: zie WandKalibratie)
        var p = wand.projectionMatrix;
        float breed = 2f * near / p[0, 0], hoog = 2f * near / p[1, 1];
        float midX = p[0, 2] * breed / 2f, midY = p[1, 2] * hoog / 2f;
        float schuif = (oogafstand / 2f) * near / Mathf.Max(0.01f, focusAfstand);
        float s = links ? schuif : -schuif;
        return Matrix4x4.Frustum(midX - breed / 2f + s, midX + breed / 2f + s, midY - hoog / 2f, midY + hoog / 2f, near, far);
    }
}
