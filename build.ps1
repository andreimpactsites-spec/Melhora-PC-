$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

if (-not (Test-Path "bin")) {
    New-Item -ItemType Directory -Path "bin" -Force | Out-Null
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   RAM BOOSTER CORE (OPEN SOURCE) - COMPILADOR C# 64-BIT   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Compilando código-fonte nativo..." -ForegroundColor Yellow

$sources = @(
    "src\NativeMethods.cs",
    "src\MemoryMetrics.cs",
    "src\MemoryEngine.cs",
    "src\AISentinel.cs",
    "src\Program.cs"
)

$references = "System.dll,System.Core.dll"
$outFile = "bin\rambooster.exe"

$iconArg = @()
if (Test-Path "assets\app.ico") {
    $iconArg = @("/win32icon:assets\app.ico")
}

& $csc /nologo /target:exe /platform:x64 /optimize+ /win32manifest:src\app.manifest @iconArg /out:$outFile /r:$references $sources

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[SUCESSO] Compilado com êxito!" -ForegroundColor Green
    Write-Host "Executável CLI gerado em: $outFile" -ForegroundColor Cyan
    Write-Host "Tamanho: $((Get-Item $outFile).Length / 1KB) KB" -ForegroundColor Gray
} else {
    Write-Host "`n[ERRO] Falha na compilação. Código: $LASTEXITCODE" -ForegroundColor Red
}
