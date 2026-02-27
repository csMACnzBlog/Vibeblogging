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

# Gemini API endpoint for Nano Banana 2 (gemini-3.1-flash-image-preview)
# This is a free-tier model available for image generation
# Refer to https://ai.google.dev/docs for latest API documentation
$apiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-image-preview:generateContent"

# Construct request body for generateContent API
# Note: This uses a different format than the predict API
$requestBody = @{
    contents = @(
        @{
            parts = @(
                @{
                    text = $imagePrompt
                }
            )
        }
    )
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
    
    # For generateContent endpoint, response is: 
    # { "candidates": [{ "content": { "parts": [{ "inlineData": { "mimeType": "image/png", "data": "..." } }] } }] }
    if ($response.candidates -and $response.candidates[0].content.parts) {
        $parts = $response.candidates[0].content.parts
        foreach ($part in $parts) {
            if ($part.inlineData -and $part.inlineData.data) {
                $imageData = $part.inlineData.data
                break
            }
        }
    }
    # Fallback to predict API format (for backward compatibility)
    elseif ($response.predictions -and $response.predictions[0].bytesBase64Encoded) {
        $imageData = $response.predictions[0].bytesBase64Encoded
    } elseif ($response.predictions -and $response.predictions[0].image) {
        $imageData = $response.predictions[0].image
    } elseif ($response.generatedImages -and $response.generatedImages[0].bytesBase64Encoded) {
        $imageData = $response.generatedImages[0].bytesBase64Encoded
    } elseif ($response.images -and $response.images[0]) {
        $imageData = $response.images[0]
    }
    
    if ([string]::IsNullOrWhiteSpace($imageData)) {
        Write-Error "No image data received from API. Response structure: $($response | ConvertTo-Json -Depth 3 -Compress)"
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
