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
        if ($nameLength -le 0 -or ($index + 1 + $nameLength + 20 + 8) -gt $Data.Length) {
            break
        }

        $entry = New-Object PSObject

        $nameBytes = $Data[($index + 1)..($index + $nameLength)]
        $fileName = [System.Text.Encoding]::UTF8.GetString($nameBytes)

        $hashStart = $index + 1 + $nameLength
        $hash = $Data[$hashStart..($hashStart + 19)]

        $timestampStart = $hashStart + 20
        $timestampBytes = $Data[$timestampStart..($timestampStart + 7)]
        $timestamp = [BitConverter]::ToInt64($timestampBytes, 0)

        $entry | Add-Member NoteProperty FileName   $fileName
        $entry | Add-Member NoteProperty SHA1Hash (($hash | ForEach-Object ToString X2) -join "")
        $entry | Add-Member NoteProperty Timestamp  $timestamp
        $entry | Add-Member NoteProperty DateTime   ([DateTimeOffset]::FromUnixTimeSeconds($timestamp).UtcDateTime)

        $flagsIndex = $timestampStart + 8
        if ($flagsIndex -lt $Data.Length) {
            $flags = $Data[$flagsIndex]
            $hasMetadata = ($flags -band 0x01) -ne 0
            $entry | Add-Member NoteProperty MessageMetadata $hasMetadata

            if ($hasMetadata) {
                $metadataStart = $flagsIndex + 1
                if ($metadataStart + 24 -le $Data.Length) {
                    $serverIDBytes = $Data[$metadataStart..($metadataStart + 7)]
                    $serverID = [BitConverter]::ToInt64($serverIDBytes, 0)

                    $channelIDBytes = $Data[($metadataStart + 8)..($metadataStart + 15)]
                    $channelID = [BitConverter]::ToInt64($channelIDBytes, 0)

                    $messageIDBytes = $Data[($metadataStart + 16)..($metadataStart + 23)]
                    $messageID = [BitConverter]::ToInt64($messageIDBytes, 0)

                    $entry | Add-Member NoteProperty ServerID $serverID
                    $entry | Add-Member NoteProperty ChannelID $channelID
                    $entry | Add-Member NoteProperty MessageID $messageID
                } else {
                    $entry | Add-Member NoteProperty ServerID 0
                    $entry | Add-Member NoteProperty ChannelID 0
                    $entry | Add-Member NoteProperty MessageID 0
                }
            } else {
                $entry | Add-Member NoteProperty ServerID 0
                $entry | Add-Member NoteProperty ChannelID 0
                $entry | Add-Member NoteProperty MessageID 0
            }
        } else {
            $entry | Add-Member NoteProperty MessageMetadata $false
            $entry | Add-Member NoteProperty ServerID 0
            $entry | Add-Member NoteProperty ChannelID 0
            $entry | Add-Member NoteProperty MessageID 0
        }

        $results += $entry

        $entrySize = 1 + $nameLength + 20 + 8
        if ($flagsIndex -lt $Data.Length) {
            $entrySize += 1
            if (($Data[$flagsIndex] -band 0x01) -ne 0) {
                $entrySize += 24
            }
        }

        $index += $entrySize
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
    $infos | Format-Table FileName, SHA1Hash, Timestamp, DateTime, MessageMetadata, ServerID, ChannelID, MessageID -AutoSize
}
