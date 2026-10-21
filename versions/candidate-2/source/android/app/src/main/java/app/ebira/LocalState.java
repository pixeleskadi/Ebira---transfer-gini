package app.ebira;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;
import org.json.*;
import java.io.*;
import java.security.KeyStore;
import javax.crypto.*;
import javax.crypto.spec.GCMParameterSpec;

/** Credentials and resume metadata are encrypted with a non-exportable Android Keystore key. */
final class LocalState {
    private final AtomicFile file;
    private final javax.crypto.SecretKey key;
    final JSONObject data;
    LocalState(Context context) throws Exception {
        file = new AtomicFile(new File(context.getFilesDir(), "state.bin"));
        KeyStore ks = KeyStore.getInstance("AndroidKeyStore"); ks.load(null);
        String alias = "ebira-state-v1";
        if (!ks.containsAlias(alias)) {
            KeyGenerator gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
            gen.init(new KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build()); gen.generateKey();
        }
        key = (javax.crypto.SecretKey) ks.getKey(alias, null);
        if (file.getBaseFile().exists() || new File(file.getBaseFile().getPath()+".bak").exists()) {
            byte[] bytes = file.readFully(); if (bytes.length < 28) throw new IOException("Invalid saved state");
            Cipher c = Cipher.getInstance("AES/GCM/NoPadding"); c.init(Cipher.DECRYPT_MODE, key, new GCMParameterSpec(128, bytes, 0, 12));
            data = new JSONObject(new String(c.doFinal(bytes, 12, bytes.length - 12), java.nio.charset.StandardCharsets.UTF_8));
        } else { data = new JSONObject(); data.put("uploads", new JSONArray()); data.put("downloads", new JSONObject()); }
    }
    synchronized void save() throws Exception {
        Cipher c = Cipher.getInstance("AES/GCM/NoPadding"); c.init(Cipher.ENCRYPT_MODE, key);
        byte[] encrypted = c.doFinal(data.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
        FileOutputStream out = null;
        try { out = file.startWrite(); out.write(c.getIV()); out.write(encrypted); file.finishWrite(out); }
        catch (Exception e) { if (out != null) file.failWrite(out); throw e; }
    }
}
