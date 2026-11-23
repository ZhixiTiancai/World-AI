package com.zhixitiancai.worldai;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;
import android.util.Base64;
import android.view.View;
import android.webkit.JavascriptInterface;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.concurrent.*;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

public final class MainActivity extends Activity {
    private static final String LOCAL = "file:///android_asset/index.html";
    private static final String GITHUB = "https://github.com/ZhixiTiancai/World-AI/releases/latest";
    private static final String RELEASE_API = "https://api.github.com/repos/ZhixiTiancai/World-AI/releases/latest";
    private final ExecutorService worker = Executors.newFixedThreadPool(3);
    private final ConcurrentMap<String, Call> calls = new ConcurrentHashMap<>();
    private WebView web;
    private AtomicFile data;
    private SharedPreferences vault;
    private ValueCallback<Uri[]> fileCallback;
    private String exportText;
    private String version;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        data = new AtomicFile(new File(getFilesDir(), "world.json"));
        vault = getSharedPreferences("local-model-keys", MODE_PRIVATE);
        try { version = getPackageManager().getPackageInfo(getPackageName(), 0).versionName; }
        catch (Exception error) { version = "0.0.0"; }
        web = new WebView(this);
        web.getSettings().setJavaScriptEnabled(true);
        web.getSettings().setAllowFileAccess(false);
        web.getSettings().setAllowContentAccess(true);
        web.getSettings().setAllowFileAccessFromFileURLs(false);
        web.getSettings().setAllowUniversalAccessFromFileURLs(false);
        web.addJavascriptInterface(new Bridge(), "WorldNative");
        web.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                return !LOCAL.equals(request.getUrl().toString());
            }
        });
        web.setWebChromeClient(new WebChromeClient() {
            @Override public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
                if (fileCallback != null) fileCallback.onReceiveValue(null);
                fileCallback = callback;
                Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*");
                try { startActivityForResult(intent, 11); }
                catch (Exception error) { fileCallback.onReceiveValue(null); fileCallback = null; }
                return true;
            }
        });
        setContentView(web);
        web.loadUrl(LOCAL);
    }

    @Override public void onBackPressed() {
        web.evaluateJavascript("window.worldBack && window.worldBack()", handled -> {
            if (!"true".equals(handled)) finish();
        });
    }

    @Override protected void onActivityResult(int code, int result, Intent value) {
        super.onActivityResult(code, result, value);
        Uri uri = result == RESULT_OK && value != null ? value.getData() : null;
        if (code == 11 && fileCallback != null) {
            fileCallback.onReceiveValue(uri == null ? null : new Uri[] { uri });
            fileCallback = null;
        } else if (code == 12 && exportText != null) {
            String text = exportText;
            exportText = null;
            if (uri == null) return;
            worker.execute(() -> {
                try (OutputStream output = getContentResolver().openOutputStream(uri, "wt")) {
                    if (output == null) throw new IOException("无法打开导出位置。");
                    output.write(text.getBytes(StandardCharsets.UTF_8));
                    emit("export", "done", "已保存文件。");
                } catch (Exception error) { emit("export", "error", reason(error)); }
            });
        }
    }

    @Override protected void onDestroy() {
        for (Call call : calls.values()) call.cancel();
        worker.shutdownNow();
        if (fileCallback != null) fileCallback.onReceiveValue(null);
        web.removeJavascriptInterface("WorldNative");
        web.destroy();
        web = null;
        super.onDestroy();
    }

    public final class Bridge {
        @JavascriptInterface public String version() { return version; }

        @JavascriptInterface public String load() {
            synchronized (data) {
                try (InputStream input = data.openRead()) { return read(input, 32 * 1024 * 1024); }
                catch (FileNotFoundException missing) { return ""; }
                catch (Exception error) { return "!无法读取本地数据：" + reason(error); }
            }
        }

        @JavascriptInterface public String save(String value) {
            synchronized (data) {
                FileOutputStream stream = null;
                try {
                    byte[] bytes = value.getBytes(StandardCharsets.UTF_8);
                    if (bytes.length > 32 * 1024 * 1024) throw new IOException("本地数据超过 32 MB，请先备份并整理会话。");
                    stream = data.startWrite();
                    stream.write(bytes);
                    data.finishWrite(stream);
                    return "";
                } catch (Exception error) {
                    if (stream != null) data.failWrite(stream);
                    return reason(error);
                }
            }
        }

        @JavascriptInterface public boolean hasKey(String id) { return vault.contains(id); }

        @JavascriptInterface public String setKey(String id, String key) {
            try {
                if (key.isEmpty()) {
                    if (!vault.edit().remove(id).commit()) throw new IOException("无法保存密钥。");
                    return "";
                }
                Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
                cipher.init(Cipher.ENCRYPT_MODE, secret());
                String value = Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP) + "." +
                    Base64.encodeToString(cipher.doFinal(key.trim().getBytes(StandardCharsets.UTF_8)), Base64.NO_WRAP);
                if (!vault.edit().putString(id, value).commit()) throw new IOException("无法保存密钥。");
                return "";
            } catch (Exception error) { return reason(error); }
        }

        @JavascriptInterface public void request(String id, String action, String params) {
            Call call = new Call();
            calls.put(id, call);
            worker.execute(() -> {
                try { requestModel(id, action, new JSONObject(params), call); }
                catch (Exception error) { emit(id, call.cancelled ? "cancel" : "error", reason(error)); }
                finally { calls.remove(id, call); call.disconnect(); }
            });
        }

        @JavascriptInterface public void cancel(String id) {
            Call call = calls.get(id);
            if (call != null) call.cancel();
        }

        @JavascriptInterface public void exportFile(String name, String mime, String content) {
            runOnUiThread(() -> {
                if (exportText != null) { emit("export", "error", "请先完成当前文件导出。"); return; }
                exportText = content;
                Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE)
                    .setType(mime).putExtra(Intent.EXTRA_TITLE, name);
                try { startActivityForResult(intent, 12); }
                catch (Exception error) { exportText = null; emit("export", "error", reason(error)); }
            });
        }

        @JavascriptInterface public void releasePage(boolean apk) {
            runOnUiThread(() -> {
                String destination = apk ? GITHUB + "/download/WorldAI-android.apk" : GITHUB;
                try { startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(destination))); }
                catch (Exception error) { emit("export", "error", reason(error)); }
            });
        }

        @JavascriptInterface public void bars(String color, boolean light) {
            runOnUiThread(() -> {
                getWindow().setStatusBarColor(Color.parseColor(color));
                getWindow().setNavigationBarColor(Color.parseColor(color));
                getWindow().getDecorView().setSystemUiVisibility(light
                    ? View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR | View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR : 0);
            });
        }
    }

    private static final class Call {
        volatile boolean cancelled;
        volatile HttpURLConnection connection;
        void cancel() { cancelled = true; disconnect(); }
        void disconnect() { if (connection != null) connection.disconnect(); }
        void check() throws IOException { if (cancelled) throw new IOException("已停止。"); }
    }

    private synchronized SecretKey secret() throws Exception {
        KeyStore storage = KeyStore.getInstance("AndroidKeyStore");
        storage.load(null);
        final String alias = "WorldAI.ModelKeys";
        java.security.Key key = storage.getKey(alias, null);
        if (key != null) return (SecretKey) key;
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());
        return generator.generateKey();
    }

    private String getKey(String id) throws Exception {
        String saved = vault.getString(id, "");
        if (saved.isEmpty()) return "";
        String[] pieces = saved.split("\\.", 2);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, secret(), new GCMParameterSpec(128, Base64.decode(pieces[0], Base64.DEFAULT)));
        return new String(cipher.doFinal(Base64.decode(pieces[1], Base64.DEFAULT)), StandardCharsets.UTF_8);
    }

    private static URL modelUrl(String address, boolean list) throws Exception {
        URI base = new URI(address.trim());
        String scheme = base.getScheme();
        if (!("http".equalsIgnoreCase(scheme) || "https".equalsIgnoreCase(scheme)) ||
                base.getHost() == null || base.getUserInfo() != null || base.getFragment() != null)
            throw new IOException("请输入有效的 HTTP 或 HTTPS API 地址。");
        String path = base.getPath().replaceAll("/+$", "");
        if (path.endsWith("/chat/completions")) path = path.substring(0, path.length() - "/chat/completions".length());
        else if (path.isEmpty()) path = "/v1";
        return new URI(scheme, null, base.getHost(), base.getPort(), path + (list ? "/models" : "/chat/completions"),
            base.getQuery(), null).toURL();
    }

    private void requestModel(String id, String action, JSONObject params, Call call) throws Exception {
        boolean release = action.equals("release");
        boolean list = action.equals("models");
        if (!release && !list && !action.equals("chat")) throw new IOException("未知请求。");
        URL url = release ? new URL(RELEASE_API) : modelUrl(params.getString("endpoint"), list);
        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        call.connection = connection;
        call.check();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(release || list ? 20000 : 120000);
        connection.setRequestProperty("User-Agent", "WorldAI-Android/" + version);
        if (!release) {
            String key = params.optString("key", "");
            if (key.isEmpty() && params.optBoolean("useSaved")) key = getKey(params.optString("modelId"));
            if (!key.isEmpty()) connection.setRequestProperty("Authorization", "Bearer " + key);
        }
        boolean stream = !release && !list && params.optBoolean("stream", true);
        connection.setRequestProperty("Accept", release ? "application/vnd.github+json" : stream ? "text/event-stream" : "application/json");
        if (!release && !list) {
            JSONObject body = new JSONObject().put("model", params.getString("name")).put("stream", stream)
                .put("messages", params.getJSONArray("messages"));
            byte[] bytes = body.toString().getBytes(StandardCharsets.UTF_8);
            connection.setRequestMethod("POST");
            connection.setDoOutput(true);
            connection.setFixedLengthStreamingMode(bytes.length);
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            try (OutputStream streamOut = connection.getOutputStream()) { streamOut.write(bytes); }
        }
        int code = connection.getResponseCode();
        if (release && code == 404) { emit(id, "done", "{}"); return; }
        if (code < 200 || code >= 300) {
            String message = "HTTP " + code;
            try (InputStream error = connection.getErrorStream()) {
                if (error != null) {
                    JSONObject payload = new JSONObject(read(error, 128 * 1024));
                    JSONObject detail = payload.optJSONObject("error");
                    message += " · " + (detail == null ? payload.optString("message") : detail.optString("message"));
                }
            } catch (Exception ignored) { }
            throw new IOException(message);
        }
        try (InputStream input = connection.getInputStream()) {
            if (release || list) { emit(id, "done", read(input, 8 * 1024 * 1024)); return; }
            String type = connection.getContentType();
            if (type != null && type.startsWith("text/event-stream")) {
                BufferedReader reader = new BufferedReader(new InputStreamReader(input, StandardCharsets.UTF_8));
                String line;
                while ((line = reader.readLine()) != null) {
                    call.check();
                    if (!line.startsWith("data:")) continue;
                    String value = line.substring(5).trim();
                    if (value.equals("[DONE]")) break;
                    if (!value.isEmpty()) fragment(id, new JSONObject(value), true);
                }
            } else fragment(id, new JSONObject(read(input, 8 * 1024 * 1024)), false);
        }
        call.check();
        emit(id, "done", "");
    }

    private void fragment(String id, JSONObject value, boolean stream) throws IOException {
        if (value.has("error")) {
            JSONObject error = value.optJSONObject("error");
            throw new IOException(error == null ? "模型请求出错。" : error.optString("message"));
        }
        JSONArray choices = value.optJSONArray("choices");
        if (choices == null || choices.length() == 0) return;
        JSONObject choice = choices.optJSONObject(0);
        JSONObject message = choice == null ? null : choice.optJSONObject(stream ? "delta" : "message");
        String text = message == null || message.isNull("content") ? "" : message.optString("content", "");
        if (!text.isEmpty()) emit(id, "chunk", text);
    }

    private void emit(String id, String kind, String text) {
        runOnUiThread(() -> {
            if (web == null) return;
            String code = "window.worldEvent(" + JSONObject.quote(id) + "," + JSONObject.quote(kind) + "," + JSONObject.quote(text) + ")";
            web.evaluateJavascript(code, null);
        });
    }

    private static String reason(Exception error) {
        String value = error.getMessage();
        return value == null || value.isEmpty() ? error.getClass().getSimpleName() : value.substring(0, Math.min(value.length(), 400));
    }

    private static String read(InputStream input, int limit) throws IOException {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = input.read(buffer)) != -1) {
            if (bytes.size() + count > limit) throw new IOException("数据超过大小限制。");
            bytes.write(buffer, 0, count);
        }
        return new String(bytes.toByteArray(), StandardCharsets.UTF_8);
    }
}
