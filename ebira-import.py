"""Approved one-time source import; checks every file and uses only fast-forward pushes."""
import hashlib, json, os, subprocess, sys
from pathlib import Path
BASE='6be591a8be92fe54e976332aad8984b24d075559'
BRANCH='feature/organize-native-versions'
ROOT=Path.cwd()
def git(*args,data=None,env=None):return subprocess.check_output(['git',*args],input=data,env=env)
def text(*args,**kw):return git(*args,**kw).decode().strip()
def entries(tree):
    out={}
    for row in git('ls-tree','-r','-z',tree).split(b'\0'):
        if row:
            meta,name=row.split(b'\t',1);mode,kind,sha=meta.decode().split()
            if kind!='blob':raise RuntimeError('Unexpected non-file source')
            out[name.decode()]=(mode,sha)
    return out
def add(mapping,name,content):mapping[name]=('100644',text('hash-object','-w','--stdin',data=content.encode()))
bootstrap=text('rev-parse','HEAD')
env=dict(os.environ,GIT_INDEX_FILE=str(ROOT/'.git'/'snapshot-import-index'))
subprocess.check_call(['git','config','user.name','pixeleskadi'])
subprocess.check_call(['git','config','user.email','226473610+pixeleskadi@users.noreply.github.com'])
snapshots=[];parent=BASE
for i in range(1,5):
    version=f'candidate-{i}'
    manifest=json.loads((ROOT/f'ebira-{version}.json').read_text(encoding='utf-8-sig'))
    patch=(ROOT/f'ebira-{version}.patch').read_bytes()
    if hashlib.sha256(patch).hexdigest()!=manifest['patch_sha256']:raise RuntimeError('Patch checksum mismatch')
    git('read-tree',BASE,env=env)
    git('apply','--cached','--whitespace=nowarn','--check','-',data=patch,env=env)
    git('apply','--cached','--whitespace=nowarn','-',data=patch,env=env)
    tree=text('write-tree',env=env)
    if tree!=manifest['source_tree']:raise RuntimeError('Reconstructed tree mismatch')
    files=entries(tree);expected={f['path']:f['sha256'] for f in manifest['files']}
    if set(files)!=set(expected):raise RuntimeError('Reconstructed file inventory mismatch')
    for name,(_,sha) in files.items():
        if hashlib.sha256(git('cat-file','blob',sha)).hexdigest()!=expected[name]:raise RuntimeError('Source checksum mismatch: '+name)
    message=f'Archive Ebira {version}: {manifest["description"]}\n\nRecovered from {manifest["source_archive"]}\nArchive SHA-256: {manifest["source_archive_sha256"]}\nImported now without invented historical dates. Generated binaries excluded.\n'
    commit=text('commit-tree',tree,'-p',parent,data=message.encode())
    tag=f'snapshots/ebira/{version}'
    manifest.update(git_commit=commit,git_tag=tag,source_tree=tree,remote_verification='All source file hashes verified at import; verify pushed refs separately.')
    snapshots.append(dict(version=version,commit=commit,tree=tree,tag=tag,files=files,manifest=manifest));parent=commit
final=dict(snapshots[-1]['files'])
for s in snapshots:
    folder=f'versions/{s["version"]}'
    for name,value in s['files'].items():final[f'{folder}/source/{name}']=value
    add(final,f'{folder}/manifest.json',json.dumps(s['manifest'],indent=2)+'\n')
    add(final,f'{folder}/README.md',f'# Ebira {s["version"]}\n\n{s["manifest"]["description"]}\n\nComplete source: `source/`. Snapshot commit: `{s["commit"]}`. Tag: `{s["tag"]}`.\n\nOriginal source/archive/build hashes are in manifest.json. Installers, generated binaries and credentials are excluded. Use installed Gradle 8.9 for Android. This is a development candidate, with physical validation gates still open.\n')
index=[{k:s[k] for k in ('version','commit','tree','tag')}|dict(description=s['manifest']['description'],source=f'versions/{s["version"]}/source') for s in snapshots]
add(final,'versions/index.json',json.dumps(index,indent=2)+'\n')
add(final,'versions/README.md','# Ebira source versions\n\nEach version has its complete source folder and checksum/provenance manifest. Root source is based on candidate 4.\n\n'+'\n'.join(f'- [{s["version"]}]({s["version"]}/README.md): {s["manifest"]["description"]}' for s in snapshots)+'\n\nOriginal PoC history remains in Git. Snapshot commits were imported now without inventing historical dates. The private pixeleskadi/ebira repository retains planning documents; this repository holds the app implementation. Installable builds are local deliverables, outside source control.\n')
build=git('cat-file','blob',final['BUILD.md'][1]).decode().replace('.\\gradlew.bat assembleDebug lintDebug','gradle assembleDebug lintDebug').replace('Gradle 8.9 (or the checked-in wrapper)','installed Gradle 8.9 (generated wrapper JAR is not versioned)')
add(final,'BUILD.md',build)
workflow=git('cat-file','blob',final['.github/workflows/native-v1.yml'][1]).decode().replace('\r\n','\n').replace('      - name: Build and lint native Android app\n        run: bash android/gradlew -p android assembleDebug lintDebug --no-daemon',"      - uses: gradle/actions/setup-gradle@v4\n        with:\n          gradle-version: '8.9'\n      - name: Build and lint native Android app\n        run: gradle -p android assembleDebug lintDebug --no-daemon")
add(final,'.github/workflows/native-v1.yml',workflow)
add(final,'.gitignore',git('cat-file','blob',final['.gitignore'][1]).decode().rstrip()+'\n/android/gradle/wrapper/gradle-wrapper.jar\n')
add(final,'README.md',git('cat-file','blob',final['README.md'][1]).decode()+'\n## Version register\n\n[Four preserved source versions](versions/README.md), with full source, snapshot tags and provenance/checksum manifests. Chip Register links the app implementation with the private Ebira planning repository.\n')
git('read-tree','--empty',env=env)
rows=b''.join(f'{mode} {sha}\t{name}\0'.encode() for name,(mode,sha) in sorted(final.items()))
git('update-index','-z','--index-info',data=rows,env=env)
tree=text('write-tree',env=env)
commit=text('commit-tree',tree,'-p',bootstrap,'-p',parent,data=b'Organize four Ebira source versions and integrate the current native candidate\n\nPreserve original history and snapshot provenance; exclude installers, generated binaries and secrets. The approved one-time import workflow is absent from the final tree.\n')
if '--dry-run' in sys.argv:
    print(json.dumps(dict(dry_run=True,head=commit,tree=tree,snapshots=index),indent=2));sys.exit(0)
if text('ls-remote','origin',f'refs/heads/{BRANCH}').split()[0]!=bootstrap:raise RuntimeError('Remote branch changed; refusing to overwrite it')
refs=[f'{commit}:refs/heads/{BRANCH}']+[f'{s["commit"]}:refs/tags/{s["tag"]}' for s in snapshots]
subprocess.check_call(['git','push','--atomic','origin',*refs])
remote=dict(line.split()[::-1] for line in text('ls-remote','origin',f'refs/heads/{BRANCH}','refs/tags/snapshots/ebira/*').splitlines())
expected={f'refs/heads/{BRANCH}':commit}|{f'refs/tags/{s["tag"]}':s['commit'] for s in snapshots}
if any(remote.get(name)!=value for name,value in expected.items()):raise RuntimeError('Post-push ref verification failed')
print(json.dumps(dict(verified=True,branch=BRANCH,head=commit,snapshots=index),indent=2))
