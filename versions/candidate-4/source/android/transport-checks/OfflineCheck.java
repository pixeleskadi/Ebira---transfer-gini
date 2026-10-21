package app.ebira;

import java.net.*;
import org.json.*;

/** Pure protocol/routing checks. Does not simulate Android hotspot hardware. */
public final class OfflineCheck {
    static int passed;
    static void check(boolean ok,String label){if(!ok)throw new AssertionError(label);passed++;System.out.println("PASS offline: "+label);}
    public static void main(String[] args)throws Exception{
        String nonce="a".repeat(32);InetAddress source=InetAddress.getByName("192.168.42.6");
        JSONObject reply=new JSONObject().put("magic","ebira-host-v1").put("nonce",nonce).put("name","Test laptop").put("host","https://192.168.42.6:8443").put("pin","b".repeat(64)).put("code","0123456789AB");
        check(LanDiscovery.validReply(reply,nonce,source),"valid local host advertisement");
        check(!LanDiscovery.validReply(reply,"c".repeat(32),source),"unsolicited reply rejected");
        check(!LanDiscovery.validReply(reply,nonce,InetAddress.getByName("192.168.42.7")),"advertised address must match packet source");
        check(!LanDiscovery.validReply(reply,nonce,InetAddress.getByName("8.8.8.8")),"public response rejected");
        for(String host:new String[]{"http://192.168.42.6:8443","https://192.168.42.6:8443/path","https://evil@192.168.42.6:8443","https://192.168.42.6:8443?x=y","https://example.com:8443","https://192.168.42.6"}){JSONObject bad=new JSONObject(reply.toString()).put("host",host);check(!LanDiscovery.validReply(bad,nonce,source),"invalid discovery origin rejected: "+host);}
        check(!LanDiscovery.validReply(new JSONObject(reply.toString()).put("pin","bad"),nonce,source),"invalid certificate pin rejected");
        LanDiscovery.Host host=new LanDiscovery.Host(reply);check(host.pin.equals("b".repeat(64))&&host.certificateEnd().equals("B".repeat(12)),"discovery preserves exact pin and certificate ending");
        byte[] a=source.getAddress();
        check(LocalRouting.sameSubnet(a,InetAddress.getByName("192.168.42.1").getAddress(),24),"matching hotspot subnet");
        check(!LocalRouting.sameSubnet(a,InetAddress.getByName("192.168.43.1").getAddress(),24),"unrelated station Wi-Fi excluded");
        check(LocalRouting.sameSubnet(a,InetAddress.getByName("192.168.43.1").getAddress(),16),"non-/24 subnet supported");
        check(LocalRouting.sameSubnet(a,a,32),"host route supported");
        check(!LocalRouting.sameSubnet(a,a,0)&&!LocalRouting.sameSubnet(a,a,33),"default and invalid prefixes excluded");
        check(!LocalRouting.sameSubnet(a,InetAddress.getByName("::1").getAddress(),24),"IPv6 excluded from IPv4 selector");
        System.out.println("OFFLINE_CHECKS_PASSED "+passed);
    }
}
