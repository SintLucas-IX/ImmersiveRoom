using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hulpmiddel bij het kalibreren (docent): druk op F9 en precies op elke hoek van de ruimte verschijnt een paal met blokjes en bollen,
/// precies op de naad tussen twee wanden. Klopt de kalibratie, dan lijkt elk object één geheel, half op de ene en half op de
/// andere wand. Nog een keer F9 = weg. Studenten hoeven hier niets mee te doen.
/// </summary>
public class HoekTest : MonoBehaviour
{
    const Key Toets = Key.F9;

    static readonly Color[] Kleuren =
    {
        new Color(0.9f, 0.25f, 0.2f),   // hoek wand 1-2
        new Color(0.2f, 0.75f, 0.3f),   // hoek wand 2-3
        new Color(0.25f, 0.45f, 0.95f), // hoek wand 3-4
        new Color(0.95f, 0.75f, 0.15f), // hoek wand 4-1
    };

    GameObject inhoud;
    Material basis;

    void Update()
    {
        var tb = Keyboard.current;
        if (tb == null || !tb[Toets].wasPressedThisFrame || WandIndeling.BezigMetInstellen) return;
        if (inhoud != null) { Destroy(inhoud); inhoud = null; Debug.Log("[HoekTest] Uit"); return; }
        Maak();
    }

    void Maak()
    {
        // Midden = de plek van de wandcamera's; wand 1 kijkt "vooruit"
        var wand1 = ZoekWandcamera(1);
        Vector3 oog = wand1 != null ? wand1.transform.position : new Vector3(0, WandKalibratie.Ooghoogte, 0);
        Vector3 vooruit = wand1 != null ? Vector3.ProjectOnPlane(wand1.transform.forward, Vector3.up).normalized : Vector3.forward;
        float vloer = oog.y - WandKalibratie.Ooghoogte;
        float hoogte = WandKalibratie.WandHoogte;

        inhoud = new GameObject("Hoektest (F9)");   // in de scene, zodat de wandcamera's het zien
        for (int h = 0; h < 4; h++)
        {
            // Precies op de echte hoek van de ruimte (zie WandKalibratie): dan moet de paal vanaf elke plek op de naad vallen
            var rechts = Vector3.Cross(Vector3.up, vooruit);   // naar wand 2
            var hp = WandKalibratie.HoekPlek(h);
            var richting = (rechts * hp.x + vooruit * hp.y).normalized;
            var voet = new Vector3(oog.x, vloer, oog.z) + rechts * hp.x + vooruit * hp.y;
            int volgende = h == 3 ? 1 : h + 2;
            var hoek = new GameObject("Hoek wand " + (h + 1) + "-" + volgende).transform;
            hoek.SetParent(inhoud.transform, false);
            hoek.position = voet;
            hoek.rotation = Quaternion.LookRotation(-richting);

            // Paal van vloer tot bovenkant van het beeld
            Deel(PrimitiveType.Cylinder, hoek, new Vector3(0, hoogte / 2f, 0), new Vector3(0.06f, hoogte / 2f, 0.06f), Color.white);
            float[] hoogtes = { 0.3f, 0.9f, 1.5f, 2.0f, 2.45f };
            for (int i = 0; i < hoogtes.Length; i++)
            {
                bool blok = i % 2 == 0;
                var kleur = i % 2 == 0 ? Kleuren[h] : Color.Lerp(Kleuren[h], Color.white, 0.5f);
                Deel(blok ? PrimitiveType.Cube : PrimitiveType.Sphere, hoek, new Vector3(0, hoogtes[i], 0), Vector3.one * 0.35f, kleur);
            }
            // Horizontale balk dwars over de naad: moet op beide wanden even hoog en recht doorlopen
            Deel(PrimitiveType.Cube, hoek, new Vector3(0, 1.2f, 0), new Vector3(1.2f, 0.05f, 0.05f), Color.black);
        }
        Debug.Log("[HoekTest] Aan: palen precies op de vier hoeken van de ruimte");
    }

    void Deel(PrimitiveType soort, Transform ouder, Vector3 plek, Vector3 schaal, Color kleur)
    {
        var go = GameObject.CreatePrimitive(soort);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(ouder, false);
        go.transform.localPosition = plek;
        go.transform.localScale = schaal;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Materiaal(kleur);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Een standaardmateriaal van Unity kan in een URP-build ontbreken (roze). Daarom een kopie van een materiaal uit de scene.
    Material Materiaal(Color kleur)
    {
        if (basis == null)
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor")) { basis = r.sharedMaterial; break; }
            }
            if (basis == null) basis = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        }
        var m = new Material(basis);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", kleur);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        m.color = kleur;
        return m;
    }

    static Camera ZoekWandcamera(int wand)
    {
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (cam.name == "Wand_" + wand) return cam;
        return null;
    }
}
