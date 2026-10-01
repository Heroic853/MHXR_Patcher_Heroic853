package mhxr.patcher.core;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.IOException;
import java.math.BigInteger;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.security.KeyFactory;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.SecureRandom;
import java.security.Signature;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.security.spec.PKCS8EncodedKeySpec;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.TimeZone;

/**
 * Chiave di firma personale, creata la prima volta e poi riusata (salvata nella cartella
 * privata dell'app): stessa firma a ogni patch, cosi' un nuovo APK patchato si installa
 * sopra quello vecchio senza disinstallare. RSA 2048 con certificato autofirmato, costruito
 * a mano in DER: niente keytool ne' BouncyCastle, funziona uguale su Android e sul PC.
 */
public final class Chiave {
    public final PrivateKey privata;
    public final X509Certificate certificato;

    private Chiave(PrivateKey p, X509Certificate c) { privata = p; certificato = c; }

    public static Chiave caricaOCrea(File cartella) throws Exception {
        File fk = new File(cartella, "mhxr-key.pk8"), fc = new File(cartella, "mhxr-cert.der");
        if (fk.isFile() && fc.isFile()) {
            PrivateKey p = KeyFactory.getInstance("RSA").generatePrivate(new PKCS8EncodedKeySpec(Files.readAllBytes(fk.toPath())));
            X509Certificate c = (X509Certificate) CertificateFactory.getInstance("X.509")
                    .generateCertificate(new ByteArrayInputStream(Files.readAllBytes(fc.toPath())));
            return new Chiave(p, c);
        }
        KeyPairGenerator g = KeyPairGenerator.getInstance("RSA");
        g.initialize(2048, new SecureRandom());
        KeyPair kp = g.generateKeyPair();
        byte[] der = certificatoAutofirmato(kp);
        X509Certificate c = (X509Certificate) CertificateFactory.getInstance("X.509").generateCertificate(new ByteArrayInputStream(der));
        cartella.mkdirs();
        Files.write(fk.toPath(), kp.getPrivate().getEncoded());
        Files.write(fc.toPath(), der);
        return new Chiave(kp.getPrivate(), c);
    }

    // --- DER minimo per un certificato X.509 v3 ---------------------------------------
    private static byte[] tlv(int tag, byte[]... parti) throws IOException {
        ByteArrayOutputStream c = new ByteArrayOutputStream();
        for (byte[] p : parti) c.write(p);
        byte[] v = c.toByteArray();
        ByteArrayOutputStream o = new ByteArrayOutputStream();
        o.write(tag);
        int n = v.length;
        if (n < 128) o.write(n);
        else if (n < 256) { o.write(0x81); o.write(n); }
        else if (n < 65536) { o.write(0x82); o.write(n >> 8); o.write(n); }
        else { o.write(0x83); o.write(n >> 16); o.write(n >> 8); o.write(n); }
        o.write(v);
        return o.toByteArray();
    }

    private static byte[] seq(byte[]... p) throws IOException { return tlv(0x30, p); }

    private static byte[] tempo(Date d) throws IOException {
        SimpleDateFormat f = new SimpleDateFormat("yyMMddHHmmss'Z'", Locale.US);
        f.setTimeZone(TimeZone.getTimeZone("UTC"));
        return tlv(0x17, f.format(d).getBytes(StandardCharsets.US_ASCII));
    }

    private static byte[] certificatoAutofirmato(KeyPair kp) throws Exception {
        byte[] sha256Rsa = seq(tlv(0x06, new byte[]{0x2a, (byte) 0x86, 0x48, (byte) 0x86, (byte) 0xf7, 0x0d, 0x01, 0x01, 0x0b}), new byte[]{0x05, 0x00});
        byte[] nome = seq(tlv(0x31, seq(tlv(0x06, new byte[]{0x55, 0x04, 0x03}), tlv(0x0c, "MHXR Patcher".getBytes(StandardCharsets.UTF_8)))));
        long ora = System.currentTimeMillis();
        // UTCTime arriva fino al 2049: validita' fino al 2049-12-31
        Date fine = new Date(2524607999000L);
        byte[] validita = seq(tempo(new Date(ora - 86400000L)), tempo(fine));
        byte[] seriale = tlv(0x02, new BigInteger(63, new SecureRandom()).add(BigInteger.ONE).toByteArray());
        byte[] versione = tlv(0xa0, tlv(0x02, new byte[]{2}));
        byte[] tbs = seq(versione, seriale, sha256Rsa, nome, validita, nome, kp.getPublic().getEncoded());
        Signature s = Signature.getInstance("SHA256withRSA");
        s.initSign(kp.getPrivate());
        s.update(tbs);
        byte[] firma = s.sign();
        byte[] bit = new byte[firma.length + 1];
        System.arraycopy(firma, 0, bit, 1, firma.length);
        return seq(tbs, sha256Rsa, tlv(0x03, bit));
    }
}
