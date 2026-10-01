package mhxr.patcher.core;

import com.android.apksig.ApkSigner;

import java.io.BufferedOutputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.util.Collections;
import java.util.Enumeration;
import java.util.function.Consumer;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

/**
 * Tutto il lavoro, senza interfaccia: si usa uguale dall'app Android e da riga di
 * comando sul PC (per provarlo). Passi:
 *   1. copia l'APK togliendo la vecchia firma (META-INF/*.RSA, .SF, MANIFEST.MF...);
 *   2. scrive l'indirizzo del server in ogni libMHS.so (arm64-v8a e armeabi-v7a);
 *   3. sistema il manifest (PatchManifest);
 *   4. firma con apksig (schemi v1+v2+v3) e la chiave personale (Chiave).
 */
public final class Patcher {
    private Patcher() {}

    public static void patcha(File apkIngresso, File apkUscita, File cartellaChiave, Consumer<String> log) throws Exception {
        File nonFirmato = new File(apkUscita.getPath() + ".unsigned");
        int librerie = 0;
        try (ZipFile zip = new ZipFile(apkIngresso);
             ScrittoreZip w = new ScrittoreZip(new BufferedOutputStream(new FileOutputStream(nonFirmato), 1 << 16))) {
            for (ZipEntry e : Collections.list((Enumeration<ZipEntry>) zip.entries())) {
                String n = e.getName();
                if (e.isDirectory()) continue;
                String up = n.toUpperCase();
                if (up.startsWith("META-INF/") && (up.endsWith(".RSA") || up.endsWith(".SF") || up.endsWith(".DSA")
                        || up.endsWith(".EC") || up.endsWith("MANIFEST.MF"))) continue;
                byte[] dati = leggi(zip, e);
                if (n.endsWith("libMHS.so")) {
                    int spazio = PatchLibreria.scrivi(dati, Indirizzo.SERVER);
                    if (spazio < 0) log.accept("WARNING: no server address found in " + n + ", left unchanged");
                    else { log.accept("address patched in " + n + " (room for " + spazio + ")"); librerie++; }
                }
                if (n.equals("AndroidManifest.xml")) {
                    PatchManifest.Esito m = PatchManifest.sistema(dati);
                    log.accept("manifest: " + m.messaggio);
                }
                boolean so = n.endsWith(".so");
                boolean memorizza = so || n.equals("resources.arsc") || e.getMethod() == ZipEntry.STORED;
                w.aggiungi(n, dati, memorizza, so ? 4096 : 4);
            }
        }
        if (librerie == 0) {
            nonFirmato.delete();
            throw new IllegalStateException("libMHS.so not found or not patchable: is this the Japanese APK?");
        }
        log.accept("zip rebuilt: native libraries stored and aligned");
        Chiave k = Chiave.caricaOCrea(cartellaChiave);
        ApkSigner.SignerConfig sc = new ApkSigner.SignerConfig.Builder("MHXR", k.privata, Collections.singletonList(k.certificato)).build();
        new ApkSigner.Builder(Collections.singletonList(sc))
                .setInputApk(nonFirmato).setOutputApk(apkUscita)
                .setV1SigningEnabled(true).setV2SigningEnabled(true).setV3SigningEnabled(true)
                .build().sign();
        nonFirmato.delete();
        log.accept("APK signed (v1+v2+v3)");
    }

    private static byte[] leggi(ZipFile zip, ZipEntry e) throws Exception {
        try (InputStream in = zip.getInputStream(e)) {
            ByteArrayOutputStream o = new ByteArrayOutputStream(e.getSize() > 0 ? (int) e.getSize() : 8192);
            byte[] buf = new byte[1 << 16];
            int r;
            while ((r = in.read(buf)) > 0) o.write(buf, 0, r);
            return o.toByteArray();
        }
    }

    /** Uso da riga di comando sul PC, per provarlo: java mhxr.patcher.core.Patcher <in.apk> <out.apk> <cartella-chiave> */
    public static void main(String[] a) throws Exception {
        patcha(new File(a[0]), new File(a[1]), new File(a[2]), System.out::println);
    }
}
