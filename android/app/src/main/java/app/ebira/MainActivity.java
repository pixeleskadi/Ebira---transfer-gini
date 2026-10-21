package app.ebira;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.graphics.Color;
import android.net.Uri;
import android.os.*;
import android.widget.*;
import org.json.*;
import java.util.*;

public final class MainActivity extends Activity {
    private EditText invitation, name; private TextView status, history, offline; private LinearLayout jobs;
    private Button find; private Thread discovery;
    private final Handler handler=new Handler(Looper.getMainLooper());
    private String rendered="";
    private final Runnable tick=new Runnable(){public void run(){refresh();handler.postDelayed(this,1000);}};
    @Override public void onCreate(Bundle saved){
        super.onCreate(saved);getWindow().setStatusBarColor(Color.rgb(244,247,249));getWindow().setNavigationBarColor(Color.rgb(244,247,249));
        ScrollView scroll=new ScrollView(this);LinearLayout page=new LinearLayout(this);page.setOrientation(LinearLayout.VERTICAL);page.setPadding(dp(24),dp(36),dp(24),dp(32));page.setBackgroundColor(Color.rgb(247,249,251));scroll.addView(page);setContentView(scroll);
        TextView title=text("Ebira",34);title.setTypeface(null,android.graphics.Typeface.BOLD);page.addView(title);page.addView(text("LOCAL FILE TRANSFER · V1 CANDIDATE",11));
        page.addView(text("1  Connect offline",21));page.addView(text("Create a private connection here, then join it from Windows Wi-Fi settings. Mobile data and internet are not needed. Android chooses the Wi-Fi band; it may disconnect your current Wi-Fi.",14));
        button(page,"Create offline connection",this::createOffline);
        button(page,"Phone hotspot settings · optional 5 GHz",()->new AlertDialog.Builder(this).setTitle("Choose a faster Wi-Fi band").setMessage("If your phone offers a 5 GHz hotspot, choose it in phone Settings and connect Windows to it. This replaces Ebira's automatic hotspot. Availability and offline behavior depend on your phone. Ebira does not turn on mobile data. Return here and tap Find my laptop.").setNegativeButton("Cancel",null).setPositiveButton("Open hotspot settings",(d,w)->{
            stopService(new Intent(this,OfflineService.class));OfflineService.status="Using phone-managed Wi-Fi. Join its hotspot on Windows, then tap Find my laptop. Mobile data is not used by Ebira.";
            try{startActivity(new Intent("android.settings.TETHER_SETTINGS"));}catch(ActivityNotFoundException e){startActivity(new Intent(android.provider.Settings.ACTION_WIRELESS_SETTINGS));}
        }).show());
        offline=text("",14);offline.setTextIsSelectable(true);page.addView(offline);
        button(page,"Stop offline connection",()->new AlertDialog.Builder(this).setTitle("Stop the private connection?").setMessage("Active transfers will pause when the link disappears. Reconnect both devices to resume.").setNegativeButton("Keep connection",null).setPositiveButton("Stop",(d,w)->stopService(new Intent(this,OfflineService.class))).show());
        page.addView(text("2  Find & pair with Windows",21));page.addView(text("Open Ebira on your laptop. After joining the private Wi-Fi, find it below and compare the pairing code AND certificate ending with the Windows screen. You can also use your own existing Wi-Fi or paste an invitation manually.",14));
        find=new Button(this);find.setText(R.string.find_laptop);find.setAllCaps(false);page.addView(find,new LinearLayout.LayoutParams(-1,-2));find.setOnClickListener(v->findLaptop());
        invitation=new EditText(this);invitation.setHint("ebira://pair?host=…&pin=…&code=…");invitation.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE);invitation.setMinLines(2);page.addView(invitation);
        name=new EditText(this);name.setSingleLine(true);name.setHint("Device name");name.setText(getString(R.string.device_name,Build.MANUFACTURER,Build.MODEL));page.addView(name);
        button(page,"Pair · then approve in Windows",this::confirmPair);
        page.addView(text("3  Send & receive",21));button(page,"Start / resume transfers",()->start(new Intent(this,TransferService.class).setAction("retry")));
        button(page,"Choose files to send…",()->{Intent pick=new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);startActivityForResult(pick,42);});
        button(page,"Pause service",()->startService(new Intent(this,TransferService.class).setAction("stop")));
        status=text("",15);status.setPadding(0,dp(12),0,dp(12));page.addView(status);
        page.addView(text("Transfer queue",21));jobs=new LinearLayout(this);jobs.setOrientation(LinearLayout.VERTICAL);page.addView(jobs);history=text("",13);page.addView(history);
        button(page,"End session",()->new AlertDialog.Builder(this).setTitle("End this device session?").setMessage("Further file requests require a new approval. Incomplete files remain until you clean them.").setNegativeButton("Keep session",null).setPositiveButton("End session",(d,w)->start(new Intent(this,TransferService.class).setAction("end"))).show());
        button(page,"Clean incomplete files / completed history",()->new AlertDialog.Builder(this).setTitle("Delete incomplete receive files?").setMessage("Cancel their transfers in Windows first. Completed received files are kept. Outgoing completed history is cleared.").setNegativeButton("Keep files",null).setPositiveButton("Delete incomplete files",(d,w)->start(new Intent(this,TransferService.class).setAction("cleanup"))).show());
        page.addView(text("60 GB per file · encrypted over your local network\nReceived photos: Pictures/Ebira; video: Movies/Ebira; audio: Music/Ebira; other files: Download/Ebira by type.\n\nKeep Windows awake. Android runs a visible transfer service while working; manufacturers and battery settings can still interrupt it. Reopen and resume after a restart. A force-stop requires opening the app again. Receiver waiting ends after 15 idle minutes.\n\nNo accounts, analytics or cloud transfer. Credentials and resume metadata stay on this device. Uninstalling removes local state; completed files stay in shared storage.",12));
        if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=android.content.pm.PackageManager.PERMISSION_GRANTED)requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},9);
        loadInvitation(getIntent());
    }
    private void createOffline(){
        String permission=Build.VERSION.SDK_INT>=33?Manifest.permission.NEARBY_WIFI_DEVICES:Manifest.permission.ACCESS_FINE_LOCATION;
        if(checkSelfPermission(permission)!=android.content.pm.PackageManager.PERMISSION_GRANTED){
            new AlertDialog.Builder(this).setTitle("Allow private Wi-Fi").setMessage(Build.VERSION.SDK_INT>=33?"Android needs Nearby Wi-Fi permission to create this offline connection.":"Android 10–12 require precise Location permission and Location switched on to create a hotspot. Ebira does not collect your location.").setNegativeButton("Cancel",null).setPositiveButton("Continue",(d,w)->requestPermissions(Build.VERSION.SDK_INT>=31&&Build.VERSION.SDK_INT<33?new String[]{Manifest.permission.ACCESS_FINE_LOCATION,Manifest.permission.ACCESS_COARSE_LOCATION}:new String[]{permission},81)).show();return;
        }
        if(Build.VERSION.SDK_INT<33&&!getSystemService(android.location.LocationManager.class).isLocationEnabled()){
            new AlertDialog.Builder(this).setTitle("Turn Location on").setMessage("Android requires Location to be on for local hotspots on this version. Return here and tap Create offline connection afterward.").setPositiveButton("Open settings",(d,w)->startActivity(new Intent(android.provider.Settings.ACTION_LOCATION_SOURCE_SETTINGS))).setNegativeButton("Cancel",null).show();return;
        }
        try{startForegroundService(new Intent(this,OfflineService.class).setAction("start"));}catch(Exception e){OfflineService.status="Could not start offline connection: "+e.getMessage();}
    }
    @Override public void onRequestPermissionsResult(int request,String[] permissions,int[] results){super.onRequestPermissionsResult(request,permissions,results);if(request==81){String permission=Build.VERSION.SDK_INT>=33?Manifest.permission.NEARBY_WIFI_DEVICES:Manifest.permission.ACCESS_FINE_LOCATION;if(checkSelfPermission(permission)==android.content.pm.PackageManager.PERMISSION_GRANTED)createOffline();else OfflineService.status="Wi-Fi permission was declined. Tap Create offline connection to try again, or use an existing private network.";}}
    private void confirmPair(){
        String value=invitation.getText().toString().trim();Uri uri=Uri.parse(value);String pin=uri.getQueryParameter("pin"),pairCode=uri.getQueryParameter("code");
        if(!"ebira".equals(uri.getScheme())||pin==null||!pin.matches("[a-fA-F0-9]{64}")){new AlertDialog.Builder(this).setTitle("Ebira").setMessage("Find your laptop or paste its complete Windows invitation first.").setPositiveButton("OK",null).show();return;}
        new AlertDialog.Builder(this).setTitle("Check your Windows screen").setMessage("Pairing code: "+pairCode+"\nCertificate ends: "+pin.substring(52).toUpperCase(Locale.ROOT)+"\n\nBoth must match the Ebira window on your laptop.\n\nFull certificate SHA-256:\n"+pin+"\n\nApprove this device in Windows after connecting.").setNegativeButton("Cancel",null).setPositiveButton("Both match · connect",(d,w)->start(new Intent(this,TransferService.class).setAction("join").putExtra("invitation",value).putExtra("name",name.getText().toString()))).show();
    }
    private void findLaptop(){
        if(discovery!=null&&discovery.isAlive())return;
        find.setEnabled(false);find.setText(R.string.finding_laptops);
        discovery=new Thread(()->{
            try{java.util.List<LanDiscovery.Host> hosts=LanDiscovery.find(this);handler.post(()->{
                if(isFinishing()||isDestroyed())return;
                if(hosts.isEmpty())new AlertDialog.Builder(this).setTitle("No laptop found").setMessage("Check that Windows joined the Wi-Fi shown above and Ebira is open there. Set this private hotspot's Windows network profile to Private and allow Ebira through its firewall. Then retry. You can also paste the invitation manually.").setPositiveButton("OK",null).show();
                else{String[] labels=new String[hosts.size()];for(int i=0;i<labels.length;i++)labels[i]=hosts.get(i).name+"\n"+hosts.get(i).host;new AlertDialog.Builder(this).setTitle("Choose your laptop").setItems(labels,(d,w)->{invitation.setText(hosts.get(w).invitation());confirmPair();}).setNegativeButton("Cancel",null).show();}
            });}catch(Exception e){handler.post(()->{if(!isFinishing()&&!isDestroyed())new AlertDialog.Builder(this).setTitle("Discovery unavailable").setMessage(e.getMessage()).setPositiveButton("OK",null).show();});}
            finally{handler.post(()->{find.setEnabled(true);find.setText(R.string.find_laptop);});}
        },"Ebira-discovery");discovery.start();
    }
    private int dp(int n){return (int)(n*getResources().getDisplayMetrics().density);}
    private TextView text(String value,int size){TextView t=new TextView(this);t.setText(value);t.setTextSize(size);t.setTextColor(Color.rgb(30,44,57));t.setPadding(0,dp(10),0,dp(8));return t;}
    private void button(LinearLayout parent,String label,Action action){Button b=new Button(this);b.setText(label);b.setAllCaps(false);b.setPadding(dp(10),dp(6),dp(10),dp(6));parent.addView(b,new LinearLayout.LayoutParams(-1,-2));b.setOnClickListener(v->{try{action.run();}catch(Exception e){new AlertDialog.Builder(this).setTitle("Ebira").setMessage(e.getMessage()).setPositiveButton("OK",null).show();}});}
    private interface Action{void run()throws Exception;}
    private void start(Intent intent){try{startForegroundService(intent);}catch(Exception e){Repository.status="Could not start: "+e.getMessage();}}
    private void loadInvitation(Intent intent){if(intent!=null&&intent.getData()!=null&&"ebira".equals(intent.getData().getScheme()))invitation.setText(intent.getData().toString());}
    @Override protected void onNewIntent(Intent intent){super.onNewIntent(intent);loadInvitation(intent);}
    @Override protected void onResume(){super.onResume();handler.post(tick);}
    @Override protected void onPause(){handler.removeCallbacks(tick);super.onPause();}
    @Override protected void onDestroy(){if(discovery!=null)discovery.interrupt();super.onDestroy();}
    @Override protected void onActivityResult(int request,int result,Intent data){
        super.onActivityResult(request,result,data);if(request!=42||result!=RESULT_OK||data==null)return;
        ArrayList<String>uris=new ArrayList<>();if(data.getClipData()!=null){for(int i=0;i<data.getClipData().getItemCount();i++)uris.add(data.getClipData().getItemAt(i).getUri().toString());}else if(data.getData()!=null)uris.add(data.getData().toString());
        try{for(String s:uris)getContentResolver().takePersistableUriPermission(Uri.parse(s),Intent.FLAG_GRANT_READ_URI_PERMISSION);start(new Intent(this,TransferService.class).setAction("add").putStringArrayListExtra("uris",uris));}
        catch(Exception e){Repository.status="This provider cannot keep file access after restart. Choose local files: "+e.getMessage();}
    }
    private void refresh(){
        offline.setText(OfflineService.active?getString(R.string.offline_credentials,OfflineService.status,OfflineService.ssid,OfflineService.password):OfflineService.status);
        status.setText(Repository.status);
        try{
            JSONObject local=Repository.snapshot(this);JSONArray uploads=local.getJSONArray("uploads");JSONObject downloads=local.getJSONObject("downloads");StringBuilder lines=new StringBuilder();
            for(int i=0;i<uploads.length();i++){JSONObject u=uploads.getJSONObject(i);lines.append("↑ ").append(u.getString("name")).append(" · ").append(u.getString("state")).append(" · ").append(String.format(Locale.ROOT,"%.1f / %.1f MB",u.optLong("offset")/1e6,u.getLong("size")/1e6)).append('\n');if(u.has("error"))lines.append(u.getString("error")).append('\n');}
            Iterator<String>keys=downloads.keys();while(keys.hasNext()){JSONObject d=downloads.getJSONObject(keys.next());lines.append("↓ ").append(d.getString("name")).append(" · ").append(d.getString("state")).append('\n');}
            history.setText(lines.length()==0?"No local transfer history yet.":lines.toString());
            String current=Repository.jobs;if(current.equals(rendered))return;rendered=current;jobs.removeAllViews();JSONArray remote=new JSONArray(current);
            for(int i=0;i<remote.length();i++){JSONObject job=remote.getJSONObject(i);String st=job.getString("state");if(st.equals("complete")||st.equals("cancelled"))continue;String id=job.getString("id");jobs.addView(text(job.getString("name")+" · "+st,14));LinearLayout row=new LinearLayout(this);row.setOrientation(LinearLayout.HORIZONTAL);jobs.addView(row);for(String control:new String[]{"pause","resume","cancel"}){Button b=new Button(this);b.setText(control);b.setAllCaps(false);row.addView(b,new LinearLayout.LayoutParams(0,-2,1));b.setOnClickListener(v->start(new Intent(this,TransferService.class).setAction("control").putExtra("id",id).putExtra("control",control)));}}
        }catch(Exception e){history.setText(getString(R.string.saved_state_error,e.getMessage()));}
    }
}
