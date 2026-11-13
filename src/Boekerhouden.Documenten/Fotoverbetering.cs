using SkiaSharp;

namespace Boekerhouden.Documenten;

/// <summary>
/// Maakt van een foto (van een bonnetje of factuur) een scan: rechtop gezet volgens de camera (EXIF), verkleind tot
/// een werkbare maat, in grijstinten, met de belichting vlakgetrokken (schaduw en vergeeld papier worden wit) en het
/// contrast verhoogd (inkt wordt zwart), en rechtgezet als de foto scheef is genomen (tot een graad of tien), zodat de
/// regels van de tekst weer horizontaal lopen. Dat leest beter, voor de tekstherkenning en voor de mens, en geeft een
/// kleinere PDF. Ligt de bon smal tussen andere dingen (in een ordner, op een ander vel met drukwerk), dan wordt hij
/// uitgesneden (<see cref="Uitsnede"/>), zodat de tekst ernaast niet meegelezen wordt; ringen van een ordner en andere
/// grote donkere vormen worden weggepoetst (<see cref="WisDik"/>).
/// </summary>
public static class Fotoverbetering
{
    /// <summary>De lange zijde wordt hoogstens zo groot (ruim genoeg voor kleine lettertjes op een A4).</summary>
    public const int MaximumZijde = 2600;

    /// <summary>De grootte van een blok waarover de achtergrond (het papier) wordt geschat.</summary>
    private const int Blok = 48;

    /// <summary>Onder deze helderheid (na het vlaktrekken) is het inkt, erboven papier; ertussen loopt het geleidelijk.</summary>
    private const int Zwart = 90;
    private const int Wit = 205;

    /// <summary>Onder deze helderheid (na het vlaktrekken) telt iets als inkt bij het zoeken van de bon; ook lichte thermische inkt.</summary>
    private const int Inkt = 205;

    /// <summary>De verbeterde foto als JPEG in grijstinten; leeg als het bestand geen leesbare afbeelding is.</summary>
    public static byte[]? Verbeter(byte[] foto) => Verbeter(foto, rechtzetten: true);

    /// <param name="rechtzetten">
    /// Een scheve foto rechtzetten. Niet bij een scan die al als pagina in een PDF staat: daar moeten de woorden op hun
    /// plek op de pagina blijven (voor aanwijzen en oplichten).
    /// </param>
    public static byte[]? Verbeter(byte[] foto, bool rechtzetten)
    {
        ArgumentNullException.ThrowIfNull(foto);
        try
        {
            using var rechtop = Rechtop(foto);
            if (rechtop is null)
            {
                return null;
            }

            var (breedte, hoogte) = (rechtop.Width, rechtop.Height);
            var grijs = Grijs(rechtop);
            var papier = Papier(grijs, breedte, hoogte);
            var uit = new byte[grijs.Length];
            for (var y = 0; y < hoogte; y++)
            {
                for (var x = 0; x < breedte; x++)
                {
                    var i = (y * breedte) + x;

                    // Vlaktrekken: de helderheid ten opzichte van het papier op die plek.
                    uit[i] = (byte)Math.Min(255, grijs[i] * 255 / Math.Max(Achtergrond(papier, x, y), 40));
                }
            }

            // Grote donkere vormen (de ringen van een ordner, een vinger, de rand van de tafel) zijn geen tekst: weg ermee,
            // vóór het contrast, zodat ze de inktgrens niet bepalen en de bon niet doorsnijden.
            var plat = rechtzetten ? (byte[])uit.Clone() : uit;
            WisDik(uit, breedte, hoogte);

            // Rechtzetten vóór het uitsnijden: dan lopen de regels horizontaal en is te zien welke tekst bij de bon hoort.
            // De hoek wordt gemeten op een voorlopig beeld met contrast (van vóór het wegpoetsen), zonder de donkere
            // vlakken naast het papier.
            if (rechtzetten)
            {
                var voorlopig = new byte[plat.Length];
                var grens = Inktgrens(plat);
                for (var i = 0; i < plat.Length; i++)
                {
                    voorlopig[i] = (byte)Math.Clamp((plat[i] - grens) * 255 / (Wit - grens), 0, 255);
                }

                WisVlakken(voorlopig, breedte, hoogte);
                var hoek = Scheefstand(voorlopig, breedte, hoogte);
                if (Math.Abs(hoek) >= 0.3)
                {
                    uit = Draai(uit, breedte, hoogte, -hoek);
                }
            }

            // Een smalle bon tussen andere dingen: alleen de bon houden.
            if (rechtzetten && Uitsnede(uit, breedte, hoogte) is var (van, tot) && (van > 0 || tot < breedte))
            {
                var smal = new byte[(tot - van) * hoogte];
                for (var y = 0; y < hoogte; y++)
                {
                    Array.Copy(uit, (y * breedte) + van, smal, y * (tot - van), tot - van);
                }

                (uit, breedte) = (smal, tot - van);
            }

            // Contrast: wat donkerder is dan de inkt wordt zwart, papier wit. Vervaagde (thermische) of dunne inkt is
            // lichter dan gewone drukinkt; dan schuift de grens mee, zodat ook die letters zwart worden.
            var zwart = Inktgrens(uit);
            // Bij lichte inkt (de grens hoog) blijft er een glijdende overgang naar het papier, anders vallen dunne streepjes
            // (een matrixprinter, een versleten printkop) weg.
            var wit = Math.Max(Wit, zwart + 45);
            for (var i = 0; i < uit.Length; i++)
            {
                uit[i] = (byte)Math.Clamp((uit[i] - zwart) * 255 / (wit - zwart), 0, 255);
            }

            WisVlakken(uit, breedte, hoogte);
            using var resultaat = new SKBitmap(new SKImageInfo(breedte, hoogte, SKColorType.Gray8, SKAlphaType.Opaque));
            System.Runtime.InteropServices.Marshal.Copy(uit, 0, resultaat.GetPixels(), uit.Length);
            using var beeld = SKImage.FromBitmap(resultaat);
            using var jpeg = beeld.Encode(SKEncodedImageFormat.Jpeg, 82);
            return jpeg?.ToArray();
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            // Een beschadigde of onbekende afbeelding: de foto blijft zoals hij is.
            return null;
        }
    }

    /// <summary>
    /// Maakt grote donkere vormen wit in het vlakgetrokken beeld: alles wat aan elkaar vastzit en in beide richtingen
    /// veel groter is dan een letter (de ring van een ordner, een vinger, de rand van de tafel, een kader). Letters,
    /// cijfers en lijnen zijn klein of in één richting dun en blijven staan; een letter waar zo'n vorm doorheen loopt,
    /// gaat mee (die was toch niet te lezen). De rand eromheen (de overgang naar het papier) gaat ook mee.
    /// </summary>
    internal static void WisDik(byte[] vlak, int breedte, int hoogte)
    {
        const int Donker = 120;
        var lang = Math.Max(breedte, hoogte);
        var (breed, hoog) = (Math.Max(40, lang / 12), Math.Max(24, lang / 30));
        var gezien = new bool[vlak.Length];
        var stapel = new Stack<int>();
        var vorm = new List<int>();
        for (var start = 0; start < vlak.Length; start++)
        {
            if (gezien[start] || vlak[start] >= Donker)
            {
                continue;
            }

            // Alles wat aan dit punt vastzit (ook schuin), met het kader eromheen.
            vorm.Clear();
            stapel.Push(start);
            gezien[start] = true;
            var (x0, y0, x1, y1) = (breedte, hoogte, 0, 0);
            while (stapel.Count > 0)
            {
                var i = stapel.Pop();
                vorm.Add(i);
                var (x, y) = (i % breedte, i / breedte);
                (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (nx, ny) = (x + dx, y + dy);
                        if (nx < 0 || ny < 0 || nx >= breedte || ny >= hoogte)
                        {
                            continue;
                        }

                        var n = (ny * breedte) + nx;
                        if (!gezien[n] && vlak[n] < Donker)
                        {
                            gezien[n] = true;
                            stapel.Push(n);
                        }
                    }
                }
            }

            if (x1 - x0 < breed || y1 - y0 < hoog)
            {
                continue;
            }

            // De vorm wit, en de overgang naar het papier eromheen ook: wat aan de vorm grenst en nog geen papier is
            // (een schaduwrand, de vervaagde rand van de tafel), tot een blok ver.
            var laag = vorm.ToList();
            foreach (var i in laag)
            {
                vlak[i] = 255;
            }

            for (var ronde = 0; ronde < Blok && laag.Count > 0; ronde++)
            {
                var volgende = new List<int>();
                foreach (var i in laag)
                {
                    var (x, y) = (i % breedte, i / breedte);
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var (nx, ny) = (x + dx, y + dy);
                            if (nx < 0 || ny < 0 || nx >= breedte || ny >= hoogte)
                            {
                                continue;
                            }

                            var n = (ny * breedte) + nx;
                            if (vlak[n] < 215 && (ronde < 3 || !gezien[n]))
                            {
                                vlak[n] = 255;
                                gezien[n] = true;
                                volgende.Add(n);
                            }
                        }
                    }
                }

                laag = volgende;
            }
        }
    }

    /// <summary>
    /// Waar de bon ligt als hij smal tussen andere dingen ligt (in een ordner, op een vel met drukwerk): de kolommen van
    /// en tot waar gesneden wordt. De bon is de strook met de meeste tekst; hij houdt op waar een brede strook zonder
    /// tekst begint (een lange dunne lijn, zoals de rand of de schaduw van het papier, telt daarbij niet als tekst).
    /// Alleen als er dan een smalle strook overblijft; een foto die al helemaal bon of factuur is, blijft heel.
    /// </summary>
    internal static (int Van, int Tot) Uitsnede(byte[] vlak, int breedte, int hoogte)
    {
        // Per strookje kolommen: welk deel van de hoogte inkt is.
        var stap = Math.Max(1, breedte / 400);
        var aantal = breedte / stap;
        if (aantal < 60 || hoogte < 60)
        {
            return (0, breedte);
        }

        // Per strookje kolommen de stukken inkt onder elkaar. Een stuk zo hoog als een regel tekst telt als regel (ook
        // een dunne stippellijn); een stuk dat veel hoger is, is een rand: de rand of schaduw van het papier (ook als
        // die door een weggepoetste ring in stukken is geknipt), een kader, een vlak naast het papier.
        var (hoogsteRegel, laagsteRand) = (Math.Max(12, hoogte / 25), Math.Max(40, hoogte / 12));
        var regels = new int[aantal];
        var rand = new bool[aantal];
        var langste = new int[aantal];
        var leeg = new bool[aantal];
        for (var k = 0; k < aantal; k++)
        {
            // Eerst het strookje zelf (regels tellen), dan met de buren erbij (een rand die iets scheef loopt, valt
            // anders over twee strookjes in stukken uiteen).
            var donker = 0;
            for (var breed = 0; breed < 2; breed++)
            {
                var (begin, wit) = (-1, 0);
                var (links, rechts) = breed == 0 ? (k * stap, (k + 1) * stap) : (Math.Max(0, k - 1) * stap, Math.Min(aantal, k + 2) * stap);
                void Sluit(int tot)
                {
                    var lengte = tot - begin;
                    if (breed == 1 && lengte >= laagsteRand)
                    {
                        rand[k] = true;
                        langste[k] = Math.Max(langste[k], lengte);
                    }
                    else if (breed == 0 && lengte is >= 2 && lengte <= hoogsteRegel)
                    {
                        regels[k]++;
                    }

                    begin = -1;
                }

                for (var y = 0; y < hoogte; y++)
                {
                    var isInkt = false;
                    for (var x = links; x < rechts; x++)
                    {
                        if (vlak[(y * breedte) + x] < Inkt)
                        {
                            isInkt = true;
                            break;
                        }
                    }

                    if (isInkt)
                    {
                        donker += breed == 0 ? 1 : 0;
                        wit = 0;
                        if (begin < 0)
                        {
                            begin = y;
                        }
                    }
                    else if (begin >= 0 && ++wit >= 2)
                    {
                        // Twee rijen wit sluiten een stuk af (één rij wit is een oneffenheid in een lijn).
                        Sluit(y - 1);
                    }
                }

                if (begin >= 0)
                {
                    Sluit(hoogte);
                }
            }

            rand[k] |= donker > hoogte * 0.6;
            leeg[k] = regels[k] <= 1 || rand[k];
        }

        // Blokken tekst, gescheiden door een rand of door een lege strook van minstens 3% van de breedte.
        var gat = Math.Max(4, aantal * 3 / 100);
        var blokken = new List<(int Van, int Tot, int Massa)>();
        var k0 = 0;
        while (k0 < aantal)
        {
            if (leeg[k0])
            {
                k0++;
                continue;
            }

            var (eind, leegte, massa) = (k0, 0, 0);
            for (var k = k0; k < aantal && leegte < gat && !rand[k]; k++)
            {
                if (leeg[k])
                {
                    leegte++;
                }
                else
                {
                    (eind, leegte) = (k, 0);
                    massa += regels[k];
                }
            }

            blokken.Add((k0, eind + 1, massa));
            k0 = eind + 1;
        }

        if (blokken.Count < 2)
        {
            return (0, breedte);
        }

        // De bon is het blok met de meeste regels. Een blok ernaast hoort erbij als er geen rand tussen ligt en zijn
        // regels op dezelfde hoogte staan als die van de bon (de kolom met bedragen); tekst van een ander papier staat
        // op eigen regels.
        // Bij gelijke hoeveelheid tekst wint het blok het dichtst bij het midden van de foto.
        double Gewicht(int i)
        {
            var midden = (blokken[i].Van + blokken[i].Tot) / 2.0;
            return blokken[i].Massa * (1 - (0.8 * Math.Abs(midden - (aantal / 2.0)) / (aantal / 2.0)));
        }

        var hoofd = Enumerable.Range(0, blokken.Count).MaxBy(Gewicht);
        var (eerste, laatste) = (hoofd, hoofd);

        // Een scheiding is een rand zo lang als de tekst van de bon hoog is: de rand van een ander vel, de rug van de
        // ordner. Een kortere rand is een vouw in de bon of een stuk van een ring.
        var tekstVanBon = Regels(vlak, breedte, hoogte, blokken[hoofd].Van * stap, blokken[hoofd].Tot * stap);
        var tekstHoogte = tekstVanBon.Count == 0 ? hoogte : tekstVanBon[^1].Tot - tekstVanBon[0].Van;
        bool Scheiding(int k) => langste[k] >= Math.Max(laagsteRand, tekstHoogte * 0.8);
        bool Hoortbij(int kandidaat, int buur)
        {
            var (tussenVan, tussenTot) = kandidaat < buur ? (blokken[kandidaat].Tot, blokken[buur].Van) : (blokken[buur].Tot, blokken[kandidaat].Van);
            var tussen = Enumerable.Range(tussenVan, tussenTot - tussenVan);
            if (tussen.Any(Scheiding))
            {
                return false;
            }

            var randTussen = tussen.Any(k => rand[k]);
            var smal = tussenTot - tussenVan < gat;
            if (smal && (blokken[kandidaat].Tot - blokken[kandidaat].Van < gat || tussen.Select(k => langste[k]).DefaultIfEmpty().Max() < tekstHoogte * 0.13))
            {
                // Een snipper tussen twee vouwen, of alleen een korte vouw ertussen (de rand van een ander vel is langer):
                // hetzelfde papier.
                return true;
            }

            // Ingesloten tussen de bon en een rand aan de andere kant (de rand van de bon zelf): dan hoort het erbij.
            var (buitenVan, buitenTot) = kandidaat < buur ? (kandidaat > 0 ? blokken[kandidaat - 1].Tot : 0, blokken[kandidaat].Van) : (blokken[kandidaat].Tot, kandidaat + 1 < blokken.Count ? blokken[kandidaat + 1].Van : aantal);
            if (!randTussen && blokken[kandidaat].Massa < blokken[hoofd].Massa && Enumerable.Range(buitenVan, buitenTot - buitenVan).Any(k => rand[k]))
            {
                return true;
            }

            var eigen = Regels(vlak, breedte, hoogte, blokken[kandidaat].Van * stap, blokken[kandidaat].Tot * stap);
            var vanBon = Regels(vlak, breedte, hoogte, blokken[eerste].Van * stap, blokken[laatste].Tot * stap);
            if (eigen.Count == 0)
            {
                return false;
            }

            var gelijk = eigen.Count(e =>
            {
                // Zelfde regel: het midden op (bijna) dezelfde hoogte en ongeveer even hoog.
                var (midden, hoog) = ((e.Van + e.Tot) / 2.0, e.Tot - e.Van);
                return vanBon.Any(b => Math.Abs(midden - ((b.Van + b.Tot) / 2.0)) <= Math.Max(2, (b.Tot - b.Van) * (randTussen ? 0.5 : 0.2)) && hoog >= (b.Tot - b.Van) * 0.5 && hoog <= (b.Tot - b.Van) * 1.6);
            });
            // Alleen door een korte rand gescheiden, zonder witte strook (een vouw, een stuk ring): hetzelfde papier, ook als
            // de regels door het kreukelen niet precies doorlopen.
            return gelijk >= eigen.Count * (smal ? 0.2 : 0.7);
        }

        while (laatste + 1 < blokken.Count && Hoortbij(laatste + 1, laatste))
        {
            laatste++;
        }

        while (eerste > 0 && Hoortbij(eerste - 1, eerste))
        {
            eerste--;
        }

        // Een marge erbij; alleen snijden als er een smalle strook overblijft (een bon), niet bij een volle pagina.
        var marge = Math.Max(2, aantal * 25 / 1000);
        var (van, tot) = (Math.Max(0, blokken[eerste].Van - marge) * stap, Math.Min(aantal, blokken[laatste].Tot + marge) * stap);
        if (blokken[laatste].Tot + marge >= aantal)
        {
            tot = breedte;
        }

        var over = (tot - van) / (double)breedte;
        return over is >= 0.2 and <= 0.68 ? (van, tot) : (0, breedte);
    }

    /// <summary>De regels tekst (van en tot welke rij) in een strook kolommen: rijen met inkt, minstens twee hoog.</summary>
    private static List<(int Van, int Tot)> Regels(byte[] vlak, int breedte, int hoogte, int van, int tot)
    {
        var regels = new List<(int Van, int Tot)>();
        var begin = -1;
        for (var y = 0; y <= hoogte; y++)
        {
            var inkt = false;
            if (y < hoogte)
            {
                for (var x = van; x < Math.Min(tot, breedte); x++)
                {
                    if (vlak[(y * breedte) + x] < Inkt)
                    {
                        inkt = true;
                        break;
                    }
                }
            }

            if (inkt && begin < 0)
            {
                begin = y;
            }
            else if (!inkt && begin >= 0)
            {
                // Hoger dan een tiende van het beeld is geen regel tekst (een lijn, een vlek).
                if (y - begin >= 2 && y - begin < hoogte / 10)
                {
                    regels.Add((begin, y));
                }

                begin = -1;
            }
        }

        return regels;
    }

    /// <summary>
    /// Maakt grote donkere vlakken wit: de tafel naast het papier, een vinger, een harde schaduw. Een vlak is waar een
    /// heel vierkant van een flinke maat donker is; letters en lijnen zijn dunner en blijven dus staan, ook als ze het
    /// vlak raken. De rand van het vlak (de overgang naar het papier) gaat mee.
    /// </summary>
    internal static void WisVlakken(byte[] grijs, int breedte, int hoogte)
    {
        var maat = Math.Max(20, Math.Max(breedte, hoogte) / 70);
        var rand = maat / 2;

        // Per pixel het grootste donkere vierkant dat daar rechtsonder eindigt; vanaf de maat is het de kern van een vlak.
        var vorige = new int[breedte + 1];
        var deze = new int[breedte + 1];
        var breed = new bool[grijs.Length];
        var gevonden = false;
        for (var y = 0; y < hoogte; y++)
        {
            for (var x = 0; x < breedte; x++)
            {
                deze[x + 1] = grijs[(y * breedte) + x] < 128 ? 1 + Math.Min(deze[x], Math.Min(vorige[x], vorige[x + 1])) : 0;
                if (deze[x + 1] >= maat)
                {
                    // Het hele vierkant (naar links) plus de rand, eerst alleen in deze rij.
                    var van = Math.Max(0, x - maat + 1 - rand);
                    Array.Fill(breed, true, (y * breedte) + van, Math.Min(breedte - 1, x + rand) - van + 1);
                    gevonden = true;
                }
            }

            (vorige, deze) = (deze, vorige);
        }

        if (!gevonden)
        {
            return;
        }

        // Dan per kolom: het vierkant naar boven plus de rand, en de rand naar onder.
        for (var x = 0; x < breedte; x++)
        {
            var onder = 0;
            for (var y = 0; y < hoogte; y++)
            {
                if (breed[(y * breedte) + x])
                {
                    for (var v = Math.Max(0, y - maat + 1 - rand); v < y; v++)
                    {
                        grijs[(v * breedte) + x] = 255;
                    }

                    onder = rand + 1;
                }

                if (onder > 0)
                {
                    grijs[(y * breedte) + x] = 255;
                    onder--;
                }
            }
        }
    }

    /// <summary>
    /// De hoek (in graden, met de klok mee positief) waaronder de tekstregels lopen: de hoek waarbij de inkt per rij het
    /// scherpst in regels en tussenruimte uiteenvalt (de grootste spreiding van de rijsommen), gezocht op een verkleind
    /// beeld tussen -10 en +10 graden.
    /// </summary>
    internal static double Scheefstand(byte[] grijs, int breedte, int hoogte)
    {
        var stap = Math.Max(1, Math.Max(breedte, hoogte) / 700);
        var punten = new List<(int X, int Y)>();
        for (var y = 0; y < hoogte; y += stap)
        {
            for (var x = 0; x < breedte; x += stap)
            {
                // Alleen bovenranden van inkt (donker met licht er vlak boven): grote donkere vlakken (de tafel naast
                // het papier) tellen dan alleen met hun rand mee.
                if (grijs[(y * breedte) + x] < 128 && y >= 2 * stap && grijs[((y - (2 * stap)) * breedte) + x] >= 128)
                {
                    punten.Add((x / stap, y / stap));
                }
            }
        }

        var (b, h) = (breedte / stap, hoogte / stap);
        if (punten.Count < 200 || punten.Count > b * h / 2)
        {
            return 0;
        }

        double Score(double graden)
        {
            var (sin, cos) = Math.SinCos(graden * Math.PI / 180);
            var rijen = new int[h + (2 * b) + 4];
            foreach (var (x, y) in punten)
            {
                rijen[(int)((y * cos) - (x * sin)) + b]++;
            }

            // Som van kwadraten: het hoogst als de inkt in weinig rijen samenvalt.
            return rijen.Sum(r => (double)r * r);
        }

        var (beste, hoogste) = (0.0, Score(0));
        for (var graden = -10.0; graden <= 10.0; graden += 0.5)
        {
            var score = Score(graden);
            if (score > hoogste)
            {
                (beste, hoogste) = (graden, score);
            }
        }

        // Alleen rechtzetten als het duidelijk beter is dan niets doen.
        return hoogste > Score(0) * 1.1 ? beste : 0;
    }

    /// <summary>Een vlak in grijstinten gedraaid om zijn midden, op wit, in dezelfde maat.</summary>
    private static byte[] Draai(byte[] grijs, int breedte, int hoogte, double graden)
    {
        using var bron = new SKBitmap(new SKImageInfo(breedte, hoogte, SKColorType.Gray8, SKAlphaType.Opaque));
        System.Runtime.InteropServices.Marshal.Copy(grijs, 0, bron.GetPixels(), grijs.Length);
        using var gedraaid = Gedraaid(bron, graden);
        var pixels = gedraaid.GetPixelSpan();
        var uit = new byte[grijs.Length];
        for (var i = 0; i < uit.Length; i++)
        {
            uit[i] = pixels[i * 4];
        }

        return uit;
    }

    /// <summary>Het beeld gedraaid om zijn midden, op een wit vlak van dezelfde maat.</summary>
    private static SKBitmap Gedraaid(SKBitmap bron, double graden)
    {
        var doel = new SKBitmap(new SKImageInfo(bron.Width, bron.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(doel);
        canvas.Clear(SKColors.White);
        canvas.RotateDegrees((float)graden, bron.Width / 2f, bron.Height / 2f);
        using var beeld = SKImage.FromBitmap(bron);
        canvas.DrawImage(beeld, new SKRect(0, 0, bron.Width, bron.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return doel;
    }

    /// <summary>
    /// De helderheid waaronder iets inkt is: gewoonlijk <see cref="Zwart"/>, maar hoger als de inkt op deze foto lichter is
    /// (de mediaan van alles wat duidelijk geen papier is), tot een grens waarbij papier nog wit blijft.
    /// </summary>
    private static int Inktgrens(byte[] vlak)
    {
        var telling = new int[256];
        foreach (var waarde in vlak)
        {
            telling[waarde]++;
        }

        // Alles onder 195 is geen papier (letters, lijnen, ook dunne of lichte); te weinig daarvan: niets te zeggen.
        const int Papier = 195;
        var donker = 0;
        for (var w = 0; w < Papier; w++)
        {
            donker += telling[w];
        }

        if (donker < vlak.Length / 500)
        {
            return Zwart;
        }

        // De mediaan van de inkt: bij dunne of lichte letters ligt die hoog, en dan moet de grens mee omhoog, anders
        // blijven juist die letters flets (vette koppen zijn donkerder maar beslaan weinig).
        var (half, mediaan) = (donker / 2, 0);
        while (mediaan < Papier - 1 && (half -= telling[mediaan]) > 0)
        {
            mediaan++;
        }

        return Math.Clamp(mediaan + 15, Zwart, 180);
    }

    /// <summary>De foto rechtop (zoals de camera hem bedoelde) en hoogstens <see cref="MaximumZijde"/> groot.</summary>
    private static SKBitmap? Rechtop(byte[] foto)
    {
        using var gegevens = SKData.CreateCopy(foto);
        using var codec = SKCodec.Create(gegevens);
        if (codec is null)
        {
            return null;
        }

        using var bron = SKBitmap.Decode(codec);
        if (bron is null || bron.Width < 8 || bron.Height < 8)
        {
            return null;
        }

        var oorsprong = codec.EncodedOrigin;
        var kwartslag = oorsprong is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom;
        var schaal = Math.Min(1f, MaximumZijde / (float)Math.Max(bron.Width, bron.Height));
        var (b, h) = ((int)Math.Round(bron.Width * schaal), (int)Math.Round(bron.Height * schaal));
        var doel = new SKBitmap(new SKImageInfo(kwartslag ? h : b, kwartslag ? b : h, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(doel);
        canvas.Clear(SKColors.White);
        switch (oorsprong)
        {
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, b / 2f, h / 2f);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(h, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, b);
                canvas.RotateDegrees(270);
                break;
        }

        using var beeld = SKImage.FromBitmap(bron);
        canvas.DrawImage(beeld, new SKRect(0, 0, b, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return doel;
    }

    private static byte[] Grijs(SKBitmap beeld)
    {
        var pixels = beeld.GetPixelSpan();
        var grijs = new byte[beeld.Width * beeld.Height];
        for (var i = 0; i < grijs.Length; i++)
        {
            var p = i * 4;
            grijs[i] = (byte)(((pixels[p] * 299) + (pixels[p + 1] * 587) + (pixels[p + 2] * 114)) / 1000);
        }

        return grijs;
    }

    /// <summary>
    /// Per blok de helderheid van het papier: een hoge percentielwaarde (tekst beslaat maar een klein deel van een blok),
    /// zodat schaduw en verloop per plek worden gevolgd.
    /// </summary>
    private static int[,] Papier(byte[] grijs, int breedte, int hoogte)
    {
        var (kolommen, rijen) = (((breedte - 1) / Blok) + 1, ((hoogte - 1) / Blok) + 1);
        var papier = new int[rijen, kolommen];
        var telling = new int[256];
        for (var r = 0; r < rijen; r++)
        {
            for (var k = 0; k < kolommen; k++)
            {
                Array.Clear(telling);
                var aantal = 0;
                for (var y = r * Blok; y < Math.Min(hoogte, (r + 1) * Blok); y++)
                {
                    for (var x = k * Blok; x < Math.Min(breedte, (k + 1) * Blok); x++)
                    {
                        telling[grijs[(y * breedte) + x]]++;
                        aantal++;
                    }
                }

                // Het 80e percentiel van boven af tellen.
                var over = aantal / 5;
                var waarde = 255;
                while (waarde > 0 && (over -= telling[waarde]) >= 0)
                {
                    waarde--;
                }

                papier[r, k] = waarde;
            }
        }

        return papier;
    }

    /// <summary>De achtergrond op een punt: tussen de middens van de blokken geïnterpoleerd, zodat er geen randen ontstaan.</summary>
    private static int Achtergrond(int[,] papier, int x, int y)
    {
        var (rijen, kolommen) = (papier.GetLength(0), papier.GetLength(1));
        var fx = Math.Clamp(((x + 0.5) / Blok) - 0.5, 0, kolommen - 1);
        var fy = Math.Clamp(((y + 0.5) / Blok) - 0.5, 0, rijen - 1);
        var (k0, r0) = ((int)fx, (int)fy);
        var (k1, r1) = (Math.Min(k0 + 1, kolommen - 1), Math.Min(r0 + 1, rijen - 1));
        var (dx, dy) = (fx - k0, fy - r0);
        var boven = (papier[r0, k0] * (1 - dx)) + (papier[r0, k1] * dx);
        var onder = (papier[r1, k0] * (1 - dx)) + (papier[r1, k1] * dx);
        return (int)((boven * (1 - dy)) + (onder * dy));
    }
}
