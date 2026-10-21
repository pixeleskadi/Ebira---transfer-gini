package app.ebira;

import org.json.*;
import java.io.*;
import java.util.*;
import java.security.*;

/** Runs the exact Android transport class on a JVM against the real transfer engine. */
public final class ApiCheck {
    static int passed;
    static void check(boolean condition,String text){if(!condition)throw new AssertionError(text);System.out.println("PASS Android transport: "+text);passed++;}
    interface Work{void run()throws Exception;}
    static void reject(Work work,String text)throws Exception{boolean failed=false;try{work.run();}catch(Exception e){failed=true;}check(failed,text);}
    public static void main(String[]args)throws Exception{
        String host=args[0],pin=args[1],code=args[2];
        reject(()->new Api("http://127.0.0.1:80",pin,""),"cleartext origin rejected");
        reject(()->new Api("https://8.8.8.8",pin,""),"public network origin rejected");
        reject(()->new Api("https://example.com",pin,""),"DNS origin rejected");
        reject(()->new Api(host,"00",""),"malformed certificate pin rejected");
        reject(()->new Api(host,"0".repeat(64),"").get("/v1/info"),"wrong certificate pin fails TLS");
        Api unpaired=new Api(host,pin,"");check(unpaired.get("/v1/info").getInt("protocol")==1,"correct pin accepts local TLS endpoint");
        JSONObject joined=unpaired.post("/v1/join",new JSONObject().put("code",code).put("name","Android transport check"));
        Api api=new Api(host,pin,joined.getString("token"));check(api.get("/v1/session").getString("state").equals("pending"),"pending session can poll status");
        reject(()->api.json("/v1/transfers","GET",null),"pending session cannot list transfers");
        BufferedReader commands=new BufferedReader(new InputStreamReader(System.in));System.out.println("APPROVE "+joined.getString("id"));commands.readLine();
        byte[]bytes=new byte[Api.CHUNK+73];new Random(230923).nextBytes(bytes);String hash=Api.hash(bytes);String id=UUID.randomUUID().toString().replace("-","");
        JSONObject registered=api.post("/v1/transfers",new JSONObject().put("id",id).put("name","android-api.bin").put("size",bytes.length).put("sha256",hash));check(registered.getLong("offset")==0,"upload registration");
        long offset=api.put(id,0,Arrays.copyOfRange(bytes,0,Api.CHUNK));check(offset==Api.CHUNK,"first chunk acknowledged");
        check(api.get("/v1/transfers/"+id).getLong("offset")==Api.CHUNK,"resume offset fetched from receiver");
        offset=api.put(id,offset,Arrays.copyOfRange(bytes,Api.CHUNK,bytes.length));check(offset==bytes.length,"last chunk acknowledged");
        JSONObject done=api.post("/v1/transfers/"+id+"/finish",new JSONObject());check(done.getString("state").equals("complete")&&hash.equals(done.getString("sha256")),"uploaded file independently SHA-256 verified");
        System.out.println("OFFER "+joined.getString("id"));String outgoing=commands.readLine();
        ByteArrayOutputStream delivered=new ByteArrayOutputStream();long received=0;
        while(received<bytes.length){byte[]chunk=api.chunk(outgoing,received);delivered.write(chunk);received+=chunk.length;api.post("/v1/transfers/"+outgoing+"/ack",new JSONObject().put("offset",received));}
        check(Arrays.equals(bytes,delivered.toByteArray()),"direct reverse transfer byte-identical across multiple TLS chunks");
        api.post("/v1/transfers/"+outgoing+"/ack",new JSONObject().put("offset",received).put("sha256",Api.hash(delivered.toByteArray())));
        check(api.get("/v1/transfers/"+outgoing).getString("state").equals("complete"),"verified receiver acknowledgement completes reverse transfer");
        reject(()->api.chunk("f".repeat(32),0),"tampered chunk hash rejected before delivery");
        api.post("/v1/end",new JSONObject());reject(()->api.json("/v1/transfers","GET",null),"session revocation enforced");
        System.out.println("ANDROID_CHECKS_PASSED "+passed);
    }
}
