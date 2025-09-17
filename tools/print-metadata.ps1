param (
    [Parameter(Mandatory = $true)]
    [string]$InputFile
)

function Parse-FileInfos {
    param (
        [byte[]]$Data
    )

    $results = @()
    $index = 0

    while ($index -lt $Data.Length) {
        $nameLength = $Data[$index]
        if ($nameLength -le 0 -or ($index + 1 + $nameLength + 32 + 8) -gt $Data.Length) {
            break
        }

        $entry = New-Object PSObject

        $nameBytes = $Data[($index + 1)..($index + $nameLength)]
        $fileName = [System.Text.Encoding]::UTF8.GetString($nameBytes)

        $hashStart = $index + 1 + $nameLength
        $hash = $Data[$hashStart..($hashStart + 31)]

        $timestampStart = $hashStart + 32
        $timestampBytes = $Data[$timestampStart..($timestampStart + 7)]
        $timestamp = [BitConverter]::ToInt64($timestampBytes, 0)

        $entry | Add-Member NoteProperty FileName   $fileName
        $entry | Add-Member NoteProperty SHA256Hash (($hash | ForEach-Object ToString X2) -join "")
        $entry | Add-Member NoteProperty Timestamp  $timestamp
        $entry | Add-Member NoteProperty DateTime   ([DateTimeOffset]::FromUnixTimeSeconds($timestamp).UtcDateTime)

        $results += $entry

        $index += (1 + $nameLength + 32 + 8)
    }

    return $results
}

if (-not (Test-Path $InputFile)) {
    throw "File '$InputFile' does not exist."
}

$data = [System.IO.File]::ReadAllBytes($InputFile)
$infos = Parse-FileInfos -Data $data

if ($infos.Count -eq 0) {
    Write-Host "No valid FileInfo entries found."
} else {
    $infos | Format-Table FileName, SHA256Hash, Timestamp, DateTime -AutoSize
}
