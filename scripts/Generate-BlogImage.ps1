#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates a blog post featured image using DeepAI API.

.DESCRIPTION
    This script calls DeepAI's text-to-image API to generate a featured image
    for a blog post based on the post content and title. The image is saved
    to the posts/images/ directory with the specified filename.

.PARAMETER PostTitle
    The title of the blog post.

.PARAMETER PostContent
    A summary or key themes from the blog post content.

.PARAMETER OutputFileName
    The filename for the generated image (e.g., "my-post-slug.png").

.PARAMETER ApiKey
    DeepAI API key. If not provided, will attempt to read from
    DEEPAI_API_KEY environment variable.

.EXAMPLE
    ./Generate-BlogImage.ps1 -PostTitle "Understanding SOLID Principles" -PostContent "Software design principles for maintainable code" -OutputFileName "solid-principles.png"

.NOTES
    Requires: DeepAI API key with Pro account (paid subscription required)
    Output: PNG image in posts/images/ directory
    Style: Pseudo realistic cell-shaded with focus and blur effects
    API: https://deepai.org/docs
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PostTitle,

    [Parameter(Mandatory = $true)]
    [string]$PostContent,

    [Parameter(Mandatory = $true)]
    [string]$OutputFileName,

    [Parameter(Mandatory = $false)]
    [string]$ApiKey = $env:DEEPAI_API_KEY
)

# Validate API key
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Error "DeepAI API key not provided. Set DEEPAI_API_KEY environment variable or pass -ApiKey parameter."
    exit 1
}

# Validate output filename
if (-not $OutputFileName.EndsWith('.png')) {
    $OutputFileName = "$OutputFileName.png"
}

# Set output path
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptPath
$outputPath = Join-Path $repoRoot "posts/images/$OutputFileName"

# Ensure output directory exists
$outputDir = Split-Path -Parent $outputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    Write-Host "Created directory: $outputDir"
}

Write-Host "Generating image for: $PostTitle"
Write-Host "Output file: $outputPath"

# Construct the style prompt
$stylePrompt = "pseudo realistic cell-shaded style with focus and focus blur effects"

# Construct the content prompt based on post title and content
$imagePrompt = @"
Create a tech-oriented featured image for a blog post using everyday scenes or objects.

Post Title: $PostTitle
Post Theme: $PostContent

Style Requirements:
- $stylePrompt
- Scene or object-based imagery: rooms, courtyards, open city/suburban spaces, OR closeup of everyday household/office items
- Examples: red stapler on desk, keyboard with coffee cup, violin on stand, pots in kitchen sink, towel on towel rail, empty office room, urban courtyard
- Modern, tech-oriented color scheme
- Limited color palette (3-5 colors)
- Include at least one element in sharp focus and one element with blur/depth-of-field effect
- No people or animals
- No text or words in the image
- Landscape orientation suitable for a blog header

Technical aesthetic: Clean, modern, minimalist with depth
"@

Write-Host "`nGenerating image with prompt:"
Write-Host "----------------------------------------"
Write-Host $imagePrompt
Write-Host "----------------------------------------`n"

# DeepAI API endpoint for text-to-image generation
# Refer to https://deepai.org/docs for latest API documentation
$apiEndpoint = "https://api.deepai.org/api/text2img"

# Construct request body for DeepAI API
# DeepAI accepts form data with 'text' parameter for the prompt
$boundary = [System.Guid]::NewGuid().ToString()
$bodyLines = @()
$bodyLines += "--$boundary"
$bodyLines += 'Content-Disposition: form-data; name="text"'
$bodyLines += ''
$bodyLines += $imagePrompt
$bodyLines += "--$boundary--"
$bodyLines += ''

$requestBody = $bodyLines -join "`r`n"

try {
    Write-Host "Calling DeepAI API..."
    
    # Make API request
    $headers = @{
        "api-key" = $ApiKey
        "Content-Type" = "multipart/form-data; boundary=$boundary"
    }
    
    $response = Invoke-RestMethod -Uri $apiEndpoint -Method Post -Headers $headers -Body $requestBody -TimeoutSec 120
    
    # Extract image URL from response
    # DeepAI response is: { "id": "...", "output_url": "https://..." }
    if (-not $response.output_url) {
        Write-Error "No image URL received from API. Response: $($response | ConvertTo-Json -Depth 3 -Compress)"
        exit 1
    }
    
    Write-Host "Image generated, downloading from: $($response.output_url)"
    
    # Download the image from the URL
    $imageBytes = Invoke-RestMethod -Uri $response.output_url -Method Get -TimeoutSec 60
    [System.IO.File]::WriteAllBytes($outputPath, $imageBytes)
    
    Write-Host "✓ Image generated successfully!"
    Write-Host "  Saved to: $outputPath"
    Write-Host "  Size: $([Math]::Round($imageBytes.Length / 1KB, 2)) KB"
    
    # Verify the file was created
    if (Test-Path $outputPath) {
        $fileInfo = Get-Item $outputPath
        Write-Host "  File verified: $($fileInfo.Length) bytes"
        
        # Optional: Validate it's a valid PNG
        $pngHeader = $imageBytes[0..7]
        $pngSignature = @(137, 80, 78, 71, 13, 10, 26, 10)
        $isValidPng = $true
        for ($i = 0; $i -lt 8; $i++) {
            if ($pngHeader[$i] -ne $pngSignature[$i]) {
                $isValidPng = $false
                break
            }
        }
        
        if (-not $isValidPng) {
            Write-Warning "Generated file may not be a valid PNG image"
        }
    } else {
        Write-Error "File was not created successfully"
        exit 1
    }
    
} catch {
    Write-Error "Failed to generate image: $_"
    
    # Check for specific error conditions and provide helpful messages
    if ($_.Exception.Response) {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq 402) {
            Write-Host ""
            Write-Host "⚠️  DeepAI API Error: Payment Required" -ForegroundColor Yellow
            Write-Host "   The free tier of DeepAI has limited access to image generation APIs." -ForegroundColor Yellow
            Write-Host "   Please upgrade to a Pro account at: https://deepai.org/dashboard" -ForegroundColor Yellow
            Write-Host ""
        }
    }
    
    Write-Error "Response: $($_.Exception.Response)"
    exit 1
}

Write-Host "`n✓ Image generation complete!"
exit 0
