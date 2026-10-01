package mhxr.patcher.app;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.text.method.ScrollingMovementMethod;
import android.view.Gravity;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.FrameLayout;
import android.widget.TextView;

import androidx.core.content.FileProvider;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

import mhxr.patcher.R;
import mhxr.patcher.core.Indirizzo;
import mhxr.patcher.core.Patcher;

/**
 * Patcher sul telefono: si sceglie l'APK giapponese originale (scaricato col telefono),
 * si crea quello patchato e si installa. Niente PC, niente Java: firma e manifest li fa
 * l'app (mhxr.patcher.core, lo stesso codice provato sul PC).
 */
public class MainActivity extends Activity {
    private static final int SCEGLI_APK = 1;

    private TextView log;
    private Button crea, installa;
    private File apkIngresso, apkUscita;
    private final Handler ui = new Handler(Looper.getMainLooper());

    @Override
    protected void onCreate(Bundle stato) {
        super.onCreate(stato);
        FrameLayout radice = new FrameLayout(this);
        radice.setBackgroundColor(Color.WHITE);
        ImageView sfondo = new ImageView(this);
        sfondo.setImageResource(R.drawable.sfondo);
        sfondo.setScaleType(ImageView.ScaleType.FIT_CENTER);
        radice.addView(sfondo, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        int p = dp(18);
        col.setPadding(p, p, p, p);
        radice.addView(col, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        TextView titolo = testo("MHXR Patcher", 22, true, Color.BLACK);
        col.addView(titolo);
        col.addView(testo("Japanese version only (09.03.06). The Taiwan build closes immediately after patching.", 13, false, Color.rgb(178, 34, 34)));
        col.addView(testo("The game will connect to the private server (" + Indirizzo.mascherato(Indirizzo.SERVER)
                + "). The English translation is downloaded from the server.", 13, false, Color.DKGRAY));

        Button scegli = bottone("1.  Choose the Japanese APK");
        scegli.setOnClickListener(v -> {
            Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            i.addCategory(Intent.CATEGORY_OPENABLE);
            i.setType("*/*");
            i.putExtra(Intent.EXTRA_MIME_TYPES, new String[]{"application/vnd.android.package-archive", "application/octet-stream"});
            startActivityForResult(i, SCEGLI_APK);
        });
        col.addView(scegli);

        crea = bottone("2.  Create the patched APK");
        crea.setEnabled(false);
        crea.setOnClickListener(v -> creaApk());
        col.addView(crea);

        installa = bottone("3.  Install");
        installa.setEnabled(false);
        installa.setOnClickListener(v -> installaApk());
        col.addView(installa);

        col.addView(testo("Before installing: set up the transfer code in the game you have now, then uninstall it "
                + "(Android won't install over it, the signature is different).", 12, false, Color.DKGRAY));

        log = testo("", 11, false, Color.BLACK);
        log.setTypeface(Typeface.MONOSPACE);
        log.setMovementMethod(new ScrollingMovementMethod());
        log.setBackgroundColor(Color.argb(170, 255, 255, 255));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        lp.topMargin = dp(8);
        col.addView(log, lp);

        setContentView(radice);
        scrivi("Download the original Japanese APK first (see the instructions on GitHub).");
    }

    private int dp(int v) { return Math.round(v * getResources().getDisplayMetrics().density); }

    private TextView testo(String t, int sp, boolean grassetto, int colore) {
        TextView x = new TextView(this);
        x.setText(t);
        x.setTextSize(sp);
        x.setTextColor(colore);
        if (grassetto) x.setTypeface(Typeface.DEFAULT_BOLD);
        x.setPadding(0, dp(4), 0, dp(4));
        return x;
    }

    private Button bottone(String t) {
        Button b = new Button(this);
        b.setText(t);
        b.setAllCaps(false);
        b.setGravity(Gravity.START | Gravity.CENTER_VERTICAL);
        return b;
    }

    private void scrivi(String riga) { ui.post(() -> log.append(riga + "\n")); }

    @Override
    protected void onActivityResult(int req, int res, Intent data) {
        super.onActivityResult(req, res, data);
        if (req != SCEGLI_APK || res != RESULT_OK || data == null || data.getData() == null) return;
        Uri uri = data.getData();
        crea.setEnabled(false);
        installa.setEnabled(false);
        scrivi("copying the APK...");
        new Thread(() -> {
            try {
                File f = new File(getCacheDir(), "originale.apk");
                try (InputStream in = getContentResolver().openInputStream(uri); OutputStream out = new FileOutputStream(f)) {
                    byte[] buf = new byte[1 << 16];
                    int r;
                    while ((r = in.read(buf)) > 0) out.write(buf, 0, r);
                }
                apkIngresso = f;
                scrivi("APK selected (" + (f.length() / 1024 / 1024) + " MB)");
                ui.post(() -> crea.setEnabled(true));
            } catch (Exception e) {
                scrivi("ERROR reading the file: " + e.getMessage());
            }
        }).start();
    }

    private void creaApk() {
        crea.setEnabled(false);
        installa.setEnabled(false);
        scrivi("--- start (it takes a minute) ---");
        new Thread(() -> {
            try {
                File cartella = new File(getExternalFilesDir(null), "output");
                cartella.mkdirs();
                apkUscita = new File(cartella, "MHXR-patched.apk");
                Patcher.patcha(apkIngresso, apkUscita, new File(getFilesDir(), "chiave"), this::scrivi);
                apkIngresso.delete();
                scrivi("--- done ---");
                ui.post(() -> installa.setEnabled(true));
            } catch (Throwable e) {
                scrivi("ERROR: " + e.getMessage());
                ui.post(() -> crea.setEnabled(apkIngresso != null && apkIngresso.isFile()));
            }
        }).start();
    }

    private void installaApk() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && !getPackageManager().canRequestPackageInstalls()) {
            scrivi("Allow this app to install apps, then press Install again.");
            startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + getPackageName())));
            return;
        }
        Uri uri = FileProvider.getUriForFile(this, getPackageName() + ".file", apkUscita);
        Intent i = new Intent(Intent.ACTION_VIEW);
        i.setDataAndType(uri, "application/vnd.android.package-archive");
        i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        startActivity(i);
    }
}
