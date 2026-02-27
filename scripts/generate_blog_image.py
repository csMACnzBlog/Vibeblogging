#!/usr/bin/env python3
"""
Generate a blog post featured image using HuggingFace Inference API.

This script calls HuggingFace's text-to-image API to generate a featured image
for a blog post based on the post content and title. The image is saved
to the posts/images/ directory with the specified filename.

Requires:
    huggingface_hub: pip install huggingface_hub
    Pillow: pip install Pillow
    
Environment:
    HUGGINGFACE_API_KEY: HuggingFace API token (required)

Usage:
    python scripts/generate_blog_image.py \\
        --title "Understanding SOLID Principles" \\
        --content "Software design principles for maintainable code" \\
        --output "solid-principles.png"
"""

import argparse
import io
import os
import sys
from pathlib import Path

try:
    from huggingface_hub import InferenceClient
except ImportError:
    print("Error: huggingface_hub is not installed.")
    print("Install it with: pip install huggingface_hub")
    sys.exit(1)

try:
    from PIL import Image
except ImportError:
    print("Error: Pillow is not installed.")
    print("Install it with: pip install Pillow")
    sys.exit(1)


def validate_api_key(api_key: str) -> str:
    """Validate and return the API key."""
    if not api_key:
        print("Error: HuggingFace API token not provided.")
        print("Set HUGGINGFACE_API_KEY environment variable or pass --api-key parameter.")
        sys.exit(1)
    return api_key


def ensure_png_extension(filename: str) -> str:
    """Ensure the filename has a .png extension."""
    if not filename.endswith('.png'):
        return f"{filename}.png"
    return filename


def get_output_path(filename: str) -> Path:
    """Get the full output path for the image file."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    output_path = repo_root / "posts" / "images" / filename
    
    # Ensure output directory exists
    output_path.parent.mkdir(parents=True, exist_ok=True)
    
    return output_path


def construct_image_prompt(title: str, content: str) -> str:
    """Construct the image generation prompt."""
    style_prompt = "pseudo realistic cell-shaded style with focus and focus blur effects"
    
    prompt = f"""Create a tech-oriented featured image for a blog post using everyday scenes or objects.

Post Title: {title}
Post Theme: {content}

Style Requirements:
- {style_prompt}
- Scene or object-based imagery: rooms, courtyards, open city/suburban spaces, OR closeup of everyday household/office items
- Examples: red stapler on desk, keyboard with coffee cup, violin on stand, pots in kitchen sink, towel on towel rail, empty office room, urban courtyard
- Modern, tech-oriented color scheme
- Limited color palette (3-5 colors)
- Include at least one element in sharp focus and one element with blur/depth-of-field effect
- No people or animals
- No text or words in the image
- Landscape orientation suitable for a blog header

Technical aesthetic: Clean, modern, minimalist with depth
"""
    
    return prompt


def validate_png(data: bytes) -> bool:
    """Validate that the data is a valid PNG file."""
    png_signature = bytes([137, 80, 78, 71, 13, 10, 26, 10])
    return data[:8] == png_signature


def generate_image(
    title: str,
    content: str,
    output_filename: str,
    api_key: str,
    model: str = "black-forest-labs/FLUX.1-schnell"
) -> None:
    """Generate a blog post featured image using HuggingFace API."""
    
    # Validate inputs
    api_key = validate_api_key(api_key)
    output_filename = ensure_png_extension(output_filename)
    output_path = get_output_path(output_filename)
    
    print(f"Generating image for: {title}")
    print(f"Output file: {output_path}")
    
    # Construct the image prompt
    image_prompt = construct_image_prompt(title, content)
    
    print("\nGenerating image with prompt:")
    print("----------------------------------------")
    print(image_prompt)
    print("----------------------------------------\n")
    
    try:
        print(f"Calling HuggingFace Inference API with model: {model}")
        
        # Initialize the inference client
        client = InferenceClient(token=api_key)
        
        # Generate the image
        # The client returns a PIL Image object
        image = client.text_to_image(
            prompt=image_prompt,
            model=model
        )
        
        # Convert PIL Image to bytes if needed
        if hasattr(image, 'save'):
            # It's a PIL Image - convert to bytes
            buffer = io.BytesIO()
            image.save(buffer, format='PNG')
            image_bytes = buffer.getvalue()
        elif isinstance(image, bytes):
            # Already bytes
            image_bytes = image
        else:
            raise ValueError(f"Unexpected image type: {type(image)}")
        
        # Save the image
        with open(output_path, 'wb') as f:
            f.write(image_bytes)
        
        # Validate and report success
        file_size_kb = len(image_bytes) / 1024
        
        print("✓ Image generated successfully!")
        print(f"  Saved to: {output_path}")
        print(f"  Size: {file_size_kb:.2f} KB")
        
        # Verify the file exists
        if output_path.exists():
            actual_size = output_path.stat().st_size
            print(f"  File verified: {actual_size} bytes")
            
            # Validate it's a valid PNG
            if not validate_png(image_bytes):
                print("⚠️  Warning: Generated file may not be a valid PNG image")
        else:
            print("Error: File was not created successfully")
            sys.exit(1)
            
    except Exception as e:
        print(f"Failed to generate image: {e}")
        
        # Check for specific error conditions
        error_str = str(e).lower()
        
        if 'unauthorized' in error_str or '401' in error_str:
            print("")
            print("⚠️  HuggingFace API Error: Unauthorized")
            print("   Your API token is invalid or has expired.")
            print("   Please generate a new token at: https://huggingface.co/settings/tokens")
            print("")
        elif 'model is currently loading' in error_str or '503' in error_str:
            print("")
            print("⚠️  HuggingFace API Error: Model Loading")
            print("   The model is currently loading. Please wait and try again in a few moments.")
            print("")
        elif 'rate limit' in error_str or '429' in error_str:
            print("")
            print("⚠️  HuggingFace API Error: Rate Limit")
            print("   You've exceeded the rate limit. Please wait a few minutes and try again.")
            print("")
        
        sys.exit(1)
    
    print("\n✓ Image generation complete!")


def main():
    """Main entry point for the script."""
    parser = argparse.ArgumentParser(
        description="Generate a blog post featured image using HuggingFace Inference API",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  python scripts/generate_blog_image.py \\
    --title "Understanding SOLID Principles" \\
    --content "Software design principles for maintainable code" \\
    --output "solid-principles.png"
  
  python scripts/generate_blog_image.py \\
    --title "Composition Over Inheritance" \\
    --content "Software design pattern emphasizing flexible composition" \\
    --output "composition-over-inheritance.png" \\
    --model "stabilityai/stable-diffusion-xl-base-1.0"
        """
    )
    
    parser.add_argument(
        '--title',
        required=True,
        help='The title of the blog post'
    )
    
    parser.add_argument(
        '--content',
        required=True,
        help='A summary or key themes from the blog post content'
    )
    
    parser.add_argument(
        '--output',
        required=True,
        help='The filename for the generated image (e.g., "my-post-slug.png")'
    )
    
    parser.add_argument(
        '--api-key',
        default=os.environ.get('HUGGINGFACE_API_KEY', ''),
        help='HuggingFace API token (defaults to HUGGINGFACE_API_KEY env var)'
    )
    
    parser.add_argument(
        '--model',
        default='black-forest-labs/FLUX.1-schnell',
        help='HuggingFace model ID to use for image generation (default: FLUX.1-schnell)'
    )
    
    args = parser.parse_args()
    
    generate_image(
        title=args.title,
        content=args.content,
        output_filename=args.output,
        api_key=args.api_key,
        model=args.model
    )


if __name__ == '__main__':
    main()
