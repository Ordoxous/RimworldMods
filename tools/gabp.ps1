<#
.SYNOPSIS
Calls a bridge tool on a running RimWorld over GABP, without GABS.

.DESCRIPTION
For the player's own game, which runs RimBridgeServer standalone: GABS can't attach to it. By default it reads the port
and token BridgeTools saves in ~/.gabs/rimworld/standalone.json, and checks that game is still running.

.EXAMPLE
pwsh tools/gabp.ps1 ordo/find_things '{"match":"^Steel$"}'
pwsh tools/gabp.ps1 -List            # every tool's name
pwsh tools/gabp.ps1 ordo/log '{}' -Port 5174 -Token <token from the game's log>
#>
param(
    [string]$Tool,
    [string]$Arguments = '{}',
    [switch]$List,
    [int]$Port = 0,
    [string]$Token = '',
    [int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'

if (-not $Token) {
    $file = Join-Path $HOME '.gabs/rimworld/standalone.json'
    if (-not (Test-Path $file)) { throw "No $file: start the game with BridgeTools deployed, or pass -Port and -Token." }
    $saved = Get-Content $file -Raw | ConvertFrom-Json
    if (-not (Get-Process -Id $saved.pid -ErrorAction SilentlyContinue)) { throw "The game in $file (pid $($saved.pid)) isn't running." }
    $Port = $saved.port
    $Token = $saved.token
}

$client = [System.Net.Sockets.TcpClient]::new('127.0.0.1', $Port)
$client.ReceiveTimeout = $TimeoutSeconds * 1000
$stream = $client.GetStream()
$encoding = [System.Text.UTF8Encoding]::new($false)
$buffer = [System.Collections.Generic.List[byte]]::new()

function Read-More {
    $chunk = New-Object byte[] 65536
    $read = $stream.Read($chunk, 0, $chunk.Length)
    if ($read -le 0) { throw 'The bridge closed the connection.' }
    $buffer.AddRange([byte[]]$chunk[0..($read - 1)])
}

# Messages are framed like LSP: "Content-Length: N" headers, a blank line, then N bytes of JSON
function Receive([string]$id) {
    while ($true) {
        while (($headerEnd = $encoding.GetString($buffer.ToArray()).IndexOf("`r`n`r`n")) -lt 0) { Read-More }
        $header = $encoding.GetString($buffer.ToArray(), 0, $headerEnd)
        $length = [int](($header -split "`r`n" | Where-Object { $_ -match '^content-length' }) -replace '.*:\s*', '')
        $start = $encoding.GetByteCount($header) + 4
        while ($buffer.Count -lt $start + $length) { Read-More }
        $body = $encoding.GetString($buffer.ToArray(), $start, $length)
        $buffer.RemoveRange(0, $start + $length)
        if (($body | ConvertFrom-Json).id -eq $id) { return $body }
    }
}

function Send([string]$method, $params) {
    $id = [guid]::NewGuid().ToString()
    $json = @{ v = 'gabp/1'; id = $id; type = 'request'; method = $method; params = $params } | ConvertTo-Json -Depth 100 -Compress
    $bytes = $encoding.GetBytes($json)
    $header = $encoding.GetBytes("Content-Length: $($bytes.Length)`r`n`r`n")
    $stream.Write($header, 0, $header.Length)
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    return Receive $id
}

try {
    $hello = Send 'session/hello' @{ token = $Token; bridgeVersion = '1.0'; platform = 'windows'; launchId = 'gabp.ps1' } | ConvertFrom-Json
    if ($hello.error) { throw "The bridge refused the token: $($hello.error.message)" }
    if ($List) {
        ((Send 'tools/list' @{}) | ConvertFrom-Json -Depth 100).result.tools.name | Sort-Object
        return
    }
    if (-not $Tool) { throw 'Name a tool, or pass -List.' }
    $response = Send 'tools/call' @{ name = $Tool; arguments = ($Arguments | ConvertFrom-Json -AsHashtable -Depth 100) } | ConvertFrom-Json -Depth 100
    if ($response.error) { throw "$Tool failed: $($response.error.message)" }
    $response.result | ConvertTo-Json -Depth 100
}
finally {
    $client.Close()
}
