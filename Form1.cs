using System.Text;

namespace MhxrPatcher;

public class Form1 : Form
{
    private readonly TextBox _percorsoApk = new();
    private readonly TextBox _indirizzo = new();
    private readonly CheckBox _cambiaIndirizzo = new();
    private readonly CheckBox _cambiaLingua = new();
    private readonly Button _crea = new();
    private readonly TextBox _log = new();

    // Lo slot piu' piccolo fra le due architetture: e' quello che comanda.
    private const int MaxCaratteri = 47;

    // L'indirizzo vero, usato davvero per patchare: non e' piu' l'utente a
    // scriverlo, lo decide il programma. Il campo in UI mostra solo una
    // versione mascherata (ultimi 4 caratteri, il resto pallini) cosi' non
    // finisce leggibile in uno screenshot condiviso in giro.
    //
    // NON e' tenuto come stringa letterale nel sorgente: e' codificato in
    // Base64 e ricostruito qui sotto. Questo NON e' vera sicurezza — chiunque
    // apra l'exe in un decompilatore vero (dnSpy, ILSpy) vede comunque il
    // valore decodificato appena il programma lo usa, ed e' un fatto della
    // piattaforma .NET, non qualcosa che si possa evitare del tutto senza un
    // offuscatore serio (es. ConfuserEx) sull'intero eseguibile. Questo passo
    // alza solo l'asticella per chi aprisse l'exe con un editor di testo o
    // "strings.exe" e cercasse l'indirizzo cosi' com'e', a colpo d'occhio.
    private static readonly string _indirizzoReale =
        Encoding.UTF8.GetString(Convert.FromBase64String("aHR0cDovL21oeHIuZHVja2Rucy5vcmcv"));

    public Form1()
    {
        // L'icona del file .exe (ApplicationIcon nel .csproj) non diventa da
        // sola quella della finestra: va assegnata qui, altrimenti la
        // titlebar mostra l'icona generica di WinForms anche con l'exe giusto.
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* nessuna icona, non e' bloccante */ }

        // Sfondo: un'immagine incorporata (stesso motivo di GUI_msg_en.arc/
        // apksigner.jar, un file solo da distribuire) gia' preparata a bassa
        // opacita' — qui non si tocca la trasparenza, e' gia' nel PNG.
        try
        {
            using var s = typeof(Form1).Assembly.GetManifestResourceStream("MhxrPatcher.sfondo.png");
            if (s != null) BackgroundImage = Image.FromStream(s);
        }
        catch { /* niente sfondo, non e' bloccante */ }
        BackgroundImageLayout = ImageLayout.Zoom;

        Text = "MHXR Patcher";
        Width = 720;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        var y = 18;

        // --- 1. APK ---------------------------------------------------------
        Controls.Add(Etichetta("1.  Choose the game's APK", 18, y, true));
        y += 26;
        _percorsoApk.SetBounds(18, y, 540, 24);
        _percorsoApk.ReadOnly = true;
        _percorsoApk.PlaceholderText = "no file selected";
        Controls.Add(_percorsoApk);

        var sfoglia = new Button { Text = "Browse...", Left = 568, Top = y - 1, Width = 110, Height = 26 };
        sfoglia.Click += (_, _) => ScegliApk();
        Controls.Add(sfoglia);
        y += 22;

        Controls.Add(Etichetta(
            "Japanese version only. The Taiwan build has an anti-tampering check and will not run after patching.",
            18, y, false, Color.Firebrick));
        y += 32;

        // --- 2. indirizzo ---------------------------------------------------
        _cambiaIndirizzo.SetBounds(18, y, 320, 22);
        _cambiaIndirizzo.Text = "2.  Change the server address";
        _cambiaIndirizzo.Font = new Font(Font, FontStyle.Bold);
        _cambiaIndirizzo.Checked = true;
        _cambiaIndirizzo.CheckedChanged += (_, _) => AggiornaStato();
        Controls.Add(_cambiaIndirizzo);
        y += 26;

        _indirizzo.SetBounds(38, y, 400, 24);
        _indirizzo.ReadOnly = true;
        _indirizzo.Text = MaschermaIndirizzo(_indirizzoReale);
        Controls.Add(_indirizzo);
        y += 34;

        // --- 3. lingua ------------------------------------------------------
        _cambiaLingua.SetBounds(18, y, 420, 22);
        _cambiaLingua.Text = "3.  Switch the game to English";
        _cambiaLingua.Font = new Font(Font, FontStyle.Bold);
        _cambiaLingua.CheckedChanged += (_, _) => AggiornaStato();
        Controls.Add(_cambiaLingua);
        y += 26;

        Controls.Add(Etichetta(
            "Partial translation: monsters, menus, common messages and skills. Some text stays in Japanese.",
            38, y, false, SystemColors.GrayText));
        y += 40;

        // --- 4. crea --------------------------------------------------------
        _crea.SetBounds(18, y, 250, 36);
        _crea.Text = "4.  Create and save the APK";
        _crea.Font = new Font(Font, FontStyle.Bold);
        _crea.Click += (_, _) => Crea();
        Controls.Add(_crea);
        y += 48;

        _log.SetBounds(18, y, 660, 180);
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font("Consolas", 8.5F);
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        Controls.Add(_log);

        AggiornaStato();
        ControllaJava();
    }

    private static Label Etichetta(string testo, int x, int y, bool grassetto, Color? colore = null)
    {
        var l = new Label { Text = testo, AutoSize = true, Left = x, Top = y };
        if (grassetto) l.Font = new Font(l.Font, FontStyle.Bold);
        if (colore.HasValue) l.ForeColor = colore.Value;
        return l;
    }

    private void Scrivi(string riga) => _log.AppendText(riga + Environment.NewLine);

    /// <summary>Ultimi 4 caratteri veri, il resto sostituito da pallini — stessa lunghezza dell'originale.</summary>
    private static string MaschermaIndirizzo(string reale)
    {
        const int visibili = 4;
        if (reale.Length <= visibili) return reale;
        return new string('•', reale.Length - visibili) + reale[^visibili..];
    }

    private void ControllaJava()
    {
        if (Firma.JavaDisponibile)
        {
            Scrivi("Java found: the APK will be signed automatically.");
        }
        else
        {
            Scrivi("WARNING: Java is not installed.");
            Scrivi("Without Java the APK is created but NOT signed, and Android will refuse to install it.");
            Scrivi("Install Java (adoptium.net) and reopen this program.");
        }
    }

    private void AggiornaStato()
    {
        _indirizzo.Enabled = _cambiaIndirizzo.Checked;

        var apkOk = File.Exists(_percorsoApk.Text);
        var indirizzoOk = !_cambiaIndirizzo.Checked
                          || (_indirizzoReale.Trim().Length is > 0 and <= MaxCaratteri);
        var qualcosaDaFare = _cambiaIndirizzo.Checked || _cambiaLingua.Checked;

        _crea.Enabled = apkOk && indirizzoOk && qualcosaDaFare;
    }

    private void ScegliApk()
    {
        using var f = new OpenFileDialog
        {
            Title = "Choose the Monster Hunter Explore APK",
            Filter = "APK files (*.apk)|*.apk|All files (*.*)|*.*",
        };
        if (f.ShowDialog(this) != DialogResult.OK) return;

        _percorsoApk.Text = f.FileName;
        Scrivi($"APK selected: {Path.GetFileName(f.FileName)} " +
               $"({new FileInfo(f.FileName).Length / 1024 / 1024} MB)");
        AggiornaStato();
    }

    private byte[]? CaricaTestiInglese()
    {
        // I testi tradotti viaggiano dentro l'eseguibile: l'utente scarica un
        // file solo e non deve tenere insieme cartelle.
        using var s = typeof(Form1).Assembly.GetManifestResourceStream("MhxrPatcher.GUI_msg_en.arc");
        if (s == null) return null;
        using var mem = new MemoryStream();
        s.CopyTo(mem);
        return mem.ToArray();
    }

    private void Crea()
    {
        byte[]? testi = null;
        if (_cambiaLingua.Checked)
        {
            testi = CaricaTestiInglese();
            if (testi == null)
            {
                MessageBox.Show(this, "The English texts are not included in this build of the program.",
                    "Translation not available", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        using var salva = new SaveFileDialog
        {
            Title = "Where should I save the patched APK?",
            Filter = "APK files (*.apk)|*.apk",
            FileName = Path.GetFileNameWithoutExtension(_percorsoApk.Text) + "-patched.apk",
        };
        if (salva.ShowDialog(this) != DialogResult.OK) return;

        _crea.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            Scrivi("");
            Scrivi("--- start ---");

            var url = _cambiaIndirizzo.Checked ? _indirizzoReale.Trim() : null;
            var esito = Patcher.Costruisci(_percorsoApk.Text, salva.FileName, url, testi);
            foreach (var r in esito.Righe) Scrivi("  " + r);

            if (!esito.Riuscito)
            {
                MessageBox.Show(this, "Patching failed. See the details at the bottom of the window.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (Firma.JavaDisponibile)
            {
                var (ok, messaggio) = Firma.FirmaApk(salva.FileName);
                Scrivi("  " + messaggio);
                if (!ok)
                {
                    MessageBox.Show(this, "The APK was created but signing failed, so it won't install. "
                        + "Details at the bottom of the window.",
                        "Signing failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            else
            {
                Scrivi("  APK NOT signed: Java is not installed, the phone will refuse it.");
            }

            Scrivi("--- done ---");
            MessageBox.Show(this,
                "APK ready.\n\nCopy it to the phone and install it.\n\n"
                + "If the game was already installed with a different signature, Android will ask you to "
                + "uninstall it first: set up the transfer code BEFORE doing that, "
                + "or you'll lose access to your character.",
                "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            Cursor = Cursors.Default;
            _crea.Enabled = true;
            AggiornaStato();
        }
    }
}
