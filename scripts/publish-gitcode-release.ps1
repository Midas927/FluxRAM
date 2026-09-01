param(
    [string]$Tag,
    [string]$Name,
    [string]$NotesPath = "docs\releases",
    [string]$AssetsPath = "dist\release-assets"
)

$ErrorActionPreference = "Stop"
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\FluxRAM.App\FluxRAM.App.csproj"
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Unable to read the FluxRAM version."
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = "v" + $version
}

if ([string]::IsNullOrWhiteSpace($Name)) {
    $Name = "FluxRAM " + $Tag
}

if ($Tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$' -or $Tag -ne ("v" + $version)) {
    throw "Release tag $Tag does not match project version $version."
}

if (Test-Path -LiteralPath (Join-Path $repoRoot $NotesPath) -PathType Container) {
    $NotesPath = Join-Path $NotesPath ($Tag + ".md")
}

$notesFile = (Resolve-Path -LiteralPath (Join-Path $repoRoot $NotesPath)).Path
$assetRoot = (Resolve-Path -LiteralPath (Join-Path $repoRoot $AssetsPath)).Path
$assetNames = @(
    "FluxRAM-Lite-Windows-x64.zip",
    "FluxRAM-Lite-Windows-x64.zip.sha256",
    "FluxRAM-Portable-Windows-x64.zip",
    "FluxRAM-Portable-Windows-x64.zip.sha256"
)
$assetFiles = foreach ($assetName in $assetNames) {
    $path = Join-Path $assetRoot $assetName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release asset is missing: $assetName"
    }

    Get-Item -LiteralPath $path
}

foreach ($zipName in @("FluxRAM-Lite-Windows-x64.zip", "FluxRAM-Portable-Windows-x64.zip")) {
    $zipPath = Join-Path $assetRoot $zipName
    $expectedHash = ((Get-Content -LiteralPath ($zipPath + ".sha256") -Raw).Trim() -split '\s+')[0]
    $actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    if (-not [string]::Equals($expectedHash, $actualHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA256 verification failed for $zipName."
    }
}

function Get-GitCodeToken {
    $gitPath = (Get-Command git -ErrorAction Stop).Source
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $gitPath
    $startInfo.Arguments = "credential fill"
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables["GCM_INTERACTIVE"] = "Never"

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.StandardInput.WriteLine("protocol=https")
    $process.StandardInput.WriteLine("host=gitcode.com")
    $process.StandardInput.WriteLine("username=Midas927")
    $process.StandardInput.WriteLine("")
    $process.StandardInput.Close()
    $output = $process.StandardOutput.ReadToEnd()
    $null = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -eq 0) {
        foreach ($line in ($output -split "`r?`n")) {
            if ($line.StartsWith("password=", [System.StringComparison]::Ordinal)) {
                return $line.Substring(9)
            }
        }
    }

    if (-not ("GitCodeCredentialReader" -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class GitCodeCredentialReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public static string ReadGenericSecret(string target)
    {
        IntPtr credentialPtr;
        if (!CredRead(target, 1, 0, out credentialPtr))
        {
            return null;
        }

        try
        {
            Credential credential = (Credential)Marshal.PtrToStructure(credentialPtr, typeof(Credential));
            return credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0
                ? null
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }
}
"@
    }

    $storedToken = [GitCodeCredentialReader]::ReadGenericSecret("git:https://gitcode.com")
    if (-not [string]::IsNullOrWhiteSpace($storedToken)) {
        return $storedToken
    }

    throw "The stored GitCode credential does not contain a PAT."
}

function New-GitCodeHeaders([string]$Token) {
    return @{
        "private-token" = $Token
        "Accept" = "application/json"
    }
}

$token = Get-GitCodeToken
$headers = New-GitCodeHeaders $token
$apiBase = "https://api.gitcode.com/api/v5/repos/Midas927/FluxRAM"
$release = $null

try {
    $user = Invoke-RestMethod -Method Get -Uri "https://api.gitcode.com/api/v5/user" -Headers $headers
    if (-not [string]::Equals([string]$user.login, "Midas927", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The stored GitCode PAT belongs to an unexpected account."
    }

    $releases = @(Invoke-RestMethod -Method Get -Uri ($apiBase + "/releases?per_page=100") -Headers $headers)
    $release = $releases | Where-Object { $_.tag_name -eq $Tag } | Select-Object -First 1
    if ($null -eq $release) {
        Add-Type -AssemblyName System.Net.Http
        $client = New-Object System.Net.Http.HttpClient
        try {
            $client.DefaultRequestHeaders.Add("private-token", $token)
            $client.DefaultRequestHeaders.Add("Accept", "application/json")
            $formValues = New-Object 'System.Collections.Generic.Dictionary[string,string]'
            $formValues.Add("tag_name", $Tag)
            $formValues.Add("name", $Name)
            $formValues.Add("body", (Get-Content -LiteralPath $notesFile -Raw))
            $formValues.Add("release_status", "latest")
            $content = [System.Net.Http.FormUrlEncodedContent]::new($formValues)
            $releaseUri = $apiBase + "/releases"
            $response = $client.PostAsync($releaseUri, $content).GetAwaiter().GetResult()
            $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $response.IsSuccessStatusCode) {
                throw "GitCode Release creation failed with HTTP $([int]$response.StatusCode): $responseBody"
            }
            $release = $responseBody | ConvertFrom-Json
        }
        finally {
            if ($null -ne $content) {
                $content.Dispose()
            }
            $releaseUri = $null
            $client.Dispose()
        }
        Write-Host ("Created GitCode Release " + $Tag + ".") -ForegroundColor Green
    } else {
        Write-Host ("GitCode Release " + $Tag + " already exists; missing assets will be uploaded.") -ForegroundColor Yellow
    }

    $existingAssetNames = @($release.assets | ForEach-Object { $_.name })
    foreach ($assetFile in $assetFiles) {
        if ($existingAssetNames -contains $assetFile.Name) {
            Write-Host ("Asset already exists: " + $assetFile.Name)
            continue
        }

        $fileName = [System.Uri]::EscapeDataString($assetFile.Name)
        $uploadInfo = Invoke-RestMethod `
            -Method Get `
            -Uri ($apiBase + "/releases/" + $Tag + "/upload_url?file_name=" + $fileName) `
            -Headers $headers
        if ([string]::IsNullOrWhiteSpace([string]$uploadInfo.url)) {
            throw "GitCode did not return an upload URL for $($assetFile.Name)."
        }

        $uploadHeaders = @{}
        foreach ($property in $uploadInfo.headers.PSObject.Properties) {
            if ($property.Name -ne "Content-Type") {
                $uploadHeaders[$property.Name] = [string]$property.Value
            }
        }
        $contentType = [string]$uploadInfo.headers.'Content-Type'
        try {
            $response = Invoke-WebRequest `
                -Method Put `
                -Uri $uploadInfo.url `
                -Headers $uploadHeaders `
                -ContentType $contentType `
                -InFile $assetFile.FullName `
                -UseBasicParsing
        }
        catch {
            throw "GitCode asset upload request failed for $($assetFile.Name)."
        }
        if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
            throw "GitCode asset upload failed for $($assetFile.Name)."
        }

        Write-Host ("Uploaded " + $assetFile.Name + ".") -ForegroundColor Green
    }

    $verified = Invoke-RestMethod -Method Get -Uri ($apiBase + "/releases/tags/" + $Tag) -Headers $headers
    $verifiedAssetNames = @($verified.assets | ForEach-Object { $_.name })
    $missingAssets = @($assetNames | Where-Object { $_ -notin $verifiedAssetNames })
    if ($missingAssets.Count -gt 0) {
        throw "GitCode Release is missing assets: $($missingAssets -join ', ')"
    }

    Write-Host ("GitCode Release verified: https://gitcode.com/Midas927/FluxRAM/releases/tag/" + $Tag) -ForegroundColor Green
}
finally {
    if ($null -ne $headers) {
        $headers.Clear()
    }
    $token = $null
    Remove-Variable token -ErrorAction SilentlyContinue
}
