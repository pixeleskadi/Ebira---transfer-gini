'use strict';
const https = require('node:https');
const fs = require('node:fs');
const fsp = fs.promises;
const path = require('node:path');
const os = require('node:os');
const {randomBytes,createHash,X509Certificate} = require('node:crypto');
const {pipeline} = require('node:stream/promises');
const {Transform} = require('node:stream');
const ROOT=__dirname, DATA=path.join(ROOT,'data');
const MAX_FILE=10*1024**3, CHUNK=4*1024**2;
function category(name){const ext=path.extname(name).toLowerCase();return /\.(jpg|jpeg|png|webp|gif|heic)$/.test(ext)?'Pictures':/\.(mp4|mov|mkv|webm|avi)$/.test(ext)?'Videos':/\.(mp3|wav|aac|flac|ogg)$/.test(ext)?'Music':/\.(pdf|docx?|xlsx?|pptx?|txt|csv|odt)$/.test(ext)?'Documents':'Other';}
function safeName(name){return String(name).replace(/[<>:"/\\|?*\x00-\x1f]/g,'_').replace(/[. ]+$/,'').slice(0,180)||'file';}
function createApp(options={}){
 const admin=randomBytes(32).toString('hex'), code=randomBytes(6).toString('hex'), sessions=new Map(), files=new Map();
 const base=options.base||DATA; fs.mkdirSync(base,{recursive:true});
 const receiveRoot=options.receiveRoot||path.join(os.homedir(),'Downloads','Ebira');
 const json=(res,status,value)=>{res.writeHead(status,{'Content-Type':'application/json','Cache-Control':'no-store'});res.end(JSON.stringify(value));};
 async function body(req){let chunks=[],n=0;for await(const c of req){n+=c.length;if(n>65536)throw Error('Request too large');chunks.push(c);}return JSON.parse(Buffer.concat(chunks).toString()||'{}');}
 function tokenOf(req){return (req.headers.authorization||'').replace(/^Bearer /,'')||(req.headers.cookie||'').match(/(?:^|;\s*)ebira=([a-f0-9]+)/)?.[1]||'';}
 function auth(req){const token=tokenOf(req);if(token===admin)return {host:true};const session=sessions.get(token);if(!session||session.state!=='approved')throw Error('Session is not approved');return {host:false,session,token};}
 function access(a,file){return file&&(a.host||file.session===a.token);}
 const handler=async(req,res)=>{try{
  const url=new URL(req.url,'https://localhost'), p=url.pathname;
  res.setHeader('X-Content-Type-Options','nosniff');
  res.setHeader('Referrer-Policy','no-referrer');
  res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'");
  if(req.method==='GET'&&['/','/app.js','/style.css'].includes(p)){const file=path.join(ROOT,'public',p==='/'?'index.html':p.slice(1));res.setHeader('Content-Type',p.endsWith('.js')?'text/javascript':p.endsWith('.css')?'text/css':'text/html');res.setHeader('Cache-Control','no-store');return fs.createReadStream(file).pipe(res);}
  if(p==='/api/join'&&req.method==='POST'){const b=await body(req);if(b.code!==code)return json(res,403,{error:'Incorrect pairing code'});if(sessions.size>=20)return json(res,429,{error:'Session limit reached; restart Ebira'});const token=randomBytes(32).toString('hex');sessions.set(token,{name:safeName(b.name||'Android device'),state:'pending',created:Date.now()});return json(res,200,{token});}
  if(p==='/api/status'){const token=tokenOf(req);return json(res,200,{state:sessions.get(token)?.state||'ended'});}
  const a=auth(req);
  res.setHeader('Set-Cookie',`ebira=${tokenOf(req)}; HttpOnly; Secure; SameSite=Strict; Path=/api`);
  if(p==='/api/sessions'&&a.host)return json(res,200,[...sessions].map(([token,s])=>({token,...s})));
  if(p==='/api/session'&&req.method==='POST'){const b=await body(req), token=a.host?b.token:a.token,s=sessions.get(token);if(!s)throw Error('Unknown session');if(!a.host&&b.action!=='end')throw Error('Not allowed');s.state=b.action==='approve'?'approved':'ended';return json(res,200,{state:s.state});}
  if(p==='/api/files'&&req.method==='GET')return json(res,200,[...files.values()].filter(f=>access(a,f)).map(({disk,busy,...f})=>f));
  if(p==='/api/files'&&req.method==='POST'){const b=await body(req),token=a.host?b.session:a.token;if(sessions.get(token)?.state!=='approved')throw Error('Choose an approved session');if(!Number.isSafeInteger(b.size)||b.size<0||b.size>MAX_FILE)throw Error('Files must be at most 10 GiB');if(files.size>=10000)throw Error('File limit reached');const id=randomBytes(16).toString('hex'),f={id,session:token,name:safeName(b.name),size:b.size,offset:0,direction:a.host?'to-device':'to-windows',complete:false,disk:path.join(base,id+'.part')};await fsp.writeFile(f.disk,'',{flag:'wx'});files.set(id,f);return json(res,200,{id,offset:0});}
  const match=p.match(/^\/api\/files\/([a-f0-9]{32})(?:\/(chunk|finish|download))?$/);
  if(match){const f=files.get(match[1]);if(!access(a,f))return json(res,404,{error:'File not found'});if(sessions.get(f.session)?.state!=='approved')throw Error('Session ended');const action=match[2];
   if(!action&&req.method==='GET')return json(res,200,{id:f.id,offset:f.offset,complete:f.complete});
   if(action==='chunk'&&req.method==='PUT'){
    if(f.complete||f.busy)throw Error('File unavailable');if((f.direction==='to-device')!==a.host)throw Error('Only sender may upload');if(Number(url.searchParams.get('offset'))!==f.offset)return json(res,409,{error:'Offset mismatch',offset:f.offset});
    const n=Number(req.headers['content-length']);if(!Number.isSafeInteger(n)||n<=0||n>CHUNK||f.offset+n>f.size)throw Error('Invalid chunk size');
    f.busy=true;const original=f.offset;try{let count=0;const limiter=new Transform({transform(c,e,cb){count+=c.length;cb(count>n?Error('Chunk too large'):null,c);}});await pipeline(req,limiter,fs.createWriteStream(f.disk,{flags:'r+',start:original}));if(count!==n)throw Error('Incomplete chunk');f.offset+=count;return json(res,200,{offset:f.offset});}catch(e){await fsp.truncate(f.disk,original);throw e;}finally{f.busy=false;}
   }
   if(action==='finish'&&req.method==='POST'){
    if((f.direction==='to-device')!==a.host)throw Error('Only sender may finish');if(f.busy||f.offset!==f.size)throw Error('Upload incomplete');if(f.complete)return json(res,200,{sha256:f.sha256});f.busy=true;
    try{const hash=createHash('sha256');for await(const c of fs.createReadStream(f.disk))hash.update(c);f.sha256=hash.digest('hex');
    if(f.direction==='to-windows'){const folder=path.join(receiveRoot,category(f.name));await fsp.mkdir(folder,{recursive:true});const ext=path.extname(f.name),stem=path.basename(f.name,ext);let target=path.join(folder,f.name),i=0;for(;;){try{await fsp.copyFile(f.disk,target,fs.constants.COPYFILE_EXCL);break;}catch(e){if(e.code!=='EEXIST')throw e;target=path.join(folder,`${stem} (${++i})${ext}`);}}await fsp.unlink(f.disk);f.disk=target;}
    f.complete=true;return json(res,200,{sha256:f.sha256});}finally{f.busy=false;}
   }
   if(action==='download'&&req.method==='GET'&&f.complete){if(!a.host&&f.direction!=='to-device')throw Error('Not an incoming file');res.writeHead(200,{'Content-Type':'application/octet-stream','Content-Length':f.size,'Content-Disposition':`attachment; filename*=UTF-8''${encodeURIComponent(f.name)}`});return fs.createReadStream(f.disk).pipe(res);}
  }
  json(res,404,{error:'Not found'});
 }catch(e){if(!res.headersSent)json(res,400,{error:e.message});else res.destroy();}};
 return {handler,admin,code,sessions,files};
}
function credentials(){return fs.existsSync(path.join(DATA,'key.pem'))?{cert:fs.readFileSync(path.join(DATA,'cert.pem')),key:fs.readFileSync(path.join(DATA,'key.pem'))}:{pfx:fs.readFileSync(path.join(DATA,'cert.pfx')),passphrase:fs.readFileSync(path.join(DATA,'pfx-password.txt'),'utf8')};}
async function listenAvailable(server,preferred=8443){
 if(!Number.isInteger(preferred)||preferred<0||preferred>65535)throw Error('EBIRA_PORT must be a number from 0 to 65535');
 function attempt(port){return new Promise((resolve,reject)=>{function failed(e){server.removeListener('listening',ready);reject(e);}function ready(){server.removeListener('error',failed);resolve(server.address().port);}server.once('error',failed);server.once('listening',ready);server.listen(port,'0.0.0.0');});}
 try{return await attempt(preferred);}catch(e){if(e.code!=='EADDRINUSE'||preferred===0)throw e;return attempt(0);}
}
if(require.main===module){
 const app=createApp(),tls=credentials(),cert=tls.cert||fs.readFileSync(path.join(DATA,'cert.cer'));
 const preferred=Number(process.env.EBIRA_PORT||8443);const server=https.createServer({...tls,minVersion:'TLSv1.2'},app.handler);server.requestTimeout=120000;
 listenAvailable(server,preferred).then(port=>{if(port!==preferred)console.log(`Port ${preferred} is busy. Ebira selected available port ${port}.`);console.log('\nEbira PoC — keep this window open.\n');console.log(`Windows control: https://localhost:${port}/#host=${app.admin}`);for(const list of Object.values(os.networkInterfaces()))for(const n of list||[])if(n.family==='IPv4'&&!n.internal)console.log(`Phone/tablet: https://${n.address}:${port}/`);console.log(`Pairing code: ${app.code}\nCertificate SHA-256: ${new X509Certificate(cert).fingerprint256}\nReceived files: ${path.join(os.homedir(),'Downloads','Ebira')}\n`);}).catch(e=>{console.error('Ebira could not start: '+e.message);process.exitCode=1;});
}
module.exports={createApp,category,safeName,credentials,listenAvailable};
