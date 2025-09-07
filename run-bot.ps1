
$port = 5159
$projectName = "TerminBot"
$cloudflaredPath = "C:\cloudflared\cloudflared.exe"
$log = Join-Path $PSScriptRoot "cf-log.txt"


Start-Process "dotnet" -ArgumentList "run --project `"$projectName`" --urls=http://localhost:$port" -WorkingDirectory $PSScriptRoot -WindowStyle Minimized


Start-Sleep -Seconds 8


if (Test-Path $log) { Remove-Item $log -Force -ErrorAction SilentlyContinue }
Start-Process -FilePath $cloudflaredPath -ArgumentList "tunnel --url http://localhost:$port --logfile `"$log`" --loglevel info" -NoNewWindow


$pattern = 'https://[^\s"]+?\.trycloudflare\.com'
$url = $null
for ($i=0; $i -lt 60 -and -not $url; $i++) {
    if (Test-Path $log) {
        $hit = Select-String -Path $log -Pattern $pattern -ErrorAction SilentlyContinue | Select-Object -Last 1
        if ($hit -and $hit.Matches.Count -gt 0) { $url = ($hit.Matches[0].Value).Trim() }
    }
    Start-Sleep -Milliseconds 500
}


if ($url) {
    if ($url.EndsWith('/')) { $url = $url.Substring(0, $url.Length-1) }
    $final = "$url/chat"
    Start-Sleep -Seconds 9
    Start-Process "explorer.exe" $final
} else {
    Start-Process "explorer.exe" ("http://localhost:{0}/chat" -f $port)
}
