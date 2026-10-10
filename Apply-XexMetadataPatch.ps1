$ErrorActionPreference = "Stop"

$root = "F:\Projects\XISO-Toolkit"

$modelPath = Join-Path $root "src\XISO.Core\XISO.Core\Models\GameImageInfo.cs"
$analyzerPath = Join-Path $root "src\XISO.OriginalXbox\XISO.OriginalXbox\XboxGameImageAnalyzer.cs"
$programPath = Join-Path $root "src\XISO.Toolkit\XISO.Toolkit\Program.cs"

$changes = @(
    @{
        Path = $modelPath
        Old = @'
    public string TitleId { get; set; } = string.Empty;
    public string MediaId { get; set; } = string.Empty;
'@
        New = @'
    public string TitleId { get; set; } = string.Empty;
    public string MediaId { get; set; } = string.Empty;

    // Xbox 360 XEX Execution ID metadata.
    public uint XexVersion { get; set; }
    public uint XexBaseVersion { get; set; }
    public byte XexPlatform { get; set; }
    public byte XexExecutableType { get; set; }
    public byte XexDiscNumber { get; set; }
    public byte XexDiscCount { get; set; }
    public uint XexSaveGameId { get; set; }
'@
    },
    @{
        Path = $analyzerPath
        Old = @'
        result.TitleId = executionId.TitleId.ToString("X8");
        result.MediaId = executionId.MediaId.ToString("X8");
        result.Version = executionId.Version.ToString();

        var lookup = _titleProvider.Find(
            result.Platform,
            result.TitleId,
            result.MediaId);

        if (lookup is not null)
        {
            result.Title = lookup.Title;
            result.ReleaseRegion = lookup.Region;
        }
'@
        New = @'
        result.TitleId = executionId.TitleId.ToString("X8");
        result.MediaId = executionId.MediaId.ToString("X8");

        result.XexVersion = executionId.Version;
        result.XexBaseVersion = executionId.BaseVersion;
        result.XexPlatform = executionId.Platform;
        result.XexExecutableType = executionId.ExecutableType;
        result.XexDiscNumber = executionId.DiscNumber;
        result.XexDiscCount = executionId.DiscCount;
        result.XexSaveGameId = executionId.SaveGameId;

        // Resolve the game title independently of the exact release.
        var titleLookup = _titleProvider.FindByTitleId(
            result.Platform,
            result.TitleId);

        if (titleLookup is not null)
        {
            result.Title = titleLookup.Title;
        }

        // The region requires a matching Title ID and Media ID.
        var releaseLookup = _titleProvider.Find(
            result.Platform,
            result.TitleId,
            result.MediaId);

        if (releaseLookup is not null)
        {
            result.ReleaseRegion = releaseLookup.Region;
        }
'@
    },
    @{
        Path = $programPath
        Old = @'
        if (game.Platform == XboxPlatform.Xbox360)
        {
            Console.WriteLine($"Media ID: {game.MediaId}");
            Console.WriteLine($"XBE Title Version: {game.Version}");
        }
'@
        New = @'
        if (game.Platform == XboxPlatform.Xbox360)
        {
            Console.WriteLine($"Media ID: {game.MediaId}");
            Console.WriteLine($"XEX Execution Version: 0x{game.XexVersion:X8}");
            Console.WriteLine($"XEX Base Version: 0x{game.XexBaseVersion:X8}");
            Console.WriteLine($"XEX Platform: 0x{game.XexPlatform:X2}");
            Console.WriteLine($"XEX Executable Type: 0x{game.XexExecutableType:X2}");

            if (game.XexDiscCount > 0)
            {
                Console.WriteLine(
                    $"XEX Disc: {game.XexDiscNumber} of {game.XexDiscCount}");
            }
            else
            {
                Console.WriteLine($"XEX Disc Number: {game.XexDiscNumber}");
            }

            Console.WriteLine($"XEX Save Game ID: 0x{game.XexSaveGameId:X8}");

            if (!string.IsNullOrWhiteSpace(game.ReleaseRegion))
            {
                Console.WriteLine($"Release Region: {game.ReleaseRegion}");
            }
        }
'@
    }
)

# Validate every file and expected source block before changing anything.
foreach ($change in $changes) {
    if (-not (Test-Path -LiteralPath $change.Path)) {
        throw "File not found: $($change.Path)"
    }

    $content = [System.IO.File]::ReadAllText($change.Path)

    if (-not $content.Contains($change.Old)) {
        throw "Expected source block was not found. No files have been changed by the edit phase: $($change.Path)"
    }
}

# Back up all affected files.
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

foreach ($change in $changes) {
    $backupPath = "$($change.Path).$timestamp.bak"
    Copy-Item -LiteralPath $change.Path -Destination $backupPath
    Write-Host "Backup: $backupPath" -ForegroundColor DarkGray
}

# Apply the verified replacements.
foreach ($change in $changes) {
    $content = [System.IO.File]::ReadAllText($change.Path)
    $updated = $content.Replace($change.Old, $change.New)

    [System.IO.File]::WriteAllText(
        $change.Path,
        $updated,
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "Updated: $($change.Path)" -ForegroundColor Green
}

Write-Host "`nPatch applied. Next, build the solution." -ForegroundColor Cyan
