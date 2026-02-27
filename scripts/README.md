# Vibeblogging Scripts

This directory contains utility scripts for managing and generating blog content.

## Available Scripts

### Generate-BlogImage.ps1

PowerShell script that generates blog post featured images using HuggingFace's Inference API.

**Purpose**: Creates abstract, cell-shaded featured images for blog posts with consistent style.

**Requirements**:
- PowerShell Core (pwsh) 7.0+
- HuggingFace API token (free tier available)
- Internet connection

**Note**: HuggingFace offers a generous free tier for text-to-image generation with several hundred requests per hour.

**Setup**:

1. Create a free account at [HuggingFace.co](https://huggingface.co/)
2. Generate an API token at [HuggingFace Settings](https://huggingface.co/settings/tokens)
3. Set the API token as an environment variable:
   ```bash
   # Linux/macOS
   export HUGGINGFACE_API_KEY="your-token-here"
   
   # Windows PowerShell
   $env:HUGGINGFACE_API_KEY="your-token-here"
   ```

**Usage**:

```bash
pwsh scripts/Generate-BlogImage.ps1 \
  -PostTitle "Your Blog Post Title" \
  -PostContent "Brief description of post themes and concepts" \
  -OutputFileName "post-slug.png"
```

**Parameters**:
- `-PostTitle` (required): The title of the blog post
- `-PostContent` (required): Summary of key themes/concepts for visual inspiration
- `-OutputFileName` (required): Filename for the image (e.g., "my-post.png")
- `-ApiKey` (optional): HuggingFace API token (defaults to `$env:HUGGINGFACE_API_KEY`)
- `-Model` (optional): HuggingFace model ID (defaults to `black-forest-labs/FLUX.1-dev`)

**Output**:
- Location: `posts/images/[filename].png`
- Format: PNG (800x500 pixels)
- Style: Pseudo realistic cell-shaded with focus and blur effects

**Examples**:

```bash
# Generate image for a design patterns post
pwsh scripts/Generate-BlogImage.ps1 \
  -PostTitle "Composition Over Inheritance" \
  -PostContent "Software design pattern emphasizing flexible composition of behaviors over rigid class hierarchies" \
  -OutputFileName "composition-over-inheritance.png"

# Generate image for a conference review
pwsh scripts/Generate-BlogImage.ps1 \
  -PostTitle ".NET Conf 2024 Review" \
  -PostContent "Conference highlights featuring new .NET features and community presentations" \
  -OutputFileName "dotnet-conf-2024-review.png"

# Provide API key inline
pwsh scripts/Generate-BlogImage.ps1 \
  -PostTitle "Getting Started with Async/Await" \
  -PostContent "Tutorial on C# asynchronous programming patterns" \
  -OutputFileName "async-await-tutorial.png" \
  -ApiKey "your-api-key-here"
```

**Style Details**:

All generated images follow a consistent visual style:
- **Pseudo realistic cell-shaded**: Flat color areas with defined edges, modern illustration aesthetic
- **Focus and blur effects**: Sharp foreground elements with blurred background for depth
- **Scene and object-based**: Uses everyday items (staplers, keyboards, coffee cups, etc.) or settings (rooms, courtyards, city spaces)
- **No people or animals**: Images are devoid of any human or animal subjects
- **No text**: Images contain no words, letters, or text
- **Limited palette**: 3-5 colors for clean, cohesive look

**Integration with Copilot Agents**:

This script is designed to be used by the `image-generator` Copilot agent (`.copilot/agents/image-generator.md`). The agent handles:
- Analyzing post content to extract visual concepts
- Constructing effective HuggingFace API prompts
- Running this script with appropriate parameters
- Validating generated images

**For blog post writers**: Use the `@image-generator` agent to generate images rather than calling this script directly. The agent provides better context analysis and prompt construction.

### run-a11y-tests.sh

Bash script that runs accessibility tests on the generated static site using pa11y-ci.

**Purpose**: Validates that generated HTML meets WCAG 2.1 AA accessibility standards.

**Usage**:
```bash
./scripts/run-a11y-tests.sh
```

Starts a local HTTP server, runs pa11y-ci on all HTML files in `output/`, and reports any accessibility issues.

## Development Notes

### Adding New Scripts

When adding new scripts to this directory:

1. **Choose the right language**:
   - Use PowerShell (`.ps1`) for cross-platform scripts that need .NET integration
   - Use Bash (`.sh`) for simple Unix-like system scripts
   - Use Python (`.py`) for complex data processing or ML tasks

2. **Document thoroughly**:
   - Include synopsis, description, parameters, and examples
   - Add to this README with usage instructions
   - Consider adding to Copilot agent instructions if relevant

3. **Make executable**:
   ```bash
   chmod +x scripts/your-script.sh
   ```

4. **Test on multiple platforms**:
   - PowerShell scripts should work on Linux, macOS, and Windows
   - Bash scripts should work on Linux and macOS

5. **Handle errors gracefully**:
   - Validate inputs
   - Provide clear error messages
   - Exit with appropriate codes (0 = success, non-zero = error)

### API Keys and Secrets

⚠️ **IMPORTANT**: Never commit API keys or secrets to the repository.

- Use environment variables for sensitive data
- Document required environment variables in this README
- Consider using `.env` files for local development (add to `.gitignore`)
- For CI/CD, store secrets in GitHub Actions secrets

## Troubleshooting

### Generate-BlogImage.ps1 Issues

**"HuggingFace API token not provided"**
- Ensure `HUGGINGFACE_API_KEY` environment variable is set
- Or pass `-ApiKey` parameter explicitly
- Generate a token at https://huggingface.co/settings/tokens

**"Unauthorized (401)"**
- Your API token is invalid or has expired
- Generate a new token at https://huggingface.co/settings/tokens
- Verify you're using the correct environment variable

**"Model Loading (503)"**
- The model is currently loading on HuggingFace's servers
- Wait a few moments and try again
- This is common with free tier after inactivity

**"Cannot write to posts/images/"**
- Ensure the directory exists: `mkdir -p posts/images`
- Check file permissions on the directory
- Verify disk space is available

**Rate limiting errors**
- Free tier: Several hundred requests per hour
- Wait and retry, or consider upgrading to PRO for higher limits
- Check your usage at https://huggingface.co/settings/billing

### General Script Issues

**Permission denied**
- Make script executable: `chmod +x scripts/script-name.sh`
- For PowerShell: Check execution policy with `Get-ExecutionPolicy`

**Command not found**
- Ensure required tools are installed (pwsh, node, npm, etc.)
- Check that tools are in your PATH
- For PowerShell scripts, use `pwsh` not `powershell`

## Contributing

When contributing new scripts or improvements:

1. Follow existing patterns and conventions
2. Update this README with documentation
3. Test on Linux and macOS (and Windows if applicable)
4. Handle errors gracefully
5. Add usage examples
6. Consider integration with Copilot agents

## Resources

- [HuggingFace Inference API Documentation](https://huggingface.co/docs/inference-providers/tasks/text-to-image)
- [PowerShell Core](https://docs.microsoft.com/powershell/scripting/install/installing-powershell)
- [pa11y-ci](https://github.com/pa11y/pa11y-ci)
