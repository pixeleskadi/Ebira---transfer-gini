$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force -Path data | Out-Null
if (!(Test-Path data/key.pem) -and !(Test-Path data/cert.pfx)) {
  # .NET Framework 4.7.2+ supports in-memory certificates in Windows PowerShell.
  $localPassword = [Guid]::NewGuid().ToString('N')
  $rsa = [System.Security.Cryptography.RSACng]::new(3072)
  $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Ebira local transfer', $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
  $localCert = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddYears(1))
  try {
    [IO.File]::WriteAllBytes((Join-Path $PWD 'data/cert.cer'), $localCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    [IO.File]::WriteAllText((Join-Path $PWD 'data/pfx-password.txt'), $localPassword)
    [IO.File]::WriteAllBytes((Join-Path $PWD 'data/cert.pfx'), $localCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $localPassword))
  } finally {
    $localCert.Dispose()
    $rsa.Dispose()
  }
}
$nodeCommand = Get-Command node.exe -ErrorAction SilentlyContinue
$nodePath = if ($nodeCommand) { $nodeCommand.Source } else { Join-Path $env:ProgramFiles 'nodejs\node.exe' }
if (!(Test-Path $nodePath)) {
  throw 'Node.js is required. Install Node.js 20 or newer, then run Start-Ebira.cmd again.'
}
& $nodePath server.js
