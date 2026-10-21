package app.ebira;

import android.app.*;
import android.content.*;
import android.database.Cursor;
import android.net.Uri;
import android.net.wifi.WifiManager;
import android.os.*;
import android.provider.*;
import org.json.*;
import java.io.*;
import java.nio.channels.FileChannel;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;

public final class TransferService extends Service {
    private static final Semaphore workerGate=new Semaphore(1);
    private final ConcurrentLinkedQueue<Intent> commands = new ConcurrentLinkedQueue<>();
    private volatile boolean running, stopping;
    private PowerManager.WakeLock wake; private WifiManager.WifiLock wifi;
    private LocalState state; private long lastWork; private long sampleTime, sampleBytes;
    private String sampleId = "";
    @Override public void onCreate() {
        super.onCreate();
        NotificationManager manager = getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel("transfer", "Local file transfers", NotificationManager.IMPORTANCE_LOW));
        startForeground(1, notification("Connecting to Windows…"));
        try { state = Repository.get(this); } catch (Exception e) { Repository.status = "Cannot open saved state: " + e.getMessage(); stopSelf(); return; }
        PowerManager power = getSystemService(PowerManager.class); wake = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "Ebira:transfer"); wake.acquire(6 * 60 * 60 * 1000L);
        WifiManager wm = (WifiManager) getApplicationContext().getSystemService(WIFI_SERVICE); wifi = wm.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "Ebira:WiFi"); wifi.acquire();
    }
    private Notification notification(String text) {
        PendingIntent open = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class), PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        PendingIntent stop = PendingIntent.getService(this, 1, new Intent(this, TransferService.class).setAction("stop"), PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(this, "transfer").setSmallIcon(android.R.drawable.stat_sys_upload).setContentTitle("Ebira · local transfer").setContentText(text).setContentIntent(open).setOngoing(true).addAction(new Notification.Action.Builder(null, "Pause & stop", stop).build()).build();
    }
    private long lastNotification;
    private void status(String text) {
        Repository.status = text;
        if (SystemClock.elapsedRealtime() - lastNotification > 1000) { getSystemService(NotificationManager.class).notify(1, notification(text)); lastNotification = SystemClock.elapsedRealtime(); }
    }
    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null && "stop".equals(intent.getAction())) { stopping = true; status("Paused. Tap Start / resume to continue."); stopSelf(); return START_NOT_STICKY; }
        if (intent != null) commands.add(intent);
        if (state != null && !running) { running = true; new Thread(this::runExclusive, "Ebira-transfer").start(); }
        return START_NOT_STICKY;
    }
    @Override public IBinder onBind(Intent intent) { return null; }
    @Override public void onTimeout(int startId, int type) { stopping = true; Repository.status = "Android stopped the transfer service at its time limit. Reopen Ebira to resume."; stopSelf(); }
    @Override public void onDestroy() { stopping = true; if (wake != null && wake.isHeld()) wake.release(); if (wifi != null && wifi.isHeld()) wifi.release(); super.onDestroy(); }
    private void check() throws InterruptedException { if (stopping) throw new InterruptedException("Paused"); }
    private void runExclusive(){try{workerGate.acquire();try{loop();}finally{workerGate.release();}}catch(InterruptedException e){Thread.currentThread().interrupt();stopSelf();}}
    private void loop() {
        long retry = 1000; lastWork = SystemClock.elapsedRealtime();
        try {
            while (!stopping) {
                try {
                    Intent command; while ((command = commands.poll()) != null) handle(command);
                    check(); Api api = Repository.api(state,this);
                    JSONObject session = api.get("/v1/session");
                    if (!"approved".equals(session.getString("state"))) { status("Waiting for approval in the Windows app…"); Thread.sleep(1500); continue; }
                    boolean worked = uploadStep(api);
                    JSONArray jobs = (JSONArray) api.json("/v1/transfers", "GET", null);
                    Repository.jobs = jobs.toString();
                    for (int i = 0; i < jobs.length(); i++) {
                        JSONObject job = jobs.getJSONObject(i);
                        if ("send".equals(job.getString("direction")) && ("active".equals(job.getString("state")) || "complete".equals(job.getString("state")))) { boolean downloaded=downloadStep(api,job);worked|=downloaded;if(downloaded)break; }
                    }
                    retry = 1000;
                    if (worked) lastWork = SystemClock.elapsedRealtime();
                    else { status("Connected · waiting for files. " + pendingSummary()); if (SystemClock.elapsedRealtime() - lastWork > 15 * 60 * 1000) { status("Receiver stopped after 15 minutes idle. Tap Start / resume for more files."); break; } Thread.sleep(1500); }
                } catch (Api.SessionEnded e) { status("Session ended. Pair again in Windows. Resume metadata is retained."); break; }
                catch (InterruptedException e) { break; }
                catch (Exception e) { status("Waiting / retrying: " + e.getMessage()); Thread.sleep(retry); retry = Math.min(retry * 2, 30000); }
            }
        } catch (InterruptedException ignored) { Thread.currentThread().interrupt(); }
        finally { running = false; stopSelf(); }
    }
    private String pendingSummary() throws Exception {
        synchronized (state) {
            JSONArray uploads = state.data.getJSONArray("uploads"); int pending = 0;
            for (int i=0;i<uploads.length();i++) if (!"complete".equals(uploads.getJSONObject(i).optString("state"))) pending++;
            return pending == 0 ? "" : pending + " outgoing file(s) pending.";
        }
    }
    private void handle(Intent intent) throws Exception {
        String action = intent.getAction();
        if ("join".equals(action)) {
            Uri invitation = Uri.parse(intent.getStringExtra("invitation"));
            if (!"ebira".equals(invitation.getScheme()) || !"pair".equals(invitation.getHost())) throw new IOException("Paste the complete ebira://pair invitation from Windows");
            String host = invitation.getQueryParameter("host"), pin = invitation.getQueryParameter("pin"), code = invitation.getQueryParameter("code");
            // Use the same explicit Wi-Fi routing for pairing as for data requests.
            JSONObject result;
            android.net.ConnectivityManager cm=getSystemService(android.net.ConnectivityManager.class);
            Api next = new Api(host, pin, "", url->{for(android.net.Network n:cm.getAllNetworks()){android.net.NetworkCapabilities caps=cm.getNetworkCapabilities(n);if(caps!=null&&caps.hasTransport(android.net.NetworkCapabilities.TRANSPORT_WIFI))return n.openConnection(url);}return url.openConnection();});
            result = next.post("/v1/join", new JSONObject().put("code", code).put("name", intent.getStringExtra("name")));
            synchronized (state) {
                // A new approval grants no authority over the previous session's jobs.
                state.data.put("host", host).put("pin", pin).put("token", result.getString("token")).put("deviceId", result.getString("id")); state.save();
            }
            status("Pairing requested. Approve this device in Windows.");
        } else if ("add".equals(action)) {
            ArrayList<String> uris = intent.getStringArrayListExtra("uris"); if (uris != null) for (String uri : uris) addUpload(Uri.parse(uri));
        } else if ("retry".equals(action)) {
            synchronized (state) { JSONArray uploads = state.data.getJSONArray("uploads"); for (int i=0;i<uploads.length();i++) { JSONObject u=uploads.getJSONObject(i); if ("failed".equals(u.optString("state"))) u.put("state", "queued").remove("error"); } state.save(); }
        } else if ("control".equals(action)) {
            Repository.api(state,this).post("/v1/transfers/" + intent.getStringExtra("id") + "/control", new JSONObject().put("action", intent.getStringExtra("control")));
        } else if ("end".equals(action)) {
            Api old=Repository.api(state,this);
            synchronized (state) { state.data.remove("token"); state.save(); }
            stopping=true;Repository.jobs="[]";status("Session ended on this Android device.");
            try{old.post("/v1/end",new JSONObject());status("Session ended on both devices.");}
            catch(Exception e){status("Ended locally. Windows is unreachable; end the session there too, or let its 24-hour expiry revoke it.");}
        } else if ("cleanup".equals(action)) {
            cleanup();
        }
    }
    private void addUpload(Uri uri) throws Exception {
        synchronized (state) { if (state.data.getJSONArray("uploads").length() >= 10000) throw new IOException("Clear completed history before adding files"); }
        String name = "file"; long size = -1;
        try (Cursor cursor = getContentResolver().query(uri, new String[]{OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE}, null, null, null)) {
            if (cursor != null && cursor.moveToFirst()) { name = cursor.getString(0); if (!cursor.isNull(1)) size = cursor.getLong(1); }
        }
        if (size < 0 || size > Api.MAX_FILE) throw new IOException("Choose local files of a known size, up to 60 GB each");
        try (ParcelFileDescriptor descriptor = getContentResolver().openFileDescriptor(uri, "r"); FileInputStream input = new FileInputStream(descriptor.getFileDescriptor())) { input.getChannel().position(0); }
        String device; synchronized (state) { device = state.data.optString("deviceId"); if (device.isEmpty()) throw new IOException("Pair before choosing files"); }
        JSONObject u = new JSONObject().put("id", UUID.randomUUID().toString().replace("-", "")).put("uri", uri.toString()).put("name", name).put("size", size).put("offset", 0).put("deviceId", device).put("state", "queued");
        synchronized (state) { state.data.getJSONArray("uploads").put(u); state.save(); }
    }
    private String hashUri(Uri uri) throws Exception {
        MessageDigest hash = MessageDigest.getInstance("SHA-256");
        try (InputStream input = getContentResolver().openInputStream(uri)) {
            if (input == null) throw new IOException("File permission expired. Select the source again."); byte[] buffer = new byte[1024 * 1024]; int n;
            while ((n = input.read(buffer)) != -1) { check(); hash.update(buffer, 0, n); }
        }
        return Api.hex(hash.digest());
    }
    private boolean uploadStep(Api api) throws Exception {
        JSONObject upload = null;
        synchronized (state) {
            JSONArray list = state.data.getJSONArray("uploads");
            for (int i=0;i<list.length();i++) { JSONObject u=list.getJSONObject(i); if (u.optString("deviceId").equals(state.data.optString("deviceId")) && ("queued".equals(u.optString("state")) || "active".equals(u.optString("state")))) { upload = u; break; } }
        }
        if (upload == null) return false;
        final JSONObject u = upload; Uri uri = Uri.parse(u.getString("uri")); String id=u.getString("id");
        try {
            String hash = u.optString("sha256");
            if (hash.isEmpty()) { status("Hashing " + u.getString("name") + "…"); hash=hashUri(uri); synchronized (state) { u.put("sha256", hash); state.save(); } }
            JSONObject remote = api.post("/v1/transfers", new JSONObject().put("id", id).put("name", u.getString("name")).put("size", u.getLong("size")).put("sha256", hash));
            String remoteState = remote.getString("state");
            if ("complete".equals(remoteState)) { synchronized (state) { u.put("state", "complete").put("offset", u.getLong("size")); state.save(); } return true; }
            if ("paused".equals(remoteState) || "queued".equals(remoteState)) return false;
            if ("cancelled".equals(remoteState) || "failed".equals(remoteState)) { synchronized (state) { u.put("state", "failed").put("error", remote.optString("error", remoteState)); state.save(); } return false; }
            long offset = remote.getLong("offset"), size=u.getLong("size");
            check();
            if (offset < size) {
                byte[] bytes = new byte[(int) Math.min(Api.CHUNK, size-offset)];
                try (ParcelFileDescriptor pfd = getContentResolver().openFileDescriptor(uri, "r"); FileInputStream input = new FileInputStream(pfd.getFileDescriptor())) {
                    FileChannel ch=input.getChannel(); if(ch.size()!=size) throw new IOException("Source size changed. Cancel and select it again."); ch.position(offset); int at=0,n; while(at<bytes.length && (n=input.read(bytes,at,bytes.length-at))!=-1)at+=n; if(at!=bytes.length)throw new IOException("Source was truncated");
                }
                long accepted=api.put(id,offset,bytes); if(accepted!=offset+bytes.length)throw new IOException("Unexpected receiver offset"); offset=accepted;
                synchronized (state) { u.put("offset",offset).put("state","active"); state.save(); } progress(id, u.getString("name"), offset, size, "Sending");
            }
            if(offset==size) { status("Verifying " + u.getString("name") + " on Windows…"); JSONObject done=api.post("/v1/transfers/"+id+"/finish",new JSONObject()); if(!hash.equals(done.getString("sha256"))||!"complete".equals(done.getString("state")))throw new IOException("Receiver verification failed"); synchronized(state){u.put("state","complete");state.save();} }
            return true;
        } catch (SecurityException | FileNotFoundException e) { synchronized(state){u.put("state","failed").put("error","Source unavailable. Select it again: "+e.getMessage());state.save();}return false; }
    }
    private void progress(String id, String name, long offset, long size, String verb) {
        long now=SystemClock.elapsedRealtime(); double rate=0;
        if(id.equals(sampleId)&&now>sampleTime)rate=Math.max(0,(offset-sampleBytes)*8.0/((now-sampleTime)/1000.0)/1e6);
        sampleId=id;sampleTime=now;sampleBytes=offset;
        status(String.format(Locale.ROOT,"%s %s · %.1f / %.1f MB · %.1f Mbps",verb,name,offset/1e6,size/1e6,rate));
    }
    private JSONObject destination(JSONObject job) throws Exception {
        String id=job.getString("id");
        synchronized(state){JSONObject saved=state.data.getJSONObject("downloads").optJSONObject(id);if(saved!=null)return saved;}
        long size=job.getLong("size"); if(size<0||size>Api.MAX_FILE)throw new IOException("File exceeds 60 GB limit");
        if(new StatFs(Environment.getExternalStorageDirectory().getPath()).getAvailableBytes()<size+16*1024*1024)throw new IOException("Not enough free storage");
        String name=job.getString("name"),category=category(name),mime=android.webkit.MimeTypeMap.getSingleton().getMimeTypeFromExtension(extension(name));
        if(mime==null)mime="application/octet-stream";
        Uri collection;String folder;
        if(category.equals("Pictures")){collection=MediaStore.Images.Media.EXTERNAL_CONTENT_URI;folder="Pictures/Ebira";}
        else if(category.equals("Videos")){collection=MediaStore.Video.Media.EXTERNAL_CONTENT_URI;folder="Movies/Ebira";}
        else if(category.equals("Music")){collection=MediaStore.Audio.Media.EXTERNAL_CONTENT_URI;folder="Music/Ebira";}
        else {collection=MediaStore.Downloads.EXTERNAL_CONTENT_URI;folder="Download/Ebira/"+category;}
        // MediaStore allocates a collision-safe destination; keep it hidden until verification.
        ContentValues values=new ContentValues();values.put(MediaStore.MediaColumns.DISPLAY_NAME,name);values.put(MediaStore.MediaColumns.MIME_TYPE,mime);values.put(MediaStore.MediaColumns.RELATIVE_PATH,folder);values.put(MediaStore.MediaColumns.IS_PENDING,1);
        Uri uri=getContentResolver().insert(collection,values);if(uri==null)throw new IOException("Could not create receive file");
        JSONObject saved=new JSONObject().put("uri",uri.toString()).put("offset",0).put("state","active").put("name",name).put("size",size).put("sha256",job.getString("sha256")).put("deviceId",job.getString("deviceId"));
        synchronized(state){state.data.getJSONObject("downloads").put(id,saved);state.save();}return saved;
    }
    private boolean downloadStep(Api api, JSONObject job) throws Exception {
        String id=job.getString("id");JSONObject saved;
        synchronized(state){saved=state.data.getJSONObject("downloads").optJSONObject(id);}
        if("complete".equals(job.getString("state"))&&(saved==null||"complete".equals(saved.optString("state"))))return false;
        if(saved==null)saved=destination(job);
        if("failed".equals(saved.optString("state")))return false;
        if("complete".equals(saved.optString("state"))) { api.post("/v1/transfers/"+id+"/ack",new JSONObject().put("offset",job.getLong("size")).put("sha256",job.getString("sha256")));return true; }
        long size=job.getLong("size"),offset=saved.getLong("offset");Uri uri=Uri.parse(saved.getString("uri"));
        // A lost acknowledgement leaves the durable receiver one chunk ahead.
        // Reconcile it before reading another chunk so progress never skips two chunks.
        long serverOffset=job.getLong("offset");
        if(offset>serverOffset)api.post("/v1/transfers/"+id+"/ack",new JSONObject().put("offset",offset));
        if(offset<serverOffset)throw new IOException("Saved receiver offset is behind Windows. Cancel and select the file again.");
        try(ParcelFileDescriptor pfd=getContentResolver().openFileDescriptor(uri,"rw");FileOutputStream out=new FileOutputStream(pfd.getFileDescriptor())) {
            FileChannel channel=out.getChannel();long actual=channel.size();if(actual<offset)throw new IOException("Partial file changed; cancel it in Windows and clean partials");channel.truncate(offset);channel.position(offset);
            check();
            if(offset<size){byte[] bytes=api.chunk(id,offset);if(bytes.length==0||offset+bytes.length>size)throw new IOException("Invalid download length");out.write(bytes);out.getFD().sync();offset+=bytes.length;synchronized(state){saved.put("offset",offset);state.save();}api.post("/v1/transfers/"+id+"/ack",new JSONObject().put("offset",offset));progress(id,job.getString("name"),offset,size,"Receiving");}
        }
        if(offset==size){
            status("Verifying "+job.getString("name")+"…");String hash=hashUri(uri);
            if(!hash.equals(job.getString("sha256"))){synchronized(state){saved.put("state","failed");state.save();}api.post("/v1/transfers/"+id+"/control",new JSONObject().put("action","pause"));throw new IOException("SHA-256 mismatch. Cancel in Windows and clean partials before retrying.");}
            // Journal verification before publishing; a crash can replay both operations safely.
            synchronized(state){saved.put("state","verified");state.save();}
            ContentValues values=new ContentValues();values.put(MediaStore.MediaColumns.IS_PENDING,0);if(getContentResolver().update(uri,values,null,null)!=1)throw new IOException("Could not publish verified file");
            synchronized(state){saved.put("state","complete");state.save();}
            api.post("/v1/transfers/"+id+"/ack",new JSONObject().put("offset",size).put("sha256",hash));
        }
        return true;
    }
    private static String extension(String name){int dot=name.lastIndexOf('.');return dot<0?"":name.substring(dot+1).toLowerCase(Locale.ROOT);}
    static String category(String name){return switch(extension(name)){case "jpg","jpeg","png","gif","webp","heic","avif"->"Pictures";case "mp4","mov","mkv","webm","avi"->"Videos";case "mp3","m4a","wav","aac","flac","ogg"->"Music";case "pdf","doc","docx","xls","xlsx","ppt","pptx","txt","csv","odt"->"Documents";default->"Other";};}
    private void cleanup() throws Exception {
        synchronized(state){
            JSONObject downloads=state.data.getJSONObject("downloads");List<String>remove=new ArrayList<>();Iterator<String>keys=downloads.keys();
            JSONArray remote=new JSONArray(Repository.jobs);Set<String>live=new HashSet<>();for(int i=0;i<remote.length();i++){JSONObject j=remote.getJSONObject(i);if(!"complete".equals(j.optString("state"))&&!"cancelled".equals(j.optString("state")))live.add(j.getString("id"));}
            while(keys.hasNext()){String id=keys.next();JSONObject d=downloads.getJSONObject(id);if(!"complete".equals(d.optString("state"))){if(live.contains(id))throw new IOException("Cancel incomplete transfers in Windows before cleaning their files");getContentResolver().delete(Uri.parse(d.getString("uri")),null,null);remove.add(id);}}
            for(String id:remove)downloads.remove(id);
            JSONArray old=state.data.getJSONArray("uploads"),keep=new JSONArray();for(int i=0;i<old.length();i++){JSONObject u=old.getJSONObject(i);if(!"complete".equals(u.optString("state")))keep.put(u);}state.data.put("uploads",keep);state.save();
        }
        status("Incomplete receive files removed and outgoing completed history cleared.");
    }
}
