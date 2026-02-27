#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates a blog post featured image using Google's Gemini API.

.DESCRIPTION
    This script calls Google's Gemini API (Imagen) to generate a featured image
    for a blog post based on the post content and title. The image is saved
    to the posts/images/ directory with the specified filename.

.PARAMETER PostTitle
    The title of the blog post.

.PARAMETER PostContent
    A summary or key themes from the blog post content.

.PARAMETER OutputFileName
    The filename for the generated image (e.g., "my-post-slug.png").

.PARAMETER ApiKey
    Google Gemini API key. If not provided, will attempt to read from
    GEMINI_API_KEY environment variable.

.EXAMPLE
    ./Generate-BlogImage.ps1 -PostTitle "Understanding SOLID Principles" -PostContent "Software design principles for maintainable code" -OutputFileName "solid-principles.png"

.NOTES
    Requires: Google Gemini API key with Imagen access
    Output: 800x500px PNG image in posts/images/ directory
    Style: Pseudo realistic cell-shaded with focus and blur effects
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
    [string]$ApiKey = $env:GEMINI_API_KEY
)

# Validate API key
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Error "Gemini API key not provided. Set GEMINI_API_KEY environment variable or pass -ApiKey parameter."
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
Create an abstract, tech-oriented featured image for a blog post.

Post Title: $PostTitle
Post Theme: $PostContent

Style Requirements:
- $stylePrompt
- Abstract geometric shapes and patterns
- Modern, tech-oriented color scheme
- Limited color palette (3-5 colors)
- Include at least one element in sharp focus and one element with blur/depth-of-field effect
- No text or words in the image
- Landscape orientation suitable for a blog header

Technical aesthetic: Clean, modern, minimalist with depth
"@

Write-Host "`nGenerating image with prompt:"
Write-Host "----------------------------------------"
Write-Host $imagePrompt
Write-Host "----------------------------------------`n"

# Gemini API endpoint for Imagen
# Note: As of early 2024, Gemini's Imagen API might be in preview
# The endpoint structure may need adjustment based on current API version
$apiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/imagen-3.0-generate-001:predict"

# Construct request body
$requestBody = @{
    instances = @(
        @{
            prompt = $imagePrompt
        }
    )
    parameters = @{
        sampleCount = 1
        aspectRatio = "16:10"  # Approximately 800x500
        negativePrompt = "text, words, letters, watermark, signature, blurry overall, low quality"
        safetyFilterLevel = "block_some"
    }
} | ConvertTo-Json -Depth 10

try {
    Write-Host "Calling Gemini API..."
    
    # Make API request
    $headers = @{
        "Content-Type" = "application/json"
        "x-goog-api-key" = $ApiKey
    }
    
    $response = Invoke-RestMethod -Uri $apiEndpoint -Method Post -Headers $headers -Body $requestBody -TimeoutSec 120
    
    # Extract base64 image data from response
    # Note: Response structure may vary based on API version
    $imageData = $null
    
    if ($response.predictions -and $response.predictions[0].bytesBase64Encoded) {
        $imageData = $response.predictions[0].bytesBase64Encoded
    } elseif ($response.predictions -and $response.predictions[0].image) {
        $imageData = $response.predictions[0].image
    } elseif ($response.images -and $response.images[0]) {
        $imageData = $response.images[0]
    } else {
        Write-Error "Unexpected API response structure. Response: $($response | ConvertTo-Json -Depth 5)"
        exit 1
    }
    
    if ([string]::IsNullOrWhiteSpace($imageData)) {
        Write-Error "No image data received from API"
        exit 1
    }
    
    # Decode base64 and save to file
    $imageBytes = [Convert]::FromBase64String($imageData)
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
    Write-Error "Response: $($_.Exception.Response)"
    exit 1
}

Write-Host "`n✓ Image generation complete!"
exit 0
