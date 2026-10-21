import https from 'node:https';
import {createHash} from 'node:crypto';
let input='';for await(const b of process.stdin)input+=b;
const p=JSON.parse(input);
await new Promise((resolve,reject)=>{
 const req=https.request(p.url,{method:p.method,headers:p.headers,rejectUnauthorized:false},res=>{
  const chunks=[];res.on('data',b=>chunks.push(b));res.on('error',reject);res.on('end',()=>{process.stdout.write(JSON.stringify({status:res.statusCode,headers:res.headers,body:Buffer.concat(chunks).toString('base64')}));resolve();});
 });
 req.on('socket',socket=>socket.on('secureConnect',()=>{if(createHash('sha256').update(socket.getPeerCertificate().raw).digest('hex')!==p.pin)req.destroy(Error('Certificate pin mismatch'));}));
 req.setTimeout(10000,()=>req.destroy(Error('TLS probe timeout')));req.on('error',reject);req.end(Buffer.from(p.body,'base64'));
}).catch(e=>{process.stderr.write(e.message);process.exitCode=1;});
