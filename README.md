# Immersive Room – template

Startproject (Unity) voor de **Immersive Room** van IX SintLucas: een ruimte met **4 wanden en 4 beamers**.
Je bouwt een beleving in Unity; elke wand toont wat de camera voor die wand ziet. Een vijfde scherm is het
**bedienscherm**: daarop zie je alle vier de wanden klein, plus in welke state de productie is.

## Openen

1. Open het project met **Unity 6000.3.28f1** (via Unity Hub: *Add project from disk*).
2. De scene **ImmersiveRoom-Demo** opent vanzelf (`Assets/ImmersiveRoom/Scenes`).
3. Druk op **Play**. Kies bovenin de **Game**-weergave het scherm: *Display 1* = bedienscherm, *Display 2 t/m 5* = wand 1 t/m 4.

## Hoe werkt het?

| Onderdeel in de scene | Wat het doet |
|---|---|
| **Wanden (camera's)** | Vier camera's (`Wand_1` t/m `Wand_4`) in het midden van de kamer, elk kijkt naar één wand. Elke wand heeft een **Tekst** en een **Videoscherm** (staan uit). |
| **Bedienscherm** | Laat de vier wanden klein zien en de **Info**: welke state, laatste toets, laatste NFC-code. |
| **Productie** | Het script **ProductieStates**: je verhaal of puzzel, in states. Plus de demo-inhoud, vloer, licht en geluid. |

### States: je storyboard

De productie bestaat uit **states**. Een state is een plaatje uit je storyboard.
In elke state kan een **toets** (of een **NFC-tag**) iets laten gebeuren, en de productie naar een volgende state brengen.
Dezelfde toets mag in een andere state iets anders doen. Zo maak je een verhaal, een beleving of een puzzel.

Alles stel je in bij **Productie → ProductieStates** in de Inspector:

- **Bij Binnenkomen**: wat er gebeurt zodra de productie in deze state komt (bv. object aan, geluid afspelen).
- **Toetsen**: per regel een toets *of* een NFC-code, de **Acties** die dan gebeuren, en of de productie naar een
  **Volgende State** gaat.
- Acties toevoegen: klik op **+**, sleep een object in het vakje, en kies wat het moet doen, bijvoorbeeld
  `GameObject → SetActive`, `AudioSource → Play` of `VideoPlayer → Play`.

Het voorbeeld in de demo: **SPATIE** start de beleving, **A** speelt een geluid, nog een keer **SPATIE** zet een video op wand 1,
en **SPATIE** begint weer opnieuw.

Liever programmeren? Kijk naar `Assets/Systeem/Scripts/VoorbeeldStatesInCode.cs`.

### Toetsen komen van een toetsenbord, ESP32, Arduino of NFC-lezer

- Voor Unity is alles **gewone toetsenbordinvoer**: een (Bluetooth-)toetsenbord, een ESP32 of Arduino die zich als
  toetsenbord voordoet, of een **USB-NFC-lezer** in toetsenbordmodus.
- Een NFC-lezer typt de **code van de tag** en daarna Enter. Scan je tag: de code staat op het bedienscherm
  (*Laatste NFC-code*) en in de Console. Kopieer die naar **Nfc Code** in de Inspector.
- Toetsen die heel snel achter elkaar komen (zoals een NFC-lezer typt) worden als NFC-code gezien en niet als losse toetsen.
  Een losse toets wordt daarom 0,08 seconde later uitgevoerd.
- Het venster van de productie moet **actief** zijn (aangeklikt), anders komen de toetsen niet aan.

### In elke state: bijvoorbeeld een figuurtje op de NFC-lezer

Onder **In Elke State** zet je toetsen of NFC-tags die **altijd** werken, in welke state de productie ook is.
Handig voor een **3D-geprint figuurtje** met een NFC-sticker in de voet (zoals Skylanders): zet het op de lezer en het karakter
verschijnt op de wand. De demo heeft een **Figuur (voorbeeld)**: toets **F** laat het zien, **G** haalt het weg, en de
NFC-code **FIGUUR** (vervang die door de code van jouw tag) doet hetzelfde. Het script **Draaien** laat het figuur ronddraaien.

Let op: de lezer merkt alleen dat je een figuur **neerzet**, niet dat je het weghaalt.

### Optie: 3D met een rood/blauw-brilletje (anaglyph)

Klik op **Wanden (camera's)** en zet bij **Anaglyph 3D** het vinkje **Aan**, of druk tijdens het spelen op **F3**.
Elke wand toont dan een 3D-beeld voor een brilletje met **rood glas links**. Kies bij **Bril**:
*Rood Cyaan Grijs* (rustigst en lichtst, aanrader), *Rood Cyaan Kleur* (houdt de sfeer), *Rood Cyaan Dubois*
(kleur met minder spookbeelden) of *Rood Blauw* (voor een brilletje met een blauw glas).

- **Oogafstand**: hoe sterk het 3D-effect is (normaal 0.065).
- **Focus Afstand**: op die afstand (in meter) ligt iets precies *op* de wand. Verder weg ligt achter de wand
  (rustig, de wand is een raam), dichterbij komt naar je toe (wow, maar vermoeiend als het te veel is).
- **Helderheid** en **Gamma**: maak het beeld lichter als het door het brilletje te donker is.
- Het bedienscherm blijft gewoon (zonder 3D).
- Elke wand wordt dan drie keer getekend: bij een zware scene kan het trager worden.

**Zo wordt 3D mooi:**

| Doe wel | Doe niet |
|---|---|
| Lichte achtergrond en lichte kleuren | Grote vlakken in felrood of felblauw (die ziet één oog bijna niet) |
| Textuur en randen op alles (schaakbord, stenen, patronen) | Effen vlakken zonder textuur (daar zie je geen diepte) |
| Veel kleine dingen op verschillende afstanden (deeltjes!) | Alles op dezelfde afstand |
| Dingen die op je af komen, af en toe | Alles de hele tijd vlak voor je neus |

Kijk naar de scene **ImmersiveRoom-3D-Demo**: een grote hal met zuilen en deuropeningen; objecten vliegen rond en langs je heen, van wand naar wand, en deeltjes wervelen om je heen
(SPATIE: rust → deeltjes → aanval → rust). Daar staat 3D meteen aan.

### Video op een wand

Zet je video (`.mp4`) in `Assets/ImmersiveRoom/Video`. Selecteer **Wanden (camera's) → Wand_1 → Videoscherm wand 1**,
sleep je video in **Video Clip** van de **Video Player**, en zet het scherm aan vanuit een state
(`GameObject → SetActive` aan, en `VideoPlayer → Play`).

## De wanden en de beamers

Van bovenaf gezien, met de klok mee (links/rechts gezien vanaf de pc, de ruimte in kijkend):

| Wand | In de ruimte | Camera | Game-weergave |
|---|---|---|---|
| 1 | de wand waar de pc tegenaan staat | `Wand_1` | Display 2 |
| 2 | links | `Wand_2` | Display 3 |
| 3 | achterwand (tegenover de pc) | `Wand_3` | Display 4 |
| 4 | kleine wand rechts | `Wand_4` | Display 5 |

Display 1 is het bedienscherm. In Unity hoef je verder niets te doen.

**Docent: welke beamer is welke wand? (F12, alleen in de build)**
Windows nummert de beamers in een eigen volgorde. Druk in de build op **F12**: elke beamer toont een grote letter
(A t/m D). Het bedienscherm vraagt per wand welke letter erop staat. De indeling wordt bewaard in
`C:\Users\Public\ImmersiveRoom\wandindeling.json` en geldt voor elke build op die pc. Backspace = vorige stap, Esc = stoppen.
Staat er linksonder op het bedienscherm een rode balk "Wandindeling niet ingesteld", dan is dit nog niet gedaan.

## Je eigen scene uit een ander project (bv. je kermis of je Hygge-beleving)

1. In je **eigen project**: klik in het **Project**-venster met de rechtermuisknop op je map
   (bv. `Assets/VR-Basics/Studenten/<Voornaam>`) → **Export As Asset Package...**
   Zet **Include all scripts** **uit**, vink alleen jouw spullen aan en klik **Export**.
2. In **dit project**: **Assets → Import Package → Custom Package...**, kies je `.unitypackage` en klik **Import**.
3. Sleep je objecten (of prefab) in de scene onder **Productie**, en zet **Demo-inhoud** uit of verwijder die.
4. VR-onderdelen (XR Origin, controllers, grab-scripts) werken hier niet: in de Immersive Room is er geen bril.
   Laat ze weg bij het exporteren.

## Mappen

| Map | Inhoud |
|---|---|
| `Assets/ImmersiveRoom/` | **Van jou**: scenes, materialen, audio, video, en `Studenten/<Voornaam>` voor je eigen werk. |
| `Assets/Systeem/` | **Niet aankomen**: scripts, bedienscherm, videoschermen. |

## Bronnen

Geluiden: CC0 (Kenney), zie `Assets/ImmersiveRoom/Audio/BRONNEN.md`.
